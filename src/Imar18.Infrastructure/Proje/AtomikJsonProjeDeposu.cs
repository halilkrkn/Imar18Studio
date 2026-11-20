using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Domain.Numerics;
using Imar18.Domain.Revizyon;

namespace Imar18.Infrastructure.Proje;

/// <summary>
/// Pilot yerel proje deposu (P5.1, FR-F6.10): proje durumu ve tüm denetim kayıtları tek UTF-8 JSON dosyasında tutulur ve her
/// commit <see cref="AtomikDosyaYazici"/> ile atomik yazılır (aynı transaction). Zarf: şema adı, pilot damgası, içeriğin SHA-256
/// özeti ve içerik. Okumada özet doğrulanır ve tüm Domain nesneleri fabrika metotlarıyla yeniden doğrulanarak kurulur; bozuk
/// kayıt istisnasız <c>ERR-CIKTI-009</c> döner ve otomatik olarak yedekten açılmaz (karar mühendisindir).
/// Şifreleme (SQLCipher/.imr18) Faz 11'dedir; bu dosya yalnızca sentetik/yerel pilot verisi içindir.
/// </summary>
public sealed class AtomikJsonProjeDeposu : IProjeDeposu
{
    /// <summary>Kayıt şeması.</summary>
    public const string Sema = "imar18-pilot-proje-v1";

    /// <summary>
    /// SAHA-014 (architect D2): <c>.DNS</c> ileri dönüşümlü kayıtların şeması. Yalnız ileri dönüşüm varken yazılır; bilinmeyen alanı
    /// yok sayan eski sürüm Deprem Öncesi orijinalleri sessizce düşürmesin diye v1 okuyucusu bu dosyayı reddeder. Okuma ikisini kabul eder.
    /// </summary>
    public const string SemaIleriDonusumlu = "imar18-pilot-proje-v2";

    /// <summary>Kabul edilen en büyük dosya boyutu.</summary>
    public const long MaxDosyaBoyutu = 256L * 1024 * 1024;

    private static readonly JsonSerializerOptions Secenekler = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    private readonly string _yol;

    public AtomikJsonProjeDeposu(string yol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yol);
        _yol = Path.GetFullPath(yol);
    }

    /// <summary>Test kancası: geçici dosya yazıldıktan sonra, yer değiştirmeden önce çağrılır (çökme benzetimi).</summary>
    internal Action? YerDegistirmeOncesi { get; set; }

    public Task<bool> VarMiAsync(CancellationToken cancellationToken = default) => Task.FromResult(File.Exists(_yol));

    public async Task<Result<ProjeKaydi>> YukleAsync(CancellationToken cancellationToken = default)
    {
        byte[] veri;
        try
        {
            var bilgi = new FileInfo(_yol);
            if (!bilgi.Exists)
            {
                return Hata("file not found");
            }

            if (bilgi.Length > MaxDosyaBoyutu)
            {
                return Hata("file too large");
            }

            veri = await File.ReadAllBytesAsync(_yol, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Hata(ex.GetType().Name);
        }

        try
        {
            using var doc = JsonDocument.Parse(veri);
            var kok = doc.RootElement;
            if (kok.ValueKind != JsonValueKind.Object
                || !kok.TryGetProperty("sema", out var sema) || sema.ValueKind != JsonValueKind.String
                || (sema.GetString() != Sema && sema.GetString() != SemaIleriDonusumlu))
            {
                return Hata("unknown schema");
            }

            if (!kok.TryGetProperty("icerikSha256", out var ozet) || ozet.ValueKind != JsonValueKind.String
                || !kok.TryGetProperty("icerik", out var icerik) || icerik.ValueKind != JsonValueKind.Object)
            {
                return Hata("missing envelope field");
            }

            var hesaplanan = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(icerik.GetRawText())));
            if (!string.Equals(hesaplanan, ozet.GetString(), StringComparison.Ordinal))
            {
                return Hata("content checksum mismatch");
            }

            var dto = icerik.Deserialize<KayitDto>(Secenekler);
            if (dto is not null && (dto.Belge?.Ileri is not null) != (sema.GetString() == SemaIleriDonusumlu))
            {
                return Hata("schema does not match forward transformation content");
            }

            return dto is null ? Hata("empty content") : Esle(dto);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return Hata("invalid content (" + ex.GetType().Name + ")");
        }
    }

    public async Task<Result<ProjeKaydi>> KaydetAsync(ProjeKaydi kayit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kayit);
        var icerik = JsonSerializer.SerializeToUtf8Bytes(Dto(kayit), Secenekler);
        var ozet = Convert.ToHexStringLower(SHA256.HashData(icerik));

        using var tampon = new MemoryStream(icerik.Length + 256);
        tampon.Write(Encoding.UTF8.GetBytes(
            "{\"sema\":\"" + (kayit.Belge.IleriDonusum is null ? Sema : SemaIleriDonusumlu) + "\",\"pilot\":" + JsonSerializer.Serialize(PilotDamgasi.Metin, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            + ",\"icerikSha256\":\"" + ozet + "\",\"icerik\":"));
        tampon.Write(icerik);
        tampon.Write("}"u8);

        // IOException/UnauthorizedAccessException CommandBus'ta ERR-CIKTI-009'a çevrilir (bellek durumu geri alınır).
        await AtomikDosyaYazici.YazAsync(_yol, tampon.ToArray(), YerDegistirmeOncesi, cancellationToken).ConfigureAwait(false);
        return Result<ProjeKaydi>.Success(kayit);
    }

    private static Result<ProjeKaydi> Hata(string neden)
    {
        var e = ErrorCatalog.Get(ErrorCodes.CiktiProjeKaydiHatasi);
        return Result<ProjeKaydi>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", neden, StringComparison.Ordinal) });
    }

    // ---------------- DTO eşlemesi ----------------

    private static KayitDto Dto(ProjeKaydi k)
    {
        var b = k.Belge;
        return new KayitDto(
            new BelgeDto(
                b.ProjeAdi,
                b.Crs is { } c ? new CrsDto(c.Datum.ToString(), c.OrtaMeridyenDerece, c.Epok, c.OlcekPaydasi) : null,
                b.Parseller.Select(p => new ParselDto(p.ParselId, p.Rol.ToString(), p.Koseler.Select(v => new[] { v.X, v.Y }).ToList())).ToList(),
                b.Dop is { } d ? new DopDto(d.R1, d.D1, d.R2, d.D2, d.R3, d.OrtakKatilimM2) : null,
                b.TecvizParselleri.Select(t => new TecvizDto(
                    t.ParselId,
                    t.TapuAlaniM2,
                    t.HesapAlaniM2,
                    t.Tecviz is { } g ? new TecvizGirdiDto(g.OlcekPaydaM, g.UretimYontemi, g.KoordinatKalite.ToString(), g.TecvizSiniriM2, g.KaynakNotu) : null)).ToList(),
                b.DuzenlemeToplamTecvizSiniriM2,
                b.HakEslestirme is { } h
                    ? new HakDto(
                        h.Dopo,
                        h.KadastroParselleri.Select(kp => new KadastroDto(kp.ParselId, kp.TapuAlaniM2, kp.Malikler.Select(m => new MalikDto(
                            m.MalikId, m.Hisse.Numerator.ToString(CultureInfo.InvariantCulture), m.Hisse.Denominator.ToString(CultureInfo.InvariantCulture), m.Muvafakatli)).ToList())).ToList(),
                        h.ImarParselleri.Select(i => new ImarDto(i.GeciciKod, i.AlanM2)).ToList(),
                        h.Eslestirme.Select(s => new SatirDto(s.MalikId, s.KadastroParselId, s.ImarParselKodu, s.AlanM2)).ToList())
                    : null,
                b.KimlikCiftleri.Select(x => new KimlikDto(x.GeciciKod, x.TescilNo)).ToList(),
                b.Onay is { } o ? new OnayDto(o.RevizyonId.Deger, o.OnaylayanId, o.Zaman) : null,
                b.Surum,
                b.MalikRehberi.Malikler.Count == 0 && b.MalikRehberi.Parseller.Count == 0
                    ? null
                    : new RehberDto(
                        b.MalikRehberi.Malikler.Select(m => new RehberMalikDto(m.MalikKodu, m.AdSoyad, m.BabaAdi, m.KisiSistemNo)).ToList(),
                        b.MalikRehberi.Parseller.Select(x => new RehberParselDto(x.KadastroParselId, x.Cilt, x.Sayfa, x.Il, x.Ilce, x.Mahalle, x.Ada, x.Parsel, x.AtYuzolcumM2)).ToList()),
                b.Kunye is { } ku ? new KunyeDto(ku.Il, ku.Ilce, ku.KoyMahalle, ku.Belediye, ku.PaftaNolari, ku.KadastroId, ku.DatumProjeksiyon) : null,
                b.Donusum is { } dn
                    ? new DonusumDto(dn.Model.ToString(), dn.Mk, dn.OrtakNoktalar.Select(n => new OrtakNoktaDto(n.NoktaNo, n.Y1, n.X1, n.Y2, n.X2)).ToList())
                    : null,
                b.NczSablonu is { } ns ? new NczSablonuDto(ns.Sha256, Convert.ToBase64String(ns.Icerik.Span)) : null,
                b.IleriDonusum is { } ik
                    ? new IleriDonusumDto(ik.A, ik.B, ik.C, ik.D, ik.Cy, ik.Cx,
                        (b.DepremOncesiParseller ?? []).Select(p => new ParselDto(p.ParselId, p.Rol.ToString(), p.Koseler.Select(v => new[] { v.X, v.Y }).ToList())).ToList())
                    : null),
            k.Denetim.ToList());
    }

    private static ParselGeometrisi[] Parseller(List<ParselDto> liste) => liste.Select(p => new ParselGeometrisi(
        p.Id ?? throw new FormatException("parcel id"),
        Enm<ParselRolu>(p.Rol),
        (p.Koseler ?? throw new FormatException("vertices")).Select(v => v is { Length: 2 } ? new GeodeticPoint(v[0], v[1]) : throw new FormatException("vertex")).ToArray())).ToArray();

    private static Result<ProjeKaydi> Esle(KayitDto dto)
    {
        var b = dto.Belge ?? throw new FormatException("belge missing");
        if (string.IsNullOrWhiteSpace(b.ProjeAdi) || b.Parseller is null || b.Tecviz is null || b.Kimlikler is null || dto.Denetim is null)
        {
            throw new FormatException("required list missing");
        }

        ProjeCrs? crs = null;
        if (b.Crs is { } c)
        {
            var r = ProjeCrs.Olustur(Enm<GeodetikDatum>(c.Datum), c.Meridyen, c.Epok, c.Olcek);
            if (r.IsFailure)
            {
                return Hata("invalid CRS");
            }

            crs = r.Value;
        }

        var parseller = Parseller(b.Parseller);

        DopGirdisi? dop = null;
        if (b.Dop is { } d)
        {
            var r = DopGirdisi.Olustur(d.R1, d.R2, d.D1, d.D2, d.R3, d.Ortak);
            if (r.IsFailure)
            {
                return Hata("invalid DOP input");
            }

            dop = r.Value;
        }

        var tecviz = new List<TecvizParselGirdisi>(b.Tecviz.Count);
        foreach (var t in b.Tecviz)
        {
            TecvizGirdisi? g = null;
            if (t.Girdi is { } tg)
            {
                var r = TecvizGirdisi.Olustur(tg.Olcek, tg.Yontem, Enm<KoordinatKalitesi>(tg.Kalite), tg.Sinir, tg.Kaynak);
                if (r.IsFailure)
                {
                    return Hata("invalid tecviz input");
                }

                g = r.Value;
            }

            tecviz.Add(new TecvizParselGirdisi(t.ParselId ?? throw new FormatException("tecviz id"), t.Tapu, t.Hesap, g));
        }

        HakEslestirmeGirdisi? hak = null;
        if (b.Hak is { } h)
        {
            hak = new HakEslestirmeGirdisi(
                h.Dopo,
                (h.Kadastro ?? throw new FormatException("cadastre")).Select(k => new KadastroParseli(
                    k.Id,
                    k.Tapu,
                    (k.Malikler ?? throw new FormatException("owners")).Select(m => new KadastroMaliki(
                        m.Id,
                        new Fraction(BigInteger.Parse(m.Pay, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), BigInteger.Parse(m.Payda, NumberStyles.None, CultureInfo.InvariantCulture)),
                        m.Muvafakat)).ToArray())).ToArray(),
                (h.Imar ?? throw new FormatException("zoning")).Select(i => new ImarParseli(i.Kod, i.Alan)).ToArray(),
                (h.Satirlar ?? throw new FormatException("rows")).Select(s => new HakEslestirmeSatiri(s.Malik, s.Kadastro, s.Imar, s.Alan)).ToArray());
        }

        var kimlikler = b.Kimlikler.Select(x => new ParselKimlikCifti(x.Gecici, x.Tescil)).ToArray();

        MuhendisOnayi? onay = null;
        if (b.Onay is { } o)
        {
            if (!RevisionId.TryParse(o.Revizyon, out var rev) || string.IsNullOrWhiteSpace(o.Onaylayan))
            {
                return Hata("invalid approval");
            }

            onay = MuhendisOnayi.Olustur(rev, o.Onaylayan, o.Zaman);
        }

        if (dto.Denetim.Any(a => a is null || string.IsNullOrWhiteSpace(a.Islem) || string.IsNullOrWhiteSpace(a.KullaniciId) || !Enum.IsDefined(a.Sonuc)))
        {
            return Hata("invalid audit record");
        }

        for (var i = 0; i < dto.Denetim.Count; i++)
        {
            if (dto.Denetim[i].Sira != i + 1)
            {
                return Hata("audit sequence gap");
            }
        }

        var belge = new ProjeBelgesi(b.ProjeAdi, crs, parseller, dop, tecviz, b.ToplamTecviz, hak, kimlikler, onay, b.Surum);
        if (b.Rehber is { } rehberDto)
        {
            belge = belge with
            {
                MalikRehberi = new Application.Girdi.MalikRehberi(
                    (rehberDto.Malikler ?? []).Select(m => new Application.Girdi.MalikRehberKaydi(m.Kod, m.AdSoyad, m.Baba, m.KisiNo)).ToList().AsReadOnly(),
                    (rehberDto.Parseller ?? []).Select(x => new Application.Girdi.KadastroTapuKaydi(x.Id, x.Cilt, x.Sayfa, x.Il, x.Ilce, x.Mahalle, x.Ada, x.Parsel, x.At)).ToList().AsReadOnly()),
            };
        }

        if (b.Kunye is { } kd)
        {
            belge = belge with { Kunye = new ProjeKunyesi(kd.Il, kd.Ilce, kd.Mahalle, kd.Belediye, kd.Pafta, kd.KadastroId, kd.Datum) };
        }

        if (b.Donusum is { } dd)
        {
            belge = belge with
            {
                Donusum = new DonusumGirdisi(Enm<DonusumModeli>(dd.Model), dd.Mk,
                    (dd.Noktalar ?? throw new FormatException("donusum points missing")).Select(n => new DonusumOrtakNoktasi(n.No, n.Y1, n.X1, n.Y2, n.X2)).ToArray()),
            };
        }

        if (b.Ncz is { } nd)
        {
            var sablon = NczSablonu.Yukle(Convert.FromBase64String(nd.Base64 ?? throw new FormatException("ncz template missing")), nd.Sha256 ?? string.Empty);
            if (sablon.IsFailure)
            {
                return Hata("ncz template rejected (" + sablon.Error.Code + ")");
            }

            belge = belge with { NczSablonu = sablon.Value };
        }

        ParselGeometrisi[]? oncesi = null;
        if (b.Ileri is { } id)
        {
            oncesi = Parseller(id.Oncesi ?? throw new FormatException("earthquake-before parcels missing"));
            belge = belge with { IleriDonusum = new DonusumKatsayilari(id.A, id.B, id.C, id.D, id.Cy, id.Cx), DepremOncesiParseller = oncesi };
        }

        // SAHA-014 (architect K2): Parseller = İleri(DepremOncesiParseller) değişmezi yüklemede de denetlenir (elle değiştirilmiş
        // aktif köşe, eksik ortak nokta veya geçersiz katsayı açılmaz).
        if (IleriDonusumDegismezi.Dogrula(belge) is { IsFailure: true } bozuk)
        {
            return Hata("forward transformation invariant violated (" + bozuk.Error.Code + ")");
        }

        // Komut doğrulamaları yüklemede yeniden çalışır (P5 reviewer M4): elle düzenlenip özeti yeniden hesaplanmış kayıt,
        // CommandBus'ın kabul etmeyeceği bir durumu açamaz. Null metin alanları revizyon özetinde ArgumentException üretir.
        var bos = ProjeBelgesi.Yeni(belge.ProjeAdi);
        var kontroller = new List<IProjeKomutu> { new ParselGeometrileriAyarlaKomutu(parseller) };
        if (oncesi is not null)
        {
            kontroller.Add(new ParselGeometrileriAyarlaKomutu(oncesi));
        }

        if (kimlikler.Length > 0)
        {
            kontroller.Add(new ParselKimlikEslemesiAyarlaKomutu(kimlikler));
        }
        if (tecviz.Count > 0)
        {
            kontroller.Add(new TecvizGirdileriAyarlaKomutu(tecviz, b.ToplamTecviz));
        }

        if (hak is not null)
        {
            kontroller.Add(new HakEslestirmeAyarlaKomutu(hak));
        }

        if (belge.Donusum is not null)
        {
            kontroller.Add(new DonusumGirdisiAyarlaKomutu(belge.Donusum));
        }

        if (belge.Kunye is not null)
        {
            kontroller.Add(new ProjeKunyesiAyarlaKomutu(belge.Kunye));
        }

        if (belge.NczSablonu is not null)
        {
            kontroller.Add(new NczSablonuAyarlaKomutu(belge.NczSablonu.Icerik.ToArray()));
        }

        // Not: Result<T> bir struct'tır; default değeri başarısız sayılır — FirstOrDefault yerine hata nesnesi aranır.
        if (kontroller.Select(k => k.Uygula(bos)).Where(r => r.IsFailure).Select(r => r.Error).FirstOrDefault() is { } red)
        {
            return Hata("content rejected by command validation (" + red.Code + ")");
        }

        _ = belge.RevizyonKimligi();
        return Result<ProjeKaydi>.Success(new ProjeKaydi(belge, dto.Denetim.ToArray()));
    }

    private static T Enm<T>(string? deger)
        where T : struct, Enum =>
        Enum.TryParse<T>(deger, ignoreCase: false, out var v) && Enum.IsDefined(v) && !int.TryParse(deger, out _)
            ? v
            : throw new FormatException("enum " + typeof(T).Name);

    private sealed record KayitDto(BelgeDto Belge, List<AuditKaydi> Denetim);

    private sealed record BelgeDto(
        string ProjeAdi,
        CrsDto? Crs,
        List<ParselDto> Parseller,
        DopDto? Dop,
        List<TecvizDto> Tecviz,
        decimal? ToplamTecviz,
        HakDto? Hak,
        List<KimlikDto> Kimlikler,
        OnayDto? Onay,
        long Surum,
        RehberDto? Rehber = null,
        KunyeDto? Kunye = null,
        DonusumDto? Donusum = null,
        NczSablonuDto? Ncz = null,
        IleriDonusumDto? Ileri = null);

    /// <summary>SAHA-014: .DNS katsayıları (Netcad düzeni) ve Deprem Öncesi (orijinal CAD) parseller; yalnız v2 şemasında bulunur.</summary>
    private sealed record IleriDonusumDto(decimal A, decimal B, decimal C, decimal D, decimal Cy, decimal Cx, List<ParselDto>? Oncesi);

    /// <summary>P7.3-C3: kaynak NCZ şablonu (base64) ve SHA-256 özeti; yüklemede özet yeniden doğrulanır (P7.3-C3 öncesi kayıtlarda yoktur).</summary>
    private sealed record NczSablonuDto(string Sha256, string Base64);

    /// <summary>P7.3-C2: proje künyesi (P7.3-C öncesi kayıtlarda yoktur).</summary>
    private sealed record KunyeDto(string Il, string Ilce, string Mahalle, string Belediye, string Pafta, string KadastroId, string Datum);

    /// <summary>P7.3-C2: ortak noktalı dönüşüm girdisi (P7.3-C öncesi kayıtlarda yoktur).</summary>
    private sealed record DonusumDto(string Model, decimal Mk, List<OrtakNoktaDto>? Noktalar);

    private sealed record OrtakNoktaDto(string No, decimal Y1, decimal X1, decimal Y2, decimal X2);

    /// <summary>P7.3-B1: yerel Malik Rehberi (P7.3 öncesi kayıtlarda yoktur → boş rehber).</summary>
    private sealed record RehberDto(List<RehberMalikDto>? Malikler, List<RehberParselDto>? Parseller);

    private sealed record RehberMalikDto(string Kod, string AdSoyad, string? Baba, string? KisiNo);

    private sealed record RehberParselDto(string Id, string? Cilt, string? Sayfa, string? Il, string? Ilce, string? Mahalle, string? Ada, string? Parsel, decimal? At);

    private sealed record CrsDto(string Datum, int Meridyen, decimal? Epok, int Olcek);

    private sealed record ParselDto(string Id, string Rol, List<decimal[]> Koseler);

    private sealed record DopDto(decimal R1, decimal D1, decimal R2, decimal D2, decimal R3, decimal Ortak);

    private sealed record TecvizDto(string ParselId, decimal Tapu, decimal Hesap, TecvizGirdiDto? Girdi);

    private sealed record TecvizGirdiDto(int Olcek, string Yontem, string Kalite, decimal Sinir, string Kaynak);

    private sealed record HakDto(decimal Dopo, List<KadastroDto> Kadastro, List<ImarDto> Imar, List<SatirDto> Satirlar);

    private sealed record KadastroDto(string Id, decimal Tapu, List<MalikDto> Malikler);

    private sealed record MalikDto(string Id, string Pay, string Payda, bool Muvafakat);

    private sealed record ImarDto(string Kod, decimal Alan);

    private sealed record SatirDto(string Malik, string Kadastro, string Imar, decimal Alan);

    private sealed record KimlikDto(string Gecici, string Tescil);

    private sealed record OnayDto(string Revizyon, string Onaylayan, DateTimeOffset Zaman);
}

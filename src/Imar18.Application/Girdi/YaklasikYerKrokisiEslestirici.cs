using System.Globalization;
using System.Text;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;

namespace Imar18.Application.Girdi;

/// <summary>Yaklaşık Yer Krokisi eşleştirme girdisi (proje geometrisi, hak için imar parselleri, doğrulanmış TAKBİS, rehber, DOPO).</summary>
public sealed record KrokiEslestirmeGirdisi(
    CadLayerDocument Kroki,
    IReadOnlyList<ParselGeometrisi> ProjeParselleri,
    IReadOnlyList<ImarParseli> ImarParselleri,
    IReadOnlyList<KadastroParseli> Takbis,
    MalikRehberi Rehber,
    decimal? Dopo);

/// <summary>Malik × kadastro parseli için kroki toplamı ile DOP sonrası net hak karşılaştırması (DOPO yoksa net hak null).</summary>
public sealed record KrokiFarki(string MalikKodu, string KadastroParselId, decimal KrokiM2, decimal? NetHakM2)
{
    public decimal? FarkM2 => NetHakM2 is { } n ? KrokiM2 - n : null;
}

/// <summary>Eşleştirme sonucu: Kroki satırları, kullanım poligonu sayıları, net hak farkları ve uyarılar (kişisel veri içermez).</summary>
public sealed record KrokiEslestirmeSonucu(
    IReadOnlyList<KrokiEslesmeGirdisi> Satirlar,
    int KullanimPoligonu,
    int CozulemeyenPoligon,
    IReadOnlyList<KrokiFarki> Farklar,
    IReadOnlyList<string> Uyarilar)
{
    /// <summary>Kroki kutusu metni: özet yorumu, <c>malik;kadastro;imar;alan</c> satırları ve malik bazında fark yorumları.</summary>
    public string KrokiMetni()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"# Yaklaşık Yer Krokisi: {KullanimPoligonu} kullanım poligonu ({KullanimPoligonu - CozulemeyenPoligon} malike eşlendi, Kip 1); kalan alan TAKBİS hisse oranında (Kip 2)")
            .AppendLine();
        foreach (var s in Satirlar)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{s.MalikKodu};{s.KadastroParselKimligi};{s.ImarParselKodu};{s.AlanM2:0.00}").AppendLine();
        }

        foreach (var f in Farklar)
        {
            sb.Append(CultureInfo.InvariantCulture, $"# {f.MalikKodu} · {f.KadastroParselId}: kroki {f.KrokiM2:0.00} m²");
            if (f.NetHakM2 is { } n)
            {
                sb.Append(CultureInfo.InvariantCulture, $" · net hak {n:0.00} m² · fark {f.FarkM2:+0.00;-0.00;0.00} m²");
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// P7.3-B2 (SAHA-006): Yaklaşık Yer Krokisi (.NCZ/.DXF/.DWG) → Kroki satırları (<c>malik;kadastro;imar;alan</c>).
/// <list type="number">
/// <item><b>Kip 1</b> — krokideki kapalı poligonlar malikin kullanım alanıdır: etiket (poligon etiketi, sonra içindeki yazılar)
/// sırasıyla TAKBİS malik kimliği (SAHA-011: <c>AD SOYAD (BABA ADI)</c> etiketi veya eski kod; Türkçe büyük harf duyarsız), rehberdeki
/// kişi sistem no, rehberdeki normalize ad, en son rehbersiz etiketin ad soyad kısmı (NCZ etiketi 15 karakterle kesildiği için en
/// az 5 karakterlik tekil önek de kabul) ile malike çözülür. Her kadastro K, imar I için ağırlık += alan(K ∩ I ∩ U).</item>
/// <item><b>Kip 2</b> — K ∩ I'nin kullanım poligonlarıyla karşılanmayan kalanı (veya tamamı) K'nin TAKBİS hisse oranlarıyla
/// (<see cref="Fraction"/>) maliklere bölünür.</item>
/// <item><b>Dengeleme</b> — her imar parselinde ağırlıklar <see cref="LargestRemainderAllocator"/> ile parsel alanına 0.01 m²
/// birimde tam eşitlenir (HakEslestirmeServisi ±1 dm² koşulu); bağlayıcı sıra büyük pay → küçük kimlik.</item>
/// <item><see cref="KiymikEsigiM2"/> altı kesişim yok sayılır (uygulama eşiği, mevzuat değeri değil — SORU-044).</item>
/// </list>
/// Malik başına kroki toplamı ile net hak (giren − DOP, HakEslestirmeServisi ile aynı yuvarlama) farkı raporlanır, otomatik
/// düzeltilmez. Uyarılar ve hatalar kroki etiketlerini (ad içerebilir) yazmaz; yalnız sayı ve parsel kimliği (KVKK).
/// </summary>
public static class YaklasikYerKrokisiEslestirici
{
    /// <summary>Kıymık eşiği (m²): bu alanın altındaki kesişimler dağıtıma girmez.</summary>
    public const decimal KiymikEsigiM2 = 0.50m;

    /// <summary>Teknik neden: kroki proje kadastro alanıyla örtüşmüyor (arayüz Türkçe ipucu için eşler).</summary>
    public const string OrtusmeYokNedeni = "sketch does not overlap the project cadastral area (check CRS / coordinate offset)";

    /// <summary>Teknik neden: dağıtılacak imar parseli yok.</summary>
    public const string ImarParseliYokNedeni = "no zoning parcels to distribute";

    public static Result<KrokiEslestirmeSonucu> Eslestir(KrokiEslestirmeGirdisi girdi, IParselKesisimHesaplayici kesisim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(girdi);
        ArgumentNullException.ThrowIfNull(kesisim);

        if (girdi.Takbis is null || girdi.Takbis.Count == 0)
        {
            return Gecersiz("TAKBIS shares are required before the sketch can be matched");
        }

        if (girdi.ImarParselleri is null || girdi.ImarParselleri.Count == 0)
        {
            return Gecersiz(ImarParseliYokNedeni);
        }

        var geometri = (girdi.ProjeParselleri ?? []).ToLookup(p => (p.Rol, p.ParselId));
        var uyarilar = new List<string>();
        var kadastro = new List<(KadastroParseli Parsel, IReadOnlyList<GeodeticPoint> Halka)>();
        foreach (var k in girdi.Takbis)
        {
            var g = geometri[(ParselRolu.Kadastro, k.ParselId)].FirstOrDefault();
            if (g is null)
            {
                uyarilar.Add($"{k.ParselId} kadastro parselinin geometrisi projede yok; krokide dağıtılmadı.");
                continue;
            }

            kadastro.Add((k, g.Koseler));
        }

        // Kip 1: kapalı poligonlar ve malik çözümü.
        var kodlar = girdi.Takbis.SelectMany(k => k.Malikler.Select(m => m.MalikId)).Distinct(StringComparer.Ordinal).ToList();
        var poligonlar = (girdi.Kroki?.Polylines ?? []).Where(p => p.IsClosed && p.Vertices.Count >= 4).ToList();
        var kullanim = new List<(string Malik, IReadOnlyList<GeodeticPoint> Halka)>();
        var cozulemeyen = 0;
        foreach (var p in poligonlar)
        {
            var malik = Etiketler(p, girdi.Kroki!.Texts).Select(e => Coz(e, kodlar, girdi.Rehber ?? MalikRehberi.Bos)).FirstOrDefault(m => m is not null);
            if (malik is null)
            {
                cozulemeyen++;
            }
            else
            {
                kullanim.Add((malik, p.Vertices));
            }
        }

        if (poligonlar.Count > 0 && kadastro.Count > 0
            && poligonlar.All(p => kadastro.All(k => kesisim.KesisimAlaniM2([p.Vertices, k.Halka], cancellationToken) < KiymikEsigiM2)))
        {
            return Gecersiz(OrtusmeYokNedeni);
        }

        if (poligonlar.Count == 0)
        {
            uyarilar.Add("Krokide kullanım poligonu yok; tüm kesişimler TAKBİS hisse oranında bölündü (Kip 2).");
        }
        else if (cozulemeyen > 0)
        {
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture,
                $"{cozulemeyen} kullanım poligonu malike çözülemedi (etiket TAKBİS malik adı/kodu, rehberdeki ad veya kişi no değil); alanı hisse oranında bölündü (Kip 2)."));
        }

        var satirlar = new List<KrokiEslesmeGirdisi>();
        foreach (var imar in girdi.ImarParselleri)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ig = geometri[(ParselRolu.Imar, imar.GeciciKod)].FirstOrDefault();
            if (ig is null)
            {
                uyarilar.Add($"{imar.GeciciKod} imar parselinin geometrisi projede yok; krokide dağıtılmadı.");
                continue;
            }

            var agirlik = new Dictionary<(string Malik, string Kadastro), decimal>();
            foreach (var (k, halka) in kadastro)
            {
                var ortak = kesisim.KesisimAlaniM2([halka, ig.Koseler], cancellationToken);
                if (ortak < KiymikEsigiM2)
                {
                    continue;
                }

                var karsilanan = 0m;
                foreach (var (malik, u) in kullanim.Where(u => k.Malikler.Any(m => m.MalikId == u.Malik)))
                {
                    var a = kesisim.KesisimAlaniM2([halka, ig.Koseler, u], cancellationToken);
                    if (a >= KiymikEsigiM2)
                    {
                        Ekle(agirlik, (malik, k.ParselId), a);
                        karsilanan += a;
                    }
                }

                var kalan = ortak - karsilanan;
                if (kalan >= KiymikEsigiM2)
                {
                    foreach (var m in k.Malikler)
                    {
                        Ekle(agirlik, (m.MalikId, k.ParselId), (Fraction.FromDecimal(kalan) * m.Hisse).ToDecimal(6));
                    }
                }
            }

            var dolu = agirlik.Where(a => a.Value > 0m).ToList();
            if (dolu.Count == 0)
            {
                uyarilar.Add($"{imar.GeciciKod} imar parseli TAKBİS'li kadastro parselleriyle kesişmiyor; satır üretilmedi.");
                continue;
            }

            var toplam = Fraction.FromDecimal(dolu.Sum(a => a.Value));
            var paylar = LargestRemainderAllocator.AllocateAlanM2(
                imar.AlanM2, dolu.Select(a => new AllocationWeight(Kimlik(a.Key), Fraction.FromDecimal(a.Value) / toplam)).ToList());
            if (paylar.IsFailure)
            {
                return Result<KrokiEslestirmeSonucu>.Failure(paylar.Error);
            }

            satirlar.AddRange(paylar.Value
                .Where(p => p.Units > 0)
                .Select(p => (Anahtar: Coz(p.Id), Alan: p.ToAlanM2()))
                .OrderBy(x => x.Anahtar.Malik, StringComparer.Ordinal).ThenBy(x => x.Anahtar.Kadastro, StringComparer.Ordinal)
                .Select(x => new KrokiEslesmeGirdisi(x.Anahtar.Malik, x.Anahtar.Kadastro, imar.GeciciKod, x.Alan)));
        }

        return Result<KrokiEslestirmeSonucu>.Success(new KrokiEslestirmeSonucu(
            satirlar.AsReadOnly(), poligonlar.Count, cozulemeyen, Farklar(satirlar, girdi), uyarilar.AsReadOnly()));
    }

    /// <summary>Poligonun etiket adayları: kendi etiketi, sonra içinde kalan yazılar (konum sırasıyla).</summary>
    private static IEnumerable<string> Etiketler(CadPolyline p, IReadOnlyList<CadText> yazilar)
    {
        if (!string.IsNullOrWhiteSpace(p.Label))
        {
            yield return p.Label;
        }

        foreach (var y in (yazilar ?? []).Where(y => !string.IsNullOrWhiteSpace(y.Text) && CadLabelMatcher.StrictlyInside(p.Vertices, y.Position))
                     .OrderByDescending(y => y.Position.X).ThenBy(y => y.Position.Y))
        {
            yield return y.Text;
        }
    }

    /// <summary>Etiketi malik koduna çözer: TAKBİS kodu → kişi no → normalize ad (tam, sonra ≥ 5 karakterlik tekil önek). SAHA-015 kroki parselasyonu da kullanır.</summary>
    internal static string? Coz(string etiket, IReadOnlyList<string> kodlar, MalikRehberi rehber)
    {
        var t = etiket.Trim();
        if (t.Length == 0)
        {
            return null;
        }

        // SAHA-011: TAKBİS kimliği (ad etiketi veya eski kod) yazım farkından bağımsız (Türkçe büyük harf) eşleşir.
        var anahtar = MalikEtiketi.Anahtar(t);
        var kod = kodlar.FirstOrDefault(k => string.Equals(MalikEtiketi.Anahtar(k), anahtar, StringComparison.Ordinal));
        if (kod is not null)
        {
            return kod;
        }

        IEnumerable<MalikRehberKaydi> adaylar = rehber.Malikler.Where(r => kodlar.Contains(r.MalikKodu, StringComparer.Ordinal));
        if (t.All(char.IsAsciiDigit))
        {
            return Tek(adaylar.Where(r => string.Equals(r.KisiSistemNo, t, StringComparison.Ordinal)));
        }

        var ad = WebtapuAktarici.Normalize(t);
        return Tek(adaylar.Where(r => string.Equals(WebtapuAktarici.Normalize(r.AdSoyad), ad, StringComparison.Ordinal)))
            ?? (ad.Length >= 5 ? Tek(adaylar.Where(r => WebtapuAktarici.Normalize(r.AdSoyad).StartsWith(ad, StringComparison.Ordinal))) : null)
            ?? EtikettenCoz(ad, kodlar);
    }

    /// <summary>
    /// SAHA-011: rehbersiz (elle yazılmış) <c>AD SOYAD (BABA ADI)</c> TAKBİS etiketlerinin ad soyad kısmıyla tam, sonra ≥ 5 karakterlik
    /// tekil önekle eşleşme.
    /// </summary>
    private static string? EtikettenCoz(string ad, IReadOnlyList<string> kodlar)
    {
        var adlar = kodlar.Select(k => (Kod: k, Ad: WebtapuAktarici.Normalize(MalikEtiketi.Ayristir(k).AdSoyad))).ToList();
        return TekKod(adlar.Where(a => string.Equals(a.Ad, ad, StringComparison.Ordinal)).Select(a => a.Kod))
            ?? (ad.Length >= 5 ? TekKod(adlar.Where(a => a.Ad.StartsWith(ad, StringComparison.Ordinal)).Select(a => a.Kod)) : null);
    }

    private static string? TekKod(IEnumerable<string> adaylar)
    {
        var liste = adaylar.Take(2).ToList();
        return liste.Count == 1 ? liste[0] : null;
    }

    private static string? Tek(IEnumerable<MalikRehberKaydi> adaylar)
    {
        var liste = adaylar.Take(2).ToList();
        return liste.Count == 1 ? liste[0].MalikKodu : null;
    }

    private static IReadOnlyList<KrokiFarki> Farklar(List<KrokiEslesmeGirdisi> satirlar, KrokiEslestirmeGirdisi girdi)
    {
        var sonuc = new List<KrokiFarki>();
        foreach (var k in girdi.Takbis.OrderBy(k => k.ParselId, StringComparer.Ordinal))
        {
            foreach (var m in k.Malikler.OrderBy(m => m.MalikId, StringComparer.Ordinal))
            {
                var kroki = satirlar.Where(s => s.MalikKodu == m.MalikId && s.KadastroParselKimligi == k.ParselId).Sum(s => s.AlanM2);
                decimal? net = null;
                if (girdi.Dopo is { } dopo)
                {
                    // HakEslestirmeServisi ile aynı: giren = round(tapu × hisse), DOP = round(giren × DOPO), net = giren − DOP.
                    var giren = decimal.Round((Fraction.FromDecimal(k.TapuAlaniM2) * m.Hisse).ToDecimal(6), 2, MidpointRounding.AwayFromZero);
                    net = giren - decimal.Round(giren * dopo, 2, MidpointRounding.AwayFromZero);
                }

                sonuc.Add(new KrokiFarki(m.MalikId, k.ParselId, kroki, net));
            }
        }

        return sonuc.AsReadOnly();
    }

    private static void Ekle(Dictionary<(string, string), decimal> d, (string, string) anahtar, decimal deger) =>
        d[anahtar] = d.GetValueOrDefault(anahtar) + deger;

    private static string Kimlik((string Malik, string Kadastro) a) => a.Malik + "\u001F" + a.Kadastro;

    private static (string Malik, string Kadastro) Coz(string kimlik)
    {
        var i = kimlik.IndexOf('\u001F', StringComparison.Ordinal);
        return (kimlik[..i], kimlik[(i + 1)..]);
    }

    private static Result<KrokiEslestirmeSonucu> Gecersiz(string neden)
    {
        var e = ErrorCatalog.Get(ErrorCodes.TapuDagitimGirdisiGecersiz);
        return Result<KrokiEslestirmeSonucu>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", neden, StringComparison.Ordinal) });
    }
}

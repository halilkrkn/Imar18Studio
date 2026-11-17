using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Domain.Revizyon;

namespace Imar18.Application.Onay;

/// <summary>Geçerli parsel halkası ve Decimal Gauss alanı (m², 2 ondalık AwayFromZero).</summary>
public sealed record DegerlendirilenParsel(ParselGeometrisi Geometri, PolygonRing Halka, decimal AlanM2);

/// <summary>
/// Onay öncesi hesap değerlendirmesi (spec 11.2 madde 1–3). <see cref="EksikGirdiler"/> boş değilse hesap tamamlanmamıştır
/// (onay/çıktı <c>ERR-CIKTI-006</c>); <see cref="KritikVar"/> ise onay engellenir (<c>ERR-CIKTI-004</c>, AC-PILOT-05).
/// </summary>
public sealed record ProjeDegerlendirmesi(
    RevisionId Revizyon,
    IReadOnlyList<DegerlendirilenParsel> Parseller,
    TecvizDegerlendirmesi? Tecviz,
    YolTerkDengelemeSonucu? Dengeleme,
    DopSonucu? Dop,
    HakEslestirmeSonucu? Hak,
    IReadOnlyList<AppError> Bulgular,
    IReadOnlyList<string> EksikGirdiler)
{
    public bool Tamamlandi => EksikGirdiler.Count == 0;

    public int KritikSayisi => Bulgular.Count(b => b.Severity == ErrorSeverity.Critical);

    public bool KritikVar => KritikSayisi > 0;

    /// <summary>DOP R1 önerisi: Σ tecviz tapu alanı (tecviz değerlendirilemediyse null).</summary>
    public decimal? R1TapuToplamiM2 => Tecviz?.ParselSonuclari.Sum(s => s.TapuAlaniM2);

    /// <summary>DOP R2 önerisi: dengelenmiş imar toplamı; dengeleme yoksa imar parsellerinin Gauss toplamı (yoksa null).</summary>
    public decimal? OnerilenR2M2 => Dengeleme?.ImarParselToplamiM2
        ?? (Parseller.Any(p => p.Geometri.Rol == ParselRolu.Imar) ? Parseller.Where(p => p.Geometri.Rol == ParselRolu.Imar).Sum(p => p.AlanM2) : null);

    /// <summary>Hak eşleştirme girdisinin imar parselleri: dengelenmiş alanlar (dengeleme yoksa Gauss alanları).</summary>
    public IReadOnlyList<ImarParseli> HakIcinImarParselleri => Dengeleme is { } d
        ? d.ImarParselleri.Select(p => new ImarParseli(p.Id, p.DengelenmisAlanM2)).ToList()
        : Parseller.Where(p => p.Geometri.Rol == ParselRolu.Imar).Select(p => new ImarParseli(p.Geometri.ParselId, p.AlanM2)).ToList();
}

/// <summary>
/// Proje belgesinden Domain hesaplarını deterministik olarak çalıştırır: geometri (PolygonRing + Gauss), tecviz (TecvizChecker),
/// Y1 dengelemesi (tek yol terk parseli varsa ve tecviz geçtiyse), DOP (DopCalculator; dengeleme imar toplamı R2 farkı için) ve
/// hak eşleştirme (HakEslestirmeServisi). Hesap üretemeyen (Error) sonuçlar eksik girdi sayılır; başarılı hesapların bulguları
/// (Warning/Critical) listelenir. Ek tutarlılık: hak eşleştirmedeki DOPO, DOP sonucunun 7 haneli DOPO'suna eşit olmalıdır.
/// P6: geçerli halkalar parseller arası topoloji analizinden (<see cref="IParselTopolojiDenetcisi"/>, NTS overlay GeometryEngine'de)
/// geçer; çakışma (<c>ERR-TOPO-005</c>), boşluk (<c>ERR-TOPO-006</c>) ve kadastro dışına taşma (<c>ERR-TOPO-007</c>) Critical'dır
/// (onay engeli, AC-PILOT-05); analiz tamamlanamazsa "topoloji analizi" eksik sayılır (<c>ERR-TOPO-008</c>).
/// </summary>
public static class ProjeDegerlendirici
{
    /// <summary>Hata mesajında listelenecek en fazla parsel kimliği (uzun boşluk/taşma listeleri kısaltılır).</summary>
    private const int MesajdakiEnFazlaKimlik = 10;

    public static ProjeDegerlendirmesi Degerlendir(ProjeBelgesi belge, IParselTopolojiDenetcisi topoloji)
    {
        ArgumentNullException.ThrowIfNull(belge);
        ArgumentNullException.ThrowIfNull(topoloji);
        var bulgular = new List<AppError>();
        var eksik = new List<string>();

        if (belge.Crs is null)
        {
            eksik.Add("CRS");
        }
        else
        {
            bulgular.AddRange(belge.Crs.Uyarilar);
        }

        var parseller = new List<DegerlendirilenParsel>();
        var topolojiGirdisi = new List<ParselGeometrisi>();
        foreach (var p in belge.Parseller)
        {
            var halka = PolygonRing.FromOpen(p.Koseler);
            if (halka.IsFailure)
            {
                Ekle(halka.Error);
                continue;
            }

            if (PolygonTopolojisi.KendiniKesiyorMu(halka.Value))
            {
                var e = ErrorCatalog.Get(ErrorCodes.TopoKendiUzerindeKesisim);
                bulgular.Add(e with { TechnicalMessage = e.TechnicalMessage.Replace("{parcelId}", p.ParselId, StringComparison.Ordinal) });
            }
            else
            {
                topolojiGirdisi.Add(p);
            }

            parseller.Add(new DegerlendirilenParsel(p, halka.Value, GaussAreaCalculator.Compute(halka.Value).AreaM2));
        }

        IReadOnlyList<ParselTopolojiBulgusu> topolojiSonucu;
        try
        {
            topolojiSonucu = topoloji.Denetle(topolojiGirdisi);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail-safe (reviewer M3): port uygulamasının beklenmeyen istisnası onay komutunu çökertmez; hesap eksik sayılır.
            topolojiSonucu = [new ParselTopolojiBulgusu(TopolojiBulguTuru.AnalizYapilamadi, [], 0m)];
        }

        Topoloji(topolojiSonucu, bulgular, Eksik);

        if (belge.Parseller.Count == 0)
        {
            eksik.Add("geometri");
        }
        else
        {
            if (!parseller.Any(p => p.Geometri.Rol == ParselRolu.Kadastro))
            {
                eksik.Add("kadastro parseli geometrisi");
            }

            if (!parseller.Any(p => p.Geometri.Rol == ParselRolu.Imar))
            {
                eksik.Add("imar parseli geometrisi");
            }
        }

        TecvizDegerlendirmesi? tecviz = null;
        if (belge.TecvizParselleri.Count == 0)
        {
            eksik.Add("tecviz");
        }
        else
        {
            var t = TecvizChecker.Degerlendir(belge.TecvizParselleri, belge.DuzenlemeToplamTecvizSiniriM2);
            if (t.IsFailure)
            {
                Eksik("tecviz", t.Error);
            }
            else
            {
                tecviz = t.Value;
                bulgular.AddRange(tecviz.Bulgular);
            }
        }

        YolTerkDengelemeSonucu? dengeleme = null;
        var yollar = parseller.Where(p => p.Geometri.Rol == ParselRolu.YolTerk).ToArray();
        if (yollar.Length > 1)
        {
            Eksik("dengeleme", Gecersiz("more than one road dedication parcel; Y1 balancing needs exactly one"));
        }
        else if (yollar.Length == 1 && tecviz is { Gecti: true })
        {
            static DengelemeKalemi Kalem(DegerlendirilenParsel p) => new(p.Geometri.ParselId, p.AlanM2);
            var d = YolTerkDengelemesi.Dengele(
                parseller.Where(p => p.Geometri.Rol == ParselRolu.Imar).Select(Kalem).ToArray(),
                Kalem(yollar[0]),
                parseller.Where(p => p.Geometri.Rol == ParselRolu.KamuAlani).Select(Kalem).ToArray(),
                tecviz);
            if (d.IsFailure)
            {
                Eksik("dengeleme", d.Error);
            }
            else
            {
                dengeleme = d.Value;
            }
        }

        DopSonucu? dop = null;
        if (belge.Dop is null)
        {
            eksik.Add("DOP");
        }
        else
        {
            var d = DopCalculator.Hesapla(belge.Dop, dengeleme?.ImarParselToplamiM2);
            if (d.IsFailure)
            {
                Eksik("DOP", d.Error);
            }
            else
            {
                dop = d.Value;
                bulgular.AddRange(dop.Bulgular);
            }
        }

        HakEslestirmeSonucu? hak = null;
        if (belge.HakEslestirme is null)
        {
            eksik.Add("hak eşleştirme");
        }
        else
        {
            var h = HakEslestirmeServisi.Eslestir(belge.HakEslestirme);
            if (h.IsFailure)
            {
                Eksik("hak eşleştirme", h.Error);
            }
            else
            {
                hak = h.Value;
                bulgular.AddRange(hak.Bulgular);
            }

            if (dop is not null && belge.HakEslestirme.Dopo != dop.Dopo)
            {
                Eksik("hak eşleştirme DOPO", Gecersiz("allocation DOPO differs from DOP calculation result"));
            }
        }

        AlanTutarliligi(belge, parseller, dengeleme, Eksik);

        return new ProjeDegerlendirmesi(belge.RevizyonKimligi(), parseller, tecviz, dengeleme, dop, hak, bulgular, eksik);

        void Ekle(AppError e)
        {
            bulgular.Add(e);
            if (e.Severity != ErrorSeverity.Critical && !eksik.Contains("geometri"))
            {
                eksik.Add("geometri");
            }
        }

        void Eksik(string ad, AppError e)
        {
            bulgular.Add(e);
            if (!eksik.Contains(ad))
            {
                eksik.Add(ad);
            }
        }
    }

    /// <summary>
    /// Alan çapraz tutarlılığı (P5 reviewer H3): aynı parsel cetvellerde tek alanla görünmelidir. (1) Tecviz hesap alanı =
    /// aynı kimlikli kadastro geometrisinin Gauss alanı; (2) hak eşleştirme tapu alanı = tecviz tapu alanı; (3) hak eşleştirme
    /// imar parseli alanı = dengelenmiş alan (dengeleme yoksa Gauss alanı) ve geometrisi bulunmalıdır. Aksi "alan tutarlılığı" eksik sayılır.
    /// </summary>
    private static void AlanTutarliligi(
        ProjeBelgesi belge,
        IReadOnlyList<DegerlendirilenParsel> parseller,
        YolTerkDengelemeSonucu? dengeleme,
        Action<string, AppError> eksik)
    {
        const string Ad = "alan tutarlılığı";
        var gauss = parseller.GroupBy(p => p.Geometri.ParselId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var dengelenmis = dengeleme?.ImarParselleri.ToDictionary(k => k.Id, k => k.DengelenmisAlanM2, StringComparer.Ordinal);

        foreach (var t in belge.TecvizParselleri)
        {
            if (gauss.TryGetValue(t.ParselId, out var g) && g.Geometri.Rol == ParselRolu.Kadastro && g.AlanM2 != t.HesapAlaniM2)
            {
                eksik(Ad, Gecersiz($"tecviz computed area differs from cadastral Gauss area for {t.ParselId}"));
            }
        }

        if (belge.HakEslestirme is not { } hak)
        {
            return;
        }

        var tapu = belge.TecvizParselleri.GroupBy(t => t.ParselId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().TapuAlaniM2, StringComparer.Ordinal);
        foreach (var k in hak.KadastroParselleri)
        {
            if (k is not null && tapu.TryGetValue(k.ParselId, out var tecvizTapu) && tecvizTapu != k.TapuAlaniM2)
            {
                eksik(Ad, Gecersiz($"allocation title area differs from tecviz title area for {k.ParselId}"));
            }
        }

        foreach (var i in hak.ImarParselleri)
        {
            if (i is null)
            {
                continue;
            }

            if (!gauss.TryGetValue(i.GeciciKod, out var g) || g.Geometri.Rol != ParselRolu.Imar)
            {
                eksik(Ad, Gecersiz($"allocation zoning parcel {i.GeciciKod} has no zoning geometry"));
                continue;
            }

            var beklenen = dengelenmis is not null && dengelenmis.TryGetValue(i.GeciciKod, out var d) ? d : g.AlanM2;
            if (beklenen != i.AlanM2)
            {
                eksik(Ad, Gecersiz($"allocation zoning area differs from balanced geometry area for {i.GeciciKod}"));
            }
        }
    }

    private static void Topoloji(IReadOnlyList<ParselTopolojiBulgusu> sonuc, List<AppError> bulgular, Action<string, AppError> eksik)
    {
        foreach (var b in sonuc)
        {
            var (kod, tur) = b.Tur switch
            {
                TopolojiBulguTuru.KadastroCakismasi => (ErrorCodes.TopoParselCakismasi, "cadastral parcels"),
                TopolojiBulguTuru.OlusanCakismasi => (ErrorCodes.TopoParselCakismasi, "new parcels"),
                TopolojiBulguTuru.KadastroBoslugu => (ErrorCodes.TopoParselBoslugu, "hole inside cadastral parcels"),
                TopolojiBulguTuru.OlusanKapsamaBoslugu => (ErrorCodes.TopoParselBoslugu, "readjustment area not covered by new parcels"),
                TopolojiBulguTuru.KadastroDisinaTasma => (ErrorCodes.TopoKadastroDisinaTasma, "outside"),
                _ => (ErrorCodes.TopoAnalizYapilamadi, "analysis"),
            };

            var e = ErrorCatalog.Get(kod);
            var idler = b.ParselIdleri.Count <= MesajdakiEnFazlaKimlik
                ? string.Join(", ", b.ParselIdleri)
                : string.Join(", ", b.ParselIdleri.Take(MesajdakiEnFazlaKimlik)) + $" (+{b.ParselIdleri.Count - MesajdakiEnFazlaKimlik})";
            e = e with
            {
                TechnicalMessage = e.TechnicalMessage
                    .Replace("{parcelIds}", idler, StringComparison.Ordinal)
                    .Replace("{area}", b.AlanM2.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    .Replace("{kind}", tur, StringComparison.Ordinal)
                    .Replace("{reason}", b.ParselIdleri.Count > 0 ? "invalid polygon for parcels " + idler : "geometry engine overlay failed", StringComparison.Ordinal),
            };

            if (b.Tur == TopolojiBulguTuru.AnalizYapilamadi)
            {
                eksik("topoloji analizi", e);
            }
            else
            {
                bulgular.Add(e);
            }
        }
    }

    private static AppError Gecersiz(string neden)
    {
        var e = ErrorCatalog.Get(ErrorCodes.GirdProjeGirdisiGecersiz);
        return e with { TechnicalMessage = e.TechnicalMessage.Replace("{command}", "evaluation", StringComparison.Ordinal).Replace("{reason}", neden, StringComparison.Ordinal) };
    }
}

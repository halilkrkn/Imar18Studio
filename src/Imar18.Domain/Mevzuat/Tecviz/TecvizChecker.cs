using Imar18.Domain.Diagnostics;

namespace Imar18.Domain.Mevzuat.Tecviz;

/// <summary>Kadastro parseli tecviz girdisi: tapu alanı, Gauss hesap alanı ve mühendis tecviz kaydı (eksikse null).</summary>
public sealed record TecvizParselGirdisi(string ParselId, decimal TapuAlaniM2, decimal HesapAlaniM2, TecvizGirdisi? Tecviz);

/// <summary>
/// Tek kontrol sonucu. <see cref="FarkM2"/> = Tapu − Hesap (işaretli; Y1 dengeleme düzeltmesi, spec 25.4),
/// <see cref="Gecti"/> = |Fark| &lt;= Sınır.
/// </summary>
public sealed record TecvizKontrolSonucu(
    string KontrolId,
    decimal TapuAlaniM2,
    decimal HesapAlaniM2,
    decimal FarkM2,
    decimal MutlakFarkM2,
    decimal SinirM2,
    bool Gecti);

/// <summary>
/// Düzenleme tecviz değerlendirmesi. Parsel bazlı kontrol esastır; toplam sınır da girilmişse çift-geçiş şartı uygulanır
/// (SORU-013/016 kapandı). <see cref="TekMesaj"/>: iki kontrol aynı sonucu verdiyse (veya toplam kontrol yoksa) true.
/// </summary>
public sealed record TecvizDegerlendirmesi(
    IReadOnlyList<TecvizKontrolSonucu> ParselSonuclari,
    TecvizKontrolSonucu? ToplamSonucu,
    bool ParselBazliGecti,
    IReadOnlyList<AppError> Bulgular)
{
    /// <summary>Tüm parsel kontrolleri geçti ve (toplam sınır girildiyse) toplam kontrol de geçti.</summary>
    public bool Gecti => ParselBazliGecti && (ToplamSonucu is null || ToplamSonucu.Gecti);

    /// <summary>Parsel bazlı ve toplam bazlı sonuçlar aynı mı (spec 4.3.1: aynıysa tek mesaj).</summary>
    public bool TekMesaj => ToplamSonucu is null || ToplamSonucu.Gecti == ParselBazliGecti;

    /// <summary>Onayı engelleyen (Critical) bulgu var mı (AC-PILOT-05).</summary>
    public bool KritikVar => Bulgular.Any(b => b.Severity == ErrorSeverity.Critical);
}

/// <summary>
/// TecvizChecker (FR-F1.4 / TC-TECVIZ-001, spec 4.3.1): sabit formül hesaplamaz; yalnızca
/// |GaussAlanı − TapuAlanı| &lt;= TecvizSiniriM2 kontrolünü <see cref="decimal"/> ile yapar.
/// Sonuçlar parsel kimliğine göre <see cref="StringComparer.Ordinal"/> sıralıdır (girdi sırasından bağımsız).
/// </summary>
public static class TecvizChecker
{
    /// <summary>Kontrolleri çalıştırır.</summary>
    /// <param name="parseller">Kadastro parselleri (en az 1, kimlikler benzersiz, alanlar &gt; 0).</param>
    /// <param name="duzenlemeToplamTecvizSiniriM2">Düzenleme toplamı tecviz sınırı (Netcad TECVİZ satırı, mühendis girdisi); girilmediyse null.</param>
    /// <returns>Girdi geçersizse <see cref="ErrorCodes.TecvizGirdisiGecersiz"/>; aksi halde değerlendirme ve bulgular.</returns>
    public static Result<TecvizDegerlendirmesi> Degerlendir(
        IReadOnlyCollection<TecvizParselGirdisi> parseller,
        decimal? duzenlemeToplamTecvizSiniriM2)
    {
        ArgumentNullException.ThrowIfNull(parseller);
        var items = parseller.ToArray();

        if (items.Length == 0)
        {
            return Gecersiz("no parcels supplied");
        }

        if (duzenlemeToplamTecvizSiniriM2 is <= 0m)
        {
            return Gecersiz("total tecviz limit must be positive");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Length; i++)
        {
            var p = items[i];
            if (p is null || string.IsNullOrWhiteSpace(p.ParselId))
            {
                return Gecersiz($"empty parcel id at item #{i + 1}");
            }

            if (!ids.Add(p.ParselId))
            {
                return Gecersiz($"duplicate parcel id at item #{i + 1}");
            }

            if (p.TapuAlaniM2 <= 0m || p.HesapAlaniM2 <= 0m)
            {
                return Gecersiz($"non-positive area at item #{i + 1}");
            }
        }

        var sirali = items.OrderBy(p => p.ParselId, StringComparer.Ordinal).ToArray();
        var bulgular = new List<AppError>();
        var parselSonuclari = new List<TecvizKontrolSonucu>();
        var parselBazliGecti = true;
        var girdiEksik = false;

        foreach (var p in sirali)
        {
            if (p.Tecviz is null)
            {
                parselBazliGecti = false;
                girdiEksik = true;
                bulgular.Add(HataSablonu.Doldur(ErrorCodes.TecvizGirdisiEksik, ("parcelId", p.ParselId)));
                continue;
            }

            var sonuc = Kontrol(p.ParselId, p.TapuAlaniM2, p.HesapAlaniM2, p.Tecviz.TecvizSiniriM2);
            parselSonuclari.Add(sonuc);
            if (!sonuc.Gecti)
            {
                parselBazliGecti = false;
                bulgular.Add(AsimHatasi(sonuc));
            }
        }

        TecvizKontrolSonucu? toplam = null;
        if (duzenlemeToplamTecvizSiniriM2 is { } toplamSinir)
        {
            toplam = Kontrol(
                "duzenleme-toplami",
                sirali.Sum(p => p.TapuAlaniM2),
                sirali.Sum(p => p.HesapAlaniM2),
                toplamSinir);

            if (!toplam.Gecti)
            {
                bulgular.Add(AsimHatasi(toplam));
            }

            // Girdi eksikse karşılaştırma anlamlı değildir (eksiklik ERR-TECVIZ-003 ile raporlanır).
            if (!girdiEksik && toplam.Gecti != parselBazliGecti)
            {
                bulgular.Add(HataSablonu.Doldur(ErrorCodes.TecvizParselToplamSonucuFarkli, ("regulationId", "duzenleme")));
            }
        }

        return Result<TecvizDegerlendirmesi>.Success(
            new TecvizDegerlendirmesi(parselSonuclari, toplam, parselBazliGecti, bulgular));
    }

    private static TecvizKontrolSonucu Kontrol(string id, decimal tapu, decimal hesap, decimal sinir)
    {
        var fark = tapu - hesap;
        var mutlak = Math.Abs(fark);
        return new TecvizKontrolSonucu(id, tapu, hesap, fark, mutlak, sinir, mutlak <= sinir);
    }

    private static AppError AsimHatasi(TecvizKontrolSonucu s) =>
        HataSablonu.Doldur(ErrorCodes.TecvizSiniriAsildi, ("delta", HataSablonu.M2(s.MutlakFarkM2)), ("limit", HataSablonu.M2(s.SinirM2)), ("parcelId", s.KontrolId));

    private static Result<TecvizDegerlendirmesi> Gecersiz(string neden) =>
        HataSablonu.Basarisiz<TecvizDegerlendirmesi>(ErrorCodes.TecvizGirdisiGecersiz, ("reason", neden));
}

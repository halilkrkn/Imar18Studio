using Imar18.Domain.Diagnostics;
using Imar18.Domain.Numerics;

namespace Imar18.Domain.Mevzuat.Dop;

/// <summary>
/// Resmî DOP sayfası girdisi (spec 25.1): R1 düzenlemeye giren, D1 eski DOPO &gt; proje DOPO alanlar, R2 imar alanları
/// (yalnız imar parselleri; PARK/yol/dini tesis girmez), D2 eski DOPO &gt; proje DOPO tahsisler, R3 DOP alınmayan alanlar,
/// ortak katılım (OKO payı). Pilot altın verisinde D1 = D2 = R3 = 0 (SORU-008: sıfırdan farklı değerler sentetik test edilir).
/// </summary>
public sealed record DopGirdisi
{
    private DopGirdisi(decimal r1, decimal d1, decimal r2, decimal d2, decimal r3, decimal ortakKatilimM2)
    {
        R1 = r1;
        D1 = d1;
        R2 = r2;
        D2 = d2;
        R3 = r3;
        OrtakKatilimM2 = ortakKatilimM2;
    }

    public decimal R1 { get; }

    public decimal D1 { get; }

    public decimal R2 { get; }

    public decimal D2 { get; }

    public decimal R3 { get; }

    public decimal OrtakKatilimM2 { get; }

    /// <summary>
    /// Doğrular: tüm alanlar &gt;= 0, R1 − D1 &gt; 0, D2 &lt;= R2, R3 &lt;= R1 − D1, ortak katılım &lt;= R1 − D1 ve DOP miktarı (pay) &gt;= 0.
    /// Hata durumunda <see cref="ErrorCodes.DopGirdisiGecersiz"/>.
    /// </summary>
    public static Result<DopGirdisi> Olustur(
        decimal r1,
        decimal r2,
        decimal d1 = 0m,
        decimal d2 = 0m,
        decimal r3 = 0m,
        decimal ortakKatilimM2 = 0m)
    {
        if (r1 < 0m || r2 < 0m || d1 < 0m || d2 < 0m || r3 < 0m || ortakKatilimM2 < 0m)
        {
            return Gecersiz("areas must not be negative");
        }

        var payda = r1 - d1;
        if (payda <= 0m)
        {
            return Gecersiz("R1 - D1 must be positive");
        }

        if (d2 > r2)
        {
            return Gecersiz("D2 must not exceed R2");
        }

        if (r3 > payda)
        {
            return Gecersiz("R3 must not exceed R1 - D1");
        }

        if (ortakKatilimM2 > payda)
        {
            return Gecersiz("common participation must not exceed R1 - D1");
        }

        if (payda - r2 + d2 + r3 < 0m)
        {
            return Gecersiz("DOP amount (R1 - D1 - R2 + D2 + R3) is negative");
        }

        return Result<DopGirdisi>.Success(new DopGirdisi(r1, d1, r2, d2, r3, ortakKatilimM2));
    }

    private static Result<DopGirdisi> Gecersiz(string neden) =>
        HataSablonu.Basarisiz<DopGirdisi>(ErrorCodes.DopGirdisiGecersiz, ("reason", neden));
}

/// <summary>DOP hesap sonucu. <see cref="Dopo"/> ve <see cref="Oko"/> 7 ondalık AwayFromZero; <see cref="DopoTam"/> tam kesir.</summary>
public sealed record DopSonucu(
    DopGirdisi Girdi,
    Fraction DopoTam,
    decimal Dopo,
    decimal Oko,
    decimal DopMiktariM2,
    decimal KamuyaAyrilanAlanM2,
    bool TavanAsildi,
    decimal AsimM2,
    decimal R2DengelemeFarkiM2,
    IReadOnlyList<AppError> Bulgular)
{
    /// <summary>Onayı engelleyen (Critical) bulgu var mı (AC-PILOT-05).</summary>
    public bool KritikVar => Bulgular.Any(b => b.Severity == ErrorSeverity.Critical);
}

/// <summary>%45 aşımını karşılama kaynakları, spec 4.4 sırasıyla.</summary>
public enum DopAsimKaynagi
{
    TescilHarici,
    BelediyeMulkiyeti,
    MuvafakatliKamuTasinmazi,
    Kamulastirma,
}

/// <summary>Kaynaklarda kullanılabilir alanlar (m², mühendis girdisi).</summary>
public sealed record DopAsimKaynaklari(
    decimal TescilHariciM2,
    decimal BelediyeMulkiyetiM2,
    decimal MuvafakatliKamuTasinmaziM2,
    decimal KamulastirmaM2);

/// <summary>Bir kaynaktan kullanılan alan.</summary>
public sealed record DopAsimKalemi(DopAsimKaynagi Kaynak, decimal MevcutM2, decimal KullanilanM2);

/// <summary>Aşım karşılama planı; karşılanamayan kalan varsa ERR-DOP-005.</summary>
public sealed record DopAsimKarsilamaPlani(
    decimal AsimM2,
    IReadOnlyList<DopAsimKalemi> Kalemler,
    decimal KarsilanamayanM2,
    IReadOnlyList<AppError> Bulgular);

/// <summary>
/// DopCalculator (spec 25.1 / D31 Kural 1, spec 4.4): DOPO = (R1 − D1 − R2 + D2 + R3) / (R1 − D1), OKO = ortak katılım / (R1 − D1).
/// Kamuya ayrılan alan = R1 − R2 TÜRETİLİR. Tavan kontrolü tam kesir üzerinden yapılır (yuvarlanmış 7 hane değil — SORU-031).
/// Dengeleme cetveli imar parsel toplamı R2'den farklıysa DOP sayfası referans alınır ve fark uyarıdır, dağıtılmaz (SORU-014).
/// </summary>
public static class DopCalculator
{
    /// <summary>Yasal üst sınır %45 (spec 4.4).</summary>
    public const decimal YasalUstSinir = 0.45m;

    /// <summary>DOPO/OKO ondalık basamak sayısı (spec 25.1, 7 hane).</summary>
    public const int OranOndalik = 7;

    public static Result<DopSonucu> Hesapla(DopGirdisi girdi, decimal? dengelemeImarParselToplamiM2 = null)
    {
        ArgumentNullException.ThrowIfNull(girdi);

        if (dengelemeImarParselToplamiM2 is < 0m)
        {
            return HataSablonu.Basarisiz<DopSonucu>(
                ErrorCodes.DopGirdisiGecersiz, ("reason", "balancing parcel total is negative"));
        }

        var payda = girdi.R1 - girdi.D1;
        var dopMiktari = payda - girdi.R2 + girdi.D2 + girdi.R3;
        var dopoTam = Fraction.FromDecimal(dopMiktari) / Fraction.FromDecimal(payda);
        var okoTam = Fraction.FromDecimal(girdi.OrtakKatilimM2) / Fraction.FromDecimal(payda);
        var tavanAsildi = dopoTam > Fraction.FromDecimal(YasalUstSinir);
        // Aşım m² kapıda 2 ondalık AwayFromZero (1 dm²) yuvarlanır.
        var asim = tavanAsildi ? Math.Round(dopMiktari - (YasalUstSinir * payda), 2, MidpointRounding.AwayFromZero) : 0m;

        var bulgular = new List<AppError>();
        if (tavanAsildi)
        {
            bulgular.Add(HataSablonu.Doldur(ErrorCodes.DopYuzde45Asildi, ("dopo", dopoTam.ToDecimal(OranOndalik).ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        var r2Farki = dengelemeImarParselToplamiM2 is { } dengeleme ? dengeleme - girdi.R2 : 0m;
        if (r2Farki != 0m)
        {
            bulgular.Add(HataSablonu.Doldur(ErrorCodes.DopR2DengelemeFarki, ("delta", HataSablonu.M2(r2Farki))));
        }

        return Result<DopSonucu>.Success(new DopSonucu(
            girdi,
            dopoTam,
            dopoTam.ToDecimal(OranOndalik),
            okoTam.ToDecimal(OranOndalik),
            dopMiktari,
            girdi.R1 - girdi.R2,
            tavanAsildi,
            asim,
            r2Farki,
            bulgular));
    }

    /// <summary>
    /// %45'i aşan DOP miktarını spec 4.4 sırasıyla (tescil harici → belediye mülkiyeti → muvafakatli kamu taşınmazı → kamulaştırma)
    /// açgözlü biçimde karşılar. Kaynak negatifse <see cref="ErrorCodes.DopGirdisiGecersiz"/>.
    /// </summary>
    public static Result<DopAsimKarsilamaPlani> AsimKarsila(DopSonucu sonuc, DopAsimKaynaklari kaynaklar)
    {
        ArgumentNullException.ThrowIfNull(sonuc);
        ArgumentNullException.ThrowIfNull(kaynaklar);

        (DopAsimKaynagi Kaynak, decimal Mevcut)[] sira =
        [
            (DopAsimKaynagi.TescilHarici, kaynaklar.TescilHariciM2),
            (DopAsimKaynagi.BelediyeMulkiyeti, kaynaklar.BelediyeMulkiyetiM2),
            (DopAsimKaynagi.MuvafakatliKamuTasinmazi, kaynaklar.MuvafakatliKamuTasinmaziM2),
            (DopAsimKaynagi.Kamulastirma, kaynaklar.KamulastirmaM2),
        ];

        if (sira.Any(k => k.Mevcut < 0m))
        {
            return HataSablonu.Basarisiz<DopAsimKarsilamaPlani>(
                ErrorCodes.DopGirdisiGecersiz, ("reason", "excess source area is negative"));
        }

        var kalan = sonuc.AsimM2;
        var kalemler = new List<DopAsimKalemi>(sira.Length);
        foreach (var (kaynak, mevcut) in sira)
        {
            var kullanilan = Math.Min(kalan, mevcut);
            kalemler.Add(new DopAsimKalemi(kaynak, mevcut, kullanilan));
            kalan -= kullanilan;
        }

        var bulgular = new List<AppError>();
        if (kalan > 0m)
        {
            bulgular.Add(HataSablonu.Doldur(
                ErrorCodes.DopAsimKarsilanamadi, ("excess", HataSablonu.M2(sonuc.AsimM2)), ("shortfall", HataSablonu.M2(kalan))));
        }

        return Result<DopAsimKarsilamaPlani>.Success(new DopAsimKarsilamaPlani(sonuc.AsimM2, kalemler, kalan, bulgular));
    }
}

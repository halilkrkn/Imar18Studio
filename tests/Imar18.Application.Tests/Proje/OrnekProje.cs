using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Domain.Numerics;

namespace Imar18.Application.Tests.Proje;

/// <summary>
/// Tamamen sentetik, elle doğrulanabilir P5 test projesi (AC-PILOT-08: gerçek veri yok). Kadastro K1 20 m × 10 m = 200.00 m²;
/// imar A1 12 m × 10 m = 120.00 m² (R2), yol Y1 8 m × 10 m = 80.00 m². DOPO = (200 − 120) / 200 = 0.4000000 (&lt; %45).
/// K1'in iki maliki yarı yarıya (giren 100.00, DOP 40.00, net hak 60.00); A1 hisseli: payda 12000 dm², paylar 6000 + 6000.
/// Tapu = hesap = 200.00 → tecviz farkı 0, Y1 düzeltmesi +0.00.
/// </summary>
public static class OrnekProje
{
    public const string Muhendis = "muhendis-01";
    public const decimal X0 = 4_400_000m;
    public const decimal Y0 = 500_000m;

    public static ProjeCrs Crs() => ProjeCrs.Olustur(GeodetikDatum.Itrf96, 33, 2005.00m, 1000).Value;

    public static ParselGeometrisi Dikdortgen(string id, ParselRolu rol, decimal y1, decimal y2, decimal x1 = 0m, decimal x2 = 10m) => new(
        id,
        rol,
        [new GeodeticPoint(X0 + x1, Y0 + y1), new GeodeticPoint(X0 + x1, Y0 + y2), new GeodeticPoint(X0 + x2, Y0 + y2), new GeodeticPoint(X0 + x2, Y0 + y1)]);

    public static IReadOnlyList<ParselGeometrisi> Geometri() =>
    [
        Dikdortgen("K1", ParselRolu.Kadastro, 0m, 20m),
        Dikdortgen("A1", ParselRolu.Imar, 0m, 12m),
        Dikdortgen("Y1", ParselRolu.YolTerk, 12m, 20m),
    ];

    public static TecvizGirdisi Tecviz() =>
        TecvizGirdisi.Olustur(1000, "Sayısallaştırma", KoordinatKalitesi.ArsivOnayli, 1.00m, "Sentetik test sınırı (mühendis girdisi)").Value;

    public static IReadOnlyList<TecvizParselGirdisi> TecvizParselleri() => [new TecvizParselGirdisi("K1", 200.00m, 200.00m, Tecviz())];

    public static DopGirdisi Dop() => DopGirdisi.Olustur(200.00m, 120.00m).Value;

    public static HakEslestirmeGirdisi Hak() => new(
        0.4m,
        [new KadastroParseli("K1", 200.00m, [new KadastroMaliki("MLK-001", new Fraction(1, 2), true), new KadastroMaliki("MLK-002", new Fraction(1, 2), false)])],
        [new ImarParseli("A1", 120.00m)],
        [new HakEslestirmeSatiri("MLK-001", "K1", "A1", 60.00m), new HakEslestirmeSatiri("MLK-002", "K1", "A1", 60.00m)]);

    public static IReadOnlyList<ParselKimlikCifti> Kimlikler() => [new ParselKimlikCifti("A1", "101/1"), new ParselKimlikCifti("Y1", "YOL")];

    /// <summary>Hesabı tamamlayan komut dizisi.</summary>
    public static IReadOnlyList<IProjeKomutu> TamKomutlar() =>
    [
        new ProjeCrsAyarlaKomutu(Crs()),
        new ParselGeometrileriAyarlaKomutu(Geometri()),
        new TecvizGirdileriAyarlaKomutu(TecvizParselleri(), null),
        new DopGirdisiAyarlaKomutu(Dop()),
        new HakEslestirmeAyarlaKomutu(Hak()),
        new ParselKimlikEslemesiAyarlaKomutu(Kimlikler()),
    ];

    /// <summary>Bellekte depo + komut yolu; tüm komutlar gönderilmiş hâlde.</summary>
    public static async Task<(ProjeKomutYolu Yol, BellekProjeDeposu Depo, SabitZaman Zaman)> TamProjeAsync()
    {
        var depo = new BellekProjeDeposu();
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(depo, "Sentetik P5 Projesi", zaman)).Value;
        foreach (var k in TamKomutlar())
        {
            var r = await yol.GonderAsync(k, Muhendis);
            if (r.IsFailure)
            {
                throw new InvalidOperationException("Örnek proje komutu reddedildi: " + r.Error.Code + " " + r.Error.TechnicalMessage);
            }
        }

        return (yol, depo, zaman);
    }
}

/// <summary>Elle ilerletilen zaman sağlayıcı.</summary>
public sealed class SabitZaman(DateTimeOffset baslangic) : TimeProvider
{
    public DateTimeOffset Simdi { get; set; } = baslangic;

    public override DateTimeOffset GetUtcNow() => Simdi;

    public void Ilerlet(TimeSpan sure) => Simdi += sure;
}

/// <summary>
/// Bellek içi depo test çifti: her <see cref="KaydetAsync"/> tek atomik commit'tir (durum + tüm denetim kayıtları birlikte).
/// <see cref="SonrakiYazmayiBozdur"/> ile yazma hatası benzetilir.
/// </summary>
public sealed class BellekProjeDeposu : IProjeDeposu
{
    public ProjeKaydi? Kayit { get; private set; }

    public int CommitSayisi { get; private set; }

    public bool SonrakiYazmayiBozdur { get; set; }

    public bool OkumaBozuk { get; set; }

    public Task<bool> VarMiAsync(CancellationToken cancellationToken = default) => Task.FromResult(Kayit is not null || OkumaBozuk);

    public Task<Result<ProjeKaydi>> YukleAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OkumaBozuk || Kayit is null
            ? Result<ProjeKaydi>.Failure(ErrorCatalog.Get(ErrorCodes.CiktiProjeKaydiHatasi))
            : Result<ProjeKaydi>.Success(Kayit));

    public Task<Result<ProjeKaydi>> KaydetAsync(ProjeKaydi kayit, CancellationToken cancellationToken = default)
    {
        if (SonrakiYazmayiBozdur)
        {
            SonrakiYazmayiBozdur = false;
            throw new IOException("Benzetilmiş disk hatası");
        }

        Kayit = kayit;
        CommitSayisi++;
        return Task.FromResult(Result<ProjeKaydi>.Success(kayit));
    }
}

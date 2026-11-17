using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Imar18.Application.Proje;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Domain.Numerics;
using Imar18.TestDestek;

namespace Imar18.Pilot.Tests.Destek;

/// <summary>Düzenlemeye giren kadastro parseli: tapu ve hesap (Gauss) alanı, malikler.</summary>
internal sealed record PilotKadastro(string Id, decimal TapuM2, decimal HesapM2, IReadOnlyList<KadastroMaliki> Malikler);

/// <summary>Oluşan parsel (imar, yol terk veya kamu alanı) ve hesap alanı.</summary>
internal sealed record PilotOlusan(string Id, ParselRolu Rol, decimal HesapM2);

/// <summary>
/// Pilot senaryo projesi kurucusu (P6.1–P6.4). Geometri <b>sentetiktir</b>: her parsel H = 100 m derinlikli, eni alan / 100 m olan
/// dikdörtgen şerittir; kadastro şeritleri ve oluşan şeritler (imar → kamu → yol) aynı düzenleme alanını yan yana tam kaplar
/// (Σ oluşan hesap = Σ kadastro hesap şartı; aksi hâlde topoloji boşluk/taşma bulgusu üretir). Alanlar decimal tam aritmetikle
/// alan değerine birebir eşittir. Gerçek koordinat, malik veya ada/parsel bilgisi içermez (AC-PILOT-08).
/// </summary>
internal sealed class PilotProjesi
{
    public const decimal Derinlik = 100m;
    public const decimal X0 = 4_200_000m;
    public const decimal Y0 = 300_000m;

    public required string Ad { get; init; }

    public required IReadOnlyList<PilotKadastro> Kadastro { get; init; }

    public required IReadOnlyList<PilotOlusan> Olusan { get; init; }

    public required decimal TecvizSiniriM2 { get; init; }

    public decimal? ToplamTecvizSiniriM2 { get; init; }

    /// <summary>Null ise R1 = Σ tapu, R2 = Σ imar hesap alanı (D1 = D2 = R3 = 0).</summary>
    public DopGirdisi? Dop { get; init; }

    /// <summary>Null ise malik → imar parseli satırları açgözlü sentetik eşleştirmeyle üretilir.</summary>
    public IReadOnlyList<HakEslestirmeSatiri>? HakSatirlari { get; init; }

    public IReadOnlyList<ParselKimlikCifti> Kimlikler { get; init; } = [];

    public int OrtaMeridyen { get; init; } = 36;

    public ProjeCrs Crs() => ProjeCrs.Olustur(GeodetikDatum.Itrf96, OrtaMeridyen, 2005.00m, 1000).Value;

    public TecvizGirdisi Tecviz() =>
        TecvizGirdisi.Olustur(1000, "Sayısallaştırma", KoordinatKalitesi.ArsivOnayli, TecvizSiniriM2, "Sentetik pilot (mühendis girdisi benzetimi)").Value;

    public DopGirdisi DopGirdisi() => Dop ?? Domain.Mevzuat.Dop.DopGirdisi.Olustur(
        Kadastro.Sum(k => k.TapuM2), Olusan.Where(o => o.Rol == ParselRolu.Imar).Sum(o => o.HesapM2)).Value;

    public decimal Dopo() => DopCalculator.Hesapla(DopGirdisi()).Value.Dopo;

    public IReadOnlyList<ParselGeometrisi> Geometri()
    {
        var liste = new List<ParselGeometrisi>();
        var y = 0m;
        foreach (var k in Kadastro)
        {
            liste.Add(Serit(k.Id, ParselRolu.Kadastro, y, y += k.HesapM2 / Derinlik));
        }

        y = 0m;
        foreach (var o in Olusan.Where(o => o.Rol == ParselRolu.Imar).Concat(Olusan.Where(o => o.Rol == ParselRolu.KamuAlani))
                     .Concat(Olusan.Where(o => o.Rol == ParselRolu.YolTerk)))
        {
            liste.Add(Serit(o.Id, o.Rol, y, y += o.HesapM2 / Derinlik));
        }

        return liste;
    }

    public HakEslestirmeGirdisi Hak()
    {
        var dopo = Dopo();
        var imar = Olusan.Where(o => o.Rol == ParselRolu.Imar).Select(o => new ImarParseli(o.Id, o.HesapM2)).ToList();
        var kadastro = Kadastro.Select(k => new KadastroParseli(k.Id, k.TapuM2, k.Malikler)).ToList();
        return new HakEslestirmeGirdisi(dopo, kadastro, imar, HakSatirlari ?? Acgozlu(kadastro, imar, dopo));
    }

    public IReadOnlyList<IProjeKomutu> Komutlar() =>
    [
        new ProjeCrsAyarlaKomutu(Crs()),
        new ParselGeometrileriAyarlaKomutu(Geometri()),
        new TecvizGirdileriAyarlaKomutu(Kadastro.Select(k => new TecvizParselGirdisi(k.Id, k.TapuM2, k.HesapM2, Tecviz())).ToList(), ToplamTecvizSiniriM2),
        new DopGirdisiAyarlaKomutu(DopGirdisi()),
        new HakEslestirmeAyarlaKomutu(Hak()),
        .. Kimlikler.Count > 0 ? [new ParselKimlikEslemesiAyarlaKomutu(Kimlikler)] : Array.Empty<IProjeKomutu>(),
    ];

    public static ParselGeometrisi Serit(string id, ParselRolu rol, decimal y1, decimal y2) => new(
        id, rol,
        [new GeodeticPoint(X0, Y0 + y1), new GeodeticPoint(X0, Y0 + y2), new GeodeticPoint(X0 + Derinlik, Y0 + y2), new GeodeticPoint(X0 + Derinlik, Y0 + y1)]);

    /// <summary>
    /// Sentetik açgözlü eşleştirme (mühendis kroki kararının yerine geçen TEST girdisi): imar parselleri sırayla hak sahiplerinin
    /// net hakkı (giren − round(giren × DOPO, 2)) dolana kadar verilir; gerekirse parsel iki hak sahibi arasında bölünür.
    /// Kalan alan (Σ net ≠ R2 kuruş farkı) son hak sahibine yazılır (ERR-TAPU-006 uyarısı beklenebilir).
    /// </summary>
    private static List<HakEslestirmeSatiri> Acgozlu(IReadOnlyList<KadastroParseli> kadastro, IReadOnlyList<ImarParseli> imar, decimal dopo)
    {
        var sahipler = kadastro.SelectMany(k => k.Malikler.Select(m =>
        {
            var giren = Math.Round(k.TapuAlaniM2 * (decimal)m.Hisse.Numerator / (decimal)m.Hisse.Denominator, 2, MidpointRounding.AwayFromZero);
            return (Parsel: k.ParselId, m.MalikId, Net: giren - Math.Round(giren * dopo, 2, MidpointRounding.AwayFromZero));
        })).ToList();
        var satirlar = new List<HakEslestirmeSatiri>();
        var s = 0;
        var kalanNet = sahipler[0].Net;
        foreach (var p in imar)
        {
            var kalan = p.AlanM2;
            while (kalan > 0m)
            {
                var son = s == sahipler.Count - 1;
                var ver = son ? kalan : Math.Min(kalan, kalanNet);
                if (ver > 0m)
                {
                    satirlar.Add(new HakEslestirmeSatiri(sahipler[s].MalikId, sahipler[s].Parsel, p.GeciciKod, ver));
                    kalan -= ver;
                    kalanNet -= ver;
                }

                if (!son && kalanNet <= 0m)
                {
                    s++;
                    kalanNet = sahipler[s].Net;
                }
            }
        }

        return satirlar;
    }

    // ---------------- Altın veri ----------------

    public static JsonElement Json(string klasor, string dosya)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TestDataLocator.Sentetik(klasor, dosya)));
        return doc.RootElement.Clone();
    }

    public static decimal Dec(JsonElement e, string ad) => e.GetProperty(ad).GetDecimal();

    public static Fraction Kesir(string s)
    {
        var p = s.Split('/');
        return new Fraction(BigInteger.Parse(p[0], CultureInfo.InvariantCulture), BigInteger.Parse(p[1], CultureInfo.InvariantCulture));
    }

    public static KadastroMaliki Tam(string malik) => new(malik, Fraction.One, true);
}

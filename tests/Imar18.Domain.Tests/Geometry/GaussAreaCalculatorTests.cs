using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Xunit;

namespace Imar18.Domain.Tests.Geometry;

/// <summary>
/// P2.2 — Ilk koseye otelemeli yerel orijin Gauss (Shoelace) alani, decimal cift kontrol (spec 4.1).
/// Koordinatlar yerel (X=Kuzey, Y=Dogu) verilip O = (4500000.000, 500000.000) TM ofseti eklenir;
/// beklenen alanlar geometrik olarak elle hesaplanmistir.
/// </summary>
public sealed class GaussAreaCalculatorTests
{
    private const decimal Ox = 4500000.000m;
    private const decimal Oy = 500000.000m;

    internal static PolygonRing Halka(params (decimal X, decimal Y)[] yerel)
    {
        var kapali = yerel.Append(yerel[0]).Select(p => new GeodeticPoint(X: Ox + p.X, Y: Oy + p.Y)).ToList();
        var sonuc = PolygonRing.FromClosed(kapali);
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.Code : "");
        return sonuc.Value;
    }

    [Fact]
    public void Gauss_Birim_Kare_Saat_Yonu_Tersi_Ve_Tersi_Isaretli_Cift_Alan_Vermeli()
    {
        var ccw = GaussAreaCalculator.Compute(Halka((0, 0), (0, 1), (1, 1), (1, 0)));
        var cw = GaussAreaCalculator.Compute(Halka((0, 0), (1, 0), (1, 1), (0, 1)));

        ccw.SignedDoubleAreaM2.Should().Be(-2m);
        ccw.Orientation.Should().Be(RingOrientation.CounterClockwise);
        cw.SignedDoubleAreaM2.Should().Be(2m);
        cw.Orientation.Should().Be(RingOrientation.Clockwise);
        ccw.AreaM2Exact.Should().Be(1m);
        cw.AreaM2Exact.Should().Be(1m);
    }

    [Theory]
    [MemberData(nameof(Sekiller))]
    public void Gauss_Buyuk_TM_Ofsetli_Sekillerde_Elle_Hesaplanan_Alani_Vermeli(string ad, decimal[] koseler, decimal beklenen)
    {
        var ciftler = Enumerable.Range(0, koseler.Length / 2).Select(i => (koseler[2 * i], koseler[2 * i + 1])).ToArray();

        var alan = GaussAreaCalculator.Compute(Halka(ciftler));

        alan.AreaM2Exact.Should().Be(beklenen, ad);
    }

    public static TheoryData<string, decimal[], decimal> Sekiller() => new()
    {
        { "dikdortgen 30x40", [0, 0, 0, 40, 30, 40, 30, 0], 1200m },
        { "ucgen 40.001 x 30.003", [0, 0, 0, 40.001m, 30.003m, 0], 600.0750015m },
        { "yamuk (40+20)/2*30", [0, 0, 0, 40, 30, 30, 30, 10], 900m },
        { "L ada 400+200", [0, 0, 0, 40, 10, 40, 10, 10, 30, 10, 30, 0], 600m },
        { "U ada 500+2*900", [0, 0, 0, 50, 100, 50, 100, 40, 10, 40, 10, 10, 100, 10, 100, 0], 2300m },
    };

    [Fact]
    public void Gauss_Dikdortgen_Iki_Formul_Ayni_Isaretli_Cift_Alani_Vermeli()
    {
        var alan = GaussAreaCalculator.Compute(Halka((0, 0), (0, 40), (30, 40), (30, 0)));

        alan.SignedDoubleAreaM2.Should().Be(-2400m);
        alan.Orientation.Should().Be(RingOrientation.CounterClockwise);
    }

    [Fact]
    public void Gauss_Ikinci_Ondalikta_AwayFromZero_Yuvarlamali()
    {
        var alan = GaussAreaCalculator.Compute(Halka((0, 0), (0, 1.005m), (1, 1.005m), (1, 0)));

        alan.AreaM2Exact.Should().Be(1.005m);
        alan.AreaM2.Should().Be(1.01m); // ToEven olsaydi 1.00
    }

    [Fact]
    public void Gauss_Ucgen_Yuvarlanmis_Alan_600_08_Olmali()
    {
        GaussAreaCalculator.Compute(Halka((0, 0), (0, 40.001m), (30.003m, 0))).AreaM2.Should().Be(600.08m);
    }

    [Fact]
    public void Gauss_Baslangic_Kosesi_Dondurulmus_Halkada_Ayni_Alani_Vermeli_Ve_Tekrarlanabilir_Olmali()
    {
        var a = GaussAreaCalculator.Compute(Halka((0, 0), (0, 40), (30, 30), (30, 10)));
        var b = GaussAreaCalculator.Compute(Halka((30, 30), (30, 10), (0, 0), (0, 40)));
        var tekrar = GaussAreaCalculator.Compute(Halka((0, 0), (0, 40), (30, 30), (30, 10)));

        b.Should().Be(a);
        tekrar.Should().Be(a);
    }

    [Fact]
    public void Gauss_3B_Halkada_Z_Atilarak_Yatay_Izdusum_Alani_Vermeli()
    {
        // G3 halkasi Z = X' ile egik: 3B yuzey alani 1697.06 olurdu; yatay izdusum 1200 olmali.
        var noktalar3B = new (decimal X, decimal Y)[] { (0, 0), (0, 40), (30, 40), (30, 0), (0, 0) }
            .Select(p => new GeodeticPoint3D(X: Ox + p.X, Y: Oy + p.Y, Z: p.X))
            .ToList();

        var izdusum = HorizontalProjection.ToHorizontal(noktalar3B);
        var alan = GaussAreaCalculator.Compute(PolygonRing.FromClosed(izdusum.Points).Value);

        alan.AreaM2Exact.Should().Be(1200m);
        izdusum.Warnings.Should().ContainSingle();
    }
}

public sealed class PolygonRingTests
{
    private static GeodeticPoint P(decimal x, decimal y) => new(X: 4500000m + x, Y: 500000m + y);

    [Fact]
    public void Halka_Null_Reddedilmeli()
    {
        var act = () => PolygonRing.FromClosed(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Halka_Kapanis_Tekrari_Saklanmamali()
    {
        var halka = PolygonRing.FromClosed([P(0, 0), P(0, 1), P(1, 1), P(0, 0)]).Value;

        halka.Vertices.Should().HaveCount(3);
    }

    [Fact]
    public void Halka_Ilk_Ve_Son_Kose_Farkliysa_ERR_TOPO_002_Dondurmeli()
    {
        PolygonRing.FromClosed([P(0, 0), P(0, 1), P(1, 1), P(1, 0)]).Error.Code.Should().Be(ErrorCodes.TopoAcikPoligon);
    }

    public static TheoryData<string, GeodeticPoint[]> Dejenereler() => new()
    {
        { "bos", [] },
        { "iki kose", [P(0, 0), P(0, 1), P(0, 0)] },
        { "ardisik tekrar", [P(0, 0), P(0, 1), P(0, 1), P(1, 1), P(0, 0)] },
        { "dogrusal sifir alan", [P(0, 0), P(0, 10), P(0, 20), P(0, 0)] },
    };

    [Theory]
    [MemberData(nameof(Dejenereler))]
    public void Halka_Dejenere_Girdide_ERR_TOPO_003_Dondurmeli(string ad, GeodeticPoint[] koseler)
    {
        PolygonRing.FromClosed(koseler).Error.Code.Should().Be(ErrorCodes.TopoDejenerePoligon, ad);
    }

    [Fact]
    public void Halka_Alti_Ondaligi_Asan_Koordinatta_ERR_GIRD_005_Dondurmeli()
    {
        var sonuc = PolygonRing.FromClosed([P(0, 0), P(0, 1.0000001m), P(1, 1), P(0, 0)]);

        sonuc.Error.Code.Should().Be(ErrorCodes.GirdKoordinatKesinlikZarfiDisinda);
    }

    [Fact]
    public void Halka_Yerel_Bir_Milyon_Metreyi_Asan_Kosede_ERR_GIRD_005_Dondurmeli()
    {
        var sonuc = PolygonRing.FromClosed([P(0, 0), P(0, 1_000_000m), P(1, 1), P(0, 0)]);

        sonuc.Error.Code.Should().Be(ErrorCodes.GirdKoordinatKesinlikZarfiDisinda);
    }

    [Fact]
    public void Halka_5001_Koseli_Girdide_ERR_GIRD_005_Dondurmeli()
    {
        var koseler = Enumerable.Range(0, PolygonRing.MaxVertexCount + 1)
            .Select(i => P(i % 2, i))
            .ToList();

        PolygonRing.FromOpen(koseler).Error.Code.Should().Be(ErrorCodes.GirdKoordinatKesinlikZarfiDisinda);
    }

    [Fact]
    public void Gauss_Alti_Ondalikli_TM_Ofsetli_Koordinatta_Tam_Alan_Vermeli()
    {
        // 0.5 * 0.000002 * 0.000003 = 3e-12 (decimal'de tam)
        var halka = PolygonRing.FromOpen([P(0, 0), P(0, 0.000002m), P(0.000003m, 0)]).Value;

        GaussAreaCalculator.Compute(halka).AreaM2Exact.Should().Be(0.000000000003m);
    }

    [Fact]
    public void Halka_Kose_Indeksli_Hata_Mesaji_Koordinat_Icermemeli()
    {
        var hatalar = new[]
        {
            PolygonRing.FromOpen([P(0, 0), P(0, 1.0000001m), P(1, 1)]).Error,      // GIRD-005 (ondalik)
            PolygonRing.FromOpen([P(0, 0), P(0, 1), P(0, 1), P(1, 1)]).Error,      // TOPO-003 (tekrar)
        };

        hatalar.Should().OnlyContain(h => !h.TechnicalMessage.Contains("4500000") && !h.TechnicalMessage.Contains('{'));
    }

    [Fact]
    public void Halka_Hata_Teknik_Mesaji_Koordinat_Icermemeli()
    {
        var hata = PolygonRing.FromClosed([P(0, 0), P(0, 1), P(1, 1), P(1, 0)]).Error;

        hata.TechnicalMessage.Should().NotContain("4500000").And.NotContain("{");
    }

    [Fact]
    public void Halka_Public_Yapici_Olmamali()
    {
        typeof(PolygonRing).GetConstructors().Should().BeEmpty();
    }
}

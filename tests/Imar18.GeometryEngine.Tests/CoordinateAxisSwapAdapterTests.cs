using FluentAssertions;
using Imar18.Domain.Geometry;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using Xunit;

namespace Imar18.GeometryEngine.Tests;

/// <summary>
/// P2.1 — Jeodezik (X=Kuzey, Y=Dogu) &lt;-&gt; NTS Kartezyen (x=Dogu, y=Kuzey) eksen takasi (spec 3.2 madde 3).
/// Eksen hatasi alani degistirmez (ayna goruntusu); bu nedenle konum ve yon ayri ayri dogrulanir.
/// </summary>
public sealed class CoordinateAxisSwapAdapterTests
{
    private static readonly GeodeticPoint Orijin = new(X: 4500000.000m, Y: 500000.000m);

    [Fact]
    public void EksenTakasi_X_NTS_Dogu_Y_NTS_Kuzey_Olmali()
    {
        var c = CoordinateAxisSwapAdapter.ToNts(new GeodeticPoint(X: 4500010.000m, Y: 500020.000m), Orijin);

        c.X.Should().Be(20d, "x_NTS = Y_jeo (Dogu)");
        c.Y.Should().Be(10d, "y_NTS = X_jeo (Kuzey)");
    }

    [Fact]
    public void EksenTakasi_Gidis_Donus_Birebir_Ayni_Noktayi_Vermeli()
    {
        var p = new GeodeticPoint(X: 4500010.123m, Y: 500020.456m);

        CoordinateAxisSwapAdapter.FromNts(CoordinateAxisSwapAdapter.ToNts(p, Orijin), Orijin).Should().Be(p);
    }

    [Fact]
    public void EksenTakasi_Donuste_Mutlak_Koordinatta_1_mm_Kuantize_Etmeli()
    {
        // y = -0.0005 -> mutlak 4499999.9995 -> 4500000.000 (yerelde yuvarlansaydi 4499999.999 olurdu)
        var p = CoordinateAxisSwapAdapter.FromNts(new Coordinate(0.0004, -0.0005), Orijin);

        p.Should().Be(new GeodeticPoint(X: 4500000.000m, Y: 500000.000m));
    }

    [Fact]
    public void EksenTakasi_Sonlu_Olmayan_Koordinat_Reddedilmeli()
    {
        var act = () => CoordinateAxisSwapAdapter.FromNts(new Coordinate(double.NaN, 1), Orijin);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EksenTakasi_Asiri_Buyuk_Sonlu_Koordinatta_ArgumentOutOfRange_Atmali()
    {
        var act = () => CoordinateAxisSwapAdapter.FromNts(new Coordinate(1e29, 1), Orijin);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EksenTakasi_Haritada_Saat_Yonundeki_Halka_NTS_te_de_Saat_Yonunde_Olmali()
    {
        // (X,Y): (0,0),(1,0),(1,1),(0,1) -> S1 = +2 -> haritada saat yonu
        var halka = new[] { (0m, 0m), (1m, 0m), (1m, 1m), (0m, 1m), (0m, 0m) }
            .Select(p => CoordinateAxisSwapAdapter.ToNts(new GeodeticPoint(X: Orijin.X + p.Item1, Y: Orijin.Y + p.Item2), Orijin))
            .ToArray();

        Orientation.IsCCW(halka).Should().BeFalse();
    }
}

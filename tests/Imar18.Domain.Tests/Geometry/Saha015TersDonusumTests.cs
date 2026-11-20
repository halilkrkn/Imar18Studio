using FluentAssertions;
using Imar18.Domain.Geometry;
using Xunit;

namespace Imar18.Domain.Tests.Geometry;

/// <summary>
/// SAHA-015: kroki parselasyonu <c>.DNS</c> ileri dönüşümü etkin projede yeni köşeleri Deprem Öncesi'ne geri taşır
/// (<see cref="DonusumKatsayilari.TersUygula"/>). Beklenen değerler elle çözülen 2×2 sistemdir.
/// </summary>
public sealed class Saha015TersDonusumTests
{
    [Fact]
    public void TersUygula_Yalniz_Oteleme_Elle_Hesaplanan_Degeri_Vermeli()
    {
        var k = new DonusumKatsayilari(A: 1m, B: 0m, C: 0m, D: 1m, Cy: 0.25m, Cx: -0.5m);

        k.TersUygula(new GeodeticPoint(X: 10m, Y: 20m)).Should().Be(new GeodeticPoint(X: 10.5m, Y: 19.75m));
    }

    [Fact]
    public void TersUygula_Olcekli_Matriste_Tam_Ters_Vermeli()
    {
        // Y2 = 1.01·Y1, X2 = 1.01·X1 → (Y2, X2) = (101, 202) için (Y1, X1) = (100, 200).
        var k = new DonusumKatsayilari(A: 1.01m, B: 0m, C: 0m, D: 1.01m, Cy: 0m, Cx: 0m);

        k.TersUygula(new GeodeticPoint(X: 202m, Y: 101m)).Should().Be(new GeodeticPoint(X: 200m, Y: 100m));
    }

    [Fact]
    public void TersUygula_Helmert_Ileri_Donusumun_Tersini_1_Mikron_Icinde_Vermeli()
    {
        var k = new DonusumKatsayilari(A: 1.0000213m, B: -0.0000187m, C: 0.0000187m, D: 1.0000213m, Cy: 0.734m, Cx: -1.218m);
        var oncesi = new GeodeticPoint(X: 4_400_123.456789m, Y: 500_321.123456m);

        var geri = k.TersUygula(k.Uygula(oncesi));

        Math.Abs(geri.X - oncesi.X).Should().BeLessThanOrEqualTo(0.000001m);
        Math.Abs(geri.Y - oncesi.Y).Should().BeLessThanOrEqualTo(0.000001m);
        decimal.Round(geri.X, 6).Should().Be(geri.X, "sonuç 6 ondalığa yuvarlanır (PolygonRing.MaxScale)");
    }

    [Fact]
    public void TersUygula_Tekil_Matriste_Istisna_Atmali()
    {
        var k = new DonusumKatsayilari(A: 1m, B: 1m, C: 1m, D: 1m, Cy: 0m, Cx: 0m);

        var act = () => k.TersUygula(new GeodeticPoint(1m, 1m));

        act.Should().Throw<InvalidOperationException>();
    }
}

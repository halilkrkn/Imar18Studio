using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Tecviz;
using Xunit;

namespace Imar18.GeometryEngine.Tests.Tecviz;

/// <summary>
/// P3.1 — TPYS Md. 8(5) geçici koordinat hata çemberi YARDIMCI hesaplayıcısı (spec 4.3.3 / TC-TECVIZ-002; varsayılan değil).
/// Bağımsız referans (dışbükey çokgen): A_dış = A + P·r + π·r², A_iç (dikdörtgen) = (a − 2r)(b − 2r).
/// </summary>
public sealed class HataCemberiHesaplayiciTests
{
    private const decimal X0 = 4_500_000.000m;
    private const decimal Y0 = 500_000.000m;

    private static PolygonRing Dikdortgen(decimal genislikY, decimal yukseklikX) =>
        PolygonRing.FromOpen(
        [
            new GeodeticPoint(X: X0, Y: Y0),
            new GeodeticPoint(X: X0, Y: Y0 + genislikY),
            new GeodeticPoint(X: X0 + yukseklikX, Y: Y0 + genislikY),
            new GeodeticPoint(X: X0 + yukseklikX, Y: Y0),
        ]).Value;

    [Fact]
    public void HataCemberi_Mp_Karekok_Mx2_Arti_My2_Olmali()
    {
        var sonuc = HataCemberiHesaplayici.Hesapla(Dikdortgen(40m, 30m), 0.3m, 0.4m);

        sonuc.IsSuccess.Should().BeTrue();
        sonuc.Value.NoktaKonumDogruluguM.Should().Be(0.5m);
    }

    [Fact]
    public void HataCemberi_Dikdortgende_Ic_Ve_Dis_Zarf_Analitik_Degerlere_Esit_Olmali()
    {
        // 40 x 30: A=1200, P=140, r=0.5 -> dis = 1200 + 70 + 0.785398 = 1270.79 ; ic = 39 x 29 = 1131.00
        var z = HataCemberiHesaplayici.Hesapla(Dikdortgen(40m, 30m), 0.3m, 0.4m).Value;

        z.AlanM2.Should().Be(1200.00m);
        z.DisAlanM2.Should().Be(1270.79m);
        z.IcAlanM2.Should().Be(1131.00m);
        z.IcAlanM2.Should().BeLessThan(z.AlanM2);
        z.DisAlanM2.Should().BeGreaterThan(z.AlanM2);
    }

    [Fact]
    public void HataCemberi_Sifir_Dogrulukta_Zarflar_Parsel_Alanina_Esit_Olmali()
    {
        var z = HataCemberiHesaplayici.Hesapla(Dikdortgen(20m, 10m), 0m, 0m).Value;

        z.IcAlanM2.Should().Be(200m);
        z.DisAlanM2.Should().Be(200m);
    }

    [Fact]
    public void HataCemberi_Parseli_Yutan_Yaricapta_Ic_Alan_Sifir_Olmali()
    {
        var z = HataCemberiHesaplayici.Hesapla(Dikdortgen(2m, 2m), 3m, 4m).Value; // r = 5

        z.IcAlanM2.Should().Be(0m);
    }

    [Theory]
    [InlineData(-0.1, 0)]
    [InlineData(0, -0.1)]
    public void HataCemberi_Negatif_Dogruluk_ERR_TECVIZ_004_Ile_Reddedilmeli(double mx, double my)
    {
        HataCemberiHesaplayici.Hesapla(Dikdortgen(20m, 10m), (decimal)mx, (decimal)my)
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Fact]
    public void HataCemberi_Asiri_Buyuk_Dogruluk_Istisna_Degil_Hata_Donmeli()
    {
        HataCemberiHesaplayici.Hesapla(Dikdortgen(20m, 10m), decimal.MaxValue, 0m)
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Fact]
    public void HataCemberi_Kendi_Uzerinde_Kesisen_Halka_ERR_TOPO_001_Vermeli()
    {
        // asimetrik papyon: (0,0)-(10,10) kenari (10,0)-(0,5) kenarini keser; isaretli alan sifir degil
        var papyon = PolygonRing.FromOpen(
        [
            new GeodeticPoint(X: X0, Y: Y0),
            new GeodeticPoint(X: X0 + 10m, Y: Y0 + 10m),
            new GeodeticPoint(X: X0 + 10m, Y: Y0),
            new GeodeticPoint(X: X0, Y: Y0 + 5m),
        ]);
        papyon.IsSuccess.Should().BeTrue("Domain halkasi kendi uzerinde kesisimi denetlemez (P2 M4)");

        HataCemberiHesaplayici.Hesapla(papyon.Value, 0.1m, 0.1m).Error.Code.Should().Be(ErrorCodes.TopoKendiUzerindeKesisim);
    }

    [Fact]
    public void HataCemberi_Null_Halka_Reddedilmeli()
    {
        var act = () => HataCemberiHesaplayici.Hesapla(null!, 0.1m, 0.1m);
        act.Should().Throw<ArgumentNullException>();
    }
}

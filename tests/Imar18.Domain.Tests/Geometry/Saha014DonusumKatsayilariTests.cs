using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Numerics;
using Xunit;

namespace Imar18.Domain.Tests.Geometry;

/// <summary>
/// SAHA-014: Netcad <c>.DNS</c> <c>$AMAT</c> katsayıları (<c>A, B, CY, CX, C, D</c>) ve doğrulanmış ileri dönüşüm formülü
/// <c>Y2 = A·Y1 + C·X1 + CY</c>, <c>X2 = B·Y1 + D·X1 + CX</c> (Deprem Öncesi → Deprem Sonrası). Beklenen değerler elle / bağımsız
/// yüksek kesinlikli hesapla (Python decimal, 50 hane) bulunmuştur; koordinatlar sentetiktir.
/// </summary>
public sealed class Saha014DonusumKatsayilariTests
{
    // Sentetik Helmert: A = D = 0.99999, C = 0.00001, B = −0.00001, CY = −25.5, CX = 18.25.
    private static readonly DonusumKatsayilari K = new(A: 0.99999m, B: -0.00001m, C: 0.00001m, D: 0.99999m, Cy: -25.5m, Cx: 18.25m);

    // Gözlenen II. sistem = hesaplanan − v; v: N1 (+0.010, −0.020), N2 (−0.010, 0), N3 (0, +0.020), N4 (0, 0).
    private static readonly DonusumOrtakNoktasi[] Noktalar =
    [
        new("N1", 500_000.000m, 4_000_000.000m, 500_009.490m, 3_999_973.270m),
        new("N2", 501_000.000m, 4_000_000.000m, 501_009.500m, 3_999_973.240m),
        new("N3", 501_000.000m, 4_001_000.000m, 501_009.500m, 4_000_973.210m),
        new("N4", 500_000.000m, 4_001_000.000m, 500_009.510m, 4_000_973.240m),
    ];

    [Fact]
    public void DonusumKatsayilari_Ileri_Formul_Y2_A_Y1_Arti_C_X1_Arti_CY_Ve_X2_B_Y1_Arti_D_X1_Arti_CX_Olmali()
    {
        // Elle: Y2 = 0.99999·500000 + 0.00001·4000000 − 25.5 = 499995 + 40 − 25.5 = 500009.5
        //       X2 = −0.00001·500000 + 0.99999·4000000 + 18.25 = −5 + 3999960 + 18.25 = 3999973.25
        var p = K.Uygula(new GeodeticPoint(X: 4_000_000m, Y: 500_000m));

        p.Y.Should().Be(500_009.5m);
        p.X.Should().Be(3_999_973.25m);
    }

    [Fact]
    public void DonusumKatsayilari_B_Ve_C_Yer_Degistirirse_Sonuc_Farkli_Olmali_Netcad_Duzeni_Ayirt_Edici()
    {
        var ters = K with { B = K.C, C = K.B };

        ters.Uygula(new GeodeticPoint(X: 4_000_000m, Y: 500_000m)).Y.Should().Be(499_995m - 40m - 25.5m);
    }

    [Fact]
    public void DonusumKatsayilari_Sonuc_PolygonRing_MaxScale_Ondaligina_AwayFromZero_Yuvarlanmali()
    {
        // Sentetik katsayı ve nokta (gerçek .DNS değeri değildir — AC-PILOT-08):
        // Y2 = 0.999996312345678·512345.67 + 0.00000712345678901234·4234567.89 − 23.4567890123456 = 512350.48861864787… → 512350.488619
        // X2 = −0.00000712345678901234·512345.67 + 0.999996312345678·4234567.89 + 17.6543210987654 = 4234566.27902627612… → 4234566.279026
        var k = new DonusumKatsayilari(0.999996312345678m, -0.00000712345678901234m, 0.00000712345678901234m, 0.999996312345678m, -23.4567890123456m, 17.6543210987654m);

        var p = k.Uygula(new GeodeticPoint(X: 4_234_567.89m, Y: 512_345.67m));

        PolygonRing.MaxScale.Should().Be(6);
        decimal.Round(p.X, 6).Should().Be(p.X);
        decimal.Round(p.Y, 6).Should().Be(p.Y);
        p.Y.Should().Be(512_350.488619m, "bağımsız yüksek kesinlikli hesap (Python decimal, 60 hane)");
        p.X.Should().Be(4_234_566.279026m);
    }

    [Fact]
    public void DonusumKatsayilari_Ayni_Nokta_Her_Zaman_Ayni_Sonucu_Vermeli_Paylasilan_Koseler_Korunur()
    {
        var a = K.Uygula(new GeodeticPoint(X: 4_000_123.456789m, Y: 500_321.123456m));
        var b = K.Uygula(new GeodeticPoint(X: 4_000_123.456789m, Y: 500_321.123456m));

        a.Should().Be(b);
    }

    [Fact]
    public void DonusumKatsayilari_Determinant_Ve_Helmert_Bicimi_Dogru_Belirlenmeli()
    {
        K.Determinant.Should().Be((0.99999m * 0.99999m) - (-0.00001m * 0.00001m));
        K.HelmertMi.Should().BeTrue();
        (K with { D = 0.99998m }).HelmertMi.Should().BeFalse();
    }

    [Theory]
    [InlineData(1, 2, 2, 4)] // tekil (det = 0)
    [InlineData(1, 0, 0, -1)] // yansıma (det < 0)
    [InlineData(2, 0, 0, 2)] // ölçek ×2 (|det − 1| > 0.01)
    public void DonusumKatsayilari_Gecersiz_Matris_ERR_CRS_004_Ile_Reddedilmeli(int a, int b, int c, int d)
    {
        var r = DonusumKatsayilari.Dogrula(new DonusumKatsayilari(a, b, c, d, 0m, 0m));

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be(ErrorCodes.CrsDonusumHesaplanamadi);
    }

    [Fact]
    public void DonusumKatsayilari_Gecerli_Matris_Dogrulamadan_Gecmeli()
    {
        DonusumKatsayilari.Dogrula(K).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void DonusumKatsayilari_Tasma_Uretecek_Buyuk_Degerler_Istisna_Atmadan_Reddedilmeli()
    {
        // Reviewer M2: A = D = 7E+28 ile determinant (A·D) decimal'de taşardı; sınır denetimi determinanttan önce yapılır.
        var dev = new DonusumKatsayilari(70_000_000_000_000_000_000_000_000_000m, 0m, 0m, 70_000_000_000_000_000_000_000_000_000m, 0m, 0m);

        var act = () => DonusumKatsayilari.Dogrula(dev);

        act.Should().NotThrow();
        act().Error.Code.Should().Be(ErrorCodes.CrsDonusumHesaplanamadi);
        DonusumKatsayilari.Dogrula(K with { Cy = 10_000_001m }).IsFailure.Should().BeTrue("|CY| > 1e7 m");
        DonusumKatsayilari.Dogrula(K with { Cx = -10_000_000m }).IsSuccess.Should().BeTrue("sınır dahil");
        DonusumKatsayilari.Dogrula(K with { B = -2.000001m }).IsFailure.Should().BeTrue("|B| > 2");
    }

    [Fact]
    public void DonusumParametreleri_Katsayilardan_Artiklar_V_Hesaplanan_Eksi_Gozlenen_Olmali()
    {
        var p = DonusumParametreleri.Katsayilardan(K, Noktalar);

        p.Model.Should().Be(DonusumModeli.Helmert2B);
        (p.A, p.B, p.C, p.D, p.Cy, p.Cx).Should().Be((0.99999m, -0.00001m, 0.00001m, 0.99999m, -25.5m, 18.25m), "DNS katsayıları birebir korunur");
        p.Artiklar.Select(a => a.NoktaNo).Should().Equal("N1", "N2", "N3", "N4");
        p.Artiklar.Select(a => (a.Vy, a.Vx)).Should().Equal((0.010m, -0.020m), (-0.010m, 0m), (0m, 0.020m), (0m, 0m));
        p.Artiklar[0].Y.Should().Be(500_009.5m);
        p.Artiklar[0].X.Should().Be(3_999_973.25m);
    }

    [Fact]
    public void DonusumParametreleri_Katsayilardan_Olcek_Donukluk_Ve_Ortalama_Hata_Bagimsiz_Referansla_Eslesmeli()
    {
        var p = DonusumParametreleri.Katsayilardan(K, Noktalar);

        // √(0.99999² + 0.00001²) = 0.9999900000500005000037500…
        p.OlcekKatsayisi!.Value.Should().BeApproximately(0.99999000005000050000375m, 1e-20m);
        // atan2(0.00001, 0.99999)·200/π = 0.000636626138607746337… grad (12 ondalık)
        p.DonuklukGrad!.Value.Should().Be(0.000636626139m);
        // √(Σv² / (2n − 4)) = √(0.0010 / 4) = 0.0158113883008418966…
        p.OrtalamaHataM!.Value.Should().BeApproximately(0.0158113883008418966m, 1e-18m);
    }

    [Fact]
    public void DonusumParametreleri_Katsayilardan_Afin_Matriste_Olcek_Ve_Donukluk_Tanimsiz_Serbestlik_2n_Eksi_6_Olmali()
    {
        var afin = K with { D = 0.99998m };

        var p = DonusumParametreleri.Katsayilardan(afin, Noktalar);

        p.Model.Should().Be(DonusumModeli.Afin2B);
        p.OlcekKatsayisi.Should().BeNull();
        p.DonuklukGrad.Should().BeNull();
        p.OrtalamaHataM.Should().NotBeNull("2·4 − 6 = 2 > 0");
        DonusumParametreleri.Katsayilardan(afin, Noktalar.Take(3).ToArray()).OrtalamaHataM.Should().BeNull("2·3 − 6 = 0");
    }

    [Fact]
    public void DonusumParametreleri_Katsayilardan_Ileri_Uygula_Katsayi_Formulunu_Kullanmali()
    {
        var p = DonusumParametreleri.Katsayilardan(K, Noktalar);

        p.Uygula(new GeodeticPoint(X: 4_000_000m, Y: 500_000m)).Should().Be(new GeodeticPoint(X: 3_999_973.25m, Y: 500_009.5m));
        p.Katsayilar.Should().Be(K);
    }

    [Fact]
    public void DonusumParametreleri_TersUygula_Ileri_Donusumu_Geri_Almali()
    {
        var p = DonusumParametreleri.Katsayilardan(K, Noktalar);

        var t = p.TersUygula(new GeodeticPoint(X: 3_999_973.25m, Y: 500_009.5m)).Value;

        t.Y.Should().BeApproximately(500_000m, 1e-12m);
        t.X.Should().BeApproximately(4_000_000m, 1e-12m);
    }

    [Theory]
    [InlineData(1, 1, "0.785398163397448309615660845820")]
    [InlineData(1, -1, "2.35619449019234492884698253746")]
    [InlineData(-1, -1, "-2.35619449019234492884698253746")]
    [InlineData(0, -1, "3.14159265358979323846264338328")]
    [InlineData(1, 0, "1.57079632679489661923132169164")]
    [InlineData(-1, 0, "-1.57079632679489661923132169164")]
    [InlineData(3, 4, "0.643501108793284386802809228717")]
    [InlineData(4, 3, "0.927295218001612232428512462922")] // |y| > |x|, x > 0 (reviewer test boşluğu)
    [InlineData(4, -3, "2.214297435588181006034130920357")] // π − atan(4/3)
    [InlineData(-4, 3, "-0.927295218001612232428512462922")]
    [InlineData(-4, -3, "-2.214297435588181006034130920357")]
    [InlineData(0, 1, "0")]
    public void DecimalMath_Atan2_Bagimsiz_Referansla_Eslesmeli(int y, int x, string beklenen)
    {
        // Referanslar: π/4, 3π/4, π, π/2 ve atan(3/4) bilinen sabitlerinin 30 haneli değerleri (double'a çevrilip kırpılmadan).
        var r = decimal.Parse(beklenen, System.Globalization.CultureInfo.InvariantCulture);

        DecimalMath.Atan2(y, x).Should().BeApproximately(r, 1e-24m);
    }

    [Fact]
    public void DecimalMath_Atan2_Kucuk_Aciyi_Yuksek_Kesinlikle_Bulmali_Ve_Sifir_Sifirda_Sifir_Donmeli()
    {
        // atan(0.00001 / 0.99999) = 0.0000100001000006666666665866653… rad (Python decimal, 50 hane Taylor serisi)
        DecimalMath.Atan2(0.00001m, 0.99999m).Should().BeApproximately(0.0000100001000006666666665867m, 1e-24m);
        DecimalMath.Atan2(0m, 0m).Should().Be(0m);
        DecimalMath.Pi.Should().Be(3.1415926535897932384626433833m);
    }
}

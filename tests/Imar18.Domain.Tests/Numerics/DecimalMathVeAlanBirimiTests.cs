using System.Globalization;
using FluentAssertions;
using Imar18.Domain.Numerics;
using Xunit;

namespace Imar18.Domain.Tests.Numerics;

/// <summary>
/// P5.3 — Cephe (kenar uzunluğu) için decimal karekök ve Fen Klasörü / Tescil Sayfası He | M2 | Dm2 alan ayrımı (spec 25.6 madde 3).
/// Beklenen değerler bağımsızdır: √2 = 1.4142135623730950488016887242… (OEIS A002193), 3-4-5 dik üçgeni;
/// 3539.74 m² → 0 He, 3539 M2, 74 Dm2 (spec 25.6 örneği).
/// </summary>
public sealed class DecimalMathVeAlanBirimiTests
{
    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("25", "5")]
    [InlineData("169", "13")]
    [InlineData("0.25", "0.5")]
    [InlineData("1000000000000", "1000000")]
    public void DecimalMath_Sqrt_Tam_Kareler_Kesin_Sonuc_Vermeli(string deger, string beklenen)
    {
        DecimalMath.Sqrt(D(deger)).Should().Be(D(beklenen));
    }

    [Fact]
    public void DecimalMath_Sqrt_Iki_28_Hane_Dogrulukla_Hesaplanmali()
    {
        var kok = DecimalMath.Sqrt(2m);

        Math.Abs(kok - 1.4142135623730950488016887242m).Should().BeLessThanOrEqualTo(0.0000000000000000000000000002m);
    }

    [Fact]
    public void DecimalMath_Sqrt_Negatif_Deger_Reddedilmeli()
    {
        var act = () => DecimalMath.Sqrt(-0.01m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DecimalMath_KenarUzunlugu_3_4_5_Ucgeninde_5_Olmali()
    {
        DecimalMath.KenarUzunlugu(new(100m, 200m), new(103m, 204m)).Should().Be(5m);
        DecimalMath.KenarUzunlugu(new(103m, 204m), new(100m, 200m)).Should().Be(5m);
    }

    [Theory]
    [InlineData("3539.74", 0, 3539, 74)]
    [InlineData("12345.67", 1, 2345, 67)]
    [InlineData("10000.00", 1, 0, 0)]
    [InlineData("0.05", 0, 0, 5)]
    [InlineData("0", 0, 0, 0)]
    [InlineData("26629.48", 2, 6629, 48)]
    public void AlanBirimi_HeM2Dm2_Resmi_Uc_Sutuna_Ayirmali(string alan, long he, int m2, int dm2)
    {
        var sonuc = HeM2Dm2.Ayir(D(alan));

        sonuc.He.Should().Be(he);
        sonuc.M2.Should().Be(m2);
        sonuc.Dm2.Should().Be(dm2);
        ((sonuc.He * 10000m) + sonuc.M2 + (sonuc.Dm2 / 100m)).Should().Be(D(alan));
    }

    [Fact]
    public void AlanBirimi_HeM2Dm2_Ikiden_Fazla_Ondalik_AwayFromZero_Yuvarlanmali()
    {
        HeM2Dm2.Ayir(3539.745m).Should().Be(new HeM2Dm2(0, 3539, 75));
        HeM2Dm2.Ayir(9999.995m).Should().Be(new HeM2Dm2(1, 0, 0));
    }

    [Fact]
    public void AlanBirimi_HeM2Dm2_Negatif_Veya_Asiri_Buyuk_Alan_Reddedilmeli()
    {
        ((Action)(() => HeM2Dm2.Ayir(-0.01m))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => HeM2Dm2.Ayir(decimal.MaxValue))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AlanBirimi_HeM2Dm2_Metin_Bicimi_Dm2_Iki_Haneli_Olmali()
    {
        HeM2Dm2.Ayir(10000.05m).ToString().Should().Be("1 0 05");
    }
}

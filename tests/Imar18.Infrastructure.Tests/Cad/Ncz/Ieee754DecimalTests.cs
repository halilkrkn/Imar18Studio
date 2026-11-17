using FluentAssertions;
using Imar18.Infrastructure.Cad.Ncz;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cad.Ncz;

/// <summary>IEEE 754 binary64 -> decimal kayipsiz cozucu (double tipi kullanilmadan, BigInteger ile).</summary>
public sealed class Ieee754DecimalTests
{
    private static decimal Coz(double d, int olcek = 6)
    {
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(d), olcek, out var sonuc).Should().BeTrue();
        return sonuc;
    }

    [Theory]
    [InlineData(0d, "0")]
    [InlineData(1.5d, "1.5")]
    [InlineData(-2.25d, "-2.25")]
    [InlineData(4000000.125d, "4000000.125")]
    [InlineData(7503.85d, "7503.85")]
    [InlineData(0.1d, "0.1")]
    public void Ieee754_Tam_Veya_Yakin_Degerleri_6_Ondalikta_Dogru_Cozmeli(double girdi, string beklenen)
    {
        Coz(girdi).Should().Be(decimal.Parse(beklenen, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Ieee754_Yarimdan_Buyuk_Kalan_Yukari_Kucuk_Kalan_Asagi_Yuvarlanmali()
    {
        Coz(Math.ScaleB(1d, -20)).Should().Be(0.000001m);   // 2^-20 = 0.00000095...
        Coz(Math.ScaleB(1d, -21)).Should().Be(0m);          // 2^-21 = 0.00000047...
        Coz(-Math.ScaleB(1d, -20)).Should().Be(-0.000001m); // simetrik (AwayFromZero)
    }

    [Fact]
    public void Ieee754_Tam_Yarim_Deger_Sifirdan_Uzaga_Yuvarlanmali()
    {
        // 0.5 ve -0.5 tam temsil edilir; olcek 0'da AwayFromZero -> 1 ve -1.
        Coz(0.5d, 0).Should().Be(1m);
        Coz(-0.5d, 0).Should().Be(-1m);
        Coz(2.5d, 0).Should().Be(3m);
    }

    [Fact]
    public void Ieee754_Negatif_Sifir_Ve_Alt_Normal_Degerler_Sifir_Vermeli()
    {
        Coz(-0d).Should().Be(0m);
        Coz(double.Epsilon).Should().Be(0m);
        Coz(-double.Epsilon).Should().Be(0m);
    }

    [Fact]
    public void Ieee754_Sonlu_Olmayan_Degerleri_Reddetmeli()
    {
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(double.NaN), 6, out _).Should().BeFalse();
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(double.PositiveInfinity), 6, out _).Should().BeFalse();
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(double.NegativeInfinity), 6, out _).Should().BeFalse();
    }

    [Fact]
    public void Ieee754_Decimal_Araligini_Asan_Degeri_Reddetmeli()
    {
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(1e23), 6, out _).Should().BeFalse();
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(double.MaxValue), 0, out _).Should().BeFalse();
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(1e22), 6, out var ok).Should().BeTrue();
        ok.Should().Be(10_000_000_000_000_000_000_000m);
    }

    [Fact]
    public void Ieee754_Olcek_28_Sinirinda_Decimal_Araligi_Dogru_Uygulanmali()
    {
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(7.9d), 28, out var kabul).Should().BeTrue();
        decimal.Round(kabul, 1).Should().Be(7.9m);
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(8d), 28, out _).Should().BeFalse();
    }

    [Fact]
    public void Ieee754_Iki_Uzeri_96_Sinirinda_Tam_Sinir_Reddedilmeli_Altindaki_Kabul_Edilmeli()
    {
        var sinir = Math.ScaleB(1d, 96);
        var altinda = Math.BitDecrement(sinir); // 2^96 - 2^43

        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(sinir), 0, out _).Should().BeFalse();
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(altinda), 0, out var deger).Should().BeTrue();
        deger.Should().Be(79_228_162_514_264_328_797_450_928_128m);

        // Olcek 6'da sinir 2^96 / 10^6 ~ 7.92e22.
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(7.9e22), 6, out _).Should().BeTrue();
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(8e22), 6, out _).Should().BeFalse();
    }

    [Fact]
    public void Ieee754_Ret_Durumunda_Cikis_Degeri_Sifir_Olmali()
    {
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(double.NaN), 6, out var a).Should().BeFalse();
        Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(-1e30), 6, out var b).Should().BeFalse();
        a.Should().Be(0m);
        b.Should().Be(0m);
    }

    [Fact]
    public void Ieee754_Negatif_Tam_Yarim_Degerler_Sifirdan_Uzaga_Yuvarlanmali()
    {
        // Binary64'te tam ondalik yarim yalnizca olcek 0'da (k + 0.5) temsil edilebilir; olcek >= 1'de 5^olcek payda gerekir.
        Coz(-1.5d, 0).Should().Be(-2m);
        Coz(-1234567.5d, 0).Should().Be(-1234568m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(29)]
    public void Ieee754_Gecersiz_Olcekte_ArgumentOutOfRange_Firlatmali(int olcek)
    {
        var eylem = () => Ieee754Decimal.TryDecode(0UL, olcek, out _);

        eylem.Should().Throw<ArgumentOutOfRangeException>();
    }
}

using System.Numerics;
using FluentAssertions;
using Imar18.Application.Girdi;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Xunit;

namespace Imar18.Application.Tests.Girdi;

/// <summary>
/// P4.2 vektorel/manuel TAKBİS ve yaklasik yer krokisi girdi sozlesmeleri (roadmap P4.2, FR-F1.12).
/// Malik kodlari anonimdir; TC Kimlik No bicimi reddedilir (KVKK, AC-PILOT-08).
/// </summary>
public sealed class TakbisKrokiGirdiTests
{
    private static TakbisHisseGirdisi H(string malik, int pay, int payda, bool muvafakat = true) =>
        new(malik, new BigInteger(pay), new BigInteger(payda), muvafakat);

    private static TakbisParselGirdisi Parsel(params TakbisHisseGirdisi[] hisseler) => new("902/1", 3539.74m, hisseler);

    [Fact]
    public void Takbis_Gecerli_Parseli_Domain_Kadastro_Parseline_Donusturmeli()
    {
        var p = TakbisGirdiDogrulayici.Dogrula(Parsel(H("Malik-01", 1, 2), H("Malik-02", 1, 4), H("Malik-03", 1, 4, false))).Value;

        p.ParselId.Should().Be("902/1");
        p.TapuAlaniM2.Should().Be(3539.74m);
        p.Malikler.Should().HaveCount(3);
        p.Malikler[0].Hisse.Should().Be(new Fraction(1, 2));
        p.Malikler[2].Muvafakatli.Should().BeFalse();
    }

    [Fact]
    public void Takbis_Hisse_Toplami_Bir_Degilse_ERR_TAPU_003_Dondurmeli()
    {
        var hata = TakbisGirdiDogrulayici.Dogrula(Parsel(H("Malik-01", 1, 2), H("Malik-02", 1, 3))).Error;

        hata.Code.Should().Be(ErrorCodes.TapuHisseToplamiBirDegil);
        hata.TechnicalMessage.Should().Contain("5/6");
    }

    [Fact]
    public void Takbis_Hissesiz_Parselde_ERR_TAPU_002_Dondurmeli()
    {
        TakbisGirdiDogrulayici.Dogrula(Parsel()).Error.Code.Should().Be(ErrorCodes.TapuHisseEksik);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(-1, 2)]
    [InlineData(1, 0)]
    [InlineData(1, -2)]
    public void Takbis_Pozitif_Olmayan_Pay_Veya_Paydada_ERR_TAPU_004_Dondurmeli(int pay, int payda)
    {
        TakbisGirdiDogrulayici.Dogrula(Parsel(H("Malik-01", pay, payda), H("Malik-02", 1, 2))).Error.Code
            .Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("100.123")]
    public void Takbis_Gecersiz_Tapu_Alaninda_ERR_TAPU_004_Dondurmeli(string alan)
    {
        var g = Parsel(H("Malik-01", 1, 1)) with { TapuAlaniM2 = decimal.Parse(alan, System.Globalization.CultureInfo.InvariantCulture) };

        TakbisGirdiDogrulayici.Dogrula(g).Error.TechnicalMessage.Should().Contain("area");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Takbis_Bos_Parsel_Kimliginde_ERR_TAPU_004_Dondurmeli(string kimlik)
    {
        TakbisGirdiDogrulayici.Dogrula(Parsel(H("Malik-01", 1, 1)) with { ParselKimligi = kimlik }).Error.Code
            .Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Fact]
    public void Takbis_TC_Kimlik_No_Bicimindeki_Malik_Kodunu_KVKK_Geregi_Reddetmeli_Ve_Sizdirmamali()
    {
        var hata = TakbisGirdiDogrulayici.Dogrula(Parsel(H("12345678901", 1, 1))).Error;

        hata.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        hata.TechnicalMessage.Should().Contain("KVKK").And.NotContain("12345678901");
    }

    [Theory]
    [InlineData("123 456 789 01")]
    [InlineData("123-456-789-01")]
    [InlineData("Ahmet Yılmaz")]
    [InlineData("Malik 01")]
    public void Takbis_Anonim_Olmayan_Malik_Kodunu_KVKK_Geregi_Reddetmeli(string kod)
    {
        var hata = TakbisGirdiDogrulayici.Dogrula(Parsel(H(kod, 1, 1))).Error;

        hata.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        hata.TechnicalMessage.Should().Contain("KVKK").And.NotContain(kod);
    }

    [Fact]
    public void Takbis_Bos_Veya_Yinelenen_Malik_Kodunu_Reddetmeli()
    {
        TakbisGirdiDogrulayici.Dogrula(Parsel(H(" ", 1, 1))).Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        TakbisGirdiDogrulayici.Dogrula(Parsel(H("Malik-01", 1, 2), H("Malik-01", 1, 2))).Error.TechnicalMessage
            .Should().Contain("duplicate");
    }

    [Fact]
    public void Takbis_Null_Girdiyi_Reddetmeli()
    {
        ((Action)(() => TakbisGirdiDogrulayici.Dogrula(null!))).Should().Throw<ArgumentNullException>();
    }

    private static KadastroParseli Kadastro() =>
        TakbisGirdiDogrulayici.Dogrula(Parsel(H("Malik-01", 1, 2), H("Malik-02", 1, 2))).Value;

    [Fact]
    public void Kroki_Gecerli_Eslemeyi_Hak_Eslestirme_Satirlarina_Donusturmeli()
    {
        KrokiEslesmeGirdisi[] kroki = [new("Malik-01", "902/1", "101/1", 663.87m), new("Malik-02", "902/1", "101/2", 450.00m)];

        var satirlar = KrokiGirdiDogrulayici.Dogrula(kroki, [Kadastro()]).Value;

        satirlar.Should().Equal(
            new HakEslestirmeSatiri("Malik-01", "902/1", "101/1", 663.87m),
            new HakEslestirmeSatiri("Malik-02", "902/1", "101/2", 450.00m));
    }

    [Fact]
    public void Kroki_Parselde_Olmayan_Malikte_ERR_TAPU_001_Dondurmeli()
    {
        KrokiEslesmeGirdisi[] kroki = [new("Malik-99", "902/1", "101/1", 10m)];

        KrokiGirdiDogrulayici.Dogrula(kroki, [Kadastro()]).Error.Code.Should().Be(ErrorCodes.TapuMalikEslesmedi);
    }

    [Fact]
    public void Kroki_Bilinmeyen_Kadastro_Parselinde_Ve_Gecersiz_Alanda_ERR_TAPU_004_Dondurmeli()
    {
        KrokiGirdiDogrulayici.Dogrula([new("Malik-01", "999/9", "101/1", 10m)], [Kadastro()]).Error.TechnicalMessage
            .Should().Contain("cadastral parcel");
        KrokiGirdiDogrulayici.Dogrula([new("Malik-01", "902/1", "101/1", 0m)], [Kadastro()]).Error.Code
            .Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        KrokiGirdiDogrulayici.Dogrula([new("Malik-01", "902/1", " ", 10m)], [Kadastro()]).Error.Code
            .Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Fact]
    public void Kroki_Bos_Listede_ERR_TAPU_004_Ve_Null_Sozlesmesi()
    {
        KrokiGirdiDogrulayici.Dogrula([], [Kadastro()]).Error.TechnicalMessage.Should().Contain("at least one");
        ((Action)(() => KrokiGirdiDogrulayici.Dogrula(null!, []))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Kroki_Yinelenen_Kadastro_Parsel_Kimliginde_Istisna_Yerine_Result_Donmeli()
    {
        var hata = KrokiGirdiDogrulayici.Dogrula([new("Malik-01", "902/1", "101/1", 10m)], [Kadastro(), Kadastro()]).Error;

        hata.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        hata.TechnicalMessage.Should().Contain("duplicate cadastral parcel");
    }
}

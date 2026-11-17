using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Xunit;

namespace Imar18.Domain.Tests.Geometry;

/// <summary>P4.2 proje CRS/dilim/olcek girdisi (D3, SORU-009, SORU-028): ITRF96 (epok 2005.00) ve ED50 TM 3° 27–45.</summary>
public sealed class ProjeCrsTests
{
    [Theory]
    [InlineData(27)]
    [InlineData(30)]
    [InlineData(33)]
    [InlineData(36)]
    [InlineData(39)]
    [InlineData(42)]
    [InlineData(45)]
    public void ProjeCrs_ITRF96_TM3_Dilimleri_27_45_Arasi_Kabul_Edilmeli(int ortaMeridyen)
    {
        var sonuc = ProjeCrs.Olustur(GeodetikDatum.Itrf96, ortaMeridyen, 2005.00m, 1000);

        sonuc.IsSuccess.Should().BeTrue();
        var crs = sonuc.Value;
        crs.OrtaMeridyenDerece.Should().Be(ortaMeridyen);
        crs.Elipsoid.Ad.Should().Be("GRS80");
        crs.Elipsoid.BuyukYariEksenM.Should().Be(6_378_137m);
        crs.Elipsoid.TersBasiklik.Should().Be(298.257222101m);
        crs.OlcekFaktoru.Should().Be(1m);
        crs.YanaOtelemeM.Should().Be(500_000m);
        crs.CrsId.Should().Be($"ITRF96/TM3-{ortaMeridyen}@2005.00");
        crs.Uyarilar.Should().BeEmpty();
    }

    [Fact]
    public void ProjeCrs_ED50_Hayford_Elipsoidiyle_Epoksuz_Olusturulmali()
    {
        var crs = ProjeCrs.Olustur(GeodetikDatum.Ed50, 30, null, 5000).Value;

        crs.Elipsoid.Ad.Should().Be("International 1924 (Hayford)");
        crs.Elipsoid.BuyukYariEksenM.Should().Be(6_378_388m);
        crs.Elipsoid.TersBasiklik.Should().Be(297m);
        crs.Epok.Should().BeNull();
        crs.CrsId.Should().Be("ED50/TM3-30");
        crs.OlcekPaydasi.Should().Be(5000);
    }

    [Theory]
    [InlineData(28)]
    [InlineData(24)]
    [InlineData(48)]
    [InlineData(0)]
    [InlineData(-30)]
    public void ProjeCrs_Desteklenmeyen_Orta_Meridyen_ERR_CRS_003_Dondurmeli(int ortaMeridyen)
    {
        var hata = ProjeCrs.Olustur(GeodetikDatum.Itrf96, ortaMeridyen, 2005.00m, 1000).Error;

        hata.Code.Should().Be(ErrorCodes.CrsGirdisiGecersiz);
        hata.TechnicalMessage.Should().Contain("central meridian");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void ProjeCrs_Pozitif_Olmayan_Olcek_Paydasi_ERR_CRS_003_Dondurmeli(int payda)
    {
        ProjeCrs.Olustur(GeodetikDatum.Ed50, 33, null, payda).Error.Code.Should().Be(ErrorCodes.CrsGirdisiGecersiz);
    }

    [Fact]
    public void ProjeCrs_ITRF96_Epoksuz_Girdide_ERR_CRS_001_Dondurmeli()
    {
        ProjeCrs.Olustur(GeodetikDatum.Itrf96, 30, null, 1000).Error.Code.Should().Be(ErrorCodes.CrsEksik);
    }

    [Fact]
    public void ProjeCrs_ITRF96_2005_Disi_Epokta_ERR_CRS_002_Uyarisiyla_Olusturulmali()
    {
        var crs = ProjeCrs.Olustur(GeodetikDatum.Itrf96, 30, 2010.50m, 1000).Value;

        crs.Uyarilar.Should().ContainSingle(u => u.Code == ErrorCodes.CrsEpokUyumsuz)
            .Which.TechnicalMessage.Should().Contain("2010.50").And.Contain("2005.00");
        crs.CrsId.Should().Be("ITRF96/TM3-30@2010.50");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1899.99")]
    [InlineData("2100.01")]
    public void ProjeCrs_Makul_Aralik_Disindaki_Epokta_ERR_CRS_003_Dondurmeli(string epok)
    {
        ProjeCrs.Olustur(GeodetikDatum.Itrf96, 30, decimal.Parse(epok, System.Globalization.CultureInfo.InvariantCulture), 1000).Error
            .TechnicalMessage.Should().Contain("epoch");
    }

    [Fact]
    public void ProjeCrs_Kimligi_Epoku_Kesin_Degeriyle_Tasimali()
    {
        ProjeCrs.Olustur(GeodetikDatum.Itrf96, 30, 2005.004m, 1000).Value.CrsId.Should().Be("ITRF96/TM3-30@2005.004");
    }

    [Fact]
    public void ProjeCrs_ED50_Icin_Epok_Girilirse_ERR_CRS_003_Dondurmeli()
    {
        ProjeCrs.Olustur(GeodetikDatum.Ed50, 30, 2005.00m, 1000).Error.TechnicalMessage.Should().Contain("epoch");
    }

    [Fact]
    public void ProjeCrs_Mevcut_ProjectCoordinateReference_Sozlesmesine_Donusturulebilmeli()
    {
        var referans = ProjeCrs.Olustur(GeodetikDatum.Itrf96, 36, 2005.00m, 2000).Value.ToCoordinateReference();

        referans.CrsId.Should().Be("ITRF96/TM3-36@2005.00");
        referans.ZoneCentralMeridianDeg.Should().Be(36);
        referans.Epoch.Should().Be(2005.00m);
        referans.MapScaleDenominator.Should().Be(2000);
    }

    [Fact]
    public void ProjeCrs_Desteklenen_Orta_Meridyen_Listesi_Sabit_Ve_Sirali_Olmali()
    {
        ProjeCrs.DesteklenenOrtaMeridyenler.Should().Equal(27, 30, 33, 36, 39, 42, 45);
    }

    [Fact]
    public void ProjeCrs_Ayni_Girdide_Esit_Kimlik_Uretmeli()
    {
        ProjeCrs.Olustur(GeodetikDatum.Itrf96, 30, 2005.0m, 1000).Value.CrsId
            .Should().Be(ProjeCrs.Olustur(GeodetikDatum.Itrf96, 30, 2005.000m, 1000).Value.CrsId);
    }
}

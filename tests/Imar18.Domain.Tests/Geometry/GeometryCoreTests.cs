using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Xunit;

namespace Imar18.Domain.Tests.Geometry;

/// <summary>
/// P2.1 — Z=0 yatay izdusum kapisi ve 1 mm koordinat kuantizasyonu (spec 4.1, 4.2; AC-CAD-02).
/// Beklenen degerler elle hesaplanmistir.
/// </summary>
public sealed class HorizontalProjectionTests
{
    [Fact]
    public void YatayIzdusum_Z_Sifir_Disi_Kotlar_Atilmali_Ve_Tek_Uyari_Uretmeli()
    {
        var girdi = new[]
        {
            new GeodeticPoint3D(X: 4500000.000m, Y: 500000.000m, Z: 0m),
            new GeodeticPoint3D(X: 4500000.000m, Y: 500040.000m, Z: 12.5m),
            new GeodeticPoint3D(X: 4500030.000m, Y: 500040.000m, Z: -3m),
        };

        var sonuc = HorizontalProjection.ToHorizontal(girdi);

        sonuc.Points.Should().Equal(
            new GeodeticPoint(X: 4500000.000m, Y: 500000.000m),
            new GeodeticPoint(X: 4500000.000m, Y: 500040.000m),
            new GeodeticPoint(X: 4500030.000m, Y: 500040.000m));
        sonuc.Warnings.Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCodes.GirdZKoduIndirgendi);
    }

    [Fact]
    public void YatayIzdusum_Tum_Z_Sifirsa_Uyari_Uretmemeli()
    {
        var girdi = new[]
        {
            new GeodeticPoint3D(X: 1m, Y: 2m, Z: 0m),
            new GeodeticPoint3D(X: 3m, Y: 4m, Z: -0.000m),
        };

        var sonuc = HorizontalProjection.ToHorizontal(girdi);

        sonuc.Warnings.Should().BeEmpty();
        sonuc.Points.Should().HaveCount(2);
    }

    [Fact]
    public void YatayIzdusum_Uyari_Teknik_Mesaji_Koordinat_Icermemeli()
    {
        var girdi = new[] { new GeodeticPoint3D(X: 4512345.678m, Y: 512345.678m, Z: 5m) };

        var uyari = HorizontalProjection.ToHorizontal(girdi).Warnings.Single();

        uyari.TechnicalMessage.Should().NotContain("4512345").And.NotContain("{path}");
    }

    [Fact]
    public void YatayIzdusum_Null_Girdi_Reddedilmeli()
    {
        var act = () => HorizontalProjection.ToHorizontal(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

public sealed class CoordinateQuantizerTests
{
    [Theory]
    [InlineData("0.0005", "0.001")]            // yarim -> sifirdan uzaga
    [InlineData("-0.0005", "-0.001")]
    [InlineData("4500000.1245", "4500000.125")]
    [InlineData("500000.00049", "500000.000")]
    [InlineData("500000.0004", "500000.000")]
    [InlineData("12.3456", "12.346")]
    public void Kuantizasyon_1_mm_Izgarasina_AwayFromZero_Yuvarlamali(string girdi, string beklenen)
    {
        CoordinateQuantizer.Quantize(D(girdi)).Should().Be(D(beklenen));
    }

    [Fact]
    public void Kuantizasyon_Idempotent_Olmali()
    {
        var bir = CoordinateQuantizer.Quantize(4500000.1245m);

        CoordinateQuantizer.Quantize(bir).Should().Be(bir);
    }

    [Fact]
    public void Kuantizasyon_Mutlak_Koordinatta_Yapilmali_Yerel_Yarim_Deger_Mutlakta_Farkli_Sonuc_Verir()
    {
        // Yerel -0.0005 tek basina -0.001'e gider; mutlak 4500000.000 + (-0.0005) = 4499999.9995 -> 4500000.000.
        var mutlak = CoordinateQuantizer.Quantize(4500000.000m + -0.0005m);

        mutlak.Should().Be(4500000.000m);
        (4500000.000m + CoordinateQuantizer.Quantize(-0.0005m)).Should().Be(4499999.999m);
    }

    [Fact]
    public void Kuantizasyon_Nokta_Ve_Izgara_Kontrolu_Tutarli_Olmali()
    {
        var p = new GeodeticPoint(X: 4500000.0004m, Y: 500000.1235m);

        var q = CoordinateQuantizer.Quantize(p);

        q.Should().Be(new GeodeticPoint(X: 4500000.000m, Y: 500000.124m));
        CoordinateQuantizer.IsOnGrid(p).Should().BeFalse();
        CoordinateQuantizer.IsOnGrid(q).Should().BeTrue();
    }

    private static decimal D(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class ProjectCoordinateReferenceTests
{
    [Fact]
    public void ProjeCrs_Gecerli_Girdi_Ile_Olusmali_Ve_Degerleri_Saklamali()
    {
        var sonuc = ProjectCoordinateReference.Create("ITRF96-TM36", zoneCentralMeridianDeg: 36, epoch: 2005.00m, mapScaleDenominator: 1000);

        sonuc.IsSuccess.Should().BeTrue();
        sonuc.Value.CrsId.Should().Be("ITRF96-TM36");
        sonuc.Value.ZoneCentralMeridianDeg.Should().Be(36);
        sonuc.Value.Epoch.Should().Be(2005.00m);
        sonuc.Value.MapScaleDenominator.Should().Be(1000);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ProjeCrs_Kimligi_Bossa_ERR_CRS_001_Dondurmeli(string? crsId)
    {
        var sonuc = ProjectCoordinateReference.Create(crsId, 36, null, 1000);

        sonuc.Error.Code.Should().Be(ErrorCodes.CrsEksik);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void ProjeCrs_Olcek_Paydasi_Pozitif_Degilse_ERR_CRS_003_Dondurmeli(int payda)
    {
        var sonuc = ProjectCoordinateReference.Create("ITRF96-TM36", 36, null, payda);

        sonuc.Error.Code.Should().Be(ErrorCodes.CrsGirdisiGecersiz);
    }

    [Fact]
    public void ProjeCrs_Kimlik_Bosluklari_Kirpilmali()
    {
        ProjectCoordinateReference.Create("  ITRF96-TM36 ", 36, null, 1000).Value.CrsId.Should().Be("ITRF96-TM36");
    }

    [Fact]
    public void ProjeCrs_Public_Yapici_Olmamali()
    {
        typeof(ProjectCoordinateReference).GetConstructors().Should().BeEmpty();
    }
}

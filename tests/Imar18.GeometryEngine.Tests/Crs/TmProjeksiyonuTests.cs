using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Crs;
using Xunit;

namespace Imar18.GeometryEngine.Tests.Crs;

/// <summary>
/// P4.2 ProjNet TM 3° izdusumu. Beklenen degerler ProjNet'ten bagimsiz olarak Kruger n-serisiyle (6. derece, Karney 2011)
/// Python'da hesaplanmistir; noktalar sentetiktir (gercek parsel degil). Tolerans 1 mm.
/// </summary>
public sealed class TmProjeksiyonuTests
{
    private const decimal MmTolerans = 0.001m;

    private static ProjeCrs Itrf(int cm) => ProjeCrs.Olustur(GeodetikDatum.Itrf96, cm, 2005.00m, 1000).Value;

    private static ProjeCrs Ed50(int cm) => ProjeCrs.Olustur(GeodetikDatum.Ed50, cm, null, 1000).Value;

    [Theory]
    [InlineData(39.5, 30.0, 30, 4374014.1135, 500000.0000)]
    [InlineData(39.5, 31.2, 30, 4374701.7007, 603217.5603)]
    [InlineData(40.1, 28.7, 30, 4441442.6967, 389148.4527)]
    [InlineData(37.25, 44.1, 45, 4124635.5858, 420151.6491)]
    public void TmProjeksiyonu_ITRF96_GRS80_Cografi_Konumu_Bagimsiz_Kruger_Referansina_1mm_Icinde_Izdusurmeli(
        double enlem, double boylam, int cm, double beklenenKuzey, double beklenenDogu)
    {
        var p = TmProjeksiyonu.CografidenTm(Itrf(cm), new CografiKonum((decimal)enlem, (decimal)boylam)).Value;

        Math.Abs(p.X - (decimal)beklenenKuzey).Should().BeLessThanOrEqualTo(MmTolerans, "X = Kuzey");
        Math.Abs(p.Y - (decimal)beklenenDogu).Should().BeLessThanOrEqualTo(MmTolerans, "Y = Dogu");
    }

    [Theory]
    [InlineData(39.5, 30.0, 30, 4374088.4729, 500000.0000)]
    [InlineData(39.5, 31.2, 30, 4374776.0911, 603222.2147)]
    [InlineData(37.25, 44.1, 45, 4124703.1512, 420148.0919)]
    public void TmProjeksiyonu_ED50_Hayford_Cografi_Konumu_Bagimsiz_Referansa_1mm_Icinde_Izdusurmeli(
        double enlem, double boylam, int cm, double beklenenKuzey, double beklenenDogu)
    {
        var p = TmProjeksiyonu.CografidenTm(Ed50(cm), new CografiKonum((decimal)enlem, (decimal)boylam)).Value;

        Math.Abs(p.X - (decimal)beklenenKuzey).Should().BeLessThanOrEqualTo(MmTolerans);
        Math.Abs(p.Y - (decimal)beklenenDogu).Should().BeLessThanOrEqualTo(MmTolerans);
    }

    [Fact]
    public void TmProjeksiyonu_Gidis_Donus_Cografi_Konumu_1e9_Derece_Icinde_Korumali()
    {
        var crs = Itrf(33);
        var konum = new CografiKonum(38.123456789m, 32.987654321m);

        var tm = TmProjeksiyonu.CografidenTm(crs, konum).Value;
        var geri = TmProjeksiyonu.TmdenCografiye(crs, tm).Value;

        Math.Abs(geri.EnlemDerece - konum.EnlemDerece).Should().BeLessThan(0.000000001m);
        Math.Abs(geri.BoylamDerece - konum.BoylamDerece).Should().BeLessThan(0.000000001m);
    }

    [Fact]
    public void TmProjeksiyonu_Ayni_Datumda_Dilim_Degisimi_Cografi_Konumu_Korumali()
    {
        var konum = new CografiKonum(39.5m, 31.4m);
        var tm30 = TmProjeksiyonu.CografidenTm(Itrf(30), konum).Value;
        var tm33Beklenen = TmProjeksiyonu.CografidenTm(Itrf(33), konum).Value;

        var donusen = TmProjeksiyonu.DilimDegistir([tm30], Itrf(30), Itrf(33)).Value.Should().ContainSingle().Subject;

        Math.Abs(donusen.X - tm33Beklenen.X).Should().BeLessThanOrEqualTo(MmTolerans);
        Math.Abs(donusen.Y - tm33Beklenen.Y).Should().BeLessThanOrEqualTo(MmTolerans);
        donusen.Y.Should().BeGreaterThan(tm33Beklenen.Y - 1m).And.BeLessThan(500_000m, "33° diliminde 31.4° bati yanindadir");
    }

    [Fact]
    public void TmProjeksiyonu_Farkli_Datumlar_Arasinda_Sabit_Parametreyle_Donusturmemeli()
    {
        // ITRF96 <-> ED50 datum kaydirmasi icin sabit parametre kodlanmaz (D3); ortak noktali Helmert/Afin raporu kullanilir.
        var hata = TmProjeksiyonu.DilimDegistir([new GeodeticPoint(X: 4_374_014m, Y: 500_000m)], Itrf(30), Ed50(30)).Error;

        hata.Code.Should().Be(ErrorCodes.CrsGirdisiGecersiz);
        hata.TechnicalMessage.Should().Contain("datum").And.Contain("Helmert");
    }

    [Fact]
    public void TmProjeksiyonu_Farkli_Epokta_Dilim_Degisimini_Reddetmeli()
    {
        var epok2025 = ProjeCrs.Olustur(GeodetikDatum.Itrf96, 33, 2025.00m, 1000).Value;

        TmProjeksiyonu.DilimDegistir([new GeodeticPoint(X: 4_374_014m, Y: 500_000m)], Itrf(30), epok2025).Error
            .TechnicalMessage.Should().Contain("epoch");
    }

    [Theory]
    [InlineData(39.0, 33.5)]
    [InlineData(39.0, 120.0)]
    public void TmProjeksiyonu_Dilim_Disindaki_Boylamda_ERR_CRS_003_Dondurmeli(double enlem, double boylam)
    {
        TmProjeksiyonu.CografidenTm(Itrf(30), new CografiKonum((decimal)enlem, (decimal)boylam)).Error.TechnicalMessage
            .Should().Contain("zone");
    }

    [Theory]
    [InlineData(91, 30)]
    [InlineData(-91, 30)]
    [InlineData(39, 181)]
    public void TmProjeksiyonu_Gecersiz_Cografi_Konumda_ERR_CRS_003_Dondurmeli(double enlem, double boylam)
    {
        TmProjeksiyonu.CografidenTm(Itrf(30), new CografiKonum((decimal)enlem, (decimal)boylam)).Error.Code
            .Should().Be(ErrorCodes.CrsGirdisiGecersiz);
    }

    [Fact]
    public void TmProjeksiyonu_Bos_Liste_Ve_Null_Sozlesmesine_Uymali()
    {
        TmProjeksiyonu.DilimDegistir([], Itrf(30), Itrf(33)).Value.Should().BeEmpty();
        ((Action)(() => TmProjeksiyonu.DilimDegistir(null!, Itrf(30), Itrf(33)))).Should().Throw<ArgumentNullException>();
        ((Action)(() => TmProjeksiyonu.CografidenTm(null!, new CografiKonum(39m, 30m)))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TmProjeksiyonu_Ciktisi_En_Fazla_6_Ondalik_Tasimali()
    {
        var p = TmProjeksiyonu.CografidenTm(Itrf(30), new CografiKonum(39.5m, 31.2m)).Value;

        decimal.Round(p.X, 6).Should().Be(p.X);
        decimal.Round(p.Y, 6).Should().Be(p.Y);
    }
}

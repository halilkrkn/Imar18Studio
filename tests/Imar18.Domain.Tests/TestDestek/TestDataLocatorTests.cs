using FluentAssertions;
using Imar18.TestDestek;
using Xunit;

namespace Imar18.Domain.Tests.TestDestek;

/// <summary>
/// BULGU-3 (P6): ortak TestDataLocator altın veriyi depo kökünde <c>Imar18Studio.sln</c> aramadan, derleme çıktısındaki
/// kopyadan bulmalıdır; gerçek veri kökü yalnız ortam değişkeni / derleme meta verisinden ve klasör varsa çözülür.
/// </summary>
public sealed class TestDataLocatorTests : IDisposable
{
    private readonly string _gecici = Path.Combine(Path.GetTempPath(), "imar18-locator-" + Guid.NewGuid().ToString("N"));

    public TestDataLocatorTests() => Directory.CreateDirectory(_gecici);

    public void Dispose() => Directory.Delete(_gecici, recursive: true);

    private static Func<string, string?> Ortam(params (string Ad, string? Deger)[] degerler) =>
        ad => degerler.FirstOrDefault(d => d.Ad == ad).Deger;

    [Fact]
    public void TestDataLocator_Sentetik_Kok_Derleme_Ciktisindan_Cozulmeli_Sln_Aranmamali()
    {
        var kok = TestDataLocator.SentetikKok;

        kok.Should().StartWith(Path.GetFullPath(AppContext.BaseDirectory), "altın veri bin klasörüne kopyalanır");
        File.Exists(TestDataLocator.Sentetik("pilot-1-concave-parcel", "saha_proje_1_golden.json")).Should().BeTrue();
    }

    [Fact]
    public void TestDataLocator_Izole_Bin_Klasorunde_Sln_Olmadan_Bulmali()
    {
        // Depo ağacı yok: yalnızca bin/test-data/synthetic kopyası var.
        Directory.CreateDirectory(Path.Combine(_gecici, "test-data", "synthetic"));

        TestDataLocator.SentetikKokuCoz(_gecici, Ortam()).Should().Be(Path.Combine(_gecici, "test-data", "synthetic"));
        File.Exists(Path.Combine(_gecici, "Imar18Studio.sln")).Should().BeFalse();
    }

    [Fact]
    public void TestDataLocator_Sentetik_Kopya_Yoksa_Null_Donmeli()
    {
        TestDataLocator.SentetikKokuCoz(_gecici, Ortam()).Should().BeNull();
    }

    [Fact]
    public void TestDataLocator_Ortam_Degiskeni_Sentetik_Koku_Ezmeli()
    {
        var ozel = Directory.CreateDirectory(Path.Combine(_gecici, "ozel")).FullName;

        TestDataLocator.SentetikKokuCoz(_gecici, Ortam((TestDataLocator.SentetikOrtamDegiskeni, ozel))).Should().Be(ozel);
    }

    [Fact]
    public void TestDataLocator_Gercek_Veri_Klasoru_Yoksa_Null_Donmeli_Ve_Istisna_Atmamali()
    {
        TestDataLocator.GercekKokuCoz(Ortam(), Path.Combine(_gecici, "yok")).Should().BeNull();
        TestDataLocator.GercekKokuCoz(Ortam(), null).Should().BeNull();
    }

    [Fact]
    public void TestDataLocator_Gercek_Veri_Ortam_Degiskeni_Meta_Veriyi_Ezmeli()
    {
        var meta = Directory.CreateDirectory(Path.Combine(_gecici, "meta")).FullName;
        var ezme = Directory.CreateDirectory(Path.Combine(_gecici, "ezme")).FullName;

        TestDataLocator.GercekKokuCoz(Ortam(), meta).Should().Be(meta);
        TestDataLocator.GercekKokuCoz(Ortam((TestDataLocator.GercekOrtamDegiskeni, ezme)), meta).Should().Be(ezme);
    }

    [Fact]
    public void TestDataLocator_Gercek_Veri_Derleme_Ciktisina_Kopyalanmamali()
    {
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, "test-data", "real")).Should().BeFalse("AC-PILOT-08");
    }

    [Fact]
    public void TestDataLocator_Olmayan_Sentetik_Dosyada_Acik_Istisna_Atmali()
    {
        var act = () => TestDataLocator.Sentetik("pilot-1-concave-parcel", "yok.json");

        act.Should().Throw<FileNotFoundException>();
    }
}

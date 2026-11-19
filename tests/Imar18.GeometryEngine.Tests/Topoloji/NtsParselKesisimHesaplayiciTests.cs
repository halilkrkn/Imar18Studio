using FluentAssertions;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Topoloji;
using Xunit;

namespace Imar18.GeometryEngine.Tests.Topoloji;

/// <summary>
/// P7.3-B2: Yaklaşık Yer Krokisi için poligon kesişim alanı (NTS overlay). Bağımsız referans: eksene paralel dikdörtgen ve
/// dik üçgenlerin elle hesaplanan alanları. Sonuç 6 ondalık decimal'dir (dağıtım ağırlığı).
/// </summary>
public sealed class NtsParselKesisimHesaplayiciTests
{
    private static readonly NtsParselKesisimHesaplayici Hesap = new();

    private static GeodeticPoint P(decimal x, decimal y) => new(X: 4_400_000m + x, Y: 500_000m + y);

    /// <summary>Kuzey X ∈ [x1, x2], Doğu Y ∈ [y1, y2] dikdörtgeni (kapalı halka).</summary>
    private static IReadOnlyList<GeodeticPoint> D(decimal x1, decimal x2, decimal y1, decimal y2) =>
        [P(x1, y1), P(x1, y2), P(x2, y2), P(x2, y1), P(x1, y1)];

    [Fact]
    public void Kesisim_Iki_Dikdortgenin_Ortak_Alanini_Vermeli()
    {
        Hesap.KesisimAlaniM2([D(0, 10, 0, 20), D(0, 10, 12, 30)]).Should().Be(80.000000m);
    }

    [Fact]
    public void Kesisim_Uc_Halkanin_Ortak_Alanini_Vermeli()
    {
        Hesap.KesisimAlaniM2([D(0, 10, 0, 20), D(0, 10, 0, 12), D(4, 20, 6, 30)]).Should().Be(36.000000m);
    }

    [Fact]
    public void Kesisim_Tek_Halkada_Halkanin_Alanini_Vermeli()
    {
        Hesap.KesisimAlaniM2([D(0, 10, 0, 20)]).Should().Be(200m);
    }

    [Fact]
    public void Kesisim_Ayrik_Ve_Yalniz_Kenardan_Degen_Halkalarda_Sifir_Vermeli()
    {
        Hesap.KesisimAlaniM2([D(0, 10, 0, 10), D(20, 30, 20, 30)]).Should().Be(0m);
        Hesap.KesisimAlaniM2([D(0, 10, 0, 10), D(0, 10, 10, 20)]).Should().Be(0m, "ortak kenar alan değildir");
    }

    [Fact]
    public void Kesisim_Bos_Liste_Ve_Alan_Olusturmayan_Halkada_Sifir_Vermeli()
    {
        Hesap.KesisimAlaniM2([]).Should().Be(0m);
        Hesap.KesisimAlaniM2([D(0, 10, 0, 10), [P(0, 0), P(5, 5)]]).Should().Be(0m);
        Hesap.KesisimAlaniM2([D(0, 10, 0, 10), [P(0, 0), P(5, 5), P(10, 10), P(0, 0)]]).Should().Be(0m, "doğrusal halka alan oluşturmaz");
    }

    [Fact]
    public void Kesisim_Acik_Ve_Kapali_Yazilmis_Halka_Ayni_Sonucu_Vermeli()
    {
        IReadOnlyList<GeodeticPoint> acik = [P(0, 0), P(0, 20), P(10, 20), P(10, 0)];

        Hesap.KesisimAlaniM2([acik, D(0, 10, 12, 30)]).Should().Be(Hesap.KesisimAlaniM2([D(0, 10, 0, 20), D(0, 10, 12, 30)]));
    }

    [Fact]
    public void Kesisim_Ucgen_Alanini_Alti_Ondalikla_Vermeli()
    {
        // Dik üçgen: dik kenarlar 1 m ve 0.333 m → 0.1665 m².
        IReadOnlyList<GeodeticPoint> ucgen = [P(0, 0), P(0, 1), P(0.333m, 0)];

        var alan = Hesap.KesisimAlaniM2([ucgen, D(0, 10, 0, 10)]);

        alan.Should().Be(0.166500m);
        decimal.Round(alan, 6).Should().Be(alan);
    }

    [Fact]
    public void Kesisim_Kendini_Kesen_Halkada_Istisnasiz_Onarilmis_Alan_Vermeli()
    {
        // Papyon (bowtie): iki 25 m² üçgen; sıfır tamponla onarılır.
        IReadOnlyList<GeodeticPoint> papyon = [P(0, 0), P(10, 10), P(10, 0), P(0, 10), P(0, 0)];

        var alan = Hesap.KesisimAlaniM2([papyon, D(-5, 15, -5, 15)]);

        alan.Should().BeInRange(0m, 50m);
    }

    [Fact]
    public void Kesisim_Iptal_Edilirse_OperationCanceled_Atmali()
    {
        using var iptal = new CancellationTokenSource();
        iptal.Cancel();

        var act = () => Hesap.KesisimAlaniM2([D(0, 10, 0, 10), D(0, 10, 0, 10)], iptal.Token);

        act.Should().Throw<OperationCanceledException>();
    }
}

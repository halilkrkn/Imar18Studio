using FluentAssertions;
using Imar18.Domain.Geometry;
using Xunit;

namespace Imar18.Domain.Tests.Geometry;

/// <summary>P5 reviewer H2 — decimal tam aritmetikli kendini kesme denetimi (ERR-TOPO-001 kaynağı).</summary>
public sealed class PolygonTopolojisiTests
{
    private static PolygonRing Halka(params (decimal X, decimal Y)[] k) =>
        PolygonRing.FromOpen(k.Select(p => new GeodeticPoint(4_400_000m + p.X, 500_000m + p.Y)).ToArray()).Value;

    [Fact]
    public void Topoloji_Kare_Ve_Konkav_L_Basit_Olmali()
    {
        PolygonTopolojisi.KendiniKesiyorMu(Halka((0, 0), (0, 10), (10, 10), (10, 0))).Should().BeFalse();
        PolygonTopolojisi.KendiniKesiyorMu(Halka((0, 0), (0, 10), (5, 10), (5, 5), (10, 5), (10, 0))).Should().BeFalse();
    }

    [Fact]
    public void Topoloji_Papyon_Kendini_Kesmeli()
    {
        PolygonTopolojisi.KendiniKesiyorMu(Halka((0, 0), (10, 12), (10, 0), (0, 4))).Should().BeTrue();
    }

    [Fact]
    public void Topoloji_Bitisik_Olmayan_Kenarda_Kose_Dokunmasi_Kendini_Kesmeli()
    {
        // 5. köşe (5, 0) ilk kenarın (0,0)-(10,0) üzerine dokunur.
        PolygonTopolojisi.KendiniKesiyorMu(Halka((0, 0), (10, 0), (10, 10), (5, 10), (5, 0.000001m), (0, 10))).Should().BeFalse();
        PolygonTopolojisi.KendiniKesiyorMu(Halka((0, 0), (10, 0), (10, 10), (5, 10), (5, 0), (0, 10))).Should().BeTrue();
    }

    [Fact]
    public void Topoloji_Geri_Katlanan_Bitisik_Kenar_Kendini_Kesmeli()
    {
        PolygonTopolojisi.KendiniKesiyorMu(Halka((0, 0), (0, 10), (10, 10), (10, 0), (10, 5))).Should().BeTrue();
    }

    [Fact]
    public void Topoloji_Dogrusal_Ara_Kose_Basit_Kalmali()
    {
        PolygonTopolojisi.KendiniKesiyorMu(Halka((0, 0), (0, 5), (0, 10), (10, 10), (10, 0))).Should().BeFalse();
    }
}

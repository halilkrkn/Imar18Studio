using Imar18.Domain.Geometry;
using NetTopologySuite.Geometries;

namespace Imar18.GeometryEngine.Topoloji;

/// <summary>
/// P7.3-B2: <see cref="IParselKesisimHesaplayici"/> NTS uygulaması. Halkalar ilk halkanın ilk köşesine göre yerel orijine
/// alınır (<see cref="CoordinateAxisSwapAdapter"/>; çift hassasiyet kaybını önler), sırayla kesiştirilir. Kendini kesen halka
/// sıfır tamponla onarılır; alan oluşturmayan halka sonucu 0 yapar. Alan kapıda 6 ondalığa (AwayFromZero) yuvarlanan decimal'dir;
/// NTS tipleri dışarı çıkmaz.
/// </summary>
public sealed class NtsParselKesisimHesaplayici : IParselKesisimHesaplayici
{
    private static readonly GeometryFactory Factory = new();

    public decimal KesisimAlaniM2(IReadOnlyList<IReadOnlyList<GeodeticPoint>> halkalar, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(halkalar);
        cancellationToken.ThrowIfCancellationRequested();
        if (halkalar.Count == 0 || halkalar.Any(h => h is null || h.Count == 0))
        {
            return 0m;
        }

        var orijin = halkalar[0][0];
        Geometry? sonuc = null;
        foreach (var h in halkalar)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var p = Poligon(h, orijin);
            if (p is null)
            {
                return 0m;
            }

            sonuc = sonuc is null ? p : (sonuc.EnvelopeInternal.Intersects(p.EnvelopeInternal) ? sonuc.Intersection(p) : null);
            if (sonuc is null || sonuc.IsEmpty)
            {
                return 0m;
            }
        }

        var alan = sonuc!.Area;
        return double.IsFinite(alan) && alan > 0d
            ? decimal.Round((decimal)alan, 6, MidpointRounding.AwayFromZero)
            : 0m;
    }

    private static Geometry? Poligon(IReadOnlyList<GeodeticPoint> koseler, GeodeticPoint orijin)
    {
        var acik = koseler.Count > 1 && koseler[0] == koseler[^1] ? koseler.Take(koseler.Count - 1).ToArray() : koseler.ToArray();
        if (acik.Length < 3)
        {
            return null;
        }

        var coords = new Coordinate[acik.Length + 1];
        for (var i = 0; i < acik.Length; i++)
        {
            coords[i] = CoordinateAxisSwapAdapter.ToNts(acik[i], orijin);
        }

        coords[^1] = coords[0].Copy();
        Geometry poligon = Factory.CreatePolygon(coords);
        if (!poligon.IsValid)
        {
            poligon = poligon.Buffer(0d);
        }

        return poligon.IsEmpty || poligon.Area <= 0d ? null : poligon;
    }
}

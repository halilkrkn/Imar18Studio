using Imar18.Application.Cad;
using Imar18.Domain.Geometry;

namespace Imar18.Presentation.Avalonia.Canvas;

/// <summary>Tuvalde cizilen cizgi (rol, etiket ve Decimal Gauss alaniyla).</summary>
public sealed record ScenePolyline(
    string LayerName,
    CadLayerRole Role,
    string Label,
    IReadOnlyList<GeodeticPoint> Vertices,
    bool IsClosed,
    decimal? AreaM2);

/// <summary>
/// Tuval sahnesi (P4.3): <see cref="CadLayerDocument"/> + katman rol eslemesinden uretilir. Alanlar Domain
/// <see cref="GaussAreaCalculator"/> ile hesaplanir (gorunum hesap yapmaz, yalnizca Domain sonucunu gosterir).
/// </summary>
public sealed class CanvasScene
{
    private CanvasScene(IReadOnlyList<ScenePolyline> polylines, (GeodeticPoint Min, GeodeticPoint Max)? bounds)
    {
        Polylines = polylines;
        Bounds = bounds;
    }

    public static CanvasScene Empty { get; } = new([], null);

    public IReadOnlyList<ScenePolyline> Polylines { get; }

    /// <summary>Tum koselerin dunya kutusu (bos sahnede null).</summary>
    public (GeodeticPoint Min, GeodeticPoint Max)? Bounds { get; }

    public static CanvasScene From(CadLayerDocument document, CadLayerRoleMap roles)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(roles);

        var list = new List<ScenePolyline>(document.Polylines.Count);
        decimal? minX = null, minY = null, maxX = null, maxY = null;
        foreach (var p in document.Polylines)
        {
            decimal? area = null;
            if (p.IsClosed)
            {
                var ring = PolygonRing.FromClosed(p.Vertices);
                if (ring.IsSuccess)
                {
                    area = GaussAreaCalculator.Compute(ring.Value).AreaM2;
                }
            }

            foreach (var v in p.Vertices)
            {
                minX = minX is null ? v.X : Math.Min(minX.Value, v.X);
                minY = minY is null ? v.Y : Math.Min(minY.Value, v.Y);
                maxX = maxX is null ? v.X : Math.Max(maxX.Value, v.X);
                maxY = maxY is null ? v.Y : Math.Max(maxY.Value, v.Y);
            }

            list.Add(new ScenePolyline(p.LayerName, roles.Resolve(p.LayerName), p.Label, p.Vertices, p.IsClosed, area));
        }

        (GeodeticPoint, GeodeticPoint)? bounds = minX is null
            ? null
            : (new GeodeticPoint(X: minX.Value, Y: minY!.Value), new GeodeticPoint(X: maxX!.Value, Y: maxY!.Value));
        return new CanvasScene(list.AsReadOnly(), bounds);
    }

    /// <summary>Noktayi iceren en kucuk alanli kapali cizgiyi verir (yoksa null).</summary>
    public ScenePolyline? HitTest(GeodeticPoint world) => Polylines
        .Where(p => p.IsClosed && CadLabelMatcher.StrictlyInside(p.Vertices, world))
        .OrderBy(p => p.AreaM2 ?? decimal.MaxValue)
        .FirstOrDefault();
}

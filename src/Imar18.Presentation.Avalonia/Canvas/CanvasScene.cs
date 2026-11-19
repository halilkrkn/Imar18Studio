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
    private readonly (decimal MinX, decimal MinY, decimal MaxX, decimal MaxY)[] _kutular;

    private CanvasScene(IReadOnlyList<ScenePolyline> polylines, (GeodeticPoint Min, GeodeticPoint Max)? bounds)
    {
        Polylines = polylines;
        Bounds = bounds;

        // P7.3-A (reviewer M2): çizgi başına dünya kutusu bir kez hesaplanır; isabet taraması kutu dışını parça parça gezmez.
        _kutular = new (decimal, decimal, decimal, decimal)[polylines.Count];
        for (var i = 0; i < polylines.Count; i++)
        {
            var v = polylines[i].Vertices;
            _kutular[i] = v.Count == 0
                ? (decimal.MaxValue, decimal.MaxValue, decimal.MinValue, decimal.MinValue)
                : (v.Min(k => k.X), v.Min(k => k.Y), v.Max(k => k.X), v.Max(k => k.Y));
        }
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

    /// <summary>
    /// P7.3-A (SAHA-005): tuvalden katman → rol atamasi icin isabet. Once kenari noktaya <paramref name="toleransM"/> (dahil)
    /// kadar yakin olan en yakin cizgi (acik cizgiler ve kapali poligon kenarlari) verilir; esitlikte daha kucuk alanli/ilk
    /// gelen. Esit uzaklikta acik cizgi kapali poligona tercih edilir (reviewer M1: Netcad'de ada kenari cogu kez parsel
    /// kenarinin tam ustune cizilir; aksi halde hic secilemezdi), sonra kucuk alanli. Yakin kenar yoksa noktayi iceren en
    /// kucuk kapali poligon (<see cref="HitTest"/>). Uzaklik karesi decimal ile karsilastirilir (karekok yok, kayan nokta
    /// yok); toleransla genisletilmis kutusu disindaki cizgiler atlanir.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Tolerans negatifse.</exception>
    public ScenePolyline? KatmanIsabeti(GeodeticPoint world, decimal toleransM)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(toleransM);
        var esik = toleransM * toleransM;
        ScenePolyline? enYakin = null;
        var enKucuk = decimal.MaxValue;
        for (var j = 0; j < Polylines.Count; j++)
        {
            var (minX, minY, maxX, maxY) = _kutular[j];
            if (world.X < minX - toleransM || world.X > maxX + toleransM || world.Y < minY - toleransM || world.Y > maxY + toleransM)
            {
                continue;
            }

            var p = Polylines[j];
            for (var i = 1; i < p.Vertices.Count; i++)
            {
                var d = UzaklikKaresi(world, p.Vertices[i - 1], p.Vertices[i]);
                if (d <= esik && (d < enKucuk || (d == enKucuk && OnceGelir(p, enYakin!))))
                {
                    enKucuk = d;
                    enYakin = p;
                }
            }
        }

        return enYakin ?? HitTest(world);
    }

    /// <summary>Esit uzaklikta oncelik: acik cizgi, sonra kucuk alanli kapali poligon (ilk gelen korunur).</summary>
    private static bool OnceGelir(ScenePolyline aday, ScenePolyline mevcut) =>
        aday.IsClosed != mevcut.IsClosed
            ? !aday.IsClosed
            : (aday.AreaM2 ?? decimal.MaxValue) < (mevcut.AreaM2 ?? decimal.MaxValue);

    /// <summary>Noktanin [a, b] dogru parcasina uzakliginin karesi (m²); a = b ise noktaya uzaklik.</summary>
    internal static decimal UzaklikKaresi(GeodeticPoint n, GeodeticPoint a, GeodeticPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var uzunluk2 = (dx * dx) + (dy * dy);
        decimal px, py;
        if (uzunluk2 == 0m)
        {
            px = a.X;
            py = a.Y;
        }
        else
        {
            var t = Math.Clamp((((n.X - a.X) * dx) + ((n.Y - a.Y) * dy)) / uzunluk2, 0m, 1m);
            px = a.X + (t * dx);
            py = a.Y + (t * dy);
        }

        var ex = n.X - px;
        var ey = n.Y - py;
        return (ex * ex) + (ey * ey);
    }
}

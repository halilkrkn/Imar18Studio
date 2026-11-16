using Imar18.Domain.Diagnostics;

namespace Imar18.Domain.Geometry;

/// <summary>Halka yonu (kuzey yukari harita gorunumunde).</summary>
public enum RingOrientation
{
    Clockwise,
    CounterClockwise,
}

/// <summary>
/// Gecerli basit poligon dis halkasi (decimal, kapanis tekrari saklanmaz). Tek giris
/// <see cref="FromClosed"/>/<see cref="FromOpen"/>; her ornek en az 3 farkli kose, ardisik tekrar yok,
/// sifirdan farkli alan ve decimal kesinlik zarfi icinde olmayi garanti eder.
/// Kendi uzerinde kesisme (ERR-TOPO-001) Domain'de degil GeometryEngine'de (NTS IsValid) denetlenir.
/// </summary>
public sealed class PolygonRing
{
    /// <summary>Yerel koordinat mutlak sinir (m): bu zarfta Gauss toplamlari decimal'de tam kesindir.</summary>
    public const decimal MaxLocalCoordinateM = 1_000_000m;

    /// <summary>Izin verilen en fazla ondalik basamak.</summary>
    public const int MaxScale = 6;

    /// <summary>Izin verilen en fazla kose sayisi.</summary>
    public const int MaxVertexCount = 5_000;

    private PolygonRing(GeodeticPoint[] vertices, decimal signedDoubleArea)
    {
        Vertices = Array.AsReadOnly(vertices);
        SignedDoubleArea = signedDoubleArea;
    }

    /// <summary>Kose listesi (kapanis tekrari yok).</summary>
    public IReadOnlyList<GeodeticPoint> Vertices { get; }

    internal decimal SignedDoubleArea { get; }

    /// <summary>Ilk ve son kosesi esit kapali halkadan olusturur.</summary>
    public static Result<PolygonRing> FromClosed(IReadOnlyList<GeodeticPoint> closedRing)
    {
        ArgumentNullException.ThrowIfNull(closedRing);

        if (closedRing.Count == 0)
        {
            return Fail(ErrorCodes.TopoDejenerePoligon, "{reason}", "ring is empty");
        }

        if (closedRing[0] != closedRing[^1])
        {
            return Fail(ErrorCodes.TopoAcikPoligon, "{parcelId}", "input ring");
        }

        return FromOpen(closedRing.Take(closedRing.Count - 1).ToArray());
    }

    /// <summary>Kapanis tekrari olmayan kose dizisinden olusturur.</summary>
    public static Result<PolygonRing> FromOpen(IReadOnlyList<GeodeticPoint> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        var v = vertices.ToArray();
        if (v.Length < 3)
        {
            return Fail(ErrorCodes.TopoDejenerePoligon, "{reason}", "fewer than 3 vertices");
        }

        if (v.Length > MaxVertexCount)
        {
            return Fail(ErrorCodes.GirdKoordinatKesinlikZarfiDisinda, "{reason}", "too many vertices");
        }

        var origin = v[0];
        for (var i = 0; i < v.Length; i++)
        {
            var p = v[i];
            if (decimal.Round(p.X, MaxScale) != p.X || decimal.Round(p.Y, MaxScale) != p.Y)
            {
                return Fail(ErrorCodes.GirdKoordinatKesinlikZarfiDisinda, "{reason}", $"more than {MaxScale} decimals at vertex #{i + 1}");
            }

            if (Math.Abs(p.X - origin.X) >= MaxLocalCoordinateM || Math.Abs(p.Y - origin.Y) >= MaxLocalCoordinateM)
            {
                return Fail(ErrorCodes.GirdKoordinatKesinlikZarfiDisinda, "{reason}", $"local offset too large at vertex #{i + 1}");
            }

            if (p == v[(i + 1) % v.Length])
            {
                return Fail(ErrorCodes.TopoDejenerePoligon, "{reason}", $"consecutive duplicate at vertex #{i + 1}");
            }
        }

        var s = GaussAreaCalculator.SignedDoubleArea(v);
        if (s == 0m)
        {
            return Fail(ErrorCodes.TopoDejenerePoligon, "{reason}", "zero area (collinear vertices)");
        }

        return Result<PolygonRing>.Success(new PolygonRing(v, s));
    }

    private static Result<PolygonRing> Fail(string code, string placeholder, string detail)
    {
        var error = ErrorCatalog.Get(code);
        return Result<PolygonRing>.Failure(
            error with { TechnicalMessage = error.TechnicalMessage.Replace(placeholder, detail, StringComparison.Ordinal) });
    }
}

/// <summary>
/// Gauss alan sonucu. <see cref="SignedDoubleAreaM2"/> = S1 = S2 (isaretli 2A, jeodezik X/Y ile);
/// <see cref="AreaM2Exact"/> yuvarlanmamis, <see cref="AreaM2"/> 2 ondalik AwayFromZero (spec 3.2 madde 1).
/// </summary>
public sealed record GaussArea(decimal SignedDoubleAreaM2, decimal AreaM2Exact, decimal AreaM2, RingOrientation Orientation);

/// <summary>
/// Ilk koseye otelemeli yerel orijin Gauss (Shoelace) alani (spec 4.1):
/// X'i = Xi - X1, Y'i = Yi - Y1; S1 = Σ X'i (Y'i+1 - Y'i-1), S2 = Σ Y'i (X'i-1 - X'i+1); A = |S1| / 2.
/// Iki formul ayni toplami vermek zorundadir (cift kontrol). S1 &gt; 0 ise halka haritada saat yonundedir.
/// </summary>
public static class GaussAreaCalculator
{
    public static GaussArea Compute(PolygonRing ring)
    {
        ArgumentNullException.ThrowIfNull(ring);

        var s1 = ring.SignedDoubleArea;
        var exact = Math.Abs(s1) / 2m;
        return new GaussArea(
            s1,
            exact,
            decimal.Round(exact, 2, MidpointRounding.AwayFromZero),
            s1 > 0m ? RingOrientation.Clockwise : RingOrientation.CounterClockwise);
    }

    /// <summary>Isaretli 2A; S1 ve S2 farkliysa (kesinlik zarfi ihlali) istisna atar.</summary>
    internal static decimal SignedDoubleArea(IReadOnlyList<GeodeticPoint> v)
    {
        var n = v.Count;
        var x1 = v[0].X;
        var y1 = v[0].Y;
        var s1 = 0m;
        var s2 = 0m;
        for (var i = 0; i < n; i++)
        {
            var prev = v[(i - 1 + n) % n];
            var cur = v[i];
            var next = v[(i + 1) % n];
            var xi = cur.X - x1;
            var yi = cur.Y - y1;
            s1 += xi * ((next.Y - y1) - (prev.Y - y1));
            s2 += yi * ((prev.X - x1) - (next.X - x1));
        }

        if (s1 != s2)
        {
            throw new InvalidOperationException("Gauss double-check mismatch (S1 != S2); precision envelope violated.");
        }

        return s1;
    }
}

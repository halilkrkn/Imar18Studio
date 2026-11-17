using System.Globalization;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using NetTopologySuite.Geometries;

namespace Imar18.GeometryEngine.Subdivision;

/// <summary>
/// Iki donguli bisection ile tek kesim (spec 4.2, TC-GEOM-001, AC-PILOT-01).
/// <list type="bullet">
/// <item>Dis dongu: cepheye gore kesim acisi tercih acisindan baslayip adim adim (once kucuk aci) [min, max] icinde taranir.</item>
/// <item>Ic dongu: kesim cizgisi cephe kenari boyunca t ∈ [0, L] kaydirilir; A(t) monoton artar, bisection ile hedefe yakinsar.</item>
/// <item>Halka, kesim yari duzlemine gore iki yarıya ayrilir (kesim dogrusu uzerindeki koseler iki yarida da kalir,
/// dogru uzerindeki geri donen cikintilar temizlenir); kesim koseleri MUTLAK koordinatta 1 mm'ye kuantize edilir.</item>
/// <item>Kuantizasyon sonrasi NTS IsValid ve Decimal Gauss alani (parca, kalan ve parca+kalan−ada) toleransla yeniden dogrulanir;
/// aksi halde sonuc dondurulmez.</item>
/// <item>Kesim dogrusu halkayi ikiden fazla noktada keserse (konkav ada, MultiPolygon) o aci reddedilir ve tarama surer
/// (SORU-023 karari, 17 Kasim 2026): [min, max] icinde tek parcali gecerli cozum bulunan ilk aci kabul edilir; hicbir acida
/// tek parcali cozum yoksa ve en az bir aci MultiPolygon urettiyse <c>ERR-TOPO-004</c> (muhendis incelemesi) doner.
/// Dogruya yalnizca teget dokunan koseler kesisim sayilmaz.</item>
/// </list>
/// Motor durumsuzdur; "geri alma" hicbir kismi sonuc dondurmemek demektir.
/// </summary>
public static class BisectionEngine
{
    /// <summary>Spec 4.2: cepheye diklik acisi taramasi alt siniri (derece).</summary>
    public const decimal SpecMinAngleDeg = 75m;

    /// <summary>Spec 4.2: cepheye diklik acisi taramasi ust siniri (derece).</summary>
    public const decimal SpecMaxAngleDeg = 105m;

    /// <summary>Uygulama guvenlik siniri (mevzuat degeri degil): en kucuk aci adimi; en fazla ~30 001 aci denenir.</summary>
    public const decimal MinAngleStepDeg = 0.001m;

    /// <summary>Uygulama guvenlik siniri (mevzuat degeri degil): ic dongu iterasyon ust siniri (double bisection ~60 adimda tukenir).</summary>
    public const int MaxInnerIterationsLimit = 200;

    /// <summary>Kesim dogrusuna uzaklik esigi (m): bunun altindaki koseler "dogru uzerinde" sayilir.</summary>
    private const double OnLineEpsilonM = 1e-9;

    public static BisectionOutcome Cut(BisectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Block);
        ArgumentNullException.ThrowIfNull(request.Options);
        cancellationToken.ThrowIfCancellationRequested();

        var block = request.Block;
        var blockArea = GaussAreaCalculator.Compute(block);

        var reason = Validate(request, blockArea);
        if (reason is not null)
        {
            return Stop(Error(ErrorCodes.PlanBolmeGirdisiGecersiz, ("{reason}", reason)), []);
        }

        var origin = block.Vertices[0];
        if (!NtsValidity.IsValid(block.Vertices, origin))
        {
            return Stop(Error(ErrorCodes.TopoKendiUzerindeKesisim, ("{parcelId}", "block")), []);
        }

        var local = block.Vertices.Select(v => CoordinateAxisSwapAdapter.ToNts(v, origin)).ToArray();
        var attempts = new List<AngleAttempt>();
        AppError? best = null;
        var bestRank = -1;

        foreach (var angle in AngleSequence(request.Options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attempt = TryAngle(request, blockArea, local, origin, angle, cancellationToken);
            attempts.Add(new AngleAttempt(angle, attempt.Outcome, attempt.Iterations));

            if (attempt.Outcome == AttemptOutcome.Accepted)
            {
                return new BisectionOutcome(Result<BisectionCut>.Success(attempt.Cut!), attempts.AsReadOnly());
            }

            var rank = Rank(attempt.Outcome);
            if (rank > bestRank)
            {
                bestRank = rank;
                best = attempt.Error;
            }
        }

        return Stop(best ?? Error(ErrorCodes.PlanHedefAciAraligindaErisilemez), attempts);
    }

    private static string? Validate(BisectionRequest r, GaussArea blockArea)
    {
        var o = r.Options;
        if (r.TargetAreaM2 <= 0m || r.TargetAreaM2 >= blockArea.AreaM2Exact)
        {
            return "target area must be greater than 0 and less than block area";
        }

        if (r.FrontageEdgeIndex < 0 || r.FrontageEdgeIndex >= r.Block.Vertices.Count)
        {
            return "frontage edge index out of range";
        }

        if (o.MinAngleDeg < SpecMinAngleDeg || o.MaxAngleDeg > SpecMaxAngleDeg
            || o.MinAngleDeg > o.PreferredAngleDeg || o.PreferredAngleDeg > o.MaxAngleDeg)
        {
            return "angles must satisfy 75 <= min <= preferred <= max <= 105";
        }

        if (o.AngleStepDeg < MinAngleStepDeg)
        {
            return "angle step must be at least 0.001 degree";
        }

        if (o.ConvergenceToleranceM2 <= 0m || o.PostQuantizationAreaToleranceM2 <= 0m
            || o.ConvergenceToleranceM2 > o.PostQuantizationAreaToleranceM2)
        {
            return "tolerances must be positive and convergence <= post-quantization tolerance";
        }

        if (o.MaxInnerIterations <= 0 || o.MaxInnerIterations > MaxInnerIterationsLimit)
        {
            return "max inner iterations must be between 1 and 200";
        }

        if (r.Block.Vertices.Any(v => !CoordinateQuantizer.IsOnGrid(v)))
        {
            return "block vertices must be on the 1 mm grid";
        }

        return null;
    }

    /// <summary>Tercih, tercih−k·adim, tercih+k·adim, ... (esit sapmada once kucuk aci — SORU-022).</summary>
    private static IEnumerable<decimal> AngleSequence(BisectionOptions o)
    {
        yield return o.PreferredAngleDeg;
        for (var k = 1; ; k++)
        {
            var lo = o.PreferredAngleDeg - (k * o.AngleStepDeg);
            var hi = o.PreferredAngleDeg + (k * o.AngleStepDeg);
            if (lo < o.MinAngleDeg && hi > o.MaxAngleDeg)
            {
                yield break;
            }

            if (lo >= o.MinAngleDeg)
            {
                yield return lo;
            }

            if (hi <= o.MaxAngleDeg)
            {
                yield return hi;
            }
        }
    }

    /// <summary>
    /// Tum acilar basarisizsa dondurulecek hatanin onceligi. MultiPolygon en ustundedir (SORU-023: hicbir acida tek parcali
    /// cozum yoksa ERR-TOPO-004); digerleri en ileri asamaya ulasan denemeye gore siralanir.
    /// </summary>
    private static int Rank(AttemptOutcome outcome) => outcome switch
    {
        AttemptOutcome.MultiPolygon => 6,
        AttemptOutcome.QuantizationTopologyBroken => 5,
        AttemptOutcome.QuantizationAreaDeviation => 4,
        AttemptOutcome.DegeneratePiece => 3,
        AttemptOutcome.NotConverged => 2,
        _ => 1,
    };

    private sealed record Attempt(AttemptOutcome Outcome, int Iterations, BisectionCut? Cut, AppError? Error);

    private static Attempt TryAngle(
        BisectionRequest r, GaussArea blockArea, Coordinate[] local, GeodeticPoint origin, decimal angle, CancellationToken ct)
    {
        var n = local.Length;
        var k = r.FrontageEdgeIndex;
        var a = local[k];
        var b = local[(k + 1) % n];
        var length = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
        var ux = (b.X - a.X) / length;
        var uy = (b.Y - a.Y) / length;

        // Ic normal: haritada (= NTS duzleminde) saat yonu tersi halkada sola, saat yonunde saga.
        var (nx, ny) = blockArea.Orientation == RingOrientation.CounterClockwise ? (-uy, ux) : (uy, -ux);

        double sin, cos;
        if (angle == 90m)
        {
            (sin, cos) = (1d, 0d); // cos(pi/2) != 0 kayan noktada; tam dik kesim icin ozel durum
        }
        else
        {
            var rad = (double)angle * Math.PI / 180d;
            (sin, cos) = (Math.Sin(rad), Math.Cos(rad));
        }

        // Kesim cizgisi F(t) = a + t·u noktasindan d = cos·u + sin·n yonunde gecer; m ⟂ d.
        // s_i(t) = (V_i - F(t))·m = c_i - t·sin; parca tarafi s < 0 (Vk'yi icerir), A(t) t ile monoton artar.
        var mx = (sin * ux) - (cos * nx);
        var my = (sin * uy) - (cos * ny);
        var c = new double[n];
        for (var i = 0; i < n; i++)
        {
            c[i] = ((local[i].X - a.X) * mx) + ((local[i].Y - a.Y) * my);
        }

        var target = (double)r.TargetAreaM2;
        var tol = (double)r.Options.ConvergenceToleranceM2;

        if (ClippedArea(local, c, 0d, sin) > target || ClippedArea(local, c, length, sin) < target)
        {
            return new Attempt(AttemptOutcome.TargetUnreachable, 0, null, Error(ErrorCodes.PlanHedefAciAraligindaErisilemez));
        }

        double lo = 0d, hi = length, t = double.NaN;
        var iterations = 0;
        while (iterations < r.Options.MaxInnerIterations)
        {
            ct.ThrowIfCancellationRequested();
            var mid = (lo + hi) / 2d;
            if (mid <= lo || mid >= hi)
            {
                break; // double araligi tukendi; tolerans bu hassasiyette saglanamaz
            }

            iterations++;
            var area = ClippedArea(local, c, mid, sin);
            if (Math.Abs(area - target) <= tol)
            {
                t = mid;
                break;
            }

            if (area < target)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        if (double.IsNaN(t))
        {
            return NotConverged(iterations);
        }

        return BuildCut(r, blockArea, local, origin, c, t, sin, angle, iterations);
    }

    private static Attempt NotConverged(int iterations) =>
        new(AttemptOutcome.NotConverged, iterations, null,
            Error(ErrorCodes.PlanHedefAlanaYakinsanamadi, ("{iterations}", iterations.ToString(CultureInfo.InvariantCulture))));

    /// <summary>Yari halka kalemi: kose/kesisim noktasi, kesim dogrusu uzerinde mi, kaynak kenar/kose indeksi.</summary>
    private readonly record struct RingItem(GeodeticPoint Point, bool OnLine, int SourceIndex, bool IsExistingVertex);

    private static Attempt BuildCut(
        BisectionRequest r, GaussArea blockArea, Coordinate[] local, GeodeticPoint origin,
        double[] c, double t, double sin, decimal angle, int iterations)
    {
        var n = local.Length;
        var k = r.FrontageEdgeIndex;
        var s = new double[n];
        var sign = new int[n];
        for (var i = 0; i < n; i++)
        {
            s[i] = c[i] - (t * sin);
            sign[i] = s[i] < -OnLineEpsilonM ? -1 : s[i] > OnLineEpsilonM ? 1 : 0;
        }

        // Kesisim sayisi: dogru uzerindeki (0) koseler atlanarak sifirdan farkli isaret degisimleri (teget kose kesisim degildir).
        var nonZero = sign.Where(x => x != 0).ToArray();
        var transitions = 0;
        for (var i = 0; i < nonZero.Length; i++)
        {
            if (nonZero[i] != nonZero[(i + 1) % nonZero.Length])
            {
                transitions++;
            }
        }

        if (transitions > 2)
        {
            return new Attempt(AttemptOutcome.MultiPolygon, iterations, null,
                Error(ErrorCodes.TopoKonkavKesimCokParcali, ("{angle}", angle.ToString(CultureInfo.InvariantCulture))));
        }

        // Cephe kenari kesimi kesin (Vk parca, Vk+1 kalan tarafinda) olmali (reviewer L1).
        if (transitions != 2 || sign[k] >= 0 || sign[(k + 1) % n] <= 0)
        {
            return NotConverged(iterations);
        }

        var vertices = r.Block.Vertices;
        var piece = new List<RingItem>();
        var remainder = new List<RingItem>();
        GeodeticPoint? cutStart = null;
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            var onLine = sign[i] == 0;
            if (sign[i] <= 0)
            {
                piece.Add(new RingItem(vertices[i], onLine, i, true));
            }

            if (sign[i] >= 0)
            {
                remainder.Add(new RingItem(vertices[i], onLine, i, true));
            }

            if (sign[i] * sign[j] < 0)
            {
                var lambda = s[i] / (s[i] - s[j]);
                var x = CoordinateAxisSwapAdapter.FromNts(new Coordinate(
                    local[i].X + (lambda * (local[j].X - local[i].X)),
                    local[i].Y + (lambda * (local[j].Y - local[i].Y))), origin);
                var item = new RingItem(x, true, i, false);
                piece.Add(item);
                remainder.Add(item);
                if (i == k)
                {
                    cutStart = x;
                }
            }
        }

        var pieceItems = CollapseCutLineRuns(Dedupe(piece));
        var remainderItems = CollapseCutLineRuns(Dedupe(remainder));

        // CutEnd: parca halkasinda CutStart ile ayni dogru-uzeri dizisinin diger ucu.
        var startIndex = pieceItems.FindIndex(p => p.Point == cutStart);
        if (startIndex < 0 || !pieceItems[(startIndex + 1) % pieceItems.Count].OnLine)
        {
            return Degenerate(iterations, "cut segment collapsed");
        }

        var cutEndItem = pieceItems[(startIndex + 1) % pieceItems.Count];

        var pieceRing = PolygonRing.FromOpen(pieceItems.Select(p => p.Point).ToList());
        var remainderRing = PolygonRing.FromOpen(remainderItems.Select(p => p.Point).ToList());
        if (pieceRing.IsFailure || remainderRing.IsFailure)
        {
            return Degenerate(iterations, "piece or remainder has no area after quantization");
        }

        if (!NtsValidity.IsValid(pieceRing.Value.Vertices, origin) || !NtsValidity.IsValid(remainderRing.Value.Vertices, origin))
        {
            return new Attempt(AttemptOutcome.QuantizationTopologyBroken, iterations, null,
                Error(ErrorCodes.KuantKoseCakismasi, ("{parcelId}", "cut piece")));
        }

        var pieceArea = GaussAreaCalculator.Compute(pieceRing.Value);
        var remainderArea = GaussAreaCalculator.Compute(remainderRing.Value);
        var tolerance = r.Options.PostQuantizationAreaToleranceM2;
        var pieceDeviation = pieceArea.AreaM2Exact - r.TargetAreaM2;
        var remainderDeviation = remainderArea.AreaM2Exact - (blockArea.AreaM2Exact - r.TargetAreaM2);
        var blockDeviation = pieceArea.AreaM2Exact + remainderArea.AreaM2Exact - blockArea.AreaM2Exact;
        var worst = new[] { pieceDeviation, remainderDeviation, blockDeviation }.MaxBy(Math.Abs);
        if (Math.Abs(worst) > tolerance)
        {
            return new Attempt(AttemptOutcome.QuantizationAreaDeviation, iterations, null,
                Error(ErrorCodes.KuantAlanSapmasi,
                    ("{delta}", worst.ToString(CultureInfo.InvariantCulture)),
                    ("{tolerance}", tolerance.ToString(CultureInfo.InvariantCulture))));
        }

        var start = cutStart!.Value;
        var frontageStart = vertices[k];
        var dx = (double)(start.X - frontageStart.X);
        var dy = (double)(start.Y - frontageStart.Y);
        var frontage = (decimal)Math.Round(Math.Sqrt((dx * dx) + (dy * dy)), CoordinateQuantizer.Scale, MidpointRounding.AwayFromZero);

        var cut = new BisectionCut(
            pieceRing.Value, remainderRing.Value, start, cutEndItem.Point,
            k, cutEndItem.SourceIndex, cutEndItem.IsExistingVertex,
            angle, frontage, pieceArea, remainderArea,
            pieceDeviation, remainderDeviation, blockDeviation, iterations);
        return new Attempt(AttemptOutcome.Accepted, iterations, cut, null);
    }

    private static Attempt Degenerate(int iterations, string reason) =>
        new(AttemptOutcome.DegeneratePiece, iterations, null, Error(ErrorCodes.TopoDejenerePoligon, ("{reason}", reason)));

    /// <summary>Ardisik (dairesel) tekrarlari birlestirir; kuantizasyonla cakisan noktada mevcut kose bilgisi korunur.</summary>
    private static List<RingItem> Dedupe(List<RingItem> ring)
    {
        var result = new List<RingItem>(ring.Count);
        foreach (var item in ring)
        {
            if (result.Count > 0 && result[^1].Point == item.Point)
            {
                result[^1] = Merge(result[^1], item);
                continue;
            }

            result.Add(item);
        }

        while (result.Count > 1 && result[0].Point == result[^1].Point)
        {
            result[0] = Merge(result[0], result[^1]);
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    private static RingItem Merge(RingItem a, RingItem b)
    {
        var existing = a.IsExistingVertex ? a : b.IsExistingVertex ? b : a;
        return existing with { OnLine = a.OnLine || b.OnLine };
    }

    /// <summary>
    /// Kesim dogrusu uzerindeki ardisik (dairesel) nokta dizilerinde yalnizca ilk ve son nokta tutulur.
    /// Tek kesisimli (iki gecisli) bolmede her yarinin dogru uzerindeki siniri tek dogru parcasidir; aradaki noktalar
    /// ya ayni dogrultuda gereksizdir ya da geri donen sifir genislikli cikintidir (reviewer H2).
    /// </summary>
    private static List<RingItem> CollapseCutLineRuns(List<RingItem> ring)
    {
        var firstOff = ring.FindIndex(p => !p.OnLine);
        if (firstOff < 0)
        {
            return ring; // tum noktalar dogru uzerinde: dejenere, PolygonRing reddeder
        }

        var rotated = ring.Skip(firstOff).Concat(ring.Take(firstOff)).ToList();
        var result = new List<RingItem>(rotated.Count);
        var i = 0;
        while (i < rotated.Count)
        {
            if (!rotated[i].OnLine)
            {
                result.Add(rotated[i]);
                i++;
                continue;
            }

            var runStart = i;
            while (i < rotated.Count && rotated[i].OnLine)
            {
                i++;
            }

            result.Add(rotated[runStart]);
            if (i - 1 > runStart)
            {
                result.Add(rotated[i - 1]);
            }
        }

        return result;
    }

    /// <summary>Halkanin s(t) &lt;= 0 yari duzlemindeki kismi alan (Sutherland–Hodgman; konkavda da alan dogrudur).</summary>
    private static double ClippedArea(Coordinate[] local, double[] c, double t, double sin)
    {
        var n = local.Length;
        var xs = new List<double>(n + 4);
        var ys = new List<double>(n + 4);
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            var si = c[i] - (t * sin);
            var sj = c[j] - (t * sin);
            if (si <= 0d)
            {
                xs.Add(local[i].X);
                ys.Add(local[i].Y);
            }

            if ((si < 0d && sj > 0d) || (si > 0d && sj < 0d))
            {
                var lambda = si / (si - sj);
                xs.Add(local[i].X + (lambda * (local[j].X - local[i].X)));
                ys.Add(local[i].Y + (lambda * (local[j].Y - local[i].Y)));
            }
        }

        var m = xs.Count;
        if (m < 3)
        {
            return 0d;
        }

        var twice = 0d;
        for (var i = 0; i < m; i++)
        {
            var j = (i + 1) % m;
            twice += (xs[i] * ys[j]) - (xs[j] * ys[i]);
        }

        return Math.Abs(twice) / 2d;
    }

    private static AppError Error(string code, params (string Placeholder, string Value)[] details)
    {
        var error = ErrorCatalog.Get(code);
        var message = error.TechnicalMessage;
        foreach (var (placeholder, value) in details)
        {
            message = message.Replace(placeholder, value, StringComparison.Ordinal);
        }

        return error with { TechnicalMessage = message };
    }

    private static BisectionOutcome Stop(AppError error, List<AngleAttempt> attempts) =>
        new(Result<BisectionCut>.Failure(error), attempts.AsReadOnly());
}

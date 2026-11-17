using System.Collections.Concurrent;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace Imar18.GeometryEngine.Crs;

/// <summary>Cografi konum (ondalik derece; enlem Kuzey +, boylam Dogu +).</summary>
public sealed record CografiKonum(decimal EnlemDerece, decimal BoylamDerece);

/// <summary>
/// ProjNet ile TM 3° izdusumu (P4.2). Elipsoit, orta meridyen ve oteleme yalnizca <see cref="ProjeCrs"/> girdisinden alinir
/// (D3). <c>double</c> yalnizca bu sinifin icinde ProjNet cagrisinda kullanilir; disariya <c>decimal</c> verilir:
/// TM koordinatlari 6, cografi konum 10 ondalik basamaga yuvarlanir. ProjNet tipleri public imzalara sizmaz.
/// Datumlar arasi (ITRF96 &lt;-&gt; ED50) donusum sabit parametreyle yapilmaz; ortak noktali Helmert/Afin kullanilir.
/// </summary>
public static class TmProjeksiyonu
{
    private const int TmScale = 6;
    private const int DegreeScale = 10;

    /// <summary>Orta meridyenden izin verilen en buyuk boylam farki (derece): TM 3° dilim yarim genisligi 1.5° + 1.5° pay.</summary>
    public const decimal MaxBoylamFarkiDerece = 3m;

    private static readonly ConcurrentDictionary<string, (MathTransform Ileri, MathTransform Geri)> Cache = new(StringComparer.Ordinal);

    // ProjNet MathTransform orneklerinin is parcacigi guvenligi belgelenmedigi icin cagrilar serilestirilir.
    private static readonly Lock TransformLock = new();

    private static double[] Transform(MathTransform transform, double[] point)
    {
        lock (TransformLock)
        {
            return transform.Transform(point);
        }
    }

    /// <summary>Cografi konumu TM 3° duzlem koordinatina (X = Kuzey, Y = Dogu) izdusurur.</summary>
    public static Result<GeodeticPoint> CografidenTm(ProjeCrs crs, CografiKonum konum)
    {
        ArgumentNullException.ThrowIfNull(crs);
        ArgumentNullException.ThrowIfNull(konum);

        if (konum.EnlemDerece is < -90m or > 90m || konum.BoylamDerece is < -180m or > 180m)
        {
            return Gecersiz<GeodeticPoint>("geographic position out of range");
        }

        if (Math.Abs(konum.BoylamDerece - crs.OrtaMeridyenDerece) > MaxBoylamFarkiDerece)
        {
            return Gecersiz<GeodeticPoint>("longitude is outside the TM3 zone (more than 3 degrees from the central meridian)");
        }

        var (ileri, _) = Donusumler(crs);
        var xy = Transform(ileri, [(double)konum.BoylamDerece, (double)konum.EnlemDerece]);
        return ToPoint(xy[0], xy[1]);
    }

    /// <summary>TM 3° duzlem koordinatini cografi konuma cevirir.</summary>
    public static Result<CografiKonum> TmdenCografiye(ProjeCrs crs, GeodeticPoint nokta)
    {
        ArgumentNullException.ThrowIfNull(crs);
        var (_, geri) = Donusumler(crs);
        var lonLat = Transform(geri, [(double)nokta.Y, (double)nokta.X]);
        if (!double.IsFinite(lonLat[0]) || !double.IsFinite(lonLat[1]))
        {
            return Gecersiz<CografiKonum>("projection produced a non-finite value");
        }

        if (Math.Abs(lonLat[0] - crs.OrtaMeridyenDerece) > (double)MaxBoylamFarkiDerece || Math.Abs(lonLat[1]) > 90d)
        {
            return Gecersiz<CografiKonum>("planar coordinate is outside the TM3 zone");
        }

        return Result<CografiKonum>.Success(new CografiKonum(
            decimal.Round((decimal)lonLat[1], DegreeScale, MidpointRounding.AwayFromZero),
            decimal.Round((decimal)lonLat[0], DegreeScale, MidpointRounding.AwayFromZero)));
    }

    /// <summary>
    /// Ayni datum icinde dilim degistirir (ornek ITRF96 TM30 -&gt; TM33) — cografi koordinat uzerinden. Farkli datumda
    /// ERR-CRS-003 doner (datum kaydirmasi icin Helmert/Afin ortak nokta raporu gerekir).
    /// </summary>
    public static Result<IReadOnlyList<GeodeticPoint>> DilimDegistir(IReadOnlyList<GeodeticPoint> noktalar, ProjeCrs kaynak, ProjeCrs hedef)
    {
        ArgumentNullException.ThrowIfNull(noktalar);
        ArgumentNullException.ThrowIfNull(kaynak);
        ArgumentNullException.ThrowIfNull(hedef);

        if (kaynak.Datum != hedef.Datum)
        {
            return Gecersiz<IReadOnlyList<GeodeticPoint>>(
                "datum change has no built-in parameters; use common-point Helmert/affine transformation");
        }

        if (kaynak.Epok != hedef.Epok)
        {
            return Gecersiz<IReadOnlyList<GeodeticPoint>>(
                "epoch change has no built-in velocity model; use common-point Helmert/affine transformation");
        }

        var sonuc = new GeodeticPoint[noktalar.Count];
        var (_, geri) = Donusumler(kaynak);
        var (ileri, _) = Donusumler(hedef);
        for (var i = 0; i < noktalar.Count; i++)
        {
            // Ara adimda yuvarlamadan kacinmak icin dogrudan double zinciri kullanilir.
            var lonLat = Transform(geri, [(double)noktalar[i].Y, (double)noktalar[i].X]);
            if (!double.IsFinite(lonLat[0]) || !double.IsFinite(lonLat[1]))
            {
                return Gecersiz<IReadOnlyList<GeodeticPoint>>("projection produced a non-finite value");
            }

            var xy = Transform(ileri, lonLat);
            var p = ToPoint(xy[0], xy[1]);
            if (p.IsFailure)
            {
                return Result<IReadOnlyList<GeodeticPoint>>.Failure(p.Error);
            }

            sonuc[i] = p.Value;
        }

        return Result<IReadOnlyList<GeodeticPoint>>.Success(Array.AsReadOnly(sonuc));
    }

    private static Result<GeodeticPoint> ToPoint(double easting, double northing)
    {
        if (!double.IsFinite(easting) || !double.IsFinite(northing) || Math.Abs(easting) > 1e8 || Math.Abs(northing) > 1e8)
        {
            return Gecersiz<GeodeticPoint>("projection produced a non-finite value");
        }

        return Result<GeodeticPoint>.Success(new GeodeticPoint(
            X: decimal.Round((decimal)northing, TmScale, MidpointRounding.AwayFromZero),
            Y: decimal.Round((decimal)easting, TmScale, MidpointRounding.AwayFromZero)));
    }

    private static (MathTransform Ileri, MathTransform Geri) Donusumler(ProjeCrs crs) => Cache.GetOrAdd(crs.CrsId, _ =>
    {
        var cs = new CoordinateSystemFactory();
        var e = crs.Elipsoid;
        var ellipsoid = cs.CreateFlattenedSphere(e.Ad, (double)e.BuyukYariEksenM, (double)e.TersBasiklik, LinearUnit.Metre);
        var datum = cs.CreateHorizontalDatum(crs.Datum.ToString(), DatumType.HD_Geocentric, ellipsoid, null);
        var gcs = cs.CreateGeographicCoordinateSystem(
            crs.Datum + " geographic", AngularUnit.Degrees, datum, PrimeMeridian.Greenwich,
            new AxisInfo("Lon", AxisOrientationEnum.East), new AxisInfo("Lat", AxisOrientationEnum.North));
        var projection = cs.CreateProjection("Transverse Mercator", "Transverse_Mercator",
        [
            new ProjectionParameter("latitude_of_origin", 0),
            new ProjectionParameter("central_meridian", crs.OrtaMeridyenDerece),
            new ProjectionParameter("scale_factor", (double)crs.OlcekFaktoru),
            new ProjectionParameter("false_easting", (double)crs.YanaOtelemeM),
            new ProjectionParameter("false_northing", 0),
        ]);
        var pcs = cs.CreateProjectedCoordinateSystem(
            crs.CrsId, gcs, projection, LinearUnit.Metre,
            new AxisInfo("East", AxisOrientationEnum.East), new AxisInfo("North", AxisOrientationEnum.North));
        var ct = new CoordinateTransformationFactory();
        var ileri = ct.CreateFromCoordinateSystems(gcs, pcs).MathTransform;
        var geri = ct.CreateFromCoordinateSystems(pcs, gcs).MathTransform;
        return (ileri, geri);
    });

    private static Result<T> Gecersiz<T>(string reason)
    {
        var e = ErrorCatalog.Get(ErrorCodes.CrsGirdisiGecersiz);
        return Result<T>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", reason, StringComparison.Ordinal) });
    }
}

using Imar18.Domain.Geometry;
using NetTopologySuite.Geometries;

namespace Imar18.GeometryEngine;

/// <summary>
/// Jeodezik (X = Kuzey, Y = Dogu) &lt;-&gt; NTS Kartezyen eksen takasi (spec 3.2 madde 3):
/// x_NTS = Y_jeo - Y0, y_NTS = X_jeo - X0 (yerel orijinli; double hassasiyet kaybini onler).
/// NTS tipleri bu sinifin disina (public imzalara) cikmaz.
/// </summary>
internal static class CoordinateAxisSwapAdapter
{
    /// <summary>Yerel cerceve siniri (m); Domain kesinlik zarfi (1e6 m) ile uyumlu, decimal tasmasini onler.</summary>
    private const double MaxLocalM = 1e7;

    public static Coordinate ToNts(GeodeticPoint point, GeodeticPoint origin) =>
        new((double)(point.Y - origin.Y), (double)(point.X - origin.X));

    /// <summary>NTS yerel koordinatini mutlak jeodezik noktaya cevirir ve MUTLAK koordinatta 1 mm kuantize eder.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Koordinat sonlu degilse veya yerel cerceve disindaysa.</exception>
    public static GeodeticPoint FromNts(Coordinate coordinate, GeodeticPoint origin)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        if (!double.IsFinite(coordinate.X) || !double.IsFinite(coordinate.Y)
            || Math.Abs(coordinate.X) >= MaxLocalM || Math.Abs(coordinate.Y) >= MaxLocalM)
        {
            throw new ArgumentOutOfRangeException(nameof(coordinate), "NTS local coordinate must be finite and within the local frame.");
        }

        return new GeodeticPoint(
            X: CoordinateQuantizer.Quantize(origin.X + (decimal)coordinate.Y),
            Y: CoordinateQuantizer.Quantize(origin.Y + (decimal)coordinate.X));
    }
}

/// <summary>NTS topoloji gecerliligi (IsValid) — yalnizca GeometryEngine icinde.</summary>
internal static class NtsValidity
{
    private static readonly GeometryFactory Factory = new();

    public static bool IsValid(IReadOnlyList<GeodeticPoint> ring, GeodeticPoint origin)
    {
        var coords = new Coordinate[ring.Count + 1];
        for (var i = 0; i < ring.Count; i++)
        {
            coords[i] = CoordinateAxisSwapAdapter.ToNts(ring[i], origin);
        }

        coords[^1] = coords[0].Copy();
        return coords.Length >= 4 && Factory.CreatePolygon(coords).IsValid;
    }
}

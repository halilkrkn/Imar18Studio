using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.Infrastructure.Cad;

/// <summary>
/// Tum CAD adaptorlerinin ortak normalizasyon kapisi (AC-CAD-02, spec §3.2 madde 3, SORU-025):
/// <list type="bullet">
/// <item>girdi noktalari adaptorde jeodezik X = Kuzey, Y = Dogu sirasina cevrilmis olarak gelir,</item>
/// <item>Z != 0 kotlari <see cref="HorizontalProjection"/> ile Z = 0'a indirgenir (<c>ERR-GIRD-004</c>),</item>
/// <item>1 mm izgarada olmayan koseler kuantize edilmeden sayilir (<c>ERR-GIRD-006</c>, muhendis incelemesi),</item>
/// <item>desteklenmeyen geometriler sayilir (<c>ERR-GIRD-008</c>),</item>
/// <item>uyarilarda koordinat, etiket veya dosya adi yer almaz.</item>
/// </list>
/// </summary>
internal sealed class CadNormalizationGate(string sourceName)
{
    private readonly SortedSet<string> _unsupportedReasons = new(StringComparer.Ordinal);
    private int _polylines;
    private int _zPolylines;
    private int _unsupported;
    private int _toleranceClosed;
    private decimal _tolerance;

    /// <summary>1 mm izgara disi kose sayisi.</summary>
    public int OffGridVertexCount { get; private set; }

    /// <summary>
    /// Bir cizgiyi normalize eder.
    /// </summary>
    /// <param name="closedFlag">Kaynak bicimin "kapali" bayragi; true ise eksik kapanis tekrari eklenir.</param>
    /// <param name="closureToleranceM">Bayraksiz cizgide son kose ilk koseye bu mesafe icindeyse ilk koseye esitlenir (0: kapali degil).</param>
    public CadPolyline Add(
        string layerName,
        string label,
        string? featureClass,
        IReadOnlyList<GeodeticPoint3D> raw,
        bool closedFlag,
        decimal closureToleranceM,
        decimal? declaredAreaM2)
    {
        var projected = HorizontalProjection.ToHorizontal(raw);
        var points = projected.Points.ToList();
        if (projected.Warnings.Count > 0)
        {
            _zPolylines++;
        }

        if (points.Count > 1 && points[0] != points[^1])
        {
            if (points.Count >= 4 && closureToleranceM > 0m && WithinDistance(points[0], points[^1], closureToleranceM))
            {
                // Son kose ilk koseye tolerans icinde: mm alti serit olusmamasi icin esitlenir (bayrakli/bayraksiz).
                points[^1] = points[0];
                _toleranceClosed++;
                _tolerance = closureToleranceM;
            }
            else if (closedFlag && points.Count >= 3)
            {
                points.Add(points[0]);
            }
        }

        // Kapali halka: en az 3 farkli kose + kapanis tekrari.
        var isClosed = points.Count >= 4 && points[0] == points[^1];
        OffGridVertexCount += (isClosed ? points.Take(points.Count - 1) : points).Count(p => !CoordinateQuantizer.IsOnGrid(p));
        _polylines++;
        return new CadPolyline(
            layerName,
            label,
            featureClass,
            points.AsReadOnly(),
            isClosed,
            declaredAreaM2);
    }

    /// <summary>Desteklenmeyen geometri nedeniyle atlanan cizgiyi kaydeder (neden: kisa, sabit, koordinatsiz metin).</summary>
    public void Unsupported(string reason)
    {
        _unsupported++;
        _unsupportedReasons.Add(reason);
    }

    /// <summary>Belge uyarilari (sira: ERR-GIRD-004, ERR-GIRD-006, ERR-GIRD-008, ERR-GIRD-009).</summary>
    public IReadOnlyList<AppError> Warnings()
    {
        var path = $"{sourceName} input";
        var list = new List<AppError>();
        if (_zPolylines > 0)
        {
            var z = ErrorCatalog.Get(ErrorCodes.GirdZKoduIndirgendi);
            list.Add(z with { TechnicalMessage = z.TechnicalMessage.Replace(
                "{path}", $"{path} ({_zPolylines} of {_polylines} polylines)", StringComparison.Ordinal) });
        }

        if (OffGridVertexCount > 0)
        {
            var g = ErrorCatalog.Get(ErrorCodes.GirdIzgaraDisiKose);
            list.Add(g with { TechnicalMessage = g.TechnicalMessage
                .Replace("{count}", OffGridVertexCount.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{path}", path, StringComparison.Ordinal) });
        }

        if (_unsupported > 0)
        {
            var u = ErrorCatalog.Get(ErrorCodes.GirdDesteklenmeyenGeometriAtlandi);
            list.Add(u with { TechnicalMessage = u.TechnicalMessage
                .Replace("{count}", _unsupported.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{reason}", string.Join(", ", _unsupportedReasons), StringComparison.Ordinal)
                .Replace("{path}", path, StringComparison.Ordinal) });
        }

        if (_toleranceClosed > 0)
        {
            var t = ErrorCatalog.Get(ErrorCodes.GirdToleranslaKapatildi);
            list.Add(t with { TechnicalMessage = t.TechnicalMessage
                .Replace("{count}", _toleranceClosed.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{tolerance}", _tolerance.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{path}", path, StringComparison.Ordinal) });
        }

        return list.AsReadOnly();
    }

    private static bool WithinDistance(GeodeticPoint a, GeodeticPoint b, decimal tolerance)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Abs(dx) <= tolerance && Math.Abs(dy) <= tolerance && (dx * dx) + (dy * dy) <= tolerance * tolerance;
    }
}

/// <summary>Boyut sinirli akis okuyucu (DoS korumasi); sinir asilirsa hic okumadan/okuma sirasinda durur.</summary>
internal static class CadInputBuffer
{
    private const int ChunkBytes = 81_920;

    public static bool TryReadAll(Stream source, long maxBytes, CancellationToken cancellationToken, out byte[] data)
    {
        data = [];
        if (source.CanSeek && source.Length - source.Position > maxBytes)
        {
            return false;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[ChunkBytes];
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length + read > maxBytes)
            {
                return false;
            }

            buffer.Write(chunk, 0, read);
        }

        data = buffer.ToArray();
        return true;
    }
}

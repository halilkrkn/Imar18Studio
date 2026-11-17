using System.Text;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Exceptions;
using ACadSharp.IO;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cad.Ncz;

namespace Imar18.Infrastructure.Cad.Acad;

/// <summary>Yedek AutoCAD <c>.dxf</c> okuyucusu (spec 2.1 madde 2, D14; ACadSharp <c>DxfReader</c>).</summary>
public sealed class DxfFormatAdapter : ICadFormatAdapter
{
    /// <summary>Kabul edilen en buyuk dosya boyutu (DoS korumasi).</summary>
    public const long MaxFileSizeBytes = AcadDocumentConverter.MaxFileSizeBytes;

    /// <inheritdoc />
    public CadFormat Format => CadFormat.Dxf;

    /// <inheritdoc />
    public bool SupportsExtension(string extension) => AcadDocumentConverter.IsExtension(extension, "dxf");

    /// <inheritdoc />
    public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) =>
        AcadDocumentConverter.Read(source, CadFormat.Dxf, cancellationToken);
}

/// <summary>Yedek AutoCAD <c>.dwg</c> okuyucusu (spec 2.1 madde 2, D14; ACadSharp <c>DwgReader</c>).</summary>
public sealed class DwgFormatAdapter : ICadFormatAdapter
{
    /// <summary>Kabul edilen en buyuk dosya boyutu (DoS korumasi).</summary>
    public const long MaxFileSizeBytes = AcadDocumentConverter.MaxFileSizeBytes;

    /// <inheritdoc />
    public CadFormat Format => CadFormat.Dwg;

    /// <inheritdoc />
    public bool SupportsExtension(string extension) => AcadDocumentConverter.IsExtension(extension, "dwg");

    /// <inheritdoc />
    public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) =>
        AcadDocumentConverter.Read(source, CadFormat.Dwg, cancellationToken);
}

/// <summary>
/// ACadSharp belgesini tek tip <see cref="CadLayerDocument"/>'a donusturur. Kartezyen CAD (X = Saga/Dogu,
/// Y = Yukari/Kuzey) jeodezik X = Kuzey, Y = Dogu'ya takas edilir; <c>double</c> degerler kapida
/// <see cref="Ieee754Decimal"/> ile tek yuvarlamayla <c>decimal</c>'e cevrilir. Yay (bulge) segmentli veya
/// +Z disi OCS normalli cizgiler alan hatasini onlemek icin atlanir (<c>ERR-GIRD-008</c>).
/// Yalnizca model alani (<see cref="CadDocument.Entities"/>) okunur; blok ici (INSERT) varliklar acilmaz.
/// </summary>
internal static class AcadDocumentConverter
{
    public const long MaxFileSizeBytes = 256L * 1024 * 1024;

    /// <summary>skill acadsharp-cad: LWPOLYLINE/POLYLINE kapanma toleransi (m).</summary>
    public const decimal ClosureToleranceM = 0.001m;

    private const int CoordinateScale = PolygonRing.MaxScale;
    private const int CancellationCheckInterval = 256;
    private const string ArcReason = "arc segments";
    private const string OcsReason = "non +Z ocs normal";
    private const string FitReason = "curve/spline fit";
    private const string EmptyReason = "no vertices";
    private const string NonFiniteReason = "non-finite coordinate";
    private const string InsertReason = "block reference not expanded";

    private static readonly byte[] BinaryDxfSentinel = Encoding.ASCII.GetBytes("AutoCAD Binary DXF");

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool IsExtension(string extension, string expected)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var e = extension.StartsWith('.') ? extension[1..] : extension;
        return string.Equals(e, expected, StringComparison.OrdinalIgnoreCase);
    }

    public static Result<CadLayerDocument> Read(Stream source, CadFormat format, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CadInputBuffer.TryReadAll(source, MaxFileSizeBytes, cancellationToken, out var data))
        {
            return Fail(ErrorCodes.GirdCadOkunamadi, "file too large (limit 256 MiB)");
        }

        if (format == CadFormat.Dxf && data.AsSpan().StartsWith(Utf8Bom))
        {
            data = data[Utf8Bom.Length..]; // ACadSharp BOM beklemez; DXF metni BOM'suz verilir
        }

        if (!HasSignature(data, format))
        {
            return Fail(ErrorCodes.GirdCadOkunamadi, $"missing {format.ToString().ToUpperInvariant()} signature");
        }

        CadDocument document;
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            document = format == CadFormat.Dwg ? DwgReader.Read(stream, null) : DxfReader.Read(stream, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException ex) when (format == CadFormat.Dxf && ex.Message.Contains("already has an owner", StringComparison.Ordinal))
        {
            // ACadSharp 3.0.0 bilinen kisiti (P4.1, STATUS SORU-033 d): DXF 3B POLYLINE okunamaz.
            return Fail(ErrorCodes.GirdCadOkunamadi, "3D polyline not supported by the DXF reader; re-export as DWG");
        }
        catch (CadNotSupportedException)
        {
            var e = ErrorCatalog.Get(ErrorCodes.GirdCadSurumUyumsuz);
            return Result<CadLayerDocument>.Failure(e with { TechnicalMessage = e.TechnicalMessage
                .Replace("{version}", "unsupported", StringComparison.Ordinal)
                .Replace("{path}", $"{format.ToString().ToUpperInvariant()} input", StringComparison.Ordinal) });
        }
#pragma warning disable CA1031 // Ucuncu parti okuyucunun her hatasi AC-CAD-03 geregi Result'a cevrilir; ic mesaj (deger icerebilir) yazilmaz.
        catch (Exception)
#pragma warning restore CA1031
        {
            return Fail(ErrorCodes.GirdCadOkunamadi, "reader rejected the file structure");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var readerCodePage = format == CadFormat.Dxf && IsUtf8DxfVersion(data) ? HeaderCodePage(document.Header.CodePage) : null;
        return Convert(document, format, readerCodePage, cancellationToken);
    }

    internal static Result<CadLayerDocument> Convert(CadDocument document, CadFormat format, int? readerCodePage, CancellationToken cancellationToken)
    {
        string Name(string? raw) => readerCodePage is null ? raw ?? "0" : RepairUtf8Mojibake(raw ?? "0", readerCodePage.Value);

        var layers = document.Layers.Select((l, i) => new CadLayer(i, Name(l.Name))).ToList().AsReadOnly();
        var gate = new CadNormalizationGate(format.ToString().ToUpperInvariant());
        var polylines = new List<CadPolyline>();
        var skipped = 0;
        var index = 0;

        foreach (var entity in document.Entities)
        {
            if (++index % CancellationCheckInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var layerName = Name(entity.Layer?.Name);
            string? reason;
            switch (entity)
            {
                case LwPolyline lw:
                    reason = AddLwPolyline(lw, layerName, gate, polylines);
                    break;
                case Polyline3D p3:
                    reason = AddPolyline(p3, layerName, threeD: true, gate, polylines);
                    break;
                case Polyline2D p2:
                    reason = AddPolyline(p2, layerName, threeD: false, gate, polylines);
                    break;
                case Insert:
                    // Blok icindeki parseller acilmaz; sessiz kayip olmamasi icin uyari uretilir.
                    gate.Unsupported(InsertReason);
                    skipped++;
                    continue;
                default:
                    skipped++;
                    continue;
            }

            if (reason is not null)
            {
                return Fail(ErrorCodes.GirdCadOkunamadi, reason);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Result<CadLayerDocument>.Success(new CadLayerDocument(
            format,
            layers,
            polylines.AsReadOnly(),
            skipped,
            UnrecognizedRecordCount: 0,
            gate.OffGridVertexCount,
            gate.Warnings()));
    }

    private static string? AddLwPolyline(LwPolyline lw, string layerName, CadNormalizationGate gate, List<CadPolyline> polylines)
    {
        if (!IsWorldZ(lw.Normal.X, lw.Normal.Y, lw.Normal.Z))
        {
            gate.Unsupported(OcsReason);
            return null;
        }

        if (lw.Vertices.Any(v => v.Bulge != 0d))
        {
            gate.Unsupported(ArcReason);
            return null;
        }

        if (lw.Vertices.Count == 0)
        {
            gate.Unsupported(EmptyReason);
            return null;
        }

        var raw = new List<GeodeticPoint3D>(lw.Vertices.Count);
        foreach (var v in lw.Vertices)
        {
            if (!TryPoint(v.Location.X, v.Location.Y, lw.Elevation, out var p))
            {
                gate.Unsupported(NonFiniteReason);
                return null;
            }

            raw.Add(p);
        }

        polylines.Add(gate.Add(layerName, string.Empty, null, raw, lw.IsClosed, ClosureToleranceM, null));
        return null;
    }

    private static string? AddPolyline(Polyline polyline, string layerName, bool threeD, CadNormalizationGate gate, List<CadPolyline> polylines)
    {
        if (!threeD && !IsWorldZ(polyline.Normal.X, polyline.Normal.Y, polyline.Normal.Z))
        {
            gate.Unsupported(OcsReason);
            return null;
        }

        if ((polyline.Flags & (PolylineFlags.CurveFit | PolylineFlags.SplineFit)) != 0)
        {
            // Uydurulmus egri: kose listesi kontrol noktalari/ek koseler icerir, halka alani yanlis olur.
            gate.Unsupported(FitReason);
            return null;
        }

        var vertices = polyline.Vertices.ToList();
        if (!threeD && vertices.Any(v => v.Bulge != 0d))
        {
            gate.Unsupported(ArcReason);
            return null;
        }

        if (vertices.Count == 0)
        {
            gate.Unsupported(EmptyReason);
            return null;
        }

        var raw = new List<GeodeticPoint3D>(vertices.Count);
        foreach (var v in vertices)
        {
            var z = threeD ? v.Location.Z : polyline.Elevation;
            if (!TryPoint(v.Location.X, v.Location.Y, z, out var p))
            {
                gate.Unsupported(NonFiniteReason);
                return null;
            }

            raw.Add(p);
        }

        polylines.Add(gate.Add(layerName, string.Empty, null, raw, polyline.IsClosed, ClosureToleranceM, null));
        return null;
    }

    /// <summary>CAD (X = Dogu, Y = Kuzey, Z) -> jeodezik (X = Kuzey, Y = Dogu, Z); tek yuvarlamali decimal.</summary>
    private static bool TryPoint(double cadX, double cadY, double cadZ, out GeodeticPoint3D point)
    {
        point = default;
        if (!Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(cadY), CoordinateScale, out var north)
            || !Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(cadX), CoordinateScale, out var east)
            || !Ieee754Decimal.TryDecode(BitConverter.DoubleToUInt64Bits(cadZ), CoordinateScale, out var z))
        {
            return false;
        }

        point = new GeodeticPoint3D(X: north, Y: east, Z: z);
        return true;
    }

    /// <summary>
    /// DXF AC1021 (AutoCAD 2007) ve sonrasi UTF-8'dir. ACadSharp 3.0.0 bu dosyalardaki metni basliktaki
    /// <c>$DWGCODEPAGE</c> (ornek ANSI_1252) ile cozebiliyor (P4.1 gozlemi: "DÜZENLEME" -> "DÃœZENLEME").
    /// Ad o kod sayfasina kayipsiz geri kodlanabiliyor ve sonuc kati UTF-8 olarak cozulebiliyorsa onarilmis ad
    /// kullanilir; aksi halde ad aynen korunur.
    /// </summary>
    internal static string RepairUtf8Mojibake(string text, int readerCodePage)
    {
        if (text.All(c => c < 128))
        {
            return text;
        }

        try
        {
            var strict = CodePagesEncodingProvider.Instance.GetEncoding(readerCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            if (strict is null)
            {
                return text;
            }

            // Karakter bazinda: once baslik kod sayfasi, olmazsa tek bayt (U+0080..U+00FF ham bayt olarak gelmis olabilir).
            var bytes = new List<byte>(text.Length);
            foreach (var c in text)
            {
                try
                {
                    bytes.AddRange(strict.GetBytes(c.ToString()));
                }
                catch (EncoderFallbackException) when (c <= '\u00FF')
                {
                    bytes.Add((byte)c);
                }
            }

            return Utf8Strict.GetString([.. bytes]);
        }
        catch (EncoderFallbackException)
        {
            return text; // zaten dogru cozulmus (Latin-1 disi karakter iceriyor)
        }
        catch (DecoderFallbackException)
        {
            return text; // gecerli UTF-8 degil: gercek Latin-1 metin
        }
    }

    /// <summary><c>$DWGCODEPAGE</c> degerinden (ornek "ANSI_1252") Windows kod sayfasi numarasi; bilinmiyorsa null.</summary>
    private static int? HeaderCodePage(string? codePage)
    {
        if (codePage is null || !codePage.StartsWith("ANSI_", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(codePage.AsSpan(5), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var cp))
        {
            return null;
        }

        return cp;
    }

    /// <summary>ASCII DXF basligindaki <c>$ACADVER</c> AC1021 veya sonrasi mi?</summary>
    private static bool IsUtf8DxfVersion(byte[] data)
    {
        if (data.AsSpan().StartsWith(BinaryDxfSentinel))
        {
            return false;
        }

        var head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 16 * 1024));
        var lines = head.Split('\n');
        for (var i = 0; i + 2 < lines.Length; i++)
        {
            if (lines[i].Trim() == "$ACADVER" && lines[i + 1].Trim() == "1")
            {
                var v = lines[i + 2].Trim();
                return v.Length == 6 && v.StartsWith("AC", StringComparison.Ordinal)
                    && int.TryParse(v.AsSpan(2), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
                    && n >= 1021;
            }
        }

        return false;
    }

    /// <summary>OCS normali dunya +Z ekseniyle ayni yonde mi (kimlik donusumu)?</summary>
    private static bool IsWorldZ(double x, double y, double z) => x == 0d && y == 0d && z > 0d;

    private static bool HasSignature(byte[] data, CadFormat format)
    {
        if (format == CadFormat.Dwg)
        {
            // DWG surum dizgesi "AC10xx" ile baslar.
            return data.Length >= 6 && data[0] == (byte)'A' && data[1] == (byte)'C' && data.AsSpan(2, 4).IndexOfAnyExceptInRange((byte)'0', (byte)'9') < 0;
        }

        if (data.AsSpan().StartsWith(BinaryDxfSentinel))
        {
            return true;
        }

        // ASCII DXF: "999" yorum ciftleri atlandiktan sonra ilk grup "0" / "SECTION" olmalidir.
        var text = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 4096));
        var lines = text.Split('\n').Select(l => l.Trim()).ToArray();
        var i = 0;
        while (i < lines.Length && lines[i].Length == 0)
        {
            i++;
        }

        while (i + 1 < lines.Length && lines[i] == "999")
        {
            i += 2;
        }

        return i + 1 < lines.Length && lines[i] == "0" && string.Equals(lines[i + 1], "SECTION", StringComparison.Ordinal);
    }

    private static Result<CadLayerDocument> Fail(string code, string reason)
    {
        var error = ErrorCatalog.Get(code);
        return Result<CadLayerDocument>.Failure(error with
        {
            TechnicalMessage = error.TechnicalMessage.Replace("{path}", $"input ({reason})", StringComparison.Ordinal),
        });
    }
}

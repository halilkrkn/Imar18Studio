using System.Buffers.Binary;
using System.Text;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.Infrastructure.Cad.Ncz;

/// <summary>
/// Birincil Netcad 8.5 <c>.NCZ</c> okuyucusu (spec 2.1, D14, P4.0 spike). Yalnizca katman tablosunu ve poligon/coklu cizgi
/// (tip 7) varliklarini okur; diger varliklar atlanip sayilir. Gomulu <c>&lt;NetmapManager&gt;</c> XML ust verisi
/// (kisi/kurum/adres alanlari) ve PNG onizleme hic ayristirilmaz (KVKK, AC-PILOT-08).
/// Ikili yerlesim: docs/spikes/P4.0_NCZ_Fizibilite_Raporu.md §2.
/// </summary>
public sealed class NczFormatAdapter : ICadFormatAdapter
{
    /// <summary>Kabul edilen en buyuk dosya boyutu (uygulama guvenlik siniri; DoS korumasi).</summary>
    public const long MaxFileSizeBytes = 64L * 1024 * 1024;

    /// <inheritdoc />
    public CadFormat Format => CadFormat.Ncz;

    /// <inheritdoc />
    public bool SupportsExtension(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var e = extension.StartsWith('.') ? extension[1..] : extension;
        return string.Equals(e, "ncz", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CadInputBuffer.TryReadAll(source, MaxFileSizeBytes, cancellationToken, out var data))
        {
            return NczParser.Fail("file too large (limit 64 MiB)");
        }

        return NczParser.Parse(data, cancellationToken);
    }
}

/// <summary>NCZ ikili ayristiricisi. Her sinir ihlali istisna yerine <c>ERR-GIRD-NCZ-001</c> doner (AC-CAD-03).</summary>
internal static class NczParser
{
    // ---- Dosya basligi ----
    private const byte Signature = 0x01;
    private const int HeaderIntCount = 12;
    private const int HeaderSize = 1 + (4 * HeaderIntCount);
    private const int HeaderConst0 = 120; // P4.0: 6 gercek dosyada sabit
    private const int HeaderConst2 = 125; // P4.0: 6 gercek dosyada sabit
    private const int HeaderFileLength = 1;
    private const int HeaderLayerTable = 3;
    private const int HeaderLayerTableEnd = 7; // son katman kaydinin bitisi - 4
    private const int HeaderEntityStream = 11;

    // ---- Katman tablosu ----
    private const int LayerTableHeaderSize = 22;
    private const int LayerCountOffset = 16;
    private const int LayerRecordSize = 29;
    private const int LayerNameCapacity = 20;
    private const int LayerTableEndBias = 4;

    // ---- Varlik kayit akisi: [bayrak:1][boyut:int32][yuk] ----
    private const int RecordHeaderSize = 5;
    private const byte EntityRecord = 0x15;
    private const byte ExtendedEntityRecord = 0x16;
    private const byte ViewRecordA = 0x1B; // gorunum/kapsam kaydi (P4.0 gozlem)
    private const byte ViewRecordB = 0x1D; // gorunum/kapsam kaydi (P4.0 gozlem)
    private const byte PolylineType = 7;
    private const int CancellationCheckInterval = 256;

    // ---- Poligon yuku (yuk baslangicina gore ofsetler) ----
    private const int PolylineLayerOffset = 30;
    private const int PolylineStringsOffset = 81;
    private const int ExtendedClassFieldSize = 28;
    private const int LabelFieldSize = 15;
    private const int PolylineFlagsSize = 4;
    private const int DeclaredAreaSize = 8;
    private const int VertexSize = 24;
    private const int DeclaredAreaFlag = 0x02;

    /// <summary>Koordinat ve alan olcegi (PolygonRing.MaxScale ile ayni; 1 mm kuantizasyon yapilmaz).</summary>
    private const int CoordinateScale = PolygonRing.MaxScale;

    private static readonly Encoding Turkish = CodePagesEncodingProvider.Instance.GetEncoding(1254)
        ?? throw new InvalidOperationException("Windows-1254 encoding is unavailable.");

    public static Result<CadLayerDocument> Fail(string reason)
    {
        var error = ErrorCatalog.Get(ErrorCodes.GirdNczOkunamadi);
        // Yalnizca yapisal neden yazilir; koordinat, etiket ve dosya adi mesaja girmez.
        return Result<CadLayerDocument>.Failure(error with
        {
            TechnicalMessage = error.TechnicalMessage.Replace("{path}", $"input ({reason})", StringComparison.Ordinal),
        });
    }

    public static Result<CadLayerDocument> Parse(ReadOnlySpan<byte> data, CancellationToken cancellationToken)
    {
        if (data.Length < HeaderSize)
        {
            return Fail("header truncated");
        }

        if (data[0] != Signature)
        {
            return Fail("invalid signature byte");
        }

        Span<int> header = stackalloc int[HeaderIntCount];
        for (var i = 0; i < HeaderIntCount; i++)
        {
            header[i] = BinaryPrimitives.ReadInt32LittleEndian(data[(1 + (4 * i))..]);
        }

        if (header[0] != HeaderConst0 || header[2] != HeaderConst2)
        {
            return Fail("unsupported header version");
        }

        if (header[HeaderFileLength] != data.Length)
        {
            return Fail("declared file length mismatch");
        }

        var layersResult = ReadLayers(data, header[HeaderLayerTable], header[HeaderLayerTableEnd], out var layers);
        if (layersResult is not null)
        {
            return Fail(layersResult);
        }

        var entityStart = header[HeaderEntityStream];
        var layerTableEnd = header[HeaderLayerTable] + LayerTableHeaderSize + (LayerRecordSize * layers.Count);
        if (entityStart < layerTableEnd || entityStart > data.Length)
        {
            return Fail("entity stream offset out of range");
        }

        var polylines = new List<CadPolyline>();
        var gate = new CadNormalizationGate("NCZ");
        var skipped = 0;
        var unrecognized = 0;
        var position = entityStart;
        var recordIndex = 0;
        while (position < data.Length)
        {
            if (++recordIndex % CancellationCheckInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (data.Length - position < RecordHeaderSize)
            {
                return Fail("truncated record header");
            }

            var flag = data[position];
            var size = BinaryPrimitives.ReadInt32LittleEndian(data[(position + 1)..]);
            if (size < 0 || size > data.Length - position - RecordHeaderSize)
            {
                return Fail("record exceeds file bounds");
            }

            var payload = data.Slice(position + RecordHeaderSize, size);
            position += RecordHeaderSize + size;

            if (size == 0)
            {
                continue; // bolum isaretcisi
            }

            if (flag is not (EntityRecord or ExtendedEntityRecord))
            {
                // Varlik disi kayit: bilinen gorunum kayitlari yok sayilir, bilinmeyenler sayilir (sessiz kayip yok).
                if (flag is not (ViewRecordA or ViewRecordB))
                {
                    unrecognized++;
                }

                continue;
            }

            if (payload[0] != PolylineType)
            {
                skipped++;
                continue;
            }

            var reason = ReadPolyline(payload, flag == ExtendedEntityRecord, layers, gate, out var polyline);
            if (reason is not null)
            {
                return Fail(reason);
            }

            polylines.Add(polyline!);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Result<CadLayerDocument>.Success(new CadLayerDocument(
            CadFormat.Ncz,
            layers,
            polylines.AsReadOnly(),
            skipped,
            unrecognized,
            gate.OffGridVertexCount,
            gate.Warnings()));
    }

    private static string? ReadLayers(ReadOnlySpan<byte> data, int tableOffset, int declaredEnd, out IReadOnlyList<CadLayer> layers)
    {
        layers = [];
        if (tableOffset < HeaderSize || tableOffset > data.Length - LayerTableHeaderSize)
        {
            return "layer table offset out of range";
        }

        int count = BinaryPrimitives.ReadInt16LittleEndian(data[(tableOffset + LayerCountOffset)..]);
        var first = tableOffset + LayerTableHeaderSize;
        if (count < 0 || (long)first + ((long)LayerRecordSize * count) > data.Length)
        {
            return "layer table truncated";
        }

        if (first + (LayerRecordSize * count) - LayerTableEndBias != declaredEnd)
        {
            return "layer table end mismatch";
        }

        var list = new CadLayer[count];
        for (var i = 0; i < count; i++)
        {
            var name = ReadPascal(data.Slice(first + (LayerRecordSize * i), 1 + LayerNameCapacity));
            if (name is null)
            {
                return "layer name pascal string length exceeds field capacity";
            }

            list[i] = new CadLayer(i, name);
        }

        layers = Array.AsReadOnly(list);
        return null;
    }

    private static string? ReadPolyline(
        ReadOnlySpan<byte> p,
        bool extended,
        IReadOnlyList<CadLayer> layers,
        CadNormalizationGate gate,
        out CadPolyline? polyline)
    {
        polyline = null;

        var fixedSize = PolylineStringsOffset + (extended ? ExtendedClassFieldSize : 0) + LabelFieldSize + PolylineFlagsSize + DeclaredAreaSize;
        if (p.Length < fixedSize)
        {
            return "polyline record truncated";
        }

        int layerIndex = BinaryPrimitives.ReadInt16LittleEndian(p[PolylineLayerOffset..]);
        if (layerIndex < 0 || layerIndex >= layers.Count)
        {
            return "layer index out of range";
        }

        var o = PolylineStringsOffset;
        string? featureClass = null;
        if (extended)
        {
            featureClass = ReadPascal(p.Slice(o, ExtendedClassFieldSize));
            if (featureClass is null)
            {
                return "class pascal string length exceeds field capacity";
            }

            o += ExtendedClassFieldSize;
        }

        var label = ReadPascal(p.Slice(o, LabelFieldSize));
        if (label is null)
        {
            return "label pascal string length exceeds field capacity";
        }

        o += LabelFieldSize;
        var flags = BinaryPrimitives.ReadInt32LittleEndian(p[o..]);
        o += PolylineFlagsSize;
        var areaBits = BinaryPrimitives.ReadUInt64LittleEndian(p[o..]);
        o += DeclaredAreaSize;

        var vertexBytes = p.Length - o;
        if (vertexBytes == 0 || vertexBytes % VertexSize != 0)
        {
            return "vertex block size is not a positive multiple of 24 bytes";
        }

        var count = vertexBytes / VertexSize;
        var raw = new GeodeticPoint3D[count];
        for (var i = 0; i < count; i++)
        {
            var v = p.Slice(o + (VertexSize * i), VertexSize);
            // P4.0: ham kayit (Kuzey, Dogu, Kot) sirasindadir -> jeodezik X = Kuzey, Y = Dogu.
            // Kot atilsa da sonlu olmayan kot bozuk kayit sayilir ve dosya reddedilir (rapor §4).
            if (!Ieee754Decimal.TryDecode(BinaryPrimitives.ReadUInt64LittleEndian(v), CoordinateScale, out var north)
                || !Ieee754Decimal.TryDecode(BinaryPrimitives.ReadUInt64LittleEndian(v[8..]), CoordinateScale, out var east)
                || !Ieee754Decimal.TryDecode(BinaryPrimitives.ReadUInt64LittleEndian(v[16..]), CoordinateScale, out var z))
            {
                return "non-finite or out-of-range coordinate";
            }

            raw[i] = new GeodeticPoint3D(X: north, Y: east, Z: z);
        }

        decimal? declaredArea = null;
        if ((flags & DeclaredAreaFlag) != 0)
        {
            if (!Ieee754Decimal.TryDecode(areaBits, CoordinateScale, out var area))
            {
                return "non-finite or out-of-range declared area";
            }

            declaredArea = area;
        }

        // NCZ kapali halkayi ilk kosenin tekrariyla saklar (P4.0 DOGRULANDI): bayrak/tolerans kullanilmaz.
        polyline = gate.Add(layers[layerIndex].Name, label, featureClass, raw, closedFlag: false, closureToleranceM: 0m, declaredArea);
        return null;
    }

    /// <summary>[uzunluk:1][metin] alanini okur; uzunluk kapasiteyi asarsa null. Uzunluktan sonraki eski baytlar yok sayilir.</summary>
    private static string? ReadPascal(ReadOnlySpan<byte> field)
    {
        var length = field[0];
        return length > field.Length - 1 ? null : Turkish.GetString(field.Slice(1, length));
    }
}

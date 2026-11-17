using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.Application.Cad;

/// <summary>Desteklenen CAD dosya bicimleri (spec 2.1, D14, D28). Oncelik: Netcad NCZ birincil, DXF/DWG yedek.</summary>
public enum CadFormat
{
    Ncz,
    Dxf,
    Dwg,
}

/// <summary>
/// Bicimden bagimsiz CAD okuma sozlesmesi (spec 2.1, FR-F2.9, AC-CAD-01..03). Uygulayicilar (Infrastructure):
/// <list type="bullet">
/// <item>kaynak eksen sirasini adaptor icinde jeodezik <c>X = Kuzey</c>, <c>Y = Dogu</c> sirasina normalize eder (AC-CAD-02),</item>
/// <item>Z != 0 kotlarini Z = 0 yatay izdusumune indirger ve tek <c>ERR-GIRD-004</c> uyarisi uretir,</item>
/// <item>bozuk/desteklenmeyen dosyada istisna atmaz, <c>ERR-GIRD-*</c> hatasi dondurur (AC-CAD-03),</item>
/// <item>hata mesajlarina koordinat, etiket veya kisisel ust veri yazmaz (security.md, AC-PILOT-08),</item>
/// <item>koordinatlari <see cref="PolygonRing.MaxScale"/> (6) ondalik basamagi asmayacak bicimde verir; 1 mm kuantizasyon
/// yapmaz (kuantizasyon kararı muhendis onayina baglidir — STATUS SORU-025, SORU-032).</item>
/// </list>
/// Okuma iptal belirtecini dinler; iptalde <see cref="OperationCanceledException"/> atar.
/// </summary>
public interface ICadFormatAdapter
{
    /// <summary>Adaptorun okudugu bicim.</summary>
    CadFormat Format { get; }

    /// <summary>Dosya uzantisi (noktali veya noktasiz, buyuk/kucuk harf duyarsiz) bu adaptorle okunabilir mi?</summary>
    bool SupportsExtension(string extension);

    /// <summary>Akisi okur ve tek tip katman belgesine donusturur. Akis kapatilmaz.</summary>
    Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default);
}

/// <summary>CAD katmani (kaynak dosyadaki sira indeksiyle).</summary>
public sealed record CadLayer(int Index, string Name);

/// <summary>
/// Cizgi/poligon varligi. <see cref="Vertices"/> jeodezik (X = Kuzey, Y = Dogu), Z = 0 duzlemine indirgenmis ve kaynakta
/// varsa kapanis tekrari dahil kose dizisidir. <see cref="DeclaredAreaM2"/> kaynak dosyada poligona yazilmis alan
/// ozniteligidir (yalnizca bilgi; resmi alan Decimal Gauss ile hesaplanir).
/// </summary>
public sealed record CadPolyline(
    string LayerName,
    string Label,
    string? FeatureClass,
    IReadOnlyList<GeodeticPoint> Vertices,
    bool IsClosed,
    decimal? DeclaredAreaM2);

/// <summary>
/// CAD yazi varligi (TEXT/MTEXT; P4.2 etiket eslestirmesi). <see cref="Position"/> jeodezik (X = Kuzey, Y = Dogu)
/// ekleme noktasidir; <see cref="Text"/> bicim kodlarindan arindirilmis, bas/son bosluksuz metindir.
/// </summary>
public sealed record CadText(string LayerName, string Text, GeodeticPoint Position);

/// <summary>
/// Bicimden bagimsiz CAD okuma sonucu (spec 2.1 <c>CadLayerDocument</c>).
/// <see cref="SkippedEntityCount"/>: taninan ama bu adaptorun cozmedigi varliklar (nokta, yazi, cizgi...).
/// <see cref="UnrecognizedRecordCount"/>: bicimi bilinmeyen kayitlar (sessiz veri kaybini gorunur kilar).
/// <see cref="OffGridVertexCount"/>: 1 mm izgarada olmayan kose sayisi (bilgi; kuantizasyon yapilmaz — STATUS SORU-025/SORU-032).
/// </summary>
public sealed record CadLayerDocument(
    CadFormat Format,
    IReadOnlyList<CadLayer> Layers,
    IReadOnlyList<CadPolyline> Polylines,
    IReadOnlyList<CadText> Texts,
    int SkippedEntityCount,
    int UnrecognizedRecordCount,
    int OffGridVertexCount,
    IReadOnlyList<AppError> Warnings);

using System.Globalization;
using Avalonia.Data.Converters;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tecviz;

namespace Imar18.Presentation.Avalonia.Gorunum;

/// <summary>
/// P7 %100 Türkçe arayüz kuralı: ekrana yansıyan enum değerlerinin Türkçe görünen adları. Kod içi tip/değer adları
/// değişmez; görünüm yalnızca bu adları (veya <see cref="TurkceAdConverter"/>'ı) kullanır.
/// </summary>
public static class TurkceAdlar
{
    public static string Ad(CadLayerRole rol) => rol switch
    {
        CadLayerRole.Unassigned => "Atanmamış",
        CadLayerRole.CadastralParcel => "Kadastro Parseli",
        CadLayerRole.ZoningParcel => "İmar Parseli",
        CadLayerRole.RoadDedication => "Yola Terk",
        CadLayerRole.RegulationBoundary => "Düzenleme Sınırı",
        CadLayerRole.BlockEdge => "Ada Kenarı",
        CadLayerRole.Park => "Park Alanı",
        CadLayerRole.CadastralLine => "Kadastro Hattı",
        CadLayerRole.NewParcelPoint => "Yeni Parsel Noktası",
        CadLayerRole.RegisteredPoint => "Tescilli Nokta",
        _ => "Bilinmeyen Rol",
    };

    public static string Ad(GeodetikDatum datum) => datum switch
    {
        GeodetikDatum.Itrf96 => "ITRF96 (TM 3°)",
        GeodetikDatum.Ed50 => "ED50 (TM 3° · Hayford)",
        _ => "Bilinmeyen Datum",
    };

    public static string Ad(KoordinatKalitesi kalite) => kalite switch
    {
        KoordinatKalitesi.ArsivOnayli => "Arşiv Onaylı",
        KoordinatKalitesi.Gecici => "Geçici Koordinatlı",
        _ => "Bilinmeyen Kalite",
    };

    public static string Ad(CiktiTuru tur) => tur switch
    {
        CiktiTuru.Cetveller => "Excel Cetvelleri",
        CiktiTuru.FenKlasoru => "Fen Klasörü",
        CiktiTuru.CksDopOzeti => "CKS DOP Özeti",
        CiktiTuru.FenKlasoruZip => "Fen Klasörü (.zip)",
        _ => "Bilinmeyen Çıktı",
    };

    public static string Ad(CadFormat format) => format switch
    {
        CadFormat.Ncz => "NCZ (Netcad)",
        CadFormat.Dxf => "DXF (AutoCAD)",
        CadFormat.Dwg => "DWG (AutoCAD)",
        _ => "Bilinmeyen Biçim",
    };

    /// <summary>TM 3° dilim orta meridyeni (DOM).</summary>
    public static string OrtaMeridyen(int derece) => string.Create(CultureInfo.InvariantCulture, $"DOM {derece}°");

    /// <summary>RevisionId rozeti: ilk 8 karakter + "…" (tamamı araç ipucunda ve panoya kopyalanır).</summary>
    public static string KisaRevizyon(string? tam) =>
        string.IsNullOrEmpty(tam) ? "—" : tam.Length > 8 ? tam[..8] + "…" : tam;

    /// <summary>Bilinen enum değerini Türkçe adına çevirir; diğer değerler metne çevrilir.</summary>
    public static string Gorunen(object? deger) => deger switch
    {
        null => string.Empty,
        CadLayerRole r => Ad(r),
        GeodetikDatum d => Ad(d),
        DonusumModeli m => m == DonusumModeli.Helmert2B ? "Helmert (2B)" : "Afin (2B)",
        KoordinatKalitesi k => Ad(k),
        CiktiTuru c => Ad(c),
        CadFormat f => Ad(f),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => deger.ToString() ?? string.Empty,
    };
}

/// <summary>XAML çevirici: enum → Türkçe görünen ad. Parametre "meridyen" ise tamsayı orta meridyen olarak yazılır.</summary>
public sealed class TurkceAdConverter : IValueConverter
{
    public static TurkceAdConverter Ornek { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        parameter is "meridyen" && value is int d ? TurkceAdlar.OrtaMeridyen(d) : TurkceAdlar.Gorunen(value);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Türkçe görünen ad tek yönlüdür.");
}

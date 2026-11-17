using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Presentation.Avalonia.Canvas;

namespace Imar18.Presentation.Avalonia.Gorunum;

/// <summary>
/// Sayıyı makine kültüründen bağımsız (ondalık ayıracı '.', cetvel/Netcad ve ViewModel özetleriyle aynı) biçimler.
/// Parametre .NET özel sayı biçimidir (ör. "0.00 m²"); boş değer "—" yazılır.
/// </summary>
public sealed class SayiBicimiConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => "—",
        IFormattable f => f.ToString(parameter as string, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Sayı biçimi tek yönlüdür.");
}

/// <summary>P7 görünüm çeviricileri (yalnız görünürlük kararları; hesap yapmaz).</summary>
public static class Ceviriciler
{
    private const string UyariOneki = "Uyarı:";

    /// <summary>Kültürden bağımsız sayı biçimi (bkz. <see cref="SayiBicimiConverter"/>).</summary>
    public static readonly IValueConverter Sayi = new SayiBicimiConverter();

    /// <summary>Sayı &gt; 0 ise true (ör. liste boşsa kart gizlenir).</summary>
    public static readonly IValueConverter Pozitif = new FuncValueConverter<int, bool>(n => n > 0);

    /// <summary>Sayı 0 ise true (ör. "henüz kayıt yok" notu).</summary>
    public static readonly IValueConverter Sifir = new FuncValueConverter<int, bool>(n => n == 0);

    /// <summary>Doğrulama mesajı "Uyarı:" ile başlıyorsa (amber gösterilir).</summary>
    public static readonly IValueConverter UyariMi =
        new FuncValueConverter<string?, bool>(s => s?.StartsWith(UyariOneki, StringComparison.Ordinal) == true);

    /// <summary>Doğrulama mesajı hata ise (kırmızı gösterilir).</summary>
    public static readonly IValueConverter HataMi =
        new FuncValueConverter<string?, bool>(s => s is { Length: > 0 } && !s.StartsWith(UyariOneki, StringComparison.Ordinal));

    /// <summary>P7: topoloji / denetim satırı ikonu (seviyeye göre).</summary>
    public static readonly IValueConverter SeviyeIkonu = new FuncValueConverter<ErrorSeverity, string>(s => s switch
    {
        ErrorSeverity.Critical or ErrorSeverity.Error => "⛔",
        ErrorSeverity.Warning => "⚠️",
        _ => "ℹ️",
    });

    /// <summary>P7: Katman → Rol satırındaki renk örneği (tuvaldeki çizgi rengiyle aynı, koyu pafta paleti).</summary>
    public static readonly IValueConverter RolFircasi = new FuncValueConverter<CadLayerRole, IBrush>(r =>
    {
        var c = CadSceneRenderer.RolRengi(r, acikPafta: false);
        return new SolidColorBrush(Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));
    });
}

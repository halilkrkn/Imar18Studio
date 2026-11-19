using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Imar18.Presentation.Avalonia.Gorunum;

/// <summary>
/// P7 UX (Numpad virgülü): kullanıcı sayı girdisinin ortak ayrıştırıcısı. Türkçe Windows klavyesinde Numpad ondalık tuşu
/// <c>,</c> bastığı için hem <c>.</c> hem <c>,</c> ondalık ayırıcı kabul edilir. Kurallar (tahmin yürütülmez):
/// <list type="bullet">
/// <item>Tek <c>,</c> ve sıfır <c>.</c> → <c>,</c> <c>.</c>'ya çevrilip <see cref="CultureInfo.InvariantCulture"/> ile okunur (<c>21,02</c> → <c>21.02m</c>).</item>
/// <item>Tek <c>.</c> her zaman ondalıktır (asla binlik değil): <c>1.078</c> → <c>1.078m</c>.</item>
/// <item><c>.</c> ile <c>,</c> birlikte veya birden çok ayırıcı (<c>1.078,90</c>, <c>8,699.44</c>, <c>12,3,4</c>) reddedilir:
/// <see cref="BinlikAyiriciUyarisi"/>.</item>
/// </list>
/// Çıktılar (Canlı Cetvel, Excel, PDF, .CKS, .NCZ) bu sınıftan etkilenmez; her zaman <c>.</c> (InvariantCulture) ile yazılır.
/// </summary>
public static class SayiGirdiAyristirici
{
    /// <summary>Binlik ayırıcı veya birden çok ondalık ayırıcı girildiğinde gösterilen açık Türkçe uyarı.</summary>
    public const string BinlikAyiriciUyarisi = "Binlik ayırıcı kullanmayın; yalnızca ondalık ayırıcı ('.' veya ',') girin (ör. 1078.90).";

    /// <summary>Boş girdi uyarısı.</summary>
    public const string BosGirdiUyarisi = "Değer girilmedi.";

    /// <summary>Sayı olmayan girdi uyarısı.</summary>
    public const string GecersizSayiUyarisi = "Geçerli bir sayı girin (ondalık ayırıcı '.' veya ',').";

    /// <inheritdoc cref="TryParseDecimal(string?, out decimal, out string?)" />
    public static bool TryParseDecimal(string? metin, out decimal deger) => TryParseDecimal(metin, out deger, out _);

    /// <summary>Metni <see cref="decimal"/> olarak ayrıştırır; geçersizse <paramref name="hata"/> Türkçe nedeni taşır.</summary>
    public static bool TryParseDecimal(string? metin, out decimal deger, out string? hata)
    {
        deger = 0m;
        var t = (metin ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            hata = BosGirdiUyarisi;
            return false;
        }

        var virgul = 0;
        var nokta = 0;
        foreach (var c in t)
        {
            if (c == ',')
            {
                virgul++;
            }
            else if (c == '.')
            {
                nokta++;
            }
        }

        if (virgul + nokta > 1)
        {
            hata = BinlikAyiriciUyarisi;
            return false;
        }

        if (virgul == 1)
        {
            t = t.Replace(',', '.');
        }

        if (!decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out deger))
        {
            deger = 0m;
            hata = GecersizSayiUyarisi;
            return false;
        }

        hata = null;
        return true;
    }

    /// <summary>
    /// Sayı kutusundan çıkıldığında gösterilecek metin: geçerli girdi <c>.</c> (InvariantCulture) biçimine çevrilir
    /// (<c>21,02</c> → <c>21.02</c>); boş veya geçersiz girdi aynen döner (hata ViewModel'de gösterilir).
    /// </summary>
    public static string Normalize(string? metin)
    {
        if (metin is null)
        {
            return string.Empty;
        }

        return TryParseDecimal(metin, out var d, out _) ? d.ToString(CultureInfo.InvariantCulture) : metin;
    }

    /// <summary>Geçersiz girdinin kutu altında gösterilecek mesajı: binlik ayırıcıysa açık uyarı, değilse <paramref name="genelMesaj"/>.</summary>
    public static string HataMesaji(string? metin, string genelMesaj) =>
        !TryParseDecimal(metin, out _, out var hata) && hata == BinlikAyiriciUyarisi ? BinlikAyiriciUyarisi : genelMesaj;
}

/// <summary>
/// P7 UX: <c>g:OdakKaybi.Komut="{Binding …Command}"</c> ile işaretlenen denetimden çıkıldığında (LostFocus) komut çalışır
/// (ör. "Metin / Toplu Yapıştır" alanındaki virgüllü ondalıkların tablodan <c>.</c> biçiminde yeniden yazılması).
/// </summary>
public static class OdakKaybi
{
    public static readonly AttachedProperty<System.Windows.Input.ICommand?> KomutProperty =
        AvaloniaProperty.RegisterAttached<InputElement, System.Windows.Input.ICommand?>("Komut", typeof(OdakKaybi));

#pragma warning disable CA1810 // Ekli özellik değişim işleyicisi tür yüklenirken bir kez kaydedilir.
    static OdakKaybi()
#pragma warning restore CA1810
    {
        KomutProperty.Changed.AddClassHandler<InputElement>(KomutDegisti);
    }

    public static System.Windows.Input.ICommand? GetKomut(InputElement oge)
    {
        ArgumentNullException.ThrowIfNull(oge);
        return oge.GetValue(KomutProperty);
    }

    public static void SetKomut(InputElement oge, System.Windows.Input.ICommand? komut)
    {
        ArgumentNullException.ThrowIfNull(oge);
        oge.SetValue(KomutProperty, komut);
    }

    private static void KomutDegisti(InputElement oge, AvaloniaPropertyChangedEventArgs e)
    {
        oge.LostFocus -= OdakKaybedildi;
        if (e.NewValue is not null)
        {
            oge.LostFocus += OdakKaybedildi;
        }
    }

    private static void OdakKaybedildi(object? sender, RoutedEventArgs e)
    {
        if (sender is InputElement oge && GetKomut(oge) is { } komut && komut.CanExecute(null))
        {
            komut.Execute(null);
        }
    }
}

/// <summary>
/// P7 UX: <c>g:SayiKutusu.Normalize="True"</c> ile işaretlenen <see cref="TextBox"/>'tan çıkıldığında (LostFocus) geçerli
/// <c>,</c> girdisi ekranda <c>.</c> biçimine normalize edilir; böylece kullanıcı değerin doğru okunduğunu anında görür.
/// </summary>
public static class SayiKutusu
{
    public static readonly AttachedProperty<bool> NormalizeProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Normalize", typeof(SayiKutusu));

#pragma warning disable CA1810 // Ekli özellik değişim işleyicisi tür yüklenirken bir kez kaydedilir.
    static SayiKutusu()
#pragma warning restore CA1810
    {
        NormalizeProperty.Changed.AddClassHandler<TextBox>(NormalizeDegisti);
    }

    public static bool GetNormalize(TextBox kutu)
    {
        ArgumentNullException.ThrowIfNull(kutu);
        return kutu.GetValue(NormalizeProperty);
    }

    public static void SetNormalize(TextBox kutu, bool deger)
    {
        ArgumentNullException.ThrowIfNull(kutu);
        kutu.SetValue(NormalizeProperty, deger);
    }

    private static void NormalizeDegisti(TextBox kutu, AvaloniaPropertyChangedEventArgs e)
    {
        kutu.LostFocus -= OdakKaybedildi;
        if (e.NewValue is true)
        {
            kutu.LostFocus += OdakKaybedildi;
        }
    }

    private static void OdakKaybedildi(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Text: { } metin } kutu)
        {
            var yeni = SayiGirdiAyristirici.Normalize(metin);
            if (!string.Equals(yeni, metin, StringComparison.Ordinal))
            {
                kutu.Text = yeni;
            }
        }
    }
}

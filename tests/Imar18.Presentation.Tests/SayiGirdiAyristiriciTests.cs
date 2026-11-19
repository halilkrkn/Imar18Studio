using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using Imar18.Presentation.Avalonia.Gorunum;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// P7 UX (kutucuklu tablolar, 4. madde): Türkçe Windows Numpad ondalık tuşu ',' basar. Tüm sayısal kutular '.' veya ',' ile
/// ondalık kabul eder; tek '.' her zaman ondalıktır (binlik değil); '.' ile ',' birlikte veya birden çok ayırıcı tahmin
/// yürütülmeden açık Türkçe uyarıyla reddedilir. Bağımsız referans: elle yazılmış beklenen <c>decimal</c> değerleri.
/// </summary>
public sealed class SayiGirdiAyristiriciTests
{
    private const string BinlikUyarisi = "Binlik ayırıcı kullanmayın; yalnızca ondalık ayırıcı ('.' veya ',') girin (ör. 1078.90).";

    [Theory]
    [InlineData("21,02", "21.02")]
    [InlineData("21.02", "21.02")]
    [InlineData("8699,44", "8699.44")]
    [InlineData("0,09", "0.09")]
    [InlineData("1600", "1600")]
    [InlineData("  3,97  ", "3.97")]
    [InlineData("-5,5", "-5.5")]
    [InlineData("4399910.00", "4399910.00")]
    public void SayiGirdiAyristirici_Tek_Ondalik_Ayirici_Virgul_Veya_Nokta_Gecerli_Sayi_Olmali(string metin, string beklenen)
    {
        SayiGirdiAyristirici.TryParseDecimal(metin, out var deger, out var hata).Should().BeTrue();

        deger.Should().Be(decimal.Parse(beklenen, System.Globalization.CultureInfo.InvariantCulture));
        hata.Should().BeNull();
    }

    [Fact]
    public void SayiGirdiAyristirici_Tek_Nokta_Her_Zaman_Ondalik_Sayilmali_Binlik_Sayilmamali()
    {
        SayiGirdiAyristirici.TryParseDecimal("1.078", out var deger).Should().BeTrue();

        deger.Should().Be(1.078m, "tek '.' her zaman ondalık ayırıcıdır");
    }

    [Theory]
    [InlineData("1.078,90")]
    [InlineData("8,699.44")]
    [InlineData("12,3,4")]
    [InlineData("1.234.567")]
    public void SayiGirdiAyristirici_Binlik_Veya_Coklu_Ayirici_Acik_Turkce_Uyariyla_Reddedilmeli(string metin)
    {
        SayiGirdiAyristirici.TryParseDecimal(metin, out var deger, out var hata).Should().BeFalse();

        deger.Should().Be(0m);
        hata.Should().Be(BinlikUyarisi);
        SayiGirdiAyristirici.BinlikAyiriciUyarisi.Should().Be(BinlikUyarisi);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1e5")]
    [InlineData(",")]
    [InlineData("12 345")]
    public void SayiGirdiAyristirici_Bos_Veya_Gecersiz_Metin_Reddedilmeli_Turkce_Hata_Donmeli(string? metin)
    {
        SayiGirdiAyristirici.TryParseDecimal(metin, out var deger, out var hata).Should().BeFalse();

        deger.Should().Be(0m);
        hata.Should().NotBeNullOrWhiteSpace().And.NotBe(BinlikUyarisi);
    }

    [Theory]
    [InlineData("21,02", "21.02")]
    [InlineData("8699,44", "8699.44")]
    [InlineData("0,09", "0.09")]
    [InlineData(" 21.02 ", "21.02")]
    [InlineData("1.078,90", "1.078,90")]
    [InlineData("abc", "abc")]
    [InlineData("", "")]
    public void SayiGirdiAyristirici_Normalize_Gecerli_Girdiyi_Nokta_Bicimine_Cevirmeli_Gecersizi_Aynen_Birakmali(string metin, string beklenen)
    {
        SayiGirdiAyristirici.Normalize(metin).Should().Be(beklenen);
    }

    [Theory]
    [InlineData("79228162514264337593543950336")] // decimal.MaxValue + 1 (taşma)
    [InlineData("-79228162514264337593543950336")]
    public void SayiGirdiAyristirici_Tasma_Decimal_Sinirini_Asan_Girdi_Reddedilmeli(string metin)
    {
        SayiGirdiAyristirici.TryParseDecimal(metin, out var deger, out var hata).Should().BeFalse();

        deger.Should().Be(0m);
        hata.Should().Be(SayiGirdiAyristirici.GecersizSayiUyarisi);
        SayiGirdiAyristirici.TryParseDecimal("79228162514264337593543950335", out var enBuyuk).Should().BeTrue();
        enBuyuk.Should().Be(decimal.MaxValue, "sınır değer kabul edilir");
    }

    [Theory]
    [InlineData(".5", "0.5")]
    [InlineData("5,", "5")]
    [InlineData("+5", "5")]
    [InlineData("-0,00", "0.00")]
    [InlineData(",25", "0.25")]
    public void SayiGirdiAyristirici_Isaretli_Ve_Eksik_Basamakli_Sinirlar_Okunmali_Ve_Normalize_Edilmeli(string metin, string beklenen)
    {
        SayiGirdiAyristirici.TryParseDecimal(metin, out var deger).Should().BeTrue();

        deger.Should().Be(decimal.Parse(beklenen, System.Globalization.CultureInfo.InvariantCulture));
        SayiGirdiAyristirici.Normalize(metin).Should().Be(beklenen);
    }

    [Fact]
    public void SayiGirdiAyristirici_Tekrar_Edilebilir_Olmali_Ayni_Girdi_Ayni_Sonucu_Vermeli()
    {
        var sonuclar = Enumerable.Range(0, 5).Select(_ => SayiGirdiAyristirici.TryParseDecimal("21,02", out var d) ? d : -1m).Distinct().ToList();

        sonuclar.Should().Equal(21.02m);
    }

    [AvaloniaFact]
    public void SayiKutusu_Odak_Kaybinda_Virgullu_Gecerli_Degeri_Noktaya_Normalize_Etmeli_Gecersizi_Korumali()
    {
        var kutu = new TextBox();
        SayiKutusu.SetNormalize(kutu, true);
        var diger = new TextBox();
        var duz = new TextBox();
        var w = new Window { Content = new StackPanel { Children = { kutu, diger, duz } } };
        w.Show();
        Dispatcher.UIThread.RunJobs();

        kutu.Focus();
        kutu.Text = "21,02";
        diger.Focus();
        Dispatcher.UIThread.RunJobs();
        kutu.Text.Should().Be("21.02", "kullanıcı değerin doğru okunduğunu anında görmeli");

        kutu.Focus();
        kutu.Text = "1.078,90";
        diger.Focus();
        Dispatcher.UIThread.RunJobs();
        kutu.Text.Should().Be("1.078,90", "geçersiz girdi tahminle değiştirilmez; hata ViewModel'de gösterilir");

        duz.Focus();
        duz.Text = "21,02";
        diger.Focus();
        Dispatcher.UIThread.RunJobs();
        duz.Text.Should().Be("21,02", "normalizasyon yalnız sayı kutusu olarak işaretlenen kutularda");
        w.Close();
    }
}

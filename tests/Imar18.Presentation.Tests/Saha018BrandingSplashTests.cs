using Avalonia.Controls;
using Xunit;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using FluentAssertions;
using Imar18.Presentation.Avalonia.Views;

namespace Imar18.Presentation.Tests;

/// <summary>SAHA-018 — marka ikonu (çok çözünürlüklü .ico) ve açılış ekranı (SplashWindow) doğrulaması.</summary>
public sealed class Saha018BrandingSplashTests
{
    private const string IkonUri = "avares://Imar18.Presentation.Avalonia/Assets/Imar18Studio.ico";

    [AvaloniaFact]
    public void Ikon_AvaloniaKaynagi_GecerliIcoBasligiVeDortBoyutIcerir()
    {
        using var akis = AssetLoader.Open(new Uri(IkonUri));
        using var ms = new MemoryStream();
        akis.CopyTo(ms);
        var b = ms.ToArray();

        b.Take(4).Should().Equal(0x00, 0x00, 0x01, 0x00);
        BitConverter.ToUInt16(b, 4).Should().Be(4);
        var boyutlar = Enumerable.Range(0, 4).Select(i => b[6 + (i * 16)] == 0 ? 256 : b[6 + (i * 16)]).ToArray();
        boyutlar.Should().Equal(16, 32, 48, 256);
        for (var i = 0; i < 4; i++)
        {
            var ofset = (int)BitConverter.ToUInt32(b, 6 + (i * 16) + 12);
            b.Skip(ofset).Take(4).Should().Equal(0x89, 0x50, 0x4E, 0x47); // PNG-in-ICO
        }
    }

    [AvaloniaFact]
    public void Pencereler_IconOzelligi_IcoKaynagindanYuklenir()
    {
        var ana = new MainWindow();
        var splash = new SplashWindow();

        ana.Icon.Should().NotBeNull();
        splash.Icon.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void Splash_Ozellikleri_CercevesizOrtadaUstteVeSabitBoyutludur()
    {
        var w = new SplashWindow();

        w.SystemDecorations.Should().Be(SystemDecorations.None);
        w.WindowStartupLocation.Should().Be(WindowStartupLocation.CenterScreen);
        w.Topmost.Should().BeTrue();
        w.CanResize.Should().BeFalse();
        w.Width.Should().Be(540);
        w.Height.Should().Be(320);
        w.FindControl<TextBlock>("Baslik")!.Text.Should().Be("Imar18Studio");
        w.FindControl<TextBlock>("AltBaslik")!.Text.Should().Be("3194 Sayılı İmar Kanunu 18. Madde Uygulama Stüdyosu");
        w.FindControl<TextBlock>("Rozet")!.Text.Should().Be("Evre 1 • Pilot");
    }

    [AvaloniaFact]
    public async Task Splash_Ilerle_TurkceDurumMetniVeYuzdeyiGunceller()
    {
        var w = new SplashWindow();
        SplashWindow.DurumAdimlari.Should().Equal(
            "Mevzuat ve hesap çekirdeği başlatılıyor...",
            "Geometri, CAD ve dönüşüm modülleri yükleniyor...",
            "Çalışma alanı ve arayüz hazırlanıyor...");

        w.DurumMetni.Should().Be(SplashWindow.DurumAdimlari[0]);
        foreach (var (adim, i) in SplashWindow.DurumAdimlari.Select((a, i) => (a, i)))
        {
            w.Ilerle(adim, (i + 1) * 30);
            w.DurumMetni.Should().Be(adim);
            w.IlerlemeYuzdesi.Should().Be((i + 1) * 30);
        }

        await w.IlerleAsync(100, 48);
        w.IlerlemeYuzdesi.Should().Be(100);
    }
}

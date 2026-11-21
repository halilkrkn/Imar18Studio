using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Imar18.Presentation.Avalonia.Views;

/// <summary>
/// Açılış / yüklenme ekranı (SAHA-018): çerçevesiz, ekran ortasında, canlı Türkçe durum metni ve ilerleme çubuğu.
/// Yalnızca gerçek masaüstü açılışında <see cref="App"/> tarafından gösterilir; Headless testlerde ana pencere doğrudan kurulur.
/// </summary>
public sealed partial class SplashWindow : Window
{
    /// <summary>Açılış adımlarının Türkçe durum metinleri (sırayla).</summary>
    public static IReadOnlyList<string> DurumAdimlari { get; } =
    [
        "Mevzuat ve hesap çekirdeği başlatılıyor...",
        "Geometri, CAD ve dönüşüm modülleri yükleniyor...",
        "Çalışma alanı ve arayüz hazırlanıyor...",
    ];

    public SplashWindow() => AvaloniaXamlLoader.Load(this);

    /// <summary>Anlık durum metni.</summary>
    public string DurumMetni => this.FindControl<TextBlock>("Durum")!.Text ?? string.Empty;

    /// <summary>Anlık ilerleme (0–100).</summary>
    public double IlerlemeYuzdesi => this.FindControl<ProgressBar>("Ilerleme")!.Value;

    /// <summary>Durum metnini ve ilerleme yüzdesini (0–100) günceller.</summary>
    public void Ilerle(string durum, double yuzde)
    {
        this.FindControl<TextBlock>("Durum")!.Text = durum;
        this.FindControl<ProgressBar>("Ilerleme")!.Value = Math.Clamp(yuzde, 0, 100);
    }

    /// <summary>İlerleme çubuğunu verilen sürede (ms) hedef yüzdeye akıcı biçimde taşır.</summary>
    public async Task IlerleAsync(double hedefYuzde, int sureMs)
    {
        var bar = this.FindControl<ProgressBar>("Ilerleme")!;
        var baslangic = bar.Value;
        var adim = Math.Max(1, sureMs / 16);
        for (var i = 1; i <= adim; i++)
        {
            bar.Value = baslangic + ((hedefYuzde - baslangic) * i / adim);
            await Task.Delay(16);
        }

        bar.Value = hedefYuzde;
    }
}

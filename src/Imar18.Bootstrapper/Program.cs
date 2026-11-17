using Avalonia;
using Imar18.Application.Cad;
using Imar18.Infrastructure.Cad.Acad;
using Imar18.Infrastructure.Cad.Ncz;
using Imar18.Presentation.Avalonia;
using Imar18.Presentation.Avalonia.ViewModels;

namespace Imar18.Bootstrapper;

/// <summary>
/// Kompozisyon koku (P4.3): Infrastructure adaptorleri Application <see cref="CadImportService"/>'e baglanir ve
/// Presentation'a fabrika olarak verilir (Presentation Infrastructure'i bilmez — NetArchTest). Pilot Windows 10/11
/// hedeflidir (AC-PILOT-10); NU1903 nedeniyle Avalonia.Desktop yerine Win32 + Skia platformu kullanilir
/// (proje sahibi karari, 17 Kasım 2026).
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.MainViewModelFactory = () => new MainWindowViewModel(
            new CadImportService([new NczFormatAdapter(), new DxfFormatAdapter(), new DwgFormatAdapter()]));
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseWin32()
        .UseSkia()
        .LogToTrace();
}

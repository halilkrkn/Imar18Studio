using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.Controls;
using Imar18.Presentation.Avalonia.ViewModels;
using Imar18.Presentation.Avalonia.Views;

namespace Imar18.Presentation.Avalonia;

/// <summary>
/// Avalonia uygulamasi (P4.3, Semi.Avalonia koyu tema). Presentation Infrastructure'i bilmez; ana ViewModel fabrikasi
/// Bootstrapper tarafindan (adaptorler baglanarak) atanir.
/// </summary>
public sealed partial class App : global::Avalonia.Application
{
    /// <summary>Bootstrapper'in atadigi ana ViewModel fabrikasi.</summary>
    public static Func<MainWindowViewModel>? MainViewModelFactory { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && MainViewModelFactory is not null)
        {
            // SAHA-018: önce açılış ekranı; ana pencere kurulurken ilerleme güncellenir (en az ~1,2 sn akıcı geçiş).
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            splash.Show();
            _ = AcilisAsync(desktop, splash);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task AcilisAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
    {
        var onceki = desktop.ShutdownMode;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var adimlar = SplashWindow.DurumAdimlari;
            splash.Ilerle(adimlar[0], 0);
            await splash.IlerleAsync(35, 400);

            splash.Ilerle(adimlar[1], 35);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render); // açılış ekranı boyansın
            var vm = MainViewModelFactory!();
            await splash.IlerleAsync(70, 400);

            splash.Ilerle(adimlar[2], 70);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var mainWindow = new MainWindow { DataContext = vm };
            await splash.IlerleAsync(100, 400);

            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            splash.Close();
            desktop.ShutdownMode = onceki;
        }
        catch (Exception)
        {
            splash.Close();
            desktop.Shutdown(1);
            throw;
        }
    }
}

using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
            desktop.MainWindow = new MainWindow { DataContext = MainViewModelFactory() };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Imar18.Presentation.Avalonia.ViewModels;

namespace Imar18.Presentation.Avalonia.Views;

/// <summary>Ana pencere (P4.3). Kod arkasi yalnizca platform islerini yapar: dosya secici ve komut cubugu klavyesi.</summary>
public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.OpenFileRequested -= OnOpenFileRequested;
            _vm.Palette.PropertyChanged -= OnPaletteChanged;
        }

        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null)
        {
            _vm.OpenFileRequested += OnOpenFileRequested;
            _vm.Palette.PropertyChanged += OnPaletteChanged;
        }
    }

    private void OnPaletteChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && _vm?.Palette.IsOpen == true)
        {
            this.FindControl<TextBox>("PaletteInput")?.Focus();
        }
    }

    private async void OnOpenFileRequested(object? sender, EventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "CAD dosyası seçin",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Netcad / AutoCAD") { Patterns = ["*.ncz", "*.NCZ", "*.dxf", "*.DXF", "*.dwg", "*.DWG"] }],
        });
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            await _vm.ImportAsync(stream, Path.GetExtension(files[0].Name));
        }
        catch (IOException)
        {
            _vm.StatusText = "Dosya okunamadı (erişim veya disk hatası).";
        }
        catch (UnauthorizedAccessException)
        {
            _vm.StatusText = "Dosyaya erişim izni yok.";
        }
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                _vm.Palette.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                _vm.Palette.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                _vm.Palette.ExecuteSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                _vm.Palette.Close();
                e.Handled = true;
                break;
        }
    }

    private void OnValidateClick(object? sender, RoutedEventArgs e) => _vm?.Wizard.Validate();
}

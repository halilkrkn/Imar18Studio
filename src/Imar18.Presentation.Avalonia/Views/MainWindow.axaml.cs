using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Imar18.Application.Cikti;
using Imar18.Presentation.Avalonia.ViewModels;

namespace Imar18.Presentation.Avalonia.Views;

/// <summary>
/// Ana pencere (P4.3, P7). Kod arkası yalnızca platform işlerini yapar: dosya seçici, pano, komut çubuğu klavyesi, uygulama
/// tema varyantı (Koyu Stüdyo / Açık Pafta) ve panel sütun/satır genişliklerinin aç-kapa ile saklanıp geri yüklenmesi.
/// </summary>
public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;
    private GridLength _solGenislik = new(310);
    private GridLength _sagGenislik = new(320);
    private GridLength _altYukseklik = new(210);

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
            _vm.PropertyChanged -= OnViewModelChanged;
            if (_vm.OnayCikti is { } eski)
            {
                eski.KayitYoluIstendi -= OnKayitYoluIstendi;
                eski.PanoyaKopyalaIstendi -= OnPanoyaKopyalaIstendi;
            }
        }

        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null)
        {
            _vm.OpenFileRequested += OnOpenFileRequested;
            _vm.Palette.PropertyChanged += OnPaletteChanged;
            _vm.PropertyChanged += OnViewModelChanged;
            if (_vm.OnayCikti is { } yeni)
            {
                yeni.KayitYoluIstendi += OnKayitYoluIstendi;
                yeni.PanoyaKopyalaIstendi += OnPanoyaKopyalaIstendi;
            }

            TemayiUygula(_vm.AcikPafta);
            SolPaneliUygula(_vm.SolPanelAcik);
            SagPaneliUygula(_vm.SagPanelAcik);
            AltPaneliUygula(_vm.AltPanelAcik);
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.AcikPafta):
                TemayiUygula(_vm.AcikPafta);
                break;
            case nameof(MainWindowViewModel.SolPanelAcik):
                SolPaneliUygula(_vm.SolPanelAcik);
                break;
            case nameof(MainWindowViewModel.SagPanelAcik):
                SagPaneliUygula(_vm.SagPanelAcik);
                break;
            case nameof(MainWindowViewModel.AltPanelAcik):
                AltPaneliUygula(_vm.AltPanelAcik);
                break;
        }
    }

    /// <summary>Dar pencere eşiği (px): altında açık yan paneller en küçük genişliğe indirilir (tuval alanı korunur).</summary>
    internal const double DarPencereEsigi = 1200d;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // Yalnızca gerçek boyutta (MinWidth üstü) geniş → dar geçişinde; ilk yerleşimin ara boyutları ve kullanıcının dar
        // pencerede bölmeyle yaptığı ayar ezilmez.
        var yeni = e.NewSize.Width;
        var oncekiGenis = e.PreviousSize.Width >= DarPencereEsigi;
        if (!oncekiGenis || yeni < MinWidth || yeni >= DarPencereEsigi || this.FindControl<Grid>("AnaIzgara") is not { } izgara)
        {
            return;
        }

        foreach (var sutun in new[] { izgara.ColumnDefinitions[0], izgara.ColumnDefinitions[4] })
        {
            if (sutun.Width.Value > sutun.MinWidth && sutun.MinWidth > 0)
            {
                sutun.Width = new GridLength(sutun.MinWidth);
            }
        }
    }

    private static void TemayiUygula(bool acikPafta)
    {
        if (global::Avalonia.Application.Current is { } uygulama)
        {
            uygulama.RequestedThemeVariant = acikPafta ? ThemeVariant.Light : ThemeVariant.Dark;
        }
    }

    private void SolPaneliUygula(bool acik) => SutunUygula(0, "SolPanel", "SolBolme", acik, ref _solGenislik, 240);

    private void SagPaneliUygula(bool acik) => SutunUygula(4, "SagPanel", "SagBolme", acik, ref _sagGenislik, 250);

    /// <summary>Paneli gizlerken son genişliği (GridSplitter ile ayarlanmış olabilir) saklar, açarken geri yükler.</summary>
    private void SutunUygula(int sutun, string panelAdi, string bolmeAdi, bool acik, ref GridLength saklanan, double enKucuk)
    {
        if (this.FindControl<Grid>("AnaIzgara") is not { } izgara || izgara.ColumnDefinitions.Count <= sutun)
        {
            return;
        }

        var tanim = izgara.ColumnDefinitions[sutun];
        if (!acik && tanim.Width.Value > 0)
        {
            saklanan = tanim.Width;
        }

        tanim.MinWidth = acik ? enKucuk : 0;
        tanim.Width = acik ? saklanan : new GridLength(0);
        if (this.FindControl<Control>(panelAdi) is { } panel)
        {
            panel.IsVisible = acik;
        }

        if (this.FindControl<Control>(bolmeAdi) is { } bolme)
        {
            bolme.IsVisible = acik;
        }
    }

    private void AltPaneliUygula(bool acik)
    {
        if (this.FindControl<Grid>("KokIzgara") is not { } izgara)
        {
            return;
        }

        var tanim = izgara.RowDefinitions[4];
        if (!acik && tanim.Height.Value > 0)
        {
            _altYukseklik = tanim.Height;
        }

        tanim.Height = acik ? _altYukseklik : new GridLength(0);
        if (this.FindControl<Control>("AltPanel") is { } panel)
        {
            panel.IsVisible = acik;
        }

        if (this.FindControl<Control>("AltBolme") is { } bolme)
        {
            bolme.IsVisible = acik;
        }
    }

    private async void OnPanoyaKopyalaIstendi(object? sender, string metin)
    {
        if (Clipboard is { } pano)
        {
            await pano.SetTextAsync(metin);
        }
    }

    /// <summary>P5.3: çıktı için kayıt yolu seçer; üretim ve denetim ViewModel/Application katmanındadır.</summary>
    private async void OnKayitYoluIstendi(object? sender, CiktiTuru tur)
    {
        if (_vm?.OnayCikti is not { } oc)
        {
            return;
        }

        var uzanti = oc.Uzanti(tur) ?? string.Empty;
        var dosya = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Taslak çıktıyı kaydet (PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ)",
            DefaultExtension = uzanti.TrimStart('.'),
            SuggestedFileName = oc.OnerilenDosyaAdi(tur),
        });
        var yol = dosya?.TryGetLocalPath();
        if (yol is null)
        {
            return;
        }

        await oc.UretAsync(tur, yol);
    }

    private void OnPaletteChanged(object? sender, PropertyChangedEventArgs e)
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

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Imar18.Application.Cikti;
using Imar18.Presentation.Avalonia.Gorunum;
using Imar18.Presentation.Avalonia.ViewModels;

namespace Imar18.Presentation.Avalonia.Views;

/// <summary>
/// Ana pencere (P4.3, P7). Kod arkası yalnızca platform işlerini yapar: dosya seçici, pano, komut çubuğu klavyesi / fare
/// (tıklama, çift tıklama, dışarı tıklama), uygulama tema varyantı (Koyu / Açık Stüdyo), pencere ikonu (Imar18Studio amblemi),
/// temizleme onay açılır penceresi ve panel sütun/satır genişliklerinin aç-kapa ile saklanıp geri yüklenmesi.
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
        Icon = Imar18Logo.PencereIkonu();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.OpenFileRequested -= OnOpenFileRequested;
            _vm.PanoyaKopyalaIstendi -= OnPanoyaKopyalaIstendi;
            _vm.Palette.PropertyChanged -= OnPaletteChanged;
            _vm.PropertyChanged -= OnViewModelChanged;
            if (_vm.OnayCikti is { } eski)
            {
                eski.KayitYoluIstendi -= OnKayitYoluIstendi;
                eski.PanoyaKopyalaIstendi -= OnPanoyaKopyalaIstendi;
            }

            if (_vm.Hesap is { } eskiHesap)
            {
                eskiHesap.WebtapuDosyasiIstendi -= OnWebtapuDosyasiIstendi;
                eskiHesap.KrokiDosyasiIstendi -= OnKrokiDosyasiIstendi;
            }
        }

        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null)
        {
            _vm.OpenFileRequested += OnOpenFileRequested;
            _vm.PanoyaKopyalaIstendi += OnPanoyaKopyalaIstendi;
            _vm.Palette.PropertyChanged += OnPaletteChanged;
            _vm.PropertyChanged += OnViewModelChanged;
            if (_vm.OnayCikti is { } yeni)
            {
                yeni.KayitYoluIstendi += OnKayitYoluIstendi;
                yeni.PanoyaKopyalaIstendi += OnPanoyaKopyalaIstendi;
            }

            if (_vm.Hesap is { } hesap)
            {
                hesap.WebtapuDosyasiIstendi += OnWebtapuDosyasiIstendi;
                hesap.KrokiDosyasiIstendi += OnKrokiDosyasiIstendi;
            }

            TemayiUygula(_vm.AcikStudyo);
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
            case nameof(MainWindowViewModel.AcikStudyo):
                TemayiUygula(_vm.AcikStudyo);
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

    /// <summary>Responsive: bu genişliğin altında pencere "dar" sınıfı alır (alt başlık ve uzun pilot rozeti kısalır; tam etiketli üst çubuk ~1600 px ister).</summary>
    internal const double DarSinifEsigi = 1680d;

    /// <summary>Responsive: bu genişliğin altında pencere "kompakt" sınıfı alır (ikincil etiketler gizlenir).</summary>
    internal const double KompaktSinifEsigi = 1280d;

    /// <summary>Genişliğe göre responsive sınıfları uygular (stiller MainWindow.axaml içindedir).</summary>
    internal void ResponsiveSiniflariUygula(double genislik)
    {
        Classes.Set("dar", genislik < DarSinifEsigi);
        Classes.Set("kompakt", genislik < KompaktSinifEsigi);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ResponsiveSiniflariUygula(e.NewSize.Width);
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

    /// <summary>Stüdyo teması: tüm uygulama tema varyantı (paneller, kartlar). Pafta (tuval) teması ViewModel'dedir.</summary>
    private static void TemayiUygula(bool acikStudyo)
    {
        if (global::Avalonia.Application.Current is { } uygulama)
        {
            uygulama.RequestedThemeVariant = acikStudyo ? ThemeVariant.Light : ThemeVariant.Dark;
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

    /// <summary>P7.3-B1: webtapu PDF'leri (çoklu seçim); okuma ve önizleme ViewModel/Application katmanındadır.</summary>
    private async void OnWebtapuDosyasiIstendi(object? sender, EventArgs e)
    {
        if (_vm?.Hesap is not { } hesap)
        {
            return;
        }

        var dosyalar = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "webtapu Tapu Kaydı PDF'lerini seçin",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("webtapu PDF") { Patterns = ["*.pdf", "*.PDF"] }],
        });
        if (dosyalar.Count == 0)
        {
            return;
        }

        var akislar = new List<Stream>(dosyalar.Count);
        try
        {
            foreach (var d in dosyalar)
            {
                akislar.Add(await d.OpenReadAsync());
            }

            await hesap.WebtapuYukleAsync(akislar);
        }
        catch (IOException)
        {
            hesap.DurumMetni = "Dosya okunamadı (erişim veya disk hatası).";
        }
        catch (UnauthorizedAccessException)
        {
            hesap.DurumMetni = "Dosyaya erişim izni yok.";
        }
        finally
        {
            foreach (var a in akislar)
            {
                await a.DisposeAsync();
            }

            foreach (var d in dosyalar)
            {
                d.Dispose();
            }
        }
    }

    /// <summary>P7.3-B2: Yaklaşık Yer Krokisi (.NCZ; yedek .DXF/.DWG); okuma ve eşleştirme ViewModel/Application katmanındadır.</summary>
    private async void OnKrokiDosyasiIstendi(object? sender, EventArgs e)
    {
        if (_vm?.Hesap is not { } hesap)
        {
            return;
        }

        var dosyalar = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Yaklaşık Yer Krokisi seçin",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Netcad / AutoCAD") { Patterns = ["*.ncz", "*.NCZ", "*.dxf", "*.DXF", "*.dwg", "*.DWG"] }],
        });
        if (dosyalar.Count == 0)
        {
            return;
        }

        using var dosya = dosyalar[0];
        try
        {
            await using var akis = await dosya.OpenReadAsync();
            await hesap.YaklasikYerKrokisiYukleAsync(akis, Path.GetExtension(dosya.Name));
        }
        catch (IOException)
        {
            hesap.DurumMetni = "Dosya okunamadı (erişim veya disk hatası).";
        }
        catch (UnauthorizedAccessException)
        {
            hesap.DurumMetni = "Dosyaya erişim izni yok.";
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

    /// <summary>"⧉" düğmesi / sağ tık "Kopyala": metin <c>Tag</c>'dedir; kopyalama ViewModel komutuyla (durum metni) yapılır.</summary>
    private void OnKopyalaClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string metin } && _vm is not null)
        {
            _vm.KopyalaCommand.Execute(metin);
            e.Handled = true;
        }
    }

    /// <summary>Komut listesinde fare tıklaması / çift tıklama: seçili komut çalışır ve çubuk kapanır.</summary>
    private void OnKomutListesiTapped(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || e.Source is not Visual kaynak || kaynak.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { } oge)
        {
            return;
        }

        if (oge.DataContext is PaletteCommand komut)
        {
            _vm.Palette.Calistir(komut);
            e.Handled = true;
        }
    }

    /// <summary>Komut çubuğunun dışındaki karartılmış alana tıklama çubuğu kapatır.</summary>
    private void OnKomutKatmaniPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is not null && ReferenceEquals(e.Source, sender))
        {
            _vm.Palette.Close();
            e.Handled = true;
        }
    }

    /// <summary>
    /// "Evet, temizle": komut burada çalıştırılır. Command bağlaması kullanılmaz — açılır pencere kapanınca düğme görsel ağaçtan
    /// ayrılıp DataContext'ini kaybettiğinden bağlı komut çalışmıyordu (canlı kullanım hatası).
    /// </summary>
    private void OnTemizleOnayClick(object? sender, RoutedEventArgs e)
    {
        var komut = _vm?.ProjeyiTemizleCommand;
        TemizleOnayiniKapat();
        if (komut?.CanExecute(null) == true)
        {
            komut.Execute(null);
        }

        e.Handled = true;
    }

    private void OnTemizleVazgecClick(object? sender, RoutedEventArgs e)
    {
        TemizleOnayiniKapat();
        e.Handled = true;
    }

    private void TemizleOnayiniKapat() => this.FindControl<Button>("UstTemizleDugmesi")?.Flyout?.Hide();
}

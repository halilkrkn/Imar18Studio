using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Cad;
using Imar18.Application.Proje;
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.Gorunum;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>Alt paneldeki canli parsel tablosu satiri.</summary>
public sealed record ParcelRow(string LayerName, string Role, string Label, decimal? AreaM2);

/// <summary>
/// Ana pencere ViewModel'i (P4.3). Yalnizca Application katmaniyla calisir (<see cref="CadImportService"/>);
/// Infrastructure adaptorleri Bootstrapper'da baglanir (NetArchTest). Ice aktarma arka planda, ilerleme ve iptal ile
/// yurur; DXF/DWG icin etiket–poligon eslestirmesi (P4.2) sihirbazdaki rol eslemesiyle yapilir.
/// P5: proje calisma alani verildiyse ice aktarilan parsel geometrisi ve dogrulanan CRS <see cref="ProjeKomutYolu"/>
/// (CommandBus) ile projeye yazilir; 5. adim <see cref="OnayCiktiViewModel"/>'dir ve Fen Klasoru baglam menusu onaya baglanir.
/// P6: 2–4. adimlar (tecviz, DOP &amp; Y1, hak eslestirme) <see cref="HesapAdimlariViewModel"/> ile canli motora baglidir.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly CadImportService _importService;
    private readonly ProjeCalismaAlani? _proje;
    private CancellationTokenSource? _importCts;
    private CadLayerDocument? _lastDocument;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelImportCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _statusText = "Hazır — CAD dosyası açın (Ctrl+O) veya komut çubuğunu kullanın (Ctrl+K).";

    /// <summary>P7: Koyu Stüdyo (false) / Açık Pafta (true) teması. Görünüm uygulama tema varyantını değiştirir.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemaEtiketi))]
    private bool _acikPafta;

    [ObservableProperty]
    private bool _solPanelAcik = true;

    [ObservableProperty]
    private bool _sagPanelAcik = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AltPanelEtiketi))]
    private bool _altPanelAcik = true;

    /// <summary>Tema anahtarının etiketi (geçilecek tema).</summary>
    public string TemaEtiketi => AcikPafta ? "🌙 Koyu Stüdyo" : "☀️ Açık Pafta";

    /// <summary>Alt canlı cetvel panelinin aç/kapat etiketi.</summary>
    public string AltPanelEtiketi => AltPanelAcik ? "▼ Cetveli Gizle" : "▲ Cetveli Göster";

    partial void OnAcikPaftaChanged(bool value) => Canvas.AcikPafta = value;

    [RelayCommand]
    private void TemaDegistir() => AcikPafta = !AcikPafta;

    [RelayCommand]
    private void SolPanelDegistir() => SolPanelAcik = !SolPanelAcik;

    [RelayCommand]
    private void SagPanelDegistir() => SagPanelAcik = !SagPanelAcik;

    [RelayCommand]
    private void AltPanelDegistir() => AltPanelAcik = !AltPanelAcik;

    public MainWindowViewModel(CadImportService importService, ProjeCalismaAlani? proje = null)
    {
        ArgumentNullException.ThrowIfNull(importService);
        _importService = importService;
        _proje = proje;

        Palette.Register(new PaletteCommand("openFile", "CAD Dosyası Aç (.ncz / .dxf / .dwg)", "import iceri aktar ncz dxf dwg", () => OpenFileRequested?.Invoke(this, EventArgs.Empty)));
        Palette.Register(new PaletteCommand("fitAll", "Tümünü Sığdır", "zoom extents ortala", Canvas.FitAll));
        Palette.Register(new PaletteCommand("swipeCadastre", "Kadastro Görünümü (%0)", "eski sinirlandirma swipe", () => Canvas.SwipePercent = 0));
        Palette.Register(new PaletteCommand("swipeZoning", "İmar Görünümü (%100)", "yeni parselasyon swipe", () => Canvas.SwipePercent = 100));
        Palette.Register(new PaletteCommand("toggleCutHandle", "Kesim Tutamacını Göster/Gizle", "bisection kesim aci", () => Canvas.CutHandle.IsVisible = !Canvas.CutHandle.IsVisible));
        Palette.Register(new PaletteCommand("nextStep", "Sonraki Sihirbaz Adımı", "ileri wizard", () => Wizard.NextCommand.Execute(null), () => Wizard.NextCommand.CanExecute(null)));
        Palette.Register(new PaletteCommand("previousStep", "Önceki Sihirbaz Adımı", "geri wizard", () => Wizard.BackCommand.Execute(null), () => Wizard.BackCommand.CanExecute(null)));
        Palette.Register(new PaletteCommand("cancelImport", "İçe Aktarmayı İptal Et", "durdur vazgec", CancelImport, () => IsBusy));
        Palette.Register(new PaletteCommand("toggleTheme", "Koyu Stüdyo / Açık Pafta Teması", "tema pafta acik koyu kagit", TemaDegistir));
        Palette.Register(new PaletteCommand("toggleLeft", "Sol Paneli Aç/Kapat", "sihirbaz girdi panel", SolPanelDegistir));
        Palette.Register(new PaletteCommand("toggleRight", "Sağ Denetçi Panelini Aç/Kapat", "denetci parsel islemleri panel", SagPanelDegistir));
        Palette.Register(new PaletteCommand("toggleBottom", "Alt Canlı Cetveli Aç/Kapat", "cetvel tablo alt panel", AltPanelDegistir));
        Palette.Register(new PaletteCommand("toggleLabels", "Parsel Etiketlerini Göster/Gizle", "etiket yazi numara", () => Canvas.EtiketGoster = !Canvas.EtiketGoster));
        Palette.Register(new PaletteCommand("toggleGrid", "Izgarayı Göster/Gizle", "izgara grid", () => Canvas.IzgaraGoster = !Canvas.IzgaraGoster));
        Wizard.LayerRolesChanged += (_, _) => LastReapplyTask = ReapplyAsync();

        if (proje is not null)
        {
            OnayCikti = new OnayCiktiViewModel(proje);
            Hesap = new HesapAdimlariViewModel(proje, () => Wizard.Tecviz);
            Hesap.Uygulandi += (_, _) => LastProjeTask = OnayCikti.YenileAsync();
            LastProjeTask = Hesap.YenileAsync();
            var fen = new RelayCommand(() => OnayCikti.FenKlasoruIsteCommand.Execute(null), () => OnayCikti.GecerliOnayVar);
            Canvas.BaglaFenKlasoru(fen);
            OnayCikti.OnayDurumuDegisti += (_, _) => fen.NotifyCanExecuteChanged();
            Palette.Register(new PaletteCommand("approve", "Mühendis Onayı Ver (güncel revizyon)", "onay revizyon approval", () => OnayCikti.OnaylaCommand.Execute(null), () => OnayCikti.OnaylaCommand.CanExecute(null)));
            Palette.Register(new PaletteCommand("revokeApproval", "Onayı Geri Al (24 saat)", "geri al onay iptal", () => OnayCikti.GeriAlCommand.Execute(null), () => OnayCikti.GeriAlCommand.CanExecute(null)));
            Palette.Register(new PaletteCommand("exportXlsx", "Excel Cetvelleri Üret (.xlsx)", "cetvel excel 4lu alan dagitim ozet", () => OnayCikti.CetvelIsteCommand.Execute(null)));
            Palette.Register(new PaletteCommand("exportPdf", "Fen Klasörü Üret (.pdf)", "fen klasoru pdf fr348 fr521 he m2 dm2", () => OnayCikti.FenKlasoruIsteCommand.Execute(null)));
            Palette.Register(new PaletteCommand("exportCks", "DOP Özeti Yaz (.CKS)", "cks netcad dop oko", () => OnayCikti.CksIsteCommand.Execute(null)));
            Wizard.GirdiDogrulandi += (_, _) =>
            {
                // Aynı CRS yeniden doğrulanınca gereksiz denetim kaydı/dosya yazımı yapılmaz (reviewer L5).
                if (Wizard.Crs is { } crs && !AyniCrs(proje.Yol.Belge.Crs, crs))
                {
                    LastProjeTask = ProjeyeGonderAsync(new ProjeCrsAyarlaKomutu(crs));
                }
            };
        }
    }

    private static bool AyniCrs(Imar18.Domain.Geometry.ProjeCrs? a, Imar18.Domain.Geometry.ProjeCrs b) =>
        a is not null && a.Datum == b.Datum && a.OrtaMeridyenDerece == b.OrtaMeridyenDerece && a.Epok == b.Epok && a.OlcekPaydasi == b.OlcekPaydasi;

    /// <summary>Son CommandBus gonderimi (testler ve gorunum bekleyebilir).</summary>
    public Task LastProjeTask { get; private set; } = Task.CompletedTask;

    /// <summary>Sihirbaz 5. adim: onay ve cikti (proje calisma alani yoksa null — P4 davranisi).</summary>
    public OnayCiktiViewModel? OnayCikti { get; }

    /// <summary>Sihirbaz 2–4. adimlar: tecviz, DOP &amp; Y1, hak eslestirme (proje calisma alani yoksa null).</summary>
    public HesapAdimlariViewModel? Hesap { get; }

    /// <summary>Son rol degisikligiyle baslatilan yeniden eslestirme (testler ve gorunum bekleyebilir).</summary>
    public Task LastReapplyTask { get; private set; } = Task.CompletedTask;

    /// <summary>Gorunumun dosya secici acmasi istendiginde (komut cubugu / Ctrl+O).</summary>
    public event EventHandler? OpenFileRequested;

    public CadCanvasViewModel Canvas { get; } = new();

    public WizardViewModel Wizard { get; } = new();

    public CommandPaletteViewModel Palette { get; } = new();

    public ObservableCollection<string> Warnings { get; } = [];

    public ObservableCollection<ParcelRow> Parcels { get; } = [];

    /// <summary>Akisi arka planda ice aktarir. Akis cagirana aittir; tamamlaninca kapatilmaz.</summary>
    public async Task ImportAsync(Stream source, string extension)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(extension);
        if (IsBusy)
        {
            return;
        }

        _importCts = new CancellationTokenSource();
        IsBusy = true;
        ProgressPercent = 0;
        StatusText = "İçe aktarılıyor…";
        // Gec gelen ilerleme bildirimleri (tamamlandiktan sonra) yok sayilir.
        var progress = new Progress<CadImportProgress>(p =>
        {
            if (IsBusy)
            {
                ProgressPercent = p.Percent;
            }
        });
        try
        {
            var result = await _importService.ImportAsync(source, extension, progress, _importCts.Token).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusText = "İçe aktarılamadı: " + result.Error.UserMessage;
                return;
            }

            Wizard.LoadLayers(result.Value.Layers);
            _lastDocument = result.Value;
            await ApplyAsync(result.Value).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "İçe aktarma iptal edildi.";
        }
        finally
        {
            ProgressPercent = 100;
            IsBusy = false;
            _importCts.Dispose();
            _importCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelImport() => _importCts?.Cancel();

    [RelayCommand]
    private void TogglePalette() => Palette.Toggle();

    [RelayCommand]
    private void RequestOpenFile() => OpenFileRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void FitAll() => Canvas.FitAll();

    private async Task ReapplyAsync()
    {
        if (_lastDocument is { } d && !IsBusy)
        {
            await ApplyAsync(d).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Etiket eslestirmesi ve Gauss alanlari (O(poligon·yazi·kose)) arka planda hesaplanir; yalnizca sonuc arayuze
    /// aktarilir (50 ms kurali). Eslestirme rol bazlidir: once imar, sonra kadastro (ust uste binen katmanlar
    /// birbirinin yazisini "paylasilan" saymaz).
    /// </summary>
    private async Task ApplyAsync(CadLayerDocument document)
    {
        var roles = Wizard.RoleMap;
        var (scene, warnings, format, layerCount, matched) = await Task.Run(() => Compute(document, roles)).ConfigureAwait(true);
        Warnings.Clear();
        foreach (var w in warnings)
        {
            Warnings.Add(w);
        }

        Canvas.LoadScene(scene);
        Parcels.Clear();
        foreach (var p in scene.Polylines.Where(p => p.IsClosed))
        {
            Parcels.Add(new ParcelRow(p.LayerName, TurkceAdlar.Ad(p.Role), p.Label, p.AreaM2));
        }

        StatusText = $"{TurkceAdlar.Ad(format)}: {scene.Polylines.Count} poligon, {layerCount} katman, {Warnings.Count} uyarı.";

        if (_proje is not null)
        {
            var geometri = ProjeGeometriEslemesi.Olustur(matched, roles);
            if (geometri.AtlananSayisi > 0)
            {
                Warnings.Add($"{geometri.AtlananSayisi} parsel poligonu geçersiz/açık olduğu için projeye alınmadı.");
            }

            LastProjeTask = ProjeyeGonderAsync(new ParselGeometrileriAyarlaKomutu(geometri.Parseller));
            await LastProjeTask.ConfigureAwait(true);
        }
    }

    /// <summary>Komutu CommandBus'a gonderir; reddedilirse kullaniciya Turkce mesaj gosterilir. Onay ekrani yenilenir.</summary>
    private async Task ProjeyeGonderAsync(IProjeKomutu komut)
    {
        var sonuc = await _proje!.Yol.GonderAsync(komut, _proje.KullaniciId).ConfigureAwait(true);
        if (sonuc.IsFailure)
        {
            StatusText = "Projeye yazılamadı: " + sonuc.Error.UserMessage;
        }

        if (OnayCikti is not null)
        {
            await OnayCikti.YenileAsync().ConfigureAwait(true);
        }

        if (Hesap is not null)
        {
            await Hesap.YenileAsync().ConfigureAwait(true);
        }
    }

    private static (CanvasScene Scene, List<string> Warnings, CadFormat Format, int LayerCount, CadLayerDocument Matched) Compute(CadLayerDocument document, CadLayerRoleMap roles)
    {
        var warnings = document.Warnings.Select(w => w.UserMessage).ToList();
        if (document.Format is CadFormat.Dxf or CadFormat.Dwg)
        {
            foreach (var role in new[] { CadLayerRole.ZoningParcel, CadLayerRole.CadastralParcel })
            {
                var match = CadLabelMatcher.Match(document, roles, [role]);
                document = document with { Polylines = match.Polylines };
                warnings.AddRange(match.Warnings.Select(w => w.UserMessage));
            }
        }

        return (CanvasScene.From(document, roles), warnings, document.Format, document.Layers.Count, document);
    }
}

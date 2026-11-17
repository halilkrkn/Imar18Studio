using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Cad;
using Imar18.Presentation.Avalonia.Canvas;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>Alt paneldeki canli parsel tablosu satiri.</summary>
public sealed record ParcelRow(string LayerName, string Role, string Label, decimal? AreaM2);

/// <summary>
/// Ana pencere ViewModel'i (P4.3). Yalnizca Application katmaniyla calisir (<see cref="CadImportService"/>);
/// Infrastructure adaptorleri Bootstrapper'da baglanir (NetArchTest). Ice aktarma arka planda, ilerleme ve iptal ile
/// yurur; DXF/DWG icin etiket–poligon eslestirmesi (P4.2) sihirbazdaki rol eslemesiyle yapilir.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly CadImportService _importService;
    private CancellationTokenSource? _importCts;
    private CadLayerDocument? _lastDocument;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelImportCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _statusText = "Hazır — CAD dosyası açın (Ctrl+O) veya komut çubuğunu kullanın (Ctrl+K).";

    public MainWindowViewModel(CadImportService importService)
    {
        ArgumentNullException.ThrowIfNull(importService);
        _importService = importService;

        Palette.Register(new PaletteCommand("openFile", "CAD Dosyası Aç (.ncz / .dxf / .dwg)", "import iceri aktar ncz dxf dwg", () => OpenFileRequested?.Invoke(this, EventArgs.Empty)));
        Palette.Register(new PaletteCommand("fitAll", "Tümünü Sığdır", "zoom extents ortala", Canvas.FitAll));
        Palette.Register(new PaletteCommand("swipeCadastre", "Kadastro Görünümü (%0)", "eski sinirlandirma swipe", () => Canvas.SwipePercent = 0));
        Palette.Register(new PaletteCommand("swipeZoning", "İmar Görünümü (%100)", "yeni parselasyon swipe", () => Canvas.SwipePercent = 100));
        Palette.Register(new PaletteCommand("toggleCutHandle", "Kesim Tutamacını Göster/Gizle", "bisection kesim aci", () => Canvas.CutHandle.IsVisible = !Canvas.CutHandle.IsVisible));
        Palette.Register(new PaletteCommand("nextStep", "Sonraki Sihirbaz Adımı", "ileri wizard", () => Wizard.NextCommand.Execute(null), () => Wizard.NextCommand.CanExecute(null)));
        Palette.Register(new PaletteCommand("previousStep", "Önceki Sihirbaz Adımı", "geri wizard", () => Wizard.BackCommand.Execute(null), () => Wizard.BackCommand.CanExecute(null)));
        Palette.Register(new PaletteCommand("cancelImport", "İçe Aktarmayı İptal Et", "durdur vazgec", CancelImport, () => IsBusy));
        Wizard.LayerRolesChanged += (_, _) => LastReapplyTask = ReapplyAsync();
    }

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
        var (scene, warnings, format, layerCount) = await Task.Run(() => Compute(document, roles)).ConfigureAwait(true);
        Warnings.Clear();
        foreach (var w in warnings)
        {
            Warnings.Add(w);
        }

        Canvas.LoadScene(scene);
        Parcels.Clear();
        foreach (var p in scene.Polylines.Where(p => p.IsClosed))
        {
            Parcels.Add(new ParcelRow(p.LayerName, p.Role.ToString(), p.Label, p.AreaM2));
        }

        StatusText = $"{format.ToString().ToUpperInvariant()}: {scene.Polylines.Count} poligon, {layerCount} katman, {Warnings.Count} uyarı.";
    }

    private static (CanvasScene Scene, List<string> Warnings, CadFormat Format, int LayerCount) Compute(CadLayerDocument document, CadLayerRoleMap roles)
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

        return (CanvasScene.From(document, roles), warnings, document.Format, document.Layers.Count);
    }
}

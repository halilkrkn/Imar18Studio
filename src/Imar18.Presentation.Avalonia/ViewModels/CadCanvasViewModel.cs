using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Presentation.Avalonia.Canvas;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>Sag tik menusu ogesi. Sonraki fazda baglanacak islemler gerekcesiyle pasiftir.</summary>
public sealed record CanvasMenuItem(string Id, string Title, string? DisabledReason, IRelayCommand Command);

/// <summary>Canli kesim tutamaci (P4.3). Aci spec §4.2 araligi 75°–105° icinde, konum cephe boyunca 0..1 oranidir.</summary>
public sealed partial class CutHandleViewModel : ObservableObject
{
    /// <summary>En kucuk kesim acisi (spec §4.2).</summary>
    public const double MinAngleDeg = 75d;

    /// <summary>En buyuk kesim acisi (spec §4.2).</summary>
    public const double MaxAngleDeg = 105d;

    private double _angleDeg = 90d;
    private double _positionRatio = 0.5d;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private bool _isAtLimit;

    public double AngleDeg
    {
        get => _angleDeg;
        set
        {
            var v = double.IsFinite(value) ? Math.Clamp(value, MinAngleDeg, MaxAngleDeg) : _angleDeg;
            IsAtLimit = v != value;
            SetProperty(ref _angleDeg, v);
        }
    }

    public double PositionRatio
    {
        get => _positionRatio;
        set
        {
            var v = double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : _positionRatio;
            IsAtLimit = v != value;
            SetProperty(ref _positionRatio, v);
        }
    }

    /// <summary>Tutamaci cephe boyunca (oran) surukler.</summary>
    public void DragBy(double deltaRatio) => PositionRatio = _positionRatio + deltaRatio;

    /// <summary>Tutamaci dondurur (derece).</summary>
    public void RotateBy(double deltaDeg) => AngleDeg = _angleDeg + deltaDeg;

    public void Reset()
    {
        AngleDeg = 90d;
        PositionRatio = 0.5d;
        IsAtLimit = false;
    }
}

/// <summary>
/// SkiaSharp 2B CAD tuvali ViewModel'i (P4.3): kamera, sahne, Kadastro↔İmar kaydiricisi, secim, imlec koordinati,
/// kesim tutamaci ve sag tik menusu. <see cref="RenderVersion"/> her gorunum degisikliginde artar; kontrol yalnizca bu
/// degistiginde yeniden cizer (60 FPS hedefi: sahne yollari onbellekte, cizim matrisle yapilir).
/// </summary>
public sealed partial class CadCanvasViewModel : ObservableObject
{
    private double _swipePercent = 100d;
    private double _viewWidth = 800d;
    private double _viewHeight = 600d;

    [ObservableProperty]
    private CanvasScene _scene = CanvasScene.Empty;

    [ObservableProperty]
    private ScenePolyline? _selected;

    [ObservableProperty]
    private string _cursorText = string.Empty;

    [ObservableProperty]
    private string _contextMenuTitle = "PARSEL İŞLEMLERİ";

    [ObservableProperty]
    private int _renderVersion;

    public CadCanvasViewModel()
    {
        const string sonraki = "Sonraki fazda (P5/P6) bağlanacak";
        IRelayCommand Pasif() => new RelayCommand(() => { }, () => false);
        ContextMenuItems =
        [
            new CanvasMenuItem("chamfer45", "📐 45° Köşe Pahı Uygula (5×5 m)", sonraki, Pasif()),
            new CanvasMenuItem("resolveShared", "🔗 Müşterek Çöz (Md. 17/1-d)", sonraki, Pasif()),
            new CanvasMenuItem("lockFrontage", "↔ Cepheyi Sabitle (Alan Kilitli)", sonraki, Pasif()),
            new CanvasMenuItem("balanceY1", "⚖️ Tecviz Farkını Y1 Yolunda Dengele", sonraki, Pasif()),
            new CanvasMenuItem("fenPreview", "📗 Fen Klasörü (He/M2/Dm2) Önizle", sonraki, Pasif()),
            new CanvasMenuItem("cutHandle", "✂️ Canlı Kesim Tutamacını Göster", null, new RelayCommand(ShowCutHandle, () => Selected is not null)),
            new CanvasMenuItem("zoomSelection", "🔍 Seçili Parsele Yakınlaş", null, new RelayCommand(ZoomToSelection, () => Selected is not null)),
        ];
    }

    public CadCamera Camera { get; } = new();

    public CutHandleViewModel CutHandle { get; } = new();

    public ObservableCollection<CanvasMenuItem> ContextMenuItems { get; }

    /// <summary>
    /// P5: "Fen Klasörü" bağlam menüsü öğesini gerçek komuta bağlar (onay yoksa komut pasiftir; gerekçe gösterilir).
    /// </summary>
    public void BaglaFenKlasoru(IRelayCommand komut)
    {
        ArgumentNullException.ThrowIfNull(komut);
        var i = ContextMenuItems.ToList().FindIndex(m => m.Id == "fenPreview");
        ContextMenuItems[i] = new CanvasMenuItem("fenPreview", "📗 Fen Klasörü (He/M2/Dm2) Üret", "Güncel revizyon için mühendis onayı gerekli", komut);
    }

    /// <summary>0 = yalnizca eski kadastro, 100 = tam yeni imar (referans tasarim "swipe" kaydiricisi).</summary>
    public double SwipePercent
    {
        get => _swipePercent;
        set
        {
            var v = double.IsFinite(value) ? Math.Clamp(value, 0d, 100d) : _swipePercent;
            if (SetProperty(ref _swipePercent, v))
            {
                Invalidate();
            }
        }
    }

    /// <summary>Gorunum yuzdesi (100 = sigdirilmis olcek degil, mutlak 1 px/m'ye gore).</summary>
    public string ZoomText => (Camera.Scale * 100d).ToString("0.#", CultureInfo.InvariantCulture) + " %";

    public void SetViewport(double width, double height)
    {
        if (width > 0 && height > 0 && double.IsFinite(width) && double.IsFinite(height))
        {
            _viewWidth = width;
            _viewHeight = height;
        }
    }

    public void LoadScene(CanvasScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Scene = scene;
        Selected = null;
        CutHandle.IsVisible = false;
        FitAll();
    }

    public void FitAll()
    {
        if (Scene.Bounds is { } b)
        {
            Camera.FitTo(b.Min, b.Max, _viewWidth, _viewHeight);
        }

        Invalidate();
    }

    public void Pan(double dx, double dy)
    {
        Camera.Pan(dx, dy);
        Invalidate();
    }

    public void ZoomAt(double screenX, double screenY, double factor)
    {
        Camera.ZoomAt(screenX, screenY, factor);
        Invalidate();
    }

    public void PointerMoved(double screenX, double screenY)
    {
        var w = Camera.ScreenToWorld(screenX, screenY);
        CursorText = string.Create(CultureInfo.InvariantCulture, $"Y: {w.Y:0.000}   X: {w.X:0.000}");
    }

    public void SelectAt(double screenX, double screenY)
    {
        Selected = Scene.HitTest(Camera.ScreenToWorld(screenX, screenY));
        Invalidate();
    }

    partial void OnSelectedChanged(ScenePolyline? value)
    {
        ContextMenuTitle = value is null
            ? "PARSEL İŞLEMLERİ"
            : $"PARSEL İŞLEMLERİ ({(value.Label.Length > 0 ? value.Label : value.LayerName)})";
        foreach (var item in ContextMenuItems)
        {
            item.Command.NotifyCanExecuteChanged();
        }
    }

    private void ShowCutHandle()
    {
        CutHandle.Reset();
        CutHandle.IsVisible = true;
        Invalidate();
    }

    private void ZoomToSelection()
    {
        if (Selected is null || Selected.Vertices.Count == 0)
        {
            return;
        }

        var min = new Domain.Geometry.GeodeticPoint(X: Selected.Vertices.Min(v => v.X), Y: Selected.Vertices.Min(v => v.Y));
        var max = new Domain.Geometry.GeodeticPoint(X: Selected.Vertices.Max(v => v.X), Y: Selected.Vertices.Max(v => v.Y));
        Camera.FitTo(min, max, _viewWidth, _viewHeight, 48);
        Invalidate();
    }

    private void Invalidate()
    {
        RenderVersion++;
        OnPropertyChanged(nameof(ZoomText));
    }
}

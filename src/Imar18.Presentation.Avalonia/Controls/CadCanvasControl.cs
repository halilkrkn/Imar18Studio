using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.ViewModels;

namespace Imar18.Presentation.Avalonia.Controls;

/// <summary>
/// SkiaSharp 2B CAD tuvali (P4.3, P7). Çizim <see cref="CadSceneRenderer"/>'dadır: yollar sahne başına bir kez üretilir,
/// her karede yalnızca ekran matrisi (Avalonia'nın kontrol ötelemesi korunarak) uygulanır (60 FPS hedefi). Sol sürükleme
/// pan, tekerlek imleç etrafında zoom, çift tık sığdır, sol tık seçim (OSNAP yakalaması varsa nokta seçimi + panoya kopyalama;
/// P7.3-A rol atama modunda tıklanan katmana rol ataması), sağ tık seçim + bağlam menüsü ("Bu katmanın rolü ▸"), Esc rol
/// atama modundan çıkar.
/// </summary>
public sealed class CadCanvasControl : Control
{
    public static readonly StyledProperty<CadCanvasViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<CadCanvasControl, CadCanvasViewModel?>(nameof(ViewModel));

    private Point? _dragStart;
    private Point _lastPoint;
    private bool _dragged;

    static CadCanvasControl()
    {
        ClipToBoundsProperty.OverrideDefaultValue<CadCanvasControl>(true);
        FocusableProperty.OverrideDefaultValue<CadCanvasControl>(true);
    }

    public CadCanvasViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var vm = ViewModel;
        context.Custom(new SceneDrawOperation(new Rect(Bounds.Size), vm is null ? null : CadRenderState.From(vm, Bounds.Width, Bounds.Height)));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewModelProperty)
        {
            if (change.OldValue is CadCanvasViewModel old)
            {
                old.PropertyChanged -= OnViewModelChanged;
            }

            if (change.NewValue is CadCanvasViewModel vm)
            {
                vm.PropertyChanged += OnViewModelChanged;
                vm.SetViewport(Bounds.Width, Bounds.Height);
            }

            InvalidateVisual();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ViewModel?.SetViewport(e.NewSize.Width, e.NewSize.Height);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            // P7.3-A: seçim + "Bu katmanın rolü ▸" menüsü sağ tıklanan katmana göre kurulur (menü bundan sonra açılır).
            ViewModel?.SagTikla(p.X, p.Y);
            return;
        }

        if (e.ClickCount == 2)
        {
            ViewModel?.FitAll();
            return;
        }

        _dragStart = p;
        _lastPoint = p;
        _dragged = false;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        ViewModel?.PointerMoved(p.X, p.Y);
        if (_dragStart is { } start)
        {
            if (!_dragged && (Math.Abs(p.X - start.X) > 3 || Math.Abs(p.Y - start.Y) > 3))
            {
                _dragged = true;
            }

            if (_dragged)
            {
                ViewModel?.Pan(p.X - _lastPoint.X, p.Y - _lastPoint.Y);
            }

            _lastPoint = p;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragStart is not null && !_dragged)
        {
            var p = e.GetPosition(this);
            // P7.3-A: rol atama modunda tıklanan katman aktif role atanır; aksi halde P7 davranışı (OSNAP noktası seçimi +
            // panoya kopyalama, parsel seçimi).
            ViewModel?.SolTikla(p.X, p.Y);
        }

        _dragStart = null;
        e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && ViewModel is { RolAtamaAktif: true } vm)
        {
            vm.RolAtamaModundanCik();
            e.Handled = true;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ViewModel?.PointerLeft();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        ViewModel?.ZoomAt(p.X, p.Y, e.Delta.Y > 0 ? 1.2 : 1 / 1.2);
        e.Handled = true;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CadCanvasViewModel.RenderVersion) or nameof(CadCanvasViewModel.Scene))
        {
            InvalidateVisual();
        }
    }

    private sealed class SceneDrawOperation(Rect bounds, CadRenderState? state) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null || state is null)
            {
                return;
            }

            using var lease = feature.Lease();
            CadSceneRenderer.Render(lease.SkCanvas, state);
        }
    }
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Imar18.Application.Cad;
using Imar18.Domain.Geometry;
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.ViewModels;
using SkiaSharp;

namespace Imar18.Presentation.Avalonia.Controls;

/// <summary>
/// SkiaSharp 2B CAD tuvali (P4.3). Yollar sahne basina bir kez (yerel metre, float) uretilip onbellege alinir; her
/// karede yalnizca ekran matrisi uygulanir (60 FPS hedefi). Sol surukleme pan, tekerlek imlec etrafinda zoom,
/// cift tik sigdir, sol tik secim, sag tik secim + baglam menusu. Kadastro katmanlari tam, imar katmanlari
/// Kadastro↔İmar kaydiricisi oraninda (soldan) kirpilarak cizilir.
/// </summary>
public sealed class CadCanvasControl : Control
{
    public static readonly StyledProperty<CadCanvasViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<CadCanvasControl, CadCanvasViewModel?>(nameof(ViewModel));

    private static readonly ConditionalWeakTable<CanvasScene, ScenePaths> PathCache = new();

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
        context.Custom(new SceneDrawOperation(new Rect(Bounds.Size), vm is null ? null : Snapshot.From(vm, Bounds.Size)));
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
            ViewModel?.SelectAt(p.X, p.Y);
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
            ViewModel?.SelectAt(p.X, p.Y);
        }

        _dragStart = null;
        e.Pointer.Capture(null);
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

    /// <summary>Cizim anindaki degismez gorunum durumu (render is parcacigina guvenle tasinir).</summary>
    private sealed record Snapshot(
        CanvasScene Scene,
        GeodeticPoint CameraOrigin,
        double Scale,
        double OffsetX,
        double OffsetY,
        double SwipePercent,
        ScenePolyline? Selected,
        bool CutHandleVisible,
        double CutAngleDeg,
        double CutPositionRatio,
        Size Size)
    {
        public static Snapshot From(CadCanvasViewModel vm, Size size) => new(
            vm.Scene, vm.Camera.Origin, vm.Camera.Scale, vm.Camera.OffsetX, vm.Camera.OffsetY, vm.SwipePercent,
            vm.Selected, vm.CutHandle.IsVisible, vm.CutHandle.AngleDeg, vm.CutHandle.PositionRatio, size);
    }

    private sealed record PathEntry(SKPath Path, CadLayerRole Role, bool IsClosed, string Label, SKPoint Centroid, ScenePolyline Source);

    /// <summary>Sahne yollari; yerel metre koordinatlari sahne sinir kutusunun minimumuna goredir.</summary>
    private sealed class ScenePaths
    {
        public ScenePaths(CanvasScene scene)
        {
            Origin = scene.Bounds?.Min ?? default;
            foreach (var p in scene.Polylines)
            {
                if (p.Vertices.Count < 2)
                {
                    continue;
                }

                var path = new SKPath();
                float sx = 0, sy = 0;
                for (var i = 0; i < p.Vertices.Count; i++)
                {
                    var x = (float)(double)(p.Vertices[i].Y - Origin.Y);
                    var y = (float)(double)(p.Vertices[i].X - Origin.X);
                    sx += x;
                    sy += y;
                    if (i == 0)
                    {
                        path.MoveTo(x, y);
                    }
                    else
                    {
                        path.LineTo(x, y);
                    }
                }

                if (p.IsClosed)
                {
                    path.Close();
                }

                Entries.Add(new PathEntry(path, p.Role, p.IsClosed, p.Label, new SKPoint(sx / p.Vertices.Count, sy / p.Vertices.Count), p));
            }
        }

        public GeodeticPoint Origin { get; }

        public List<PathEntry> Entries { get; } = [];
    }

    private sealed class SceneDrawOperation(Rect bounds, Snapshot? snapshot) : ICustomDrawOperation
    {
        private static readonly SKColor Background = SKColor.Parse("#0D0F13");
        private static readonly SKColor GridMinor = new(255, 255, 255, 8);
        private static readonly SKColor Cadastre = SKColor.Parse("#FBBF24");
        private static readonly SKColor Zoning = SKColor.Parse("#60A5FA");
        private static readonly SKColor SelectionFill = new(59, 130, 246, 64);
        private static readonly SKColor SelectionStroke = SKColor.Parse("#93C5FD");
        private static readonly SKColor LabelColor = SKColor.Parse("#E2E8F0");
        private static readonly SKColor CutColor = SKColor.Parse("#F59E0B");

        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null)
            {
                return;
            }

            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
            canvas.Clear(Background);
            if (snapshot is not null)
            {
                DrawGrid(canvas, snapshot);
                DrawScene(canvas, snapshot);
            }

            canvas.Restore();
        }

        private static void DrawGrid(SKCanvas canvas, Snapshot s)
        {
            // 10^k metre aralikli, ekranda en az 40 px'lik izgara.
            var step = Math.Pow(10, Math.Ceiling(Math.Log10(40 / Math.Max(s.Scale, 1e-9))));
            var px = step * s.Scale;
            if (!double.IsFinite(px) || px < 4)
            {
                return;
            }

            using var paint = new SKPaint { Color = GridMinor, StrokeWidth = 1, IsAntialias = false };
            var startX = s.OffsetX % px;
            for (var x = startX; x < s.Size.Width; x += px)
            {
                canvas.DrawLine((float)x, 0, (float)x, (float)s.Size.Height, paint);
            }

            var startY = s.OffsetY % px;
            for (var y = startY; y < s.Size.Height; y += px)
            {
                canvas.DrawLine(0, (float)y, (float)s.Size.Width, (float)y, paint);
            }
        }

        private static void DrawScene(SKCanvas canvas, Snapshot s)
        {
            var paths = PathCache.GetValue(s.Scene, scene => new ScenePaths(scene));
            var tx = s.OffsetX + ((double)(paths.Origin.Y - s.CameraOrigin.Y) * s.Scale);
            var ty = s.OffsetY - ((double)(paths.Origin.X - s.CameraOrigin.X) * s.Scale);
            var matrix = new SKMatrix((float)s.Scale, 0, (float)tx, 0, (float)-s.Scale, (float)ty, 0, 0, 1);
            var unit = (float)(1 / s.Scale);

            using var dash = SKPathEffect.CreateDash([6 * unit, 4 * unit], 0);
            using var cadastre = new SKPaint
            {
                Color = Cadastre, IsStroke = true, StrokeWidth = unit, IsAntialias = true, PathEffect = dash,
            };
            using var zoning = new SKPaint { Color = Zoning, IsStroke = true, StrokeWidth = 0, IsAntialias = true };

            canvas.Save();
            canvas.SetMatrix(matrix);
            foreach (var e in paths.Entries.Where(e => IsCadastre(e.Role)))
            {
                canvas.DrawPath(e.Path, cadastre);
            }

            canvas.Restore();

            // Imar katmanlari: Kadastro↔İmar kaydiricisi oraninda soldan kirpilir (0: gizli, 100: tam).
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)(s.Size.Width * s.SwipePercent / 100d), (float)s.Size.Height));
            canvas.SetMatrix(matrix);
            foreach (var e in paths.Entries.Where(e => !IsCadastre(e.Role)))
            {
                canvas.DrawPath(e.Path, zoning);
            }

            canvas.Restore();

            var selected = s.Selected is null ? null : paths.Entries.FirstOrDefault(e => ReferenceEquals(e.Source, s.Selected));
            if (selected is not null)
            {
                using var fill = new SKPaint { Color = SelectionFill, IsStroke = false, IsAntialias = true };
                using var stroke = new SKPaint { Color = SelectionStroke, IsStroke = true, StrokeWidth = 2 * unit, IsAntialias = true };
                canvas.Save();
                canvas.SetMatrix(matrix);
                canvas.DrawPath(selected.Path, fill);
                canvas.DrawPath(selected.Path, stroke);
                canvas.Restore();
            }

            DrawLabels(canvas, matrix, paths);
            if (selected is not null && s.CutHandleVisible)
            {
                DrawCutHandle(canvas, matrix, selected, s);
            }
        }

        private static void DrawLabels(SKCanvas canvas, SKMatrix matrix, ScenePaths paths)
        {
            using var text = new SKPaint { Color = LabelColor, TextSize = 11, IsAntialias = true, TextAlign = SKTextAlign.Center };
            foreach (var e in paths.Entries.Where(e => e.Label.Length > 0).Take(5000))
            {
                var p = matrix.MapPoint(e.Centroid);
                canvas.DrawText(e.Label, p.X, p.Y, text);
            }
        }

        private static void DrawCutHandle(SKCanvas canvas, SKMatrix matrix, PathEntry selected, Snapshot s)
        {
            // Gosterim: secili poligon merkezinden gecen, Dogu eksenine gore AngleDeg acili 120 px'lik kesim cizgisi;
            // tutamac cizgi boyunca PositionRatio konumundadir. Gercek kesim BisectionEngine ile yapilir; tutamac yalniz onizlemedir (P6: sihirbaza bagli degildir, P7 kapsami).
            var c = matrix.MapPoint(selected.Centroid);
            var rad = s.CutAngleDeg * Math.PI / 180d;
            var dx = (float)(Math.Cos(rad) * 60);
            var dy = (float)(-Math.Sin(rad) * 60);
            using var line = new SKPaint { Color = CutColor, IsStroke = true, StrokeWidth = 2, IsAntialias = true };
            canvas.DrawLine(c.X - dx, c.Y - dy, c.X + dx, c.Y + dy, line);
            var t = (float)((s.CutPositionRatio * 2) - 1);
            using var knob = new SKPaint { Color = CutColor, IsStroke = false, IsAntialias = true };
            canvas.DrawCircle(c.X + (dx * t), c.Y + (dy * t), 6, knob);
        }

        private static bool IsCadastre(CadLayerRole role) =>
            role is CadLayerRole.CadastralParcel or CadLayerRole.CadastralLine or CadLayerRole.RegulationBoundary;
    }
}

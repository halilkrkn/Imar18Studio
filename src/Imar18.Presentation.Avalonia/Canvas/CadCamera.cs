using Imar18.Domain.Geometry;

namespace Imar18.Presentation.Avalonia.Canvas;

/// <summary>
/// 2B CAD tuvali kamerasi (P4.3). Harita gorunumu kuzey yukari: ekran x = (Dogu - Dogu0) * olcek + ofsetX,
/// ekran y = ofsetY - (Kuzey - Kuzey0) * olcek. <c>double</c> yalnizca ekran donusum matrisi icindir
/// (architecture.md: SkiaSharp ekran matrisleri); dunya koordinati <c>decimal</c> olarak girer/cikar.
/// </summary>
public sealed class CadCamera
{
    /// <summary>En kucuk olcek (piksel / metre).</summary>
    public const double MinScale = 1e-4;

    /// <summary>En buyuk olcek (piksel / metre).</summary>
    public const double MaxScale = 1e4;

    /// <summary>Dunya orijini (yerel otelemenin referansi).</summary>
    public GeodeticPoint Origin { get; private set; }

    /// <summary>Olcek (piksel / metre).</summary>
    public double Scale { get; private set; } = 1d;

    /// <summary>Orijinin ekran x konumu.</summary>
    public double OffsetX { get; private set; }

    /// <summary>Orijinin ekran y konumu.</summary>
    public double OffsetY { get; private set; }

    public (double X, double Y) WorldToScreen(GeodeticPoint p) =>
        (((double)(p.Y - Origin.Y) * Scale) + OffsetX, OffsetY - ((double)(p.X - Origin.X) * Scale));

    /// <summary>Ekran noktasini dunya koordinatina cevirir (mm'ye yuvarlanir).</summary>
    public GeodeticPoint ScreenToWorld(double screenX, double screenY) => new(
        X: Origin.X + decimal.Round((decimal)((OffsetY - screenY) / Scale), 3, MidpointRounding.AwayFromZero),
        Y: Origin.Y + decimal.Round((decimal)((screenX - OffsetX) / Scale), 3, MidpointRounding.AwayFromZero));

    public void Pan(double dx, double dy)
    {
        if (double.IsFinite(dx) && double.IsFinite(dy))
        {
            OffsetX += dx;
            OffsetY += dy;
        }
    }

    /// <summary>Ekran noktasini sabit tutarak yakinlastirir; gecersiz carpan yok sayilir, olcek sinirlanir.</summary>
    public void ZoomAt(double screenX, double screenY, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0d || !double.IsFinite(screenX) || !double.IsFinite(screenY))
        {
            return;
        }

        var newScale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        var k = newScale / Scale;
        OffsetX = screenX - ((screenX - OffsetX) * k);
        OffsetY = screenY - ((screenY - OffsetY) * k);
        Scale = newScale;
    }

    /// <summary>Dunya kutusunu (min/max) gorunume sigdirir ve ortalar.</summary>
    public void FitTo(GeodeticPoint min, GeodeticPoint max, double viewWidth, double viewHeight, double marginPx = 24d)
    {
        Origin = min;
        var widthM = Math.Max((double)(max.Y - min.Y), 1e-6);
        var heightM = Math.Max((double)(max.X - min.X), 1e-6);
        var w = Math.Max(viewWidth - (2 * marginPx), 1d);
        var h = Math.Max(viewHeight - (2 * marginPx), 1d);
        Scale = Math.Clamp(Math.Min(w / widthM, h / heightM), MinScale, MaxScale);
        OffsetX = (viewWidth - (widthM * Scale)) / 2d;
        OffsetY = (viewHeight + (heightM * Scale)) / 2d;
    }
}

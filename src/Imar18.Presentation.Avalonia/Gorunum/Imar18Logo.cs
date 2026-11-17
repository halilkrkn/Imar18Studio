using Avalonia.Controls;
using SkiaSharp;

namespace Imar18.Presentation.Avalonia.Gorunum;

/// <summary>
/// P7: Imar18Studio amblemi — mavi yuvarlatılmış kare üzerinde beyaz imar adası, adayı parsellere bölen amber kesim çizgileri
/// (Md. 18 parselasyonu) ve sağ altta amber "18" rozeti. Başlıktaki vektörel <c>DrawingImage</c> (App.axaml "Imar18Logo") ile
/// aynı 64 × 64 birimlik çizimdir; pencere / görev çubuğu ikonu bu sınıftan PNG olarak üretilir (harici dosya gerekmez).
/// </summary>
public static class Imar18Logo
{
    /// <summary>Ana renk (CadAccent ile uyumlu mavi).</summary>
    public static readonly SKColor Mavi = SKColor.Parse("#2563EB");

    /// <summary>Koyu mavi (degrade sonu).</summary>
    public static readonly SKColor KoyuMavi = SKColor.Parse("#1E3A8A");

    /// <summary>Parsel kesim çizgisi ve "18" rozeti rengi.</summary>
    public static readonly SKColor Amber = SKColor.Parse("#FBBF24");

    private static readonly SKColor Lacivert = SKColor.Parse("#0F172A");

    /// <summary>Amblemi verilen piksel boyutunda kare bitmap olarak çizer (saydam zemin).</summary>
    public static SKBitmap Ciz(int boyut)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boyut, 16);
        var bmp = new SKBitmap(new SKImageInfo(boyut, boyut, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        c.Scale(boyut / 64f);

        using (var zemin = new SKPaint { IsAntialias = true })
        {
            zemin.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(64, 64), [Mavi, KoyuMavi], SKShaderTileMode.Clamp);
            c.DrawRoundRect(new SKRect(0, 0, 64, 64), 14, 14, zemin);
        }

        using (var ada = new SKPath())
        {
            ada.MoveTo(10, 14);
            ada.LineTo(44, 10);
            ada.LineTo(48, 40);
            ada.LineTo(8, 44);
            ada.Close();
            using var dolgu = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(40) };
            using var kenar = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 2.5f, Color = SKColors.White, StrokeJoin = SKStrokeJoin.Round };
            c.DrawPath(ada, dolgu);
            c.DrawPath(ada, kenar);
        }

        using (var kesim = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 2f, Color = Amber, StrokeCap = SKStrokeCap.Round })
        {
            c.DrawLine(27, 12, 28, 42, kesim);
            c.DrawLine(9, 29, 46, 25, kesim);
        }

        using (var rozet = new SKPaint { IsAntialias = true, Color = Amber })
        using (var rozetKenar = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 2f, Color = KoyuMavi })
        {
            c.DrawCircle(47, 47, 13, rozet);
            c.DrawCircle(47, 47, 13, rozetKenar);
        }

        using (var rakam = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 2.3f, Color = Lacivert, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round })
        {
            using var bir = new SKPath();
            bir.MoveTo(40.5f, 42);
            bir.LineTo(43.5f, 40);
            bir.LineTo(43.5f, 54);
            c.DrawPath(bir, rakam);
            c.DrawOval(new SKRect(48, 40.2f, 54, 45.8f), rakam);
            c.DrawOval(new SKRect(47.6f, 46.2f, 54.4f, 53.8f), rakam);
        }

        return bmp;
    }

    /// <summary>Amblemi PNG bayt dizisi olarak üretir.</summary>
    public static byte[] Png(int boyut)
    {
        using var bmp = Ciz(boyut);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Pencere / görev çubuğu ikonu (64 px PNG).</summary>
    public static WindowIcon PencereIkonu() => new(new MemoryStream(Png(64)));
}

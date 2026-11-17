using System.Runtime.CompilerServices;
using Imar18.Application.Cad;
using Imar18.Domain.Geometry;
using Imar18.Presentation.Avalonia.ViewModels;
using SkiaSharp;

namespace Imar18.Presentation.Avalonia.Canvas;

/// <summary>Çizim anındaki değişmez görünüm durumu (render iş parçacığına güvenle taşınır).</summary>
internal sealed record CadRenderState(
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
    double Width,
    double Height,
    bool AcikPafta,
    bool IzgaraGoster,
    bool EtiketGoster,
    OsnapSonucu? Yakalama,
    OsnapSonucu? SecilenNokta = null)
{
    public static CadRenderState From(CadCanvasViewModel vm, double width, double height) => new(
        vm.Scene, vm.Camera.Origin, vm.Camera.Scale, vm.Camera.OffsetX, vm.Camera.OffsetY, vm.SwipePercent,
        vm.Selected, vm.CutHandle.IsVisible, vm.CutHandle.AngleDeg, vm.CutHandle.PositionRatio, width, height,
        vm.AcikPafta, vm.IzgaraGoster, vm.EtiketGoster, vm.Yakalama, vm.SecilenNokta);

    /// <summary>İmar perdesinin (swipe) sağ kenarı (yerel piksel).</summary>
    public double PerdeX => Width * SwipePercent / 100d;

    /// <summary>Dünya → yerel ekran (kamera ile birebir aynı formül).</summary>
    public SKPoint Ekran(GeodeticPoint p) => new(
        (float)(((double)(p.Y - CameraOrigin.Y) * Scale) + OffsetX),
        (float)(OffsetY - ((double)(p.X - CameraOrigin.X) * Scale)));
}

/// <summary>Ekrana basılacak parsel etiketi (yerel piksel; metin merkezi).</summary>
internal sealed record EtiketKonumu(string Metin, float X, float Y, bool Kadastro);

/// <summary>
/// SkiaSharp 2B CAD sahne çizicisi (P7). Tüm çizim, Avalonia'nın verdiği başlangıç matrisi (kontrolün pencere içindeki
/// konumu) <b>korunarak</b> yapılır: dünya matrisi <c>Concat</c> ile eklenir (eski <c>SetMatrix</c> başlangıç ötelemesini
/// silip poligonları pencere başına, etiketleri ise ötelenmiş konuma çiziyordu — etiketler "sağa uçuyordu").
/// Etiketler, ızgara, seçim, kesim tutamacı ve OSNAP işareti aynı yerel ekran dönüşümünü (<see cref="CadRenderState.Ekran"/>)
/// kullanır. Yollar sahne başına bir kez üretilip önbelleğe alınır (60 FPS hedefi).
/// </summary>
internal static class CadSceneRenderer
{
    /// <summary>Tek karede en fazla çizilecek etiket sayısı.</summary>
    public const int EnFazlaEtiket = 5000;

    private const float EtiketBoyu = 11f;

    private static readonly ConditionalWeakTable<CanvasScene, ScenePaths> PathCache = new();

    private sealed record Palet(SKColor Arkaplan, SKColor IzgaraKucuk, SKColor IzgaraBuyuk, SKColor Kadastro, SKColor Imar,
        SKColor SecimDolgu, SKColor SecimCizgi, SKColor KadastroEtiket, SKColor ImarEtiket, SKColor Kesim, SKColor Yakalama, SKColor Perde,
        SKColor Yol, SKColor Park, SKColor Sinir, SKColor AdaKenari, SKColor Diger, SKColor EtiketZemin, SKColor EtiketYazi);

    // P7 cilası: koyu paftada ızgara kontrastı artırıldı (ince çizgi ~%12, ana çizgi ~%33 opaklık; önceki %3 / %6 koyu zeminde
    // görünmüyordu). OSNAP sembolü parlak camgöbeği, etiket kutusu yarı saydam zeminlidir.
    private static readonly Palet Koyu = new(
        SKColor.Parse("#0D0F13"), new SKColor(148, 163, 184, 30), new SKColor(96, 165, 250, 84), SKColor.Parse("#FBBF24"),
        SKColor.Parse("#60A5FA"), new SKColor(59, 130, 246, 64), SKColor.Parse("#93C5FD"), SKColor.Parse("#FCD34D"),
        SKColor.Parse("#E2E8F0"), SKColor.Parse("#F59E0B"), SKColor.Parse("#22D3EE"), new SKColor(251, 191, 36, 140),
        SKColor.Parse("#94A3B8"), SKColor.Parse("#34D399"), SKColor.Parse("#F87171"), SKColor.Parse("#C084FC"), new SKColor(148, 163, 184, 110),
        new SKColor(15, 23, 42, 225), SKColor.Parse("#E0F2FE"));

    private static readonly Palet Acik = new(
        SKColor.Parse("#F8FAFC"), new SKColor(15, 23, 42, 22), new SKColor(37, 99, 235, 60), SKColor.Parse("#B45309"),
        SKColor.Parse("#2563EB"), new SKColor(37, 99, 235, 48), SKColor.Parse("#1D4ED8"), SKColor.Parse("#92400E"),
        SKColor.Parse("#0F172A"), SKColor.Parse("#D97706"), SKColor.Parse("#0891B2"), new SKColor(180, 83, 9, 150),
        SKColor.Parse("#475569"), SKColor.Parse("#047857"), SKColor.Parse("#DC2626"), SKColor.Parse("#7C3AED"), new SKColor(71, 85, 105, 120),
        new SKColor(255, 255, 255, 235), SKColor.Parse("#0C4A6E"));

    /// <summary>Izgara renkleri (testler kontrastı doğrular): arka plan, ince ve ana çizgi.</summary>
    internal static (SKColor Arkaplan, SKColor Kucuk, SKColor Buyuk) IzgaraRenkleri(bool acikPafta)
    {
        var p = acikPafta ? Acik : Koyu;
        return (p.Arkaplan, p.IzgaraKucuk, p.IzgaraBuyuk);
    }

    /// <summary>OSNAP yakalama sembolü rengi (parlak, zeminden belirgin).</summary>
    internal static SKColor YakalamaRengi(bool acikPafta) => (acikPafta ? Acik : Koyu).Yakalama;

    /// <summary>
    /// P7: katman rolüne göre çizgi rengi — Katman → Rol eşlemesi değişince tuval anında farklı renkle çizilir (kadastro amber,
    /// imar mavi, yola terk gri, park yeşil, düzenleme sınırı kırmızı, ada kenarı mor, atanmamış soluk).
    /// </summary>
    internal static SKColor RolRengi(CadLayerRole rol, bool acikPafta)
    {
        var p = acikPafta ? Acik : Koyu;
        return rol switch
        {
            CadLayerRole.CadastralParcel or CadLayerRole.CadastralLine => p.Kadastro,
            CadLayerRole.ZoningParcel => p.Imar,
            CadLayerRole.RoadDedication => p.Yol,
            CadLayerRole.Park => p.Park,
            CadLayerRole.RegulationBoundary => p.Sinir,
            CadLayerRole.BlockEdge => p.AdaKenari,
            _ => p.Diger,
        };
    }

    public static void Render(SKCanvas canvas, CadRenderState s)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(s);
        var palet = s.AcikPafta ? Acik : Koyu;
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, (float)s.Width, (float)s.Height));
        canvas.Clear(palet.Arkaplan);
        if (s.IzgaraGoster)
        {
            DrawGrid(canvas, s, palet);
        }

        DrawScene(canvas, s, palet);
        canvas.Restore();
    }

    /// <summary>
    /// Görünür parsel etiketleri: kapalı poligonun alan merkezinde; ekranda metinden küçük kalan poligonun etiketi
    /// gizlenir (etiket yığını oluşmaz); imar etiketleri perde dışında çizilmez.
    /// </summary>
    public static IReadOnlyList<EtiketKonumu> EtiketKonumlari(CadRenderState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (!s.EtiketGoster)
        {
            return [];
        }

        var paths = PathCache.GetValue(s.Scene, scene => new ScenePaths(scene));
        var matrix = WorldMatrix(s, paths);
        var perde = s.PerdeX;
        var sonuc = new List<EtiketKonumu>();
        foreach (var e in paths.Entries)
        {
            if (e.Label.Length == 0 || sonuc.Count >= EnFazlaEtiket)
            {
                continue;
            }

            var kutu = matrix.MapRect(e.Path.Bounds);
            var metinGenisligi = e.Label.Length * EtiketBoyu * 0.6f;
            if (kutu.Width < metinGenisligi * 0.8f || kutu.Height < EtiketBoyu)
            {
                continue;
            }

            if (kutu.Right < 0 || kutu.Left > s.Width || kutu.Bottom < 0 || kutu.Top > s.Height)
            {
                continue;
            }

            var p = matrix.MapPoint(e.Centroid);
            var kadastro = IsCadastre(e.Role);
            if (!kadastro && p.X > perde)
            {
                continue;
            }

            sonuc.Add(new EtiketKonumu(e.Label, p.X, p.Y, kadastro));
        }

        return sonuc;
    }

    private static SKMatrix WorldMatrix(CadRenderState s, ScenePaths paths)
    {
        var tx = s.OffsetX + ((double)(paths.Origin.Y - s.CameraOrigin.Y) * s.Scale);
        var ty = s.OffsetY - ((double)(paths.Origin.X - s.CameraOrigin.X) * s.Scale);
        return new SKMatrix((float)s.Scale, 0, (float)tx, 0, (float)-s.Scale, (float)ty, 0, 0, 1);
    }

    private static void DrawGrid(SKCanvas canvas, CadRenderState s, Palet palet)
    {
        // 10^k metre aralıklı, ekranda en az 40 px'lik ızgara; her 10 çizgide bir ana çizgi.
        var step = Math.Pow(10, Math.Ceiling(Math.Log10(40 / Math.Max(s.Scale, 1e-9))));
        var px = step * s.Scale;
        if (!double.IsFinite(px) || px < 4)
        {
            return;
        }

        using var kucuk = new SKPaint { Color = palet.IzgaraKucuk, StrokeWidth = 1, IsAntialias = false };
        using var buyuk = new SKPaint { Color = palet.IzgaraBuyuk, StrokeWidth = 1.25f, IsAntialias = false };
        var anaAdim = px * 10;
        for (var x = Mod(s.OffsetX, px); x < s.Width; x += px)
        {
            var ana = Math.Abs(Mod(x - s.OffsetX, anaAdim)) < 0.5 || Math.Abs(Mod(x - s.OffsetX, anaAdim) - anaAdim) < 0.5;
            canvas.DrawLine((float)x, 0, (float)x, (float)s.Height, ana ? buyuk : kucuk);
        }

        for (var y = Mod(s.OffsetY, px); y < s.Height; y += px)
        {
            var ana = Math.Abs(Mod(y - s.OffsetY, anaAdim)) < 0.5 || Math.Abs(Mod(y - s.OffsetY, anaAdim) - anaAdim) < 0.5;
            canvas.DrawLine(0, (float)y, (float)s.Width, (float)y, ana ? buyuk : kucuk);
        }
    }

    private static double Mod(double a, double m) => ((a % m) + m) % m;

    private static void DrawScene(SKCanvas canvas, CadRenderState s, Palet palet)
    {
        var paths = PathCache.GetValue(s.Scene, scene => new ScenePaths(scene));
        var matrix = WorldMatrix(s, paths);
        var unit = (float)(1 / s.Scale);

        using var dash = SKPathEffect.CreateDash([6 * unit, 4 * unit], 0);
        using var cizgi = new SKPaint { IsStroke = true, IsAntialias = true };

        // Başlangıç matrisi (kontrol konumu) korunur: Concat. Kadastro tarafı kesikli; renk role göredir (P7).
        canvas.Save();
        canvas.Concat(ref matrix);
        foreach (var e in paths.Entries.Where(e => IsCadastre(e.Role)))
        {
            cizgi.Color = RolRengi(e.Role, s.AcikPafta);
            cizgi.StrokeWidth = (e.Role == CadLayerRole.RegulationBoundary ? 2f : 1f) * unit;
            cizgi.PathEffect = dash;
            canvas.DrawPath(e.Path, cizgi);
        }

        canvas.Restore();

        // İmar katmanları: Kadastro↔İmar perdesi oranında soldan kırpılır (0: gizli, 100: tam).
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, (float)s.PerdeX, (float)s.Height));
        canvas.Concat(ref matrix);
        cizgi.PathEffect = null;
        foreach (var e in paths.Entries.Where(e => !IsCadastre(e.Role)))
        {
            cizgi.Color = RolRengi(e.Role, s.AcikPafta);
            cizgi.StrokeWidth = (e.Role is CadLayerRole.Unassigned or CadLayerRole.NewParcelPoint or CadLayerRole.RegisteredPoint ? 1f : 1.5f) * unit;
            canvas.DrawPath(e.Path, cizgi);
        }

        canvas.Restore();

        if (s.SwipePercent is > 0 and < 100)
        {
            using var perde = new SKPaint { Color = palet.Perde, StrokeWidth = 1.5f, IsAntialias = true };
            canvas.DrawLine((float)s.PerdeX, 0, (float)s.PerdeX, (float)s.Height, perde);
        }

        var selected = s.Selected is null ? null : paths.Entries.FirstOrDefault(e => ReferenceEquals(e.Source, s.Selected));
        if (selected is not null)
        {
            using var fill = new SKPaint { Color = palet.SecimDolgu, IsStroke = false, IsAntialias = true };
            using var stroke = new SKPaint { Color = palet.SecimCizgi, IsStroke = true, StrokeWidth = 2 * unit, IsAntialias = true };
            canvas.Save();
            canvas.Concat(ref matrix);
            canvas.DrawPath(selected.Path, fill);
            canvas.DrawPath(selected.Path, stroke);
            canvas.Restore();
        }

        DrawLabels(canvas, s, palet);
        if (selected is not null && s.CutHandleVisible)
        {
            DrawCutHandle(canvas, matrix.MapPoint(selected.Centroid), s, palet);
        }

        if (s.SecilenNokta is { } secilen)
        {
            DrawSecilenNokta(canvas, s.Ekran(secilen.Nokta), palet);
        }

        if (s.Yakalama is { } y)
        {
            var p = s.Ekran(y.Nokta);
            DrawSnap(canvas, p, y.Kip, palet);
            DrawEtiketKutusu(canvas, p, CadCanvasViewModel.OsnapEtiketi(y), s, palet);
        }
    }

    /// <summary>Seçilen OSNAP noktası: dolgu baklava + artı (imleç ayrılsa da işaretli kalır).</summary>
    private static void DrawSecilenNokta(SKCanvas canvas, SKPoint p, Palet palet)
    {
        const float r = 7f;
        using var dolgu = new SKPaint { Color = palet.Yakalama.WithAlpha(110), IsStroke = false, IsAntialias = true };
        using var cizgi = new SKPaint { Color = palet.Yakalama, IsStroke = true, StrokeWidth = 2f, IsAntialias = true };
        using var baklava = new SKPath();
        baklava.MoveTo(p.X, p.Y - r);
        baklava.LineTo(p.X + r, p.Y);
        baklava.LineTo(p.X, p.Y + r);
        baklava.LineTo(p.X - r, p.Y);
        baklava.Close();
        canvas.DrawPath(baklava, dolgu);
        canvas.DrawPath(baklava, cizgi);
        canvas.DrawLine(p.X - (2 * r), p.Y, p.X + (2 * r), p.Y, cizgi);
        canvas.DrawLine(p.X, p.Y - (2 * r), p.X, p.Y + (2 * r), cizgi);
    }

    /// <summary>Yakalama sembolünün sağ üstünde yarı saydam zeminli koordinat etiketi (tuval dışına taşmaz).</summary>
    private static void DrawEtiketKutusu(SKCanvas canvas, SKPoint p, string metin, CadRenderState s, Palet palet)
    {
        using var yazi = new SKPaint { Color = palet.EtiketYazi, TextSize = 11f, IsAntialias = true, FakeBoldText = true };
        var genislik = yazi.MeasureText(metin) + 12f;
        const float yukseklik = 20f;
        var x = p.X + 14f;
        var y = p.Y - 14f - yukseklik;
        if (x + genislik > s.Width)
        {
            x = p.X - 14f - genislik;
        }

        if (y < 0)
        {
            y = p.Y + 14f;
        }

        var kutu = new SKRect(x, y, x + genislik, y + yukseklik);
        using var zemin = new SKPaint { Color = palet.EtiketZemin, IsStroke = false, IsAntialias = true };
        using var kenar = new SKPaint { Color = palet.Yakalama, IsStroke = true, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(kutu, 5f, 5f, zemin);
        canvas.DrawRoundRect(kutu, 5f, 5f, kenar);
        canvas.DrawText(metin, x + 6f, y + 14f, yazi);
    }

    private static void DrawLabels(SKCanvas canvas, CadRenderState s, Palet palet)
    {
        var etiketler = EtiketKonumlari(s);
        if (etiketler.Count == 0)
        {
            return;
        }

        using var kadastro = new SKPaint { Color = palet.KadastroEtiket, TextSize = EtiketBoyu, IsAntialias = true, TextAlign = SKTextAlign.Center };
        using var imar = new SKPaint { Color = palet.ImarEtiket, TextSize = EtiketBoyu, IsAntialias = true, TextAlign = SKTextAlign.Center };
        foreach (var e in etiketler)
        {
            canvas.DrawText(e.Metin, e.X, e.Y + (EtiketBoyu / 3f), e.Kadastro ? kadastro : imar);
        }
    }

    private static void DrawCutHandle(SKCanvas canvas, SKPoint c, CadRenderState s, Palet palet)
    {
        // Gösterim: seçili poligon merkezinden geçen, Doğu eksenine göre AngleDeg açılı 120 px'lik kesim çizgisi; tutamaç
        // çizgi boyunca PositionRatio konumundadır. Gerçek kesim BisectionEngine ile yapılır; tutamaç yalnız önizlemedir.
        var rad = s.CutAngleDeg * Math.PI / 180d;
        var dx = (float)(Math.Cos(rad) * 60);
        var dy = (float)(-Math.Sin(rad) * 60);
        using var line = new SKPaint { Color = palet.Kesim, IsStroke = true, StrokeWidth = 2, IsAntialias = true };
        canvas.DrawLine(c.X - dx, c.Y - dy, c.X + dx, c.Y + dy, line);
        var t = (float)((s.CutPositionRatio * 2) - 1);
        using var knob = new SKPaint { Color = palet.Kesim, IsStroke = false, IsAntialias = true };
        canvas.DrawCircle(c.X + (dx * t), c.Y + (dy * t), 6, knob);
    }

    private static void DrawSnap(SKCanvas canvas, SKPoint p, OsnapKipi kip, Palet palet)
    {
        // P7: parlak, kalın sembol + yumuşak hale (koyu ve açık paftada belirgin).
        using var hale = new SKPaint { Color = palet.Yakalama.WithAlpha(60), IsStroke = false, IsAntialias = true };
        canvas.DrawCircle(p, 13f, hale);
        using var paint = new SKPaint { Color = palet.Yakalama, IsStroke = true, StrokeWidth = 2.25f, IsAntialias = true };
        const float r = 7f;
        switch (kip)
        {
            case OsnapKipi.Uc:
                canvas.DrawRect(new SKRect(p.X - r, p.Y - r, p.X + r, p.Y + r), paint);
                break;
            case OsnapKipi.Orta:
                using (var tri = new SKPath())
                {
                    tri.MoveTo(p.X, p.Y - r);
                    tri.LineTo(p.X + r, p.Y + r);
                    tri.LineTo(p.X - r, p.Y + r);
                    tri.Close();
                    canvas.DrawPath(tri, paint);
                }

                break;
            case OsnapKipi.Dik:
                canvas.DrawLine(p.X - r, p.Y + r, p.X + r, p.Y + r, paint);
                canvas.DrawLine(p.X - r, p.Y + r, p.X - r, p.Y - r, paint);
                canvas.DrawLine(p.X - r, p.Y, p.X, p.Y, paint);
                canvas.DrawLine(p.X, p.Y, p.X, p.Y + r, paint);
                break;
            default:
                // Kesişim: çarpı + halka.
                canvas.DrawLine(p.X - r, p.Y - r, p.X + r, p.Y + r, paint);
                canvas.DrawLine(p.X - r, p.Y + r, p.X + r, p.Y - r, paint);
                canvas.DrawCircle(p, r + 2f, paint);
                break;
        }
    }

    internal static bool IsCadastre(CadLayerRole role) =>
        role is CadLayerRole.CadastralParcel or CadLayerRole.CadastralLine or CadLayerRole.RegulationBoundary;

    private sealed record PathEntry(SKPath Path, CadLayerRole Role, string Label, SKPoint Centroid, ScenePolyline Source);

    /// <summary>Sahne yolları; yerel metre koordinatları sahne sınır kutusunun minimumuna göredir.</summary>
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
                var noktalar = new SKPoint[p.Vertices.Count];
                for (var i = 0; i < p.Vertices.Count; i++)
                {
                    noktalar[i] = new SKPoint((float)(double)(p.Vertices[i].Y - Origin.Y), (float)(double)(p.Vertices[i].X - Origin.X));
                    if (i == 0)
                    {
                        path.MoveTo(noktalar[i]);
                    }
                    else
                    {
                        path.LineTo(noktalar[i]);
                    }
                }

                if (p.IsClosed)
                {
                    path.Close();
                }

                Entries.Add(new PathEntry(path, p.Role, p.Label, Merkez(noktalar, p.IsClosed), p));
            }
        }

        public GeodeticPoint Origin { get; }

        public List<PathEntry> Entries { get; } = [];

        /// <summary>
        /// Etiket konumu: kapalı halkada alan merkezi (yinelenen kapanış köşesi etkisizdir); dejenere/açık çizgide
        /// tekrarsız köşe ortalaması. Yalnız ekran gösterimi içindir (alan hesabı Domain'dedir).
        /// </summary>
        private static SKPoint Merkez(SKPoint[] n, bool kapali)
        {
            var adet = n.Length > 1 && n[0] == n[^1] ? n.Length - 1 : n.Length;
            if (kapali && adet >= 3)
            {
                double a = 0, cx = 0, cy = 0;
                for (var i = 0; i < adet; i++)
                {
                    var p = n[i];
                    var q = n[(i + 1) % adet];
                    var k = ((double)p.X * q.Y) - ((double)q.X * p.Y);
                    a += k;
                    cx += (p.X + q.X) * k;
                    cy += (p.Y + q.Y) * k;
                }

                if (Math.Abs(a) > 1e-9)
                {
                    return new SKPoint((float)(cx / (3 * a)), (float)(cy / (3 * a)));
                }
            }

            double sx = 0, sy = 0;
            for (var i = 0; i < adet; i++)
            {
                sx += n[i].X;
                sy += n[i].Y;
            }

            return new SKPoint((float)(sx / adet), (float)(sy / adet));
        }
    }
}

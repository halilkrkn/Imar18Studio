using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Geometry;
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.ViewModels;
using SkiaSharp;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// P7 tuval revizyonu: Avalonia, Skia tuvaline kontrolün pencere içindeki konumunu (sol panel + üst çubuk ötelemesi)
/// başlangıç matrisi olarak verir. Poligonlar ve parsel etiketleri bu ötelemeyle aynı eksen/ölçek/ofset dönüşümünü
/// kullanmalıdır; aksi halde (eski <c>SetMatrix</c> hatası) etiketler poligonlardan sağa/aşağı kayar.
/// </summary>
public sealed class TuvalCizimTests
{
    private static readonly SKColor Arkaplan = SKColor.Parse("#0D0F13");

    private static GeodeticPoint P(decimal x, decimal y) => new(X: 4_000_000m + x, Y: 500_000m + y);

    private static CadCanvasViewModel Sahne()
    {
        var kare = new CadPolyline("TK_PARSEL_OLUŞAN", "101/1", null, [P(0, 0), P(0, 100), P(100, 100), P(100, 0), P(0, 0)], true, null);
        var belge = new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "TK_PARSEL_OLUŞAN")], [kare], [], 0, 0, 0, []);
        var vm = new CadCanvasViewModel { IzgaraGoster = false };
        vm.SetViewport(400, 300);
        vm.LoadScene(CanvasScene.From(belge, CadLayerRoleMap.Standard));
        return vm;
    }

    private static bool BosDegil(SKBitmap b, int x0, int y0, int x1, int y1)
    {
        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
            {
                if (x >= 0 && y >= 0 && x < b.Width && y < b.Height && b.GetPixel(x, y) != Arkaplan)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Fact]
    public void Tuval_Kontrol_Otelemesi_Altinda_Poligon_Ve_Etiket_Ayni_Donusumu_Kullanmali()
    {
        const int otelemeX = 150, otelemeY = 40;
        var vm = Sahne();
        using var bitmap = new SKBitmap(800, 600);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(Arkaplan);
            canvas.Translate(otelemeX, otelemeY); // Avalonia kontrol konumu (sol panel + üst çubuk)
            CadSceneRenderer.Render(canvas, CadRenderState.From(vm, 400, 300));
        }

        var sol = vm.Camera.WorldToScreen(P(50, 0)); // batı kenarı orta noktası
        var merkez = vm.Camera.WorldToScreen(P(50, 50));
        var solX = (int)Math.Round(sol.X) + otelemeX;
        var ortaY = (int)Math.Round(sol.Y) + otelemeY;

        BosDegil(bitmap, solX - 2, ortaY - 2, solX + 2, ortaY + 2).Should().BeTrue("poligon kenarı kontrol ötelemesiyle çizilmeli");
        BosDegil(bitmap, solX - otelemeX - 2, ortaY - otelemeY - 2, solX - otelemeX + 2, ortaY - otelemeY + 2)
            .Should().BeFalse("öteleme silinip pencere başına çizilmemeli");
        var mx = (int)Math.Round(merkez.X) + otelemeX;
        var my = (int)Math.Round(merkez.Y) + otelemeY;
        BosDegil(bitmap, mx - 20, my - 14, mx + 20, my + 6).Should().BeTrue("etiket poligon merkezinde olmalı");
    }

    [Fact]
    public void Tuval_Etiket_Konumu_Kamera_Donusumuyle_Ayni_Olmali()
    {
        var vm = Sahne();
        var durum = CadRenderState.From(vm, 400, 300);

        var etiket = CadSceneRenderer.EtiketKonumlari(durum).Single();
        var beklenen = vm.Camera.WorldToScreen(P(50, 50));

        etiket.Metin.Should().Be("101/1");
        etiket.X.Should().BeApproximately((float)beklenen.X, 0.5f);
        etiket.Y.Should().BeApproximately((float)beklenen.Y, 0.5f);
    }

    [Fact]
    public void Tuval_Pan_Sonrasi_Etiket_Poligonla_Birlikte_Kaymali()
    {
        var vm = Sahne();
        var once = CadSceneRenderer.EtiketKonumlari(CadRenderState.From(vm, 400, 300)).Single();

        vm.Pan(37, -12);
        var sonra = CadSceneRenderer.EtiketKonumlari(CadRenderState.From(vm, 400, 300)).Single();

        (sonra.X - once.X).Should().BeApproximately(37f, 0.01f);
        (sonra.Y - once.Y).Should().BeApproximately(-12f, 0.01f);
    }

    [Fact]
    public void Tuval_Ekranda_Cok_Kucuk_Kalan_Poligonun_Etiketi_Gizlenmeli()
    {
        var vm = Sahne();
        vm.ZoomAt(200, 150, 1 / 1000d);

        CadSceneRenderer.EtiketKonumlari(CadRenderState.From(vm, 400, 300)).Should().BeEmpty("etiket yığını (karmaşa) oluşmamalı");
    }

    [Fact]
    public void Tuval_Etiketler_Kapaliyken_Etiket_Uretmemeli()
    {
        var vm = Sahne();
        vm.EtiketGoster = false;

        CadSceneRenderer.EtiketKonumlari(CadRenderState.From(vm, 400, 300)).Should().BeEmpty();
    }

    [Fact]
    public void Tuval_Imar_Perdesi_Kapaliyken_Imar_Etiketi_Gizlenmeli()
    {
        var vm = Sahne();
        vm.SwipePercent = 0;

        CadSceneRenderer.EtiketKonumlari(CadRenderState.From(vm, 400, 300)).Should().BeEmpty("perde dışındaki imar etiketi kadastro görünümünü kirletmemeli");
    }

    [Fact]
    public void Tuval_Acik_Pafta_Temasinda_Arkaplan_Acik_Olmali()
    {
        var vm = Sahne();
        vm.AcikPafta = true;
        using var bitmap = new SKBitmap(50, 50);
        using (var canvas = new SKCanvas(bitmap))
        {
            CadSceneRenderer.Render(canvas, CadRenderState.From(vm, 50, 50));
        }

        var p = bitmap.GetPixel(1, 1);
        (p.Red + p.Green + p.Blue).Should().BeGreaterThan(600, "açık pafta kağıt teması");
    }
}

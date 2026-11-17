using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Geometry;
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.ViewModels;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>P4.3 SkiaSharp CAD tuvali: kamera (pan/zoom/fit, kuzey yukari), sahne, swipe ve kesim tutamaci.</summary>
public sealed class CanvasTests
{
    private static GeodeticPoint P(decimal x, decimal y) => new(X: 4_000_000m + x, Y: 500_000m + y);

    private static CadPolyline Kare(string katman, decimal x0, decimal y0, decimal kenar, string etiket = "") =>
        new(katman, etiket, null, [P(x0, y0), P(x0 + kenar, y0), P(x0 + kenar, y0 + kenar), P(x0, y0 + kenar), P(x0, y0)], true, null);

    private static CadLayerDocument Belge(params CadPolyline[] p) =>
        new(CadFormat.Ncz, [new CadLayer(0, "TK_PARSEL_GİREN"), new CadLayer(1, "TK_PARSEL_OLUŞAN")], p, [], 0, 0, 0, []);

    [Fact]
    public void Kamera_Kuzey_Yukari_Dogu_Saga_Ekran_Donusumu_Yapmali()
    {
        var k = new CadCamera();
        k.FitTo(P(0, 0), P(100, 200), 400, 300, 0);

        var solAlt = k.WorldToScreen(P(0, 0));
        var sagUst = k.WorldToScreen(P(100, 200));

        sagUst.X.Should().BeGreaterThan(solAlt.X, "Dogu (Y) saga");
        sagUst.Y.Should().BeLessThan(solAlt.Y, "Kuzey (X) yukari");
        k.Scale.Should().BeApproximately(2.0, 1e-9, "min(400 px / 200 m Dogu, 300 px / 100 m Kuzey)");
    }

    [Fact]
    public void Kamera_Ekrandan_Dunyaya_Ters_Donusum_Mm_Hassasiyetinde_Olmali()
    {
        var k = new CadCamera();
        k.FitTo(P(0, 0), P(100, 100), 800, 800, 20);
        var dunya = P(12.345m, 67.891m);

        var ekran = k.WorldToScreen(dunya);
        var geri = k.ScreenToWorld(ekran.X, ekran.Y);

        Math.Abs(geri.X - dunya.X).Should().BeLessThan(0.001m);
        Math.Abs(geri.Y - dunya.Y).Should().BeLessThan(0.001m);
    }

    [Fact]
    public void Kamera_Imlec_Etrafinda_Zoom_Imlec_Altindaki_Noktayi_Sabit_Tutmali()
    {
        var k = new CadCamera();
        k.FitTo(P(0, 0), P(100, 100), 500, 500, 0);
        var once = k.ScreenToWorld(123, 321);

        k.ZoomAt(123, 321, 2.5);
        var sonra = k.ScreenToWorld(123, 321);

        Math.Abs(sonra.X - once.X).Should().BeLessThan(0.001m);
        Math.Abs(sonra.Y - once.Y).Should().BeLessThan(0.001m);
    }

    [Theory]
    [InlineData(1e9)]
    [InlineData(1e-9)]
    public void Kamera_Olcegi_Sinirlar_Icinde_Kalmali(double carpan)
    {
        var k = new CadCamera();
        k.ZoomAt(0, 0, carpan);

        k.Scale.Should().BeInRange(CadCamera.MinScale, CadCamera.MaxScale);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Kamera_Gecersiz_Zoom_Carpanini_Yok_Saymali(double carpan)
    {
        var k = new CadCamera();
        var olcek = k.Scale;

        k.ZoomAt(10, 10, carpan);

        k.Scale.Should().Be(olcek);
    }

    [Fact]
    public void Kamera_Pan_Ekran_Otelemesini_Uygulamali()
    {
        var k = new CadCamera();
        var once = k.WorldToScreen(P(0, 0));

        k.Pan(15, -7);

        var sonra = k.WorldToScreen(P(0, 0));
        (sonra.X - once.X).Should().BeApproximately(15, 1e-9);
        (sonra.Y - once.Y).Should().BeApproximately(-7, 1e-9);
    }

    [Fact]
    public void Sahne_Rolleri_Alanlari_Ve_Sinirlari_Uretmeli()
    {
        var s = CanvasScene.From(Belge(Kare("TK_PARSEL_GİREN", 0, 0, 40), Kare("TK_PARSEL_OLUŞAN", 0, 0, 20, "101/1")), CadLayerRoleMap.Standard);

        s.Polylines.Select(p => p.Role).Should().Equal(CadLayerRole.CadastralParcel, CadLayerRole.ZoningParcel);
        s.Polylines[1].AreaM2.Should().Be(400.00m);
        s.Bounds.Should().NotBeNull();
        s.Bounds!.Value.Min.Should().Be(P(0, 0));
        s.Bounds.Value.Max.Should().Be(P(40, 40));
    }

    [Fact]
    public void Sahne_Isabet_Testi_Icteki_En_Kucuk_Poligonu_Secmeli()
    {
        var s = CanvasScene.From(Belge(Kare("TK_PARSEL_GİREN", 0, 0, 40), Kare("TK_PARSEL_OLUŞAN", 0, 0, 20, "101/1")), CadLayerRoleMap.Standard);

        s.HitTest(P(5, 5))!.Label.Should().Be("101/1");
        s.HitTest(P(30, 30))!.Role.Should().Be(CadLayerRole.CadastralParcel);
        s.HitTest(P(100, 100)).Should().BeNull();
    }

    [Fact]
    public void Sahne_Bos_Belgede_Sinirsiz_Olmali()
    {
        CanvasScene.Empty.Polylines.Should().BeEmpty();
        CanvasScene.Empty.Bounds.Should().BeNull();
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-5, 0)]
    [InlineData(42.5, 42.5)]
    public void Tuval_Kadastro_Imar_Kaydiricisi_0_100_Arasinda_Kalmali(double girdi, double beklenen)
    {
        var vm = new CadCanvasViewModel { SwipePercent = girdi };

        vm.SwipePercent.Should().Be(beklenen);
    }

    [Fact]
    public void Tuval_Secim_Imlec_Koordinati_Ve_Sigdirma_Calismali()
    {
        var vm = new CadCanvasViewModel();
        vm.SetViewport(400, 400);
        vm.LoadScene(CanvasScene.From(Belge(Kare("TK_PARSEL_OLUŞAN", 0, 0, 20, "101/1")), CadLayerRoleMap.Standard));
        var merkez = vm.Camera.WorldToScreen(P(10, 10));

        vm.PointerMoved(merkez.X, merkez.Y);
        vm.SelectAt(merkez.X, merkez.Y);

        vm.Selected!.Label.Should().Be("101/1");
        vm.CursorText.Should().Contain("Y: 500010.000").And.Contain("X: 4000010.000");
        vm.ContextMenuTitle.Should().Contain("101/1");
    }

    [Fact]
    public void Tuval_Her_Gorunum_Degisikliginde_Yeniden_Cizim_Surumunu_Artirmali()
    {
        var vm = new CadCanvasViewModel();
        var surum = vm.RenderVersion;

        vm.Pan(1, 1);
        vm.ZoomAt(0, 0, 1.1);
        vm.SwipePercent = 10;

        vm.RenderVersion.Should().Be(surum + 3);
    }

    [Theory]
    [InlineData(40, 75)]
    [InlineData(120, 105)]
    [InlineData(88, 88)]
    public void Kesim_Tutamaci_Aci_75_105_Araliginda_Kalmali(double aci, double beklenen)
    {
        var t = new CutHandleViewModel { AngleDeg = aci };

        t.AngleDeg.Should().Be(beklenen);
    }

    [Fact]
    public void Kesim_Tutamaci_Surukleme_Konumu_0_1_Araliginda_Tutmali_Ve_Sinirda_Isaretlemeli()
    {
        var t = new CutHandleViewModel();

        t.DragBy(0.3);
        t.PositionRatio.Should().BeApproximately(0.8, 1e-12);
        t.IsAtLimit.Should().BeFalse();
        t.DragBy(0.5);
        t.PositionRatio.Should().Be(1);
        t.IsAtLimit.Should().BeTrue();
        t.Reset();
        t.PositionRatio.Should().Be(0.5);
        t.AngleDeg.Should().Be(90);
    }

    [Fact]
    public void Sag_Tik_Menusu_Referans_Ogelerini_Ve_Sonraki_Faz_Gerekcesini_Sunmali()
    {
        var vm = new CadCanvasViewModel();
        vm.SetViewport(400, 400);
        vm.LoadScene(CanvasScene.From(Belge(Kare("TK_PARSEL_OLUŞAN", 0, 0, 20, "101/1")), CadLayerRoleMap.Standard));

        vm.ContextMenuItems.Select(i => i.Id).Should().Contain(["chamfer45", "resolveShared", "lockFrontage", "balanceY1", "fenPreview", "cutHandle", "zoomSelection"]);
        vm.ContextMenuItems.Where(i => i.Id is "chamfer45" or "resolveShared" or "lockFrontage" or "balanceY1" or "fenPreview")
            .Should().OnlyContain(i => !i.Command.CanExecute(null) && i.DisabledReason!.Length > 0);
        vm.ContextMenuItems.Single(i => i.Id == "cutHandle").Command.CanExecute(null).Should().BeFalse("secim yokken");

        var m = vm.Camera.WorldToScreen(P(10, 10));
        vm.SelectAt(m.X, m.Y);
        var kesim = vm.ContextMenuItems.Single(i => i.Id == "cutHandle").Command;
        kesim.CanExecute(null).Should().BeTrue();
        kesim.Execute(null);
        vm.CutHandle.IsVisible.Should().BeTrue();
    }
}

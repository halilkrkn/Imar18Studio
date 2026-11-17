using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.ViewModels;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// Antigravity Bağımsız Test ve Denetim Paketi — Faz P4.3 Avalonia CAD Stüdyosu.
/// Üretim koduna dokunulmadan, bağımsız olarak:
/// - WizardViewModel 5 adımlı sihirbaz sınır ve geçiş testleri,
/// - CadCanvasViewModel kamera, viewport, zoom/pan ve SwipePercent clamping testleri,
/// - CutHandleViewModel 75°–105° diklik açısı ve cephe oranı sınır testleri,
/// - CommandPaletteViewModel (Ctrl+K) Türkçe karakter normalizasyonu ve gezinme sınır testleri.
/// </summary>
public sealed class AntigravityIndependentPresentationAuditTests
{
    private static WizardViewModel OlusturGecerliSihirbaz()
    {
        var w = new WizardViewModel();
        w.LoadLayers([
            new CadLayer(0, "TK_PARSEL_GİREN"),
            new CadLayer(1, "TK_PARSEL_OLUŞAN"),
            new CadLayer(2, "PL_PARK"),
            new CadLayer(3, "YAZI")
        ]);
        w.Datum = GeodetikDatum.Itrf96;
        w.OrtaMeridyen = 30;
        w.EpokText = "2005.00";
        w.OlcekPaydasi = 1000;
        w.TecvizOlcekPaydasi = 1000;
        w.TecvizUretimYontemi = "Elektronik Takometre";
        w.TecvizKoordinatKalitesi = KoordinatKalitesi.ArsivOnayli;
        w.TecvizSiniriText = "15,50";
        w.TecvizKaynakNotu = "Test Denetim";
        return w;
    }

    #region 1. WizardViewModel Sınır ve Doğrulama Testleri

    [Fact]
    public void Sihirbaz_Adim_Gezinmesi_Sinirlari_Aşmamali()
    {
        var w = OlusturGecerliSihirbaz();
        w.Validate().Should().BeTrue();

        // İlk adımda geri komutu pasiftir
        w.CurrentIndex.Should().Be(0);
        w.BackCommand.CanExecute(null).Should().BeFalse();

        // Bir adım ileri git ve geri dön
        w.NextCommand.CanExecute(null).Should().BeTrue();
        w.NextCommand.Execute(null);
        w.CurrentIndex.Should().Be(1);
        w.BackCommand.CanExecute(null).Should().BeTrue();
        w.BackCommand.Execute(null);
        w.CurrentIndex.Should().Be(0);

        // Adımları ileri ilerlet
        while (w.NextCommand.CanExecute(null))
        {
            w.NextCommand.Execute(null);
        }

        w.CurrentIndex.Should().Be(4, "son adım 5. adım olup index 4'tür");
        w.NextCommand.CanExecute(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-10")]
    [InlineData("0")]
    [InlineData("")]
    public void Sihirbaz_Gecersiz_Tecviz_Sinirinda_Ilerlememeli_Ve_Kullanici_Dostu_Mesaj_Gostermeli(string gecersizTecviz)
    {
        var w = OlusturGecerliSihirbaz();
        w.TecvizSiniriText = gecersizTecviz;

        w.Validate().Should().BeFalse();
        w.NextCommand.CanExecute(null).Should().BeFalse();
        w.Errors.Should().Contain(e => e.Contains("Tecviz", StringComparison.Ordinal));
        w.Errors.Should().NotContain(e => e.Contains("Exception", StringComparison.Ordinal));
    }

    [Fact]
    public void Sihirbaz_Zorunlu_Roller_Atanmadiginda_Dogrulamayi_Gecirmemeli()
    {
        var w = new WizardViewModel();
        w.LoadLayers([new CadLayer(0, "BILINMEYEN_KATMAN_1"), new CadLayer(1, "BILINMEYEN_KATMAN_2")]);
        w.Datum = GeodetikDatum.Itrf96;
        w.OrtaMeridyen = 30;
        w.EpokText = "2005.00";
        w.TecvizSiniriText = "20.00";

        // Rol atanmadı (varsayılan Unassigned)
        w.Validate().Should().BeFalse();
        w.Errors.Should().Contain(e => e.Contains("kadastro", StringComparison.OrdinalIgnoreCase) || e.Contains("imar", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sihirbaz_ED50_Secildiginde_Epoksuz_Dogrulamayi_Basariyla_Gecmeli()
    {
        var w = OlusturGecerliSihirbaz();
        w.Datum = GeodetikDatum.Ed50;
        w.EpokText = ""; // ED50 epoksuzdur

        w.Validate().Should().BeTrue();
        w.Crs.Should().NotBeNull();
        w.Crs!.Datum.Should().Be(GeodetikDatum.Ed50);
        w.Crs.Epok.Should().BeNull();
    }

    #endregion

    #region 2. CutHandleViewModel 75°–105° Diklik Sınır Testleri

    [Theory]
    [InlineData(74.99, 75.0, true)]
    [InlineData(60.0, 75.0, true)]
    [InlineData(105.01, 105.0, true)]
    [InlineData(120.0, 105.0, true)]
    [InlineData(90.0, 90.0, false)]
    [InlineData(75.0, 75.0, false)]
    [InlineData(105.0, 105.0, false)]
    public void CutHandle_Aci_Degeri_75_105_Araligina_Kenetlenmeli_Ve_Sinir_Durumu_Bildirilmeli(
        double girilenAci, double beklenenAci, bool beklenenSinirda)
    {
        var handle = new CutHandleViewModel();
        handle.AngleDeg = girilenAci;

        handle.AngleDeg.Should().Be(beklenenAci);
        handle.IsAtLimit.Should().Be(beklenenSinirda);
    }

    [Theory]
    [InlineData(-0.5, 0.0, true)]
    [InlineData(1.5, 1.0, true)]
    [InlineData(0.5, 0.5, false)]
    [InlineData(0.0, 0.0, false)]
    [InlineData(1.0, 1.0, false)]
    public void CutHandle_Cephe_Orani_0_1_Araligina_Kenetlenmeli(
        double girilenOran, double beklenenOran, bool beklenenSinirda)
    {
        var handle = new CutHandleViewModel();
        handle.PositionRatio = girilenOran;

        handle.PositionRatio.Should().Be(beklenenOran);
        handle.IsAtLimit.Should().Be(beklenenSinirda);
    }

    [Fact]
    public void CutHandle_Gecersiz_FloatingPoint_Degerinde_Mevcut_Aciyi_Korumali()
    {
        var handle = new CutHandleViewModel { AngleDeg = 90.0 };

        handle.AngleDeg = double.NaN;
        handle.AngleDeg.Should().Be(90.0);

        handle.AngleDeg = double.PositiveInfinity;
        handle.AngleDeg.Should().Be(90.0);
    }

    #endregion

    #region 3. CadCanvasViewModel Viewport ve SwipePercent Sınır Testleri

    [Fact]
    public void CadCanvas_Sifir_Veya_Negatif_Viewport_Boyutunda_Cokmemeli()
    {
        var canvas = new CadCanvasViewModel();

        var act1 = () => canvas.SetViewport(0, 0);
        var act2 = () => canvas.SetViewport(-500, -300);

        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Theory]
    [InlineData(-25.0, 0.0)]
    [InlineData(150.0, 100.0)]
    [InlineData(50.0, 50.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(100.0, 100.0)]
    public void CadCanvas_SwipePercent_0_100_Araligina_Kenetlenmeli(double girilen, double beklenen)
    {
        var canvas = new CadCanvasViewModel();
        canvas.SwipePercent = girilen;

        canvas.SwipePercent.Should().Be(beklenen);
    }

    [Fact]
    public void CadCanvas_Secim_Temizleme_Ve_Secim_Degisimi_Calismali()
    {
        var vm = new CadCanvasViewModel();
        vm.SetViewport(400, 400);

        var belge = new CadLayerDocument(
            CadFormat.Ncz,
            [new CadLayer(0, "TK_PARSEL_OLUŞAN")],
            [new CadPolyline("TK_PARSEL_OLUŞAN", "101/1", null, [
                new GeodeticPoint(4_000_000m, 500_000m),
                new GeodeticPoint(4_000_010m, 500_000m),
                new GeodeticPoint(4_000_010m, 500_010m),
                new GeodeticPoint(4_000_000m, 500_010m),
                new GeodeticPoint(4_000_000m, 500_000m)
            ], true, null)],
            [], 0, 0, 0, []);

        vm.LoadScene(CanvasScene.From(belge, CadLayerRoleMap.Standard));

        // Poligon merkezine tıklayarak seç
        var merkez = vm.Camera.WorldToScreen(new GeodeticPoint(4_000_005m, 500_005m));
        vm.SelectAt(merkez.X, merkez.Y);

        vm.Selected.Should().NotBeNull();
        vm.Selected!.Label.Should().Be("101/1");
        vm.ContextMenuTitle.Should().Contain("101/1");

        // Boş bir alana tıklayarak seçimi kaldır
        vm.SelectAt(-500, -500);
        vm.Selected.Should().BeNull();
        vm.ContextMenuTitle.Should().Be("PARSEL İŞLEMLERİ");
    }

    #endregion

    #region 4. CommandPaletteViewModel (Ctrl+K) Sınır ve Türkçe Normalizasyon Testleri

    [Fact]
    public void KomutCubugu_Turkce_Buyuk_Kucuk_Harf_Ve_I_I_Donusumlerini_Kusursuz_Eslestirmeli()
    {
        var palette = new CommandPaletteViewModel();
        var calisti = false;

        palette.Register(new PaletteCommand("zoom_fit", "Tümünü Sığdır", "ekrana sigdir zoom extents", () => calisti = true));
        palette.Register(new PaletteCommand("open_cad", "CAD Dosyası Aç", "ncz dxf dwg yukle", () => { }));

        // "sigdir", "SIGDIR", "sığdır", "SIĞDIR" aramaları
        string[] sorgular = ["sigdir", "SIGDIR", "sığdır", "SIĞDIR"];
        foreach (var q in sorgular)
        {
            palette.Query = q;
            palette.Results.Should().NotBeEmpty($"'{q}' sorgusu sonuç döndürmelidir");
            palette.Results.First().Id.Should().Be("zoom_fit");
        }

        // Komutu çalıştırıp tetiklendiğini doğrula
        palette.SelectedIndex = 0;
        palette.ExecuteSelected();
        calisti.Should().BeTrue();

        // "aç", "AC", "AÇ", "ac" aramaları
        string[] cadSorgulari = ["ac", "aç", "AÇ", "AC"];
        foreach (var q in cadSorgulari)
        {
            palette.Query = q;
            palette.Results.Should().NotBeEmpty($"'{q}' sorgusu sonuç döndürmelidir");
            palette.Results.First().Id.Should().Be("open_cad");
        }
    }

    [Fact]
    public void KomutCubugu_Ozel_Karakterler_Ve_Eslesmeyen_Sorgularda_Guvenli_Calismali()
    {
        var palette = new CommandPaletteViewModel();
        palette.Register(new PaletteCommand("c1", "Test Komutu", "anahtar", () => { }));

        // Özel karakterler çökertmemeli
        palette.Query = "<script>alert('xss')</script>";
        palette.Results.Should().BeEmpty();
        palette.SelectedIndex.Should().Be(-1);

        palette.Query = "!@#$%^&*()_+{}|:\"<>?";
        palette.Results.Should().BeEmpty();
        palette.SelectedIndex.Should().Be(-1);
    }

    [Fact]
    public void KomutCubugu_Pasif_Komutu_Calistirmamali_Ve_Paleti_Kapatmamali()
    {
        var palette = new CommandPaletteViewModel();
        var calisti = false;

        palette.Register(new PaletteCommand(
            "pasif_komut",
            "Pasif İşlem",
            "pasif",
            () => calisti = true,
            CanExecute: () => false));

        palette.Toggle();
        palette.IsOpen.Should().BeTrue();

        palette.Query = "pasif";
        palette.Results.Should().ContainSingle();
        palette.SelectedIndex.Should().Be(0);

        palette.ExecuteSelected();

        calisti.Should().BeFalse("CanExecute false olduğu için komut çalıştırılamaz");
        palette.IsOpen.Should().BeTrue("Çalıştırılamayan komut sonrası palet açık kalmalıdır");
    }

    [Fact]
    public void KomutCubugu_Gecerli_Komut_Calistiginda_Paleti_Kapatip_Sorguyu_Temizlemeli()
    {
        var palette = new CommandPaletteViewModel();
        var calisti = false;

        palette.Register(new PaletteCommand("aktif_komut", "Aktif İşlem", "aktif", () => calisti = true));

        palette.Toggle();
        palette.IsOpen.Should().BeTrue();

        palette.Query = "aktif";
        palette.SelectedIndex.Should().Be(0);

        palette.ExecuteSelected();

        calisti.Should().BeTrue();
        palette.IsOpen.Should().BeFalse();
        palette.Query.Should().BeEmpty();
    }

    #endregion
}

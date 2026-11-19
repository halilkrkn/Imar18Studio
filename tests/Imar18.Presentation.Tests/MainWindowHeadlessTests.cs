using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Presentation.Avalonia;
using Imar18.Presentation.Avalonia.ViewModels;
using Imar18.Presentation.Avalonia.Views;

[assembly: AvaloniaTestApplication(typeof(Imar18.Presentation.Tests.HeadlessUygulama))]

namespace Imar18.Presentation.Tests;

/// <summary>Avalonia.Headless test uygulaması: gerçek App.axaml (Semi tema + P7 tema sözlükleri) yüklenir; çizim başsızdır.</summary>
public static class HeadlessUygulama
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>
/// P7 UI revizyonu — Avalonia.Headless ile gerçek ana pencere: %100 Türkçe görünür metin, bağlama göre sağ panel (1. adımda
/// kırmızı engel yığını yok, 5. adımda onay/çıktı), kısa RevisionId rozeti, Ctrl+K arama çubuğu, Koyu/Açık Pafta teması,
/// GridSplitter'lı açılır-kapanır paneller, 5 sekmeli alt cetvel, tuval HUD'u ve OSNAP çubuğu.
/// </summary>
public sealed class MainWindowHeadlessTests
{
    private sealed class SahteAdaptor(CadLayerDocument belge) : ICadFormatAdapter
    {
        public CadFormat Format => belge.Format;

        public bool SupportsExtension(string extension) => true;

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) => Result<CadLayerDocument>.Success(belge);
    }

    private sealed class SahteUretici(CiktiTuru tur) : ICiktiUretici
    {
        public CiktiTuru Tur => tur;

        public string Uzanti => ".bin";

        public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default) => Result<byte[]>.Success([1]);
    }

    private sealed class BellekYazici : ICiktiYazici
    {
        public Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SilAsync(string yol, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Ekranda görünmemesi gereken ham kod adları ("Park" Türkçe bir sözcük olduğu için hariçtir).</summary>
    private static readonly string[] HamKodAdlari =
    [
        .. Enum.GetNames<CadLayerRole>().Where(n => n != nameof(CadLayerRole.Park)),
        .. Enum.GetNames<GeodetikDatum>(),
        .. Enum.GetNames<KoordinatKalitesi>(),
        .. Enum.GetNames<CiktiTuru>(),
        .. Enum.GetNames<CadFormat>(),
    ];

    private static GeodeticPoint P(decimal x, decimal y) => new(X: 4_000_000m + x, Y: 500_000m + y);

    private static CadLayerDocument Belge() => new(
        CadFormat.Ncz,
        [new CadLayer(0, "TK_PARSEL_GİREN"), new CadLayer(1, "TK_PARSEL_OLUŞAN"), new CadLayer(2, "YAZI")],
        [
            new CadPolyline("TK_PARSEL_GİREN", "436/14", null, [P(0, 0), P(40, 0), P(40, 40), P(0, 40), P(0, 0)], true, null),
            new CadPolyline("TK_PARSEL_OLUŞAN", "101/1", null, [P(0, 0), P(20, 0), P(20, 20), P(0, 20), P(0, 0)], true, null),
        ],
        [], 0, 0, 0, []);

    private static ProjeCalismaAlani Alan(ProjeKomutYolu yol, SabitZaman zaman) => new(
        yol,
        new ApprovalService(yol, zaman, OrnekProje.Topoloji),
        new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru), new SahteUretici(CiktiTuru.Cetveller), new SahteUretici(CiktiTuru.CksDopOzeti)], new BellekYazici(), OrnekProje.Topoloji),
        OrnekProje.Muhendis);

    private static async Task<MainWindowViewModel> BosProjeliVm()
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik P7 Projesi", zaman)).Value;
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(Belge())]), Alan(yol, zaman));
        await vm.LastProjeTask;
        return vm;
    }

    private static MainWindow Ac(MainWindowViewModel vm)
    {
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static List<string> GorunurMetinler(Visual kok) => kok.GetVisualDescendants().OfType<TextBlock>()
        .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text))
        .Select(t => t.Text!)
        .ToList();

    private static T Bul<T>(Window w, string ad) where T : Control => w.FindControl<T>(ad) ?? throw new InvalidOperationException(ad + " bulunamadı");

    [AvaloniaFact]
    public async Task AnaPencere_Ekranda_Ham_Ingilizce_Enum_Kod_Adi_Gosterilmemeli()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(Belge())]));
        vm.Canvas.SetViewport(800, 600);
        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        var w = Ac(vm);
        var merkez = vm.Canvas.Camera.WorldToScreen(P(10, 10));
        vm.Canvas.SelectAt(merkez.X, merkez.Y);
        vm.Wizard.Validate();
        Dispatcher.UIThread.RunJobs();

        var metinler = GorunurMetinler(w);

        var ihlaller = metinler.Where(m => HamKodAdlari.Any(k => Regex.IsMatch(m, $@"\b{k}\b"))).ToList();
        ihlaller.Should().BeEmpty("ekrana yansıyan tüm enum değerleri Türkçe görünen adla gösterilmeli");
        metinler.Should().Contain("Kadastro Parseli").And.Contain("İmar Parseli").And.Contain("Atanmamış");
        metinler.Should().Contain("ITRF96 (TM 3°)").And.Contain("Arşiv Onaylı").And.Contain("DOM 30°");
        metinler.Should().Contain("1600.00 m²").And.Contain("🎯 101/1 · İmar Parseli · 400.00 m²", "ondalık ayıracı makine kültüründen bağımsız ve tutarlı olmalı");
        w.Close();
    }

    [AvaloniaFact]
    public async Task AnaPencere_Ilk_Adimda_Kirmizi_Onay_Engeli_Yigini_Gosterilmemeli_Ve_Revizyon_Rozeti_Kisa_Olmali()
    {
        var vm = await BosProjeliVm();
        vm.OnayCikti!.Engeller.Should().NotBeEmpty("boş projede onay engelleri vardır (sınama ön koşulu)");
        var w = Ac(vm);

        Bul<Panel>(w, "SagOnayModu").IsEffectivelyVisible.Should().BeFalse();
        Bul<Panel>(w, "SagOzetModu").IsEffectivelyVisible.Should().BeTrue();
        var kirmizi = w.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Classes.Contains("hata") && vm.OnayCikti.Engeller.Contains(t.Text ?? string.Empty));
        kirmizi.Should().BeEmpty("1. adımda henüz hesap çalışmamışken kırmızı hata yığını basılmamalı");
        Bul<TextBlock>(w, "RevizyonRozeti").Text.Should().Be("🔖 Rev: " + vm.OnayCikti.RevizyonKisa);
        vm.OnayCikti.RevizyonKisa.Length.Should().BeLessThanOrEqualTo(9);

        vm.Wizard.CurrentIndex = 4;
        Dispatcher.UIThread.RunJobs();

        Bul<Panel>(w, "SagOnayModu").IsEffectivelyVisible.Should().BeTrue("onay ve çıktılar yalnızca 5. adımda");
        GorunurMetinler(w).Should().Contain(vm.OnayCikti.Engeller[0]).And.Contain("📤 TASLAK ÇIKTILAR");
        w.Close();
    }

    [AvaloniaFact]
    public void AnaPencere_Alt_Panel_Bes_Sekmeli_Ve_Acilir_Kapanir_Olmali()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(Belge())]));
        var w = Ac(vm);
        var sekmeler = Bul<TabControl>(w, "AltSekmeler");

        sekmeler.Items.Should().HaveCount(5);
        sekmeler.Items.OfType<TabItem>().Select(t => t.Header?.ToString()).Should()
            .Equal("📋 Parseller", "📐 Tecviz", "⚖️ DOP & Y1", "🧾 Hak & Hisse", "⚠️ Uyarı & Denetim (0)");

        vm.AltPanelDegistirCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Bul<Border>(w, "AltPanel").IsVisible.Should().BeFalse();
        Bul<Grid>(w, "KokIzgara").RowDefinitions[4].Height.Value.Should().Be(0);

        vm.AltPanelDegistirCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Bul<Border>(w, "AltPanel").IsVisible.Should().BeTrue();
        Bul<Grid>(w, "KokIzgara").RowDefinitions[4].Height.Value.Should().BeGreaterThan(0);
        w.Close();
    }

    [AvaloniaFact]
    public void AnaPencere_GridSplitter_Ile_Boyutlanan_Paneller_Kapanip_Ayni_Genislikte_Acilmali()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(Belge())]));
        var w = Ac(vm);
        var izgara = Bul<Grid>(w, "AnaIzgara");

        w.GetVisualDescendants().OfType<GridSplitter>().Select(s => s.Name).Should().BeEquivalentTo("SolBolme", "SagBolme", "AltBolme");
        izgara.ColumnDefinitions[0].Width = new GridLength(355); // kullanıcı bölmeyi sürükledi

        vm.SolPanelDegistirCommand.Execute(null);
        vm.SagPanelDegistirCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        izgara.ColumnDefinitions[0].Width.Value.Should().Be(0);
        izgara.ColumnDefinitions[4].Width.Value.Should().Be(0);
        Bul<GridSplitter>(w, "SolBolme").IsVisible.Should().BeFalse();

        vm.SolPanelDegistirCommand.Execute(null);
        vm.SagPanelDegistirCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        izgara.ColumnDefinitions[0].Width.Value.Should().Be(355);
        izgara.ColumnDefinitions[4].Width.Value.Should().Be(320);
        w.Close();
    }

    [AvaloniaFact]
    public void AnaPencere_Dar_Pencerede_Yan_Paneller_En_Kucuk_Genislige_Inmeli()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(Belge())]));
        var w = Ac(vm);
        var izgara = Bul<Grid>(w, "AnaIzgara");

        w.Width = 1100;
        Dispatcher.UIThread.RunJobs();

        izgara.ColumnDefinitions[0].Width.Value.Should().Be(240);
        izgara.ColumnDefinitions[4].Width.Value.Should().Be(250);
        w.Close();
    }

    [AvaloniaFact]
    public void AnaPencere_Tema_Anahtari_Uygulamayi_Acik_Pafta_Ve_Koyu_Studyo_Arasinda_Gecirmeli()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(Belge())]));
        var w = Ac(vm);
        try
        {
            Bul<Button>(w, "TemaAnahtari").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            global::Avalonia.Application.Current!.RequestedThemeVariant.Should().Be(ThemeVariant.Light);
            vm.Canvas.AcikPafta.Should().BeTrue();

            Bul<Button>(w, "TemaAnahtari").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            global::Avalonia.Application.Current!.RequestedThemeVariant.Should().Be(ThemeVariant.Dark);
        }
        finally
        {
            global::Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            w.Close();
        }
    }

    [AvaloniaFact]
    public void AnaPencere_Ust_Ctrl_K_Arama_Cubugu_Gorunur_Olmali_Ve_Komut_Cubugunu_Acmali()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(Belge())]));
        var w = Ac(vm);
        var arama = Bul<Button>(w, "AramaCubugu");

        arama.IsEffectivelyVisible.Should().BeTrue();
        GorunurMetinler(arama).Should().Contain("Ctrl+K");
        arama.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        vm.Palette.IsOpen.Should().BeTrue();
        Bul<Border>(w, "KomutKatmani").IsEffectivelyVisible.Should().BeTrue();
        w.Close();
    }

    [AvaloniaFact]
    public void AnaPencere_Tuval_Turkce_HUD_Ve_OSNAP_Cubugu_Gostermeli()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(Belge())]));
        var w = Ac(vm);

        Bul<Border>(w, "TuvalHud").IsEffectivelyVisible.Should().BeTrue();
        GorunurMetinler(Bul<Border>(w, "TuvalHud")).Should().Contain("Seçim yok — parsele tıklayın");
        var osnap = Bul<Border>(w, "OsnapCubugu").GetVisualDescendants().OfType<ToggleButton>().Select(t => t.Content?.ToString()).ToList();
        osnap.Should().Contain(["⬚ Uç", "△ Orta", "⊥ Dik 90°", "✕ Kesişim", "▦ Izgara", "🏷 Etiketler"]);
        w.Close();
    }

    [AvaloniaFact]
    public void AnaPencere_Sihirbaz_Adim_Cubugu_Bes_Tiklanabilir_Adim_Ve_Sabit_Geri_Ileri_Sunmali()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(Belge())]));
        var w = Ac(vm);

        var adimlar = Bul<ItemsControl>(w, "AdimCubugu").GetVisualDescendants().OfType<Button>().ToList();
        adimlar.Should().HaveCount(5);
        adimlar[0].Classes.Should().Contain("aktif");
        adimlar[1].Command!.CanExecute(adimlar[1].CommandParameter).Should().BeFalse("doğrulanmadan ileri atlanamaz");
        Bul<Button>(w, "GeriDugmesi").IsEffectivelyVisible.Should().BeTrue();
        Bul<Button>(w, "IleriDugmesi").IsEffectivelyVisible.Should().BeTrue();
        w.Close();
    }

    [AvaloniaFact]
    public async Task AnaPencere_Sag_Panel_Parsel_Islemleri_Yalnizca_Bagli_Gercek_Aksiyonlari_Gostermeli()
    {
        var vm = await BosProjeliVm();
        var w = Ac(vm);

        var basliklar = Bul<ItemsControl>(w, "ParselIslemleri").GetVisualDescendants().OfType<Button>().Select(b => b.Content?.ToString()).ToList();

        basliklar.Should().Contain("🔍 Seçili Parsele Yakınlaş").And.Contain("🗺️ Tümünü Sığdır").And.Contain("📦 Fen Klasörü (.zip) Üret");
        basliklar.Should().NotContain(b => b != null && b.Contains("Köşe Pahı", StringComparison.Ordinal), "sonraki faz işlemleri yalnız sağ tık menüsünde (gerekçeli pasif)");
        w.Close();
    }
}

using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
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
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.Gorunum;
using Imar18.Presentation.Avalonia.ViewModels;
using Imar18.Presentation.Avalonia.Views;
using SkiaSharp;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// P7 saha ve UI cilası (feat/p7-saha-ve-ui-cilasi, canlı Windows geri bildirimi): koyu pafta ızgara kontrastı, OSNAP görsel geri
/// bildirimi ve nokta seçimi/kopyalama, Ctrl+K komut çubuğu fare/Esc/dışarı tıklama, numaralı stepper, seçilebilir/kopyalanabilir
/// metinler, Imar18Studio amblemi, canlı Katman → Rol eşlemesi, alan bazlı hata çerçevesi, Zorunlu/İsteğe bağlı rozetleri, Türkçe
/// cetvel başlığı, rutin içe aktarma notu filtresi, Projeyi Temizle / Sıfırla ve ayrı Stüdyo / Pafta temaları.
/// </summary>
public sealed class P7SahaUiCilasiTests
{
    private sealed class SahteAdaptor(Func<CadLayerDocument> belge) : ICadFormatAdapter
    {
        public CadFormat Format => CadFormat.Ncz;

        public bool SupportsExtension(string extension) => true;

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) => Result<CadLayerDocument>.Success(belge());
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

    private sealed class SabitTopoloji(IReadOnlyList<ParselTopolojiBulgusu> sonuc) : IParselTopolojiDenetcisi
    {
        public IReadOnlyList<ParselTopolojiBulgusu> Denetle(IReadOnlyList<ParselGeometrisi> parseller, CancellationToken cancellationToken = default) => sonuc;
    }

    private static GeodeticPoint P(decimal x, decimal y) => new(X: 4_000_000m + x, Y: 500_000m + y);

    private static CadLayerDocument Belge(params AppError[] uyarilar) => new(
        CadFormat.Ncz,
        [new CadLayer(0, "TK_PARSEL_GİREN"), new CadLayer(1, "TK_PARSEL_OLUŞAN"), new CadLayer(2, "YAZI")],
        [
            new CadPolyline("TK_PARSEL_GİREN", "K1", null, [P(0, 0), P(40, 0), P(40, 40), P(0, 40), P(0, 0)], true, null),
            new CadPolyline("TK_PARSEL_OLUŞAN", "101/1", null, [P(0, 0), P(20, 0), P(20, 20), P(0, 20), P(0, 0)], true, null),
        ],
        [], 0, 0, 0, uyarilar);

    private static async Task<MainWindowViewModel> ProjeliVm(IParselTopolojiDenetcisi? topoloji = null, Func<CadLayerDocument>? belge = null) =>
        (await ProjeliAlan(topoloji, belge)).Vm;

    private static async Task<(MainWindowViewModel Vm, ProjeCalismaAlani Alan)> ProjeliAlan(
        IParselTopolojiDenetcisi? topoloji = null, Func<CadLayerDocument>? belge = null, BellekProjeDeposu? depo = null)
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(depo ?? new BellekProjeDeposu(), "Sentetik P7 Cila", zaman)).Value;
        var t = topoloji ?? OrnekProje.Topoloji;
        var alan = new ProjeCalismaAlani(yol, new ApprovalService(yol, zaman, t),
            new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru), new SahteUretici(CiktiTuru.Cetveller), new SahteUretici(CiktiTuru.CksDopOzeti)], new BellekYazici(), t),
            OrnekProje.Muhendis);
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(belge ?? (() => Belge()))]), alan);
        await vm.LastProjeTask;
        return (vm, alan);
    }

    private static MainWindowViewModel Vm(params AppError[] uyarilar) => new(new CadImportService([new SahteAdaptor(() => Belge(uyarilar))]));

    private static MainWindow Ac(MainWindowViewModel vm)
    {
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static T Bul<T>(Window w, string ad) where T : Control => w.FindControl<T>(ad) ?? throw new InvalidOperationException(ad + " bulunamadı");

    private static List<string> GorunurMetinler(Visual kok) => kok.GetVisualDescendants().OfType<TextBlock>()
        .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!).ToList();

    // ============================ 1. Koyu pafta ızgara kontrastı ============================

    private static int KanalFarki(SKColor zemin, SKColor cizgi)
    {
        var a = cizgi.Alpha / 255d;
        int Kanal(byte z, byte c) => (int)Math.Round(Math.Abs(((1 - a) * z) + (a * c) - z));
        return new[] { Kanal(zemin.Red, cizgi.Red), Kanal(zemin.Green, cizgi.Green), Kanal(zemin.Blue, cizgi.Blue) }.Max();
    }

    [Fact]
    public void Izgara_Koyu_Paftada_Ince_Ve_Ana_Cizgiler_Zeminden_Belirgin_Olmali()
    {
        var (zemin, kucuk, buyuk) = CadSceneRenderer.IzgaraRenkleri(acikPafta: false);

        // Eski değerler (beyaz %3, mavi %6) koyu zeminde 6–14 seviye fark veriyordu (görünmez).
        KanalFarki(zemin, kucuk).Should().BeGreaterThanOrEqualTo(15, "ince ızgara koyu zeminde görünmeli");
        KanalFarki(zemin, buyuk).Should().BeGreaterThanOrEqualTo(40, "ana ızgara çizgisi net seçilmeli");
        KanalFarki(zemin, buyuk).Should().BeGreaterThan(KanalFarki(zemin, kucuk), "ana çizgi ince çizgiden belirgin olmalı");
    }

    [Fact]
    public void Izgara_Koyu_Pafta_Cizimi_Zemin_Disi_Piksel_Uretmeli()
    {
        var vm = new CadCanvasViewModel { IzgaraGoster = true };
        vm.SetViewport(400, 300);
        vm.LoadScene(CanvasScene.Empty);
        using var bmp = new SKBitmap(400, 300);
        using (var c = new SKCanvas(bmp))
        {
            CadSceneRenderer.Render(c, CadRenderState.From(vm, 400, 300));
        }

        var zemin = CadSceneRenderer.IzgaraRenkleri(false).Arkaplan;
        var farkli = 0;
        for (var x = 0; x < 400; x++)
        {
            var p = bmp.GetPixel(x, 150);
            if (Math.Abs(p.Red - zemin.Red) + Math.Abs(p.Green - zemin.Green) + Math.Abs(p.Blue - zemin.Blue) >= 30)
            {
                farkli++;
            }
        }

        farkli.Should().BeGreaterThanOrEqualTo(3, "boş pafta ızgaralı görünmeli (yeni CAD dosyasına hazır)");
    }

    // ============================ 2. OSNAP görsel geri bildirim + seçim/kopyalama ============================

    private static CadCanvasViewModel KareSahne()
    {
        var kare = new CadPolyline("TK_PARSEL_OLUŞAN", "101/1", null, [P(0, 0), P(0, 100), P(100, 100), P(100, 0), P(0, 0)], true, null);
        var vm = new CadCanvasViewModel { IzgaraGoster = false, EtiketGoster = false };
        vm.SetViewport(400, 300);
        vm.LoadScene(CanvasScene.From(new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "TK_PARSEL_OLUŞAN")], [kare], [], 0, 0, 0, []), CadLayerRoleMap.Standard));
        return vm;
    }

    [Fact]
    public void Osnap_Kose_Yakalaninca_Tiklama_Noktayi_Secip_Koordinati_Yayinlamali()
    {
        var vm = KareSahne();
        var kose = vm.Camera.WorldToScreen(P(100, 100));
        string? yayinlanan = null;
        vm.NoktaSecildi += (_, m) => yayinlanan = m;

        vm.PointerMoved(kose.X + 3, kose.Y - 2);
        vm.Yakalama.Should().NotBeNull().And.Match<OsnapSonucu>(y => y.Kip == OsnapKipi.Uc);
        vm.YakalamayiSec().Should().BeTrue();

        vm.SecilenNokta.Should().Be(vm.Yakalama);
        yayinlanan.Should().Be("Y=500100.000 X=4000100.000 (Uç)");
        vm.SecilenNoktaMetni.Should().Be("📍 Y=500100.000 X=4000100.000 (Uç)");
        CadCanvasViewModel.OsnapEtiketi(vm.Yakalama!).Should().Be("Uç · Y 500100.000 · X 4000100.000");
    }

    [Fact]
    public void Osnap_Yakalama_Yokken_Tiklama_Nokta_Secmemeli_Yeni_Sahne_Secimi_Silmeli()
    {
        var vm = KareSahne();
        vm.PointerMoved(200, 150);
        vm.YakalamayiSec().Should().BeFalse();
        vm.SecilenNokta.Should().BeNull();

        var kose = vm.Camera.WorldToScreen(P(0, 0));
        vm.PointerMoved(kose.X, kose.Y);
        vm.YakalamayiSec().Should().BeTrue();
        vm.LoadScene(CanvasScene.Empty);
        vm.SecilenNokta.Should().BeNull();
    }

    [Fact]
    public void Osnap_Yakalama_Sembolu_Tuvalde_Parlak_Renkle_Cizilmeli()
    {
        var vm = KareSahne();
        var kose = vm.Camera.WorldToScreen(P(100, 100));
        vm.PointerMoved(kose.X, kose.Y);
        using var bmp = new SKBitmap(400, 300);
        using (var c = new SKCanvas(bmp))
        {
            CadSceneRenderer.Render(c, CadRenderState.From(vm, 400, 300));
        }

        var renk = CadSceneRenderer.YakalamaRengi(false);
        var bulundu = false;
        for (var dx = -9; dx <= 9 && !bulundu; dx++)
        {
            for (var dy = -9; dy <= 9 && !bulundu; dy++)
            {
                var p = bmp.GetPixel((int)kose.X + dx, (int)kose.Y + dy);
                bulundu = Math.Abs(p.Red - renk.Red) < 40 && Math.Abs(p.Green - renk.Green) < 40 && Math.Abs(p.Blue - renk.Blue) < 40;
            }
        }

        bulundu.Should().BeTrue("Uç yakalaması parlak camgöbeği sembolle köşede gösterilmeli");
    }

    [Fact]
    public void Osnap_Nokta_Secimi_Ana_Pencereden_Panoya_Kopyalanmali()
    {
        var vm = Vm();
        string? pano = null;
        vm.PanoyaKopyalaIstendi += (_, m) => pano = m;
        vm.Canvas.SetViewport(400, 300);
        vm.Canvas.LoadScene(KareSahne().Scene);
        var kose = vm.Canvas.Camera.WorldToScreen(P(0, 100));
        vm.Canvas.PointerMoved(kose.X, kose.Y);

        vm.Canvas.YakalamayiSec();

        pano.Should().Be("Y=500100.000 X=4000000.000 (Uç)");
        vm.StatusText.Should().Contain("OSNAP noktası seçildi");
    }

    // ============================ 3. Komut çubuğu (Ctrl+K) ============================

    [Fact]
    public void Komut_Cubugu_Calistir_Komutu_Calistirip_Kapatmali_Calistirilamayan_Komutta_Acik_Kalmali()
    {
        var p = new CommandPaletteViewModel();
        var sayac = 0;
        p.Register(new PaletteCommand("a", "Sığdır", "fit", () => sayac++));
        p.Register(new PaletteCommand("b", "Onayla", "approve", () => sayac += 10, () => false));
        p.Toggle();

        p.Calistir(p.Results[1]).Should().BeFalse();
        p.IsOpen.Should().BeTrue();
        p.Mesaj.Should().Contain("şu anda kullanılamaz");

        p.KomutuCalistirCommand.Execute(p.Results[0]);
        sayac.Should().Be(1);
        p.IsOpen.Should().BeFalse();
        p.Mesaj.Should().BeEmpty();

        p.Toggle();
        p.KapatCommand.Execute(null);
        p.IsOpen.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Komut_Cubugu_Listede_Fareyle_Tiklanan_Komutu_Calistirip_Kapanmali()
    {
        var vm = Vm();
        var w = Ac(vm);
        vm.TogglePaletteCommand.Execute(null);
        vm.Palette.Query = "pafta (yalnız";
        Dispatcher.UIThread.RunJobs();
        var liste = Bul<ListBox>(w, "KomutListesi");
        var oge = liste.ContainerFromIndex(0).Should().BeOfType<ListBoxItem>().Subject;
        ((PaletteCommand)oge.DataContext!).Id.Should().Be("togglePaper");
        var nokta = oge.TranslatePoint(new Point(12, 8), w)!.Value;

        w.MouseDown(nokta, MouseButton.Left);
        w.MouseUp(nokta, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        vm.AcikPafta.Should().BeTrue("tıklanan 'Koyu / Açık Pafta' komutu çalışmalı");
        vm.Palette.IsOpen.Should().BeFalse();
        w.Close();
    }

    [AvaloniaFact]
    public void Komut_Cubugu_Disari_Tiklama_Ve_Esc_Ile_Kapanmali()
    {
        var vm = Vm();
        var w = Ac(vm);
        vm.TogglePaletteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        w.MouseDown(new Point(8, 880), MouseButton.Left);
        w.MouseUp(new Point(8, 880), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        vm.Palette.IsOpen.Should().BeFalse("karartılmış dış alana tıklama çubuğu kapatmalı");

        vm.TogglePaletteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        vm.Palette.IsOpen.Should().BeFalse("Esc çubuğu kapatmalı");
        w.Close();
    }

    // ============================ 4. Profesyonel stepper ============================

    [Fact]
    public void Stepper_Adimlari_Numarali_Rozet_Tamamlanan_Onay_Ve_Baglanti_Durumu_Vermeli()
    {
        var w = new WizardViewModel();
        w.AdimOgeleri.Select(a => a.Ad).Should().Equal("Girdi & Sınır", "Tecviz", "DOP & Y1 Dengeleme", "Hak Eşleştirme & Hisse", "Onay & Çıktı");
        w.AdimOgeleri.Select(a => a.RozetMetni).Should().Equal("1", "2", "3", "4", "5");
        w.AdimOgeleri[0].BaglantiVar.Should().BeFalse();
        w.AdimOgeleri.Skip(1).Should().OnlyContain(a => a.BaglantiVar);

        w.CurrentIndex = 2;

        w.AdimOgeleri.Select(a => a.RozetMetni).Should().Equal("✓", "✓", "3", "4", "5");
        w.AdimOgeleri.Select(a => a.BagTamam).Should().Equal(true, true, true, false, false);
    }

    [AvaloniaFact]
    public void Stepper_Ana_Pencerede_Daire_Rozetler_Ve_Baglanti_Cizgileriyle_Cizilmeli()
    {
        var vm = Vm();
        var w = Ac(vm);
        var cubuk = Bul<ItemsControl>(w, "AdimCubugu");

        var rozetler = cubuk.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("adimRozet")).ToList();
        rozetler.Should().HaveCount(5);
        rozetler[0].Classes.Should().Contain("aktif");
        rozetler.Should().OnlyContain(r => r.CornerRadius.TopLeft >= r.Width / 2 - 0.01, "daire rozet");
        cubuk.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("adimBag") && b.IsVisible).Should().Be(4);

        vm.Wizard.CurrentIndex = 1;
        Dispatcher.UIThread.RunJobs();
        rozetler[0].Classes.Should().Contain("tamam");
        GorunurMetinler(rozetler[0]).Should().Contain("✓");
        w.Close();
    }

    // ============================ 5. Seçilebilir / kopyalanabilir metinler ============================

    [AvaloniaFact]
    public async Task Metinler_Uyari_Listesinde_Secilebilir_Ve_Tek_Tikla_Kopyalanabilir_Olmali()
    {
        var vm = Vm(ErrorCatalog.Get(ErrorCodes.GirdDesteklenmeyenGeometriAtlandi));
        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        string? pano = null;
        vm.PanoyaKopyalaIstendi += (_, m) => pano = m;
        var w = Ac(vm);
        Bul<TabControl>(w, "AltSekmeler").SelectedIndex = 4;
        Dispatcher.UIThread.RunJobs();
        var liste = Bul<ItemsControl>(w, "UyariListesi");

        liste.GetVisualDescendants().OfType<SelectableTextBlock>().Select(t => t.Text).Should().Contain(vm.Warnings[0]);
        var dugme = liste.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("kopyala"));
        dugme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        pano.Should().Be(vm.Warnings[0]);
        vm.StatusText.Should().StartWith("📋 Panoya kopyalandı");
        w.Close();
    }

    [Fact]
    public void Metinler_Kopyala_Komutu_Bos_Metni_Kopyalamamali()
    {
        var vm = Vm();
        var sayac = 0;
        vm.PanoyaKopyalaIstendi += (_, _) => sayac++;

        vm.KopyalaCommand.Execute("  ");
        vm.KopyalaCommand.Execute(null);
        vm.KopyalaCommand.Execute("ERR yok");

        sayac.Should().Be(1);
    }

    // ============================ 6. Imar18Studio amblemi ============================

    [Fact]
    public void Logo_Amblemi_Mavi_Zemin_Ada_Ve_Amber_18_Rozeti_Icermeli()
    {
        using var bmp = Imar18Logo.Ciz(64);

        bmp.GetPixel(1, 1).Alpha.Should().BeLessThan(40, "yuvarlatılmış köşe saydam");
        var rozet = bmp.GetPixel(47, 57); // rozet içi, "1" ve "8" çizgilerinin ve kenar çizgisinin dışında
        rozet.Should().Be(new SKColor(Imar18Logo.Amber.Red, Imar18Logo.Amber.Green, Imar18Logo.Amber.Blue, 255), "18 rozeti amber");
        var zemin = bmp.GetPixel(58, 6);
        zemin.Blue.Should().BeGreaterThan(zemin.Red, "zemin mavi degrade");
        Imar18Logo.Png(32).Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void Logo_Baslikta_Vektor_Amblem_Ve_Pencere_Ikonu_Olarak_Kullanilmali_Emoji_Olmamali()
    {
        var w = Ac(Vm());

        w.Icon.Should().NotBeNull();
        Bul<Image>(w, "Logo").Source.Should().BeOfType<global::Avalonia.Media.DrawingImage>();
        Bul<TextBlock>(w, "MarkaAdi").Text.Should().Be("Imar18Studio");
        GorunurMetinler(w).Should().NotContain(m => m.Contains("🏗", StringComparison.Ordinal));
        w.Close();
    }

    // ============================ 7. Katman → Rol eşlemesi canlı ============================

    [Fact]
    public async Task KatmanRol_Degisince_Tuval_Tablo_Ve_Proje_Geometrisi_Anlik_Guncellenmeli()
    {
        var (vm, alan) = await ProjeliAlan();
        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        await vm.LastProjeTask;
        alan.Yol.Belge.Parseller.Should().Contain(p => p.ParselId == "101/1" && p.Rol == ParselRolu.Imar);

        vm.Wizard.LayerRoles.Single(r => r.LayerName == "TK_PARSEL_OLUŞAN").Role = CadLayerRole.RoadDedication;
        await vm.LastReapplyTask;

        vm.Parcels.Single(r => r.Label == "101/1").Role.Should().Be("Yola Terk");
        vm.Canvas.Scene.Polylines.Single(p => p.Label == "101/1").Role.Should().Be(CadLayerRole.RoadDedication);
        alan.Yol.Belge.Parseller.Should().Contain(p => p.ParselId == "101/1" && p.Rol == ParselRolu.YolTerk);

        await vm.EslemeyiUygulaCommand.ExecuteAsync(null);
        vm.StatusText.Should().StartWith("Katman → Rol eşlemesi uygulandı");
    }

    [Fact]
    public void KatmanRol_Her_Parsel_Rolu_Tuvalde_Ayirt_Edilebilir_Renkle_Cizilmeli()
    {
        var roller = new[] { CadLayerRole.CadastralParcel, CadLayerRole.ZoningParcel, CadLayerRole.RoadDedication, CadLayerRole.Park, CadLayerRole.RegulationBoundary, CadLayerRole.BlockEdge };
        foreach (var acik in new[] { false, true })
        {
            roller.Select(r => CadSceneRenderer.RolRengi(r, acik)).Distinct().Should().HaveCount(roller.Length);
        }
    }

    // ============================ 8. Alan bazlı hata çerçevesi + 9. Zorunlu / İsteğe bağlı ============================

    [Fact]
    public void AlanHatasi_Bos_Tecviz_Alanlari_Ayri_Ayri_Isaretlenmeli_Duzenlenince_Temizlenmeli()
    {
        var w = new WizardViewModel { EpokText = "abc", TecvizSiniriText = "0" };

        w.Validate().Should().BeFalse();

        w.EpokHatasi.Should().Contain("Epok");
        w.TecvizUretimYontemiHatasi.Should().Contain("Üretim yöntemi");
        w.TecvizKaynakNotuHatasi.Should().Contain("Kaynak notu");
        w.TecvizSiniriHatasi.Should().Contain("0'dan büyük");
        w.KatmanRolHatasi.Should().Contain("kadastro");
        w.HataSayisi.Should().BeGreaterThan(0);
        w.DogrulamaOzeti.Should().EndWith("hata · 0 uyarı");

        w.TecvizUretimYontemi = "Sayısallaştırma";
        w.EpokText = "2005.00";
        w.TecvizUretimYontemiHatasi.Should().BeNull();
        w.EpokHatasi.Should().BeNull();
        w.TecvizKaynakNotuHatasi.Should().NotBeNull("düzenlenmeyen alanın hatası kalır");
    }

    [AvaloniaFact]
    public void AlanHatasi_Hatali_Kutu_Kirmizi_Cerceve_Ve_Altinda_Hata_Satiri_Gostermeli()
    {
        var vm = Vm();
        var w = Ac(vm);
        var kutu = Bul<TextBox>(w, "UretimYontemiKutusu");
        kutu.Classes.Should().NotContain("hatali");

        Bul<Button>(w, "DogrulaDugmesi").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        kutu.Classes.Should().Contain("hatali");
        var satir = Bul<Grid>(w, "UretimYontemiHataSatiri");
        satir.IsEffectivelyVisible.Should().BeTrue();
        GorunurMetinler(satir).Should().Contain("⛔").And.Contain(vm.Wizard.TecvizUretimYontemiHatasi!);
        Bul<Border>(w, "DogrulamaSonucKarti").IsEffectivelyVisible.Should().BeTrue();
        GorunurMetinler(Bul<Border>(w, "DogrulamaSonucKarti")).Should().Contain(m => m.EndsWith(" hata", StringComparison.Ordinal));

        vm.Wizard.TecvizUretimYontemi = "Sayısallaştırma";
        Dispatcher.UIThread.RunJobs();
        kutu.Classes.Should().NotContain("hatali");
        satir.IsEffectivelyVisible.Should().BeFalse();
        w.Close();
    }

    [AvaloniaFact]
    public void Etiketler_Zorunlu_Ve_Istege_Bagli_Rozetleri_Gostermeli()
    {
        var w = Ac(Vm());

        GorunurMetinler(Bul<Border>(w, "SolPanel")).Count(m => m == "(* Zorunlu)").Should().BeGreaterThanOrEqualTo(9);
        w.Close();

        var kok = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var xaml = XDocument.Load(Path.Combine(kok, "src", "Imar18.Presentation.Avalonia", "Views", "MainWindow.axaml"));
        xaml.Descendants().Count(e => e.Attribute("Text")?.Value == "(İsteğe bağlı)").Should().BeGreaterThanOrEqualTo(7,
            "toplam tecviz sınırı, R2, D1, D2, R3, ortak katılım ve geçici↔tescil isteğe bağlıdır");
    }

    [Fact]
    public async Task AlanHatasi_Gecersiz_Tapu_Ve_DOP_Girdileri_Kendi_Kutularinda_Isaretlenmeli()
    {
        var vm = await ProjeliVm();
        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        vm.Wizard.TecvizUretimYontemi = "Sayısallaştırma";
        vm.Wizard.TecvizSiniriText = "10";
        vm.Wizard.TecvizKaynakNotu = "Sentetik";
        vm.Wizard.Validate();
        var h = vm.Hesap!;
        await h.YenileAsync();
        h.TecvizSatirlari.Should().ContainSingle();
        h.TecvizSatirlari[0].TapuAlaniText = "x1";

        await h.TecvizUygulaCommand.ExecuteAsync(null);

        h.TecvizSatirlari[0].Hata.Should().NotBeNullOrEmpty();
        h.TecvizSatirlari[0].TapuAlaniText = "1600";
        h.TecvizSatirlari[0].Hata.Should().BeNull();
        await h.TecvizUygulaCommand.ExecuteAsync(null);
        h.R1.Should().Be(1600m, h.DurumMetni);

        h.D1Text = "a";
        h.R3Text = "b";
        await h.DopUygulaCommand.ExecuteAsync(null);
        h.D1Hatasi.Should().NotBeNull();
        h.R3Hatasi.Should().NotBeNull();
        h.D2Hatasi.Should().BeNull();
        h.D1Text = "0";
        h.D1Hatasi.Should().BeNull("düzenlenen alanın hatası temizlenir");
    }

    // ============================ 10. Tam Türkçe cetvel başlığı ============================

    [AvaloniaFact]
    public void Cetvel_Basligi_Tam_Turkce_Olmali_System_Decimal_Gostermemeli()
    {
        var w = Ac(Vm());

        Bul<TextBlock>(w, "CetvelBasligi").Text.Should().Be("CANLI CETVEL • Tam Kesinlikli Hesap • cm² Hassasiyetli");
        GorunurMetinler(w).Should().NotContain(m => m.Contains("System.Decimal", StringComparison.Ordinal));
        w.Close();
    }

    // ============================ 11. İçe aktarma uyarı filtresi ============================

    [Fact]
    public async Task UyariFiltresi_Z0_Ve_1mm_Notu_Bilgi_Satiri_Olmali_Uyari_Sayisina_Girmemeli()
    {
        var vm = Vm(ErrorCatalog.Get(ErrorCodes.GirdZKoduIndirgendi), ErrorCatalog.Get(ErrorCodes.GirdIzgaraDisiKose),
            ErrorCatalog.Get(ErrorCodes.GirdDesteklenmeyenGeometriAtlandi));

        await vm.ImportAsync(new MemoryStream([1]), ".ncz");

        vm.Warnings.Should().ContainSingle().Which.Should().Be(ErrorCatalog.Get(ErrorCodes.GirdDesteklenmeyenGeometriAtlandi).UserMessage);
        vm.BilgiNotlari.Should().HaveCount(2);
        vm.DenetimUyariSayisi.Should().Be(1);
        vm.StatusText.Should().Contain("1 uyarı, 2 bilgi notu");
    }

    [Fact]
    public async Task UyariFiltresi_Topoloji_Yalniz_Onay_Engeli_Sayilmali_Tolerans_Ve_Imar_Yolu_Sayilmamali()
    {
        var vm = await ProjeliVm(new SabitTopoloji([
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanCakismasi, ["101/1", "101/2"], 0.01m, SinirToleransiIcinde: true),
            new ParselTopolojiBulgusu(TopolojiBulguTuru.ImarYoluAlani, ["101/1", "102/1"], 60.00m),
            new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["101/2", "K1"], 0.20m),
        ]));
        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        await vm.LastProjeTask;

        vm.OnayCikti!.TopolojiNotlari.Should().HaveCount(3, "tüm bulgular listelenir (bilgi / uyarı / engel)");
        vm.OnayCikti.TopolojiNotlari[0].Metin.Should().Be("Oluşan parseller çakışması: 101/1 ↔ 101/2 — 0.01 m² (sınır toleransı içinde; onayı engellemez)");
        vm.OnayCikti.TopolojiUyariSayisi.Should().Be(1);
        vm.DenetimUyariSayisi.Should().Be(1);
    }

    // ============================ 12. Projeyi Temizle / Sıfırla ============================

    [Fact]
    public async Task Temizle_Harita_Tablolar_Girdiler_Ve_Proje_Durumu_Bosalmali()
    {
        var vm = await ProjeliVm();
        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        vm.Wizard.TecvizSiniriText = "5";
        vm.Canvas.IzgaraGoster = false;
        vm.Wizard.CurrentIndex = 0;
        vm.Parcels.Should().NotBeEmpty();

        await vm.ProjeyiTemizleCommand.ExecuteAsync(null);

        vm.Parcels.Should().BeEmpty();
        vm.Warnings.Should().BeEmpty();
        vm.Canvas.Scene.Polylines.Should().BeEmpty();
        vm.Canvas.IzgaraGoster.Should().BeTrue("ızgaralı boş tuval");
        vm.Wizard.LayerRoles.Should().BeEmpty();
        vm.Wizard.TecvizSiniriText.Should().BeEmpty();
        vm.Hesap!.TecvizSatirlari.Should().BeEmpty();
        vm.OnayCikti!.Ekran!.ParselSayisi.Should().Be(0);
        vm.StatusText.Should().Contain("Proje temizlendi");

        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        vm.Parcels.Should().HaveCount(2, "temizlikten sonra yeni CAD dosyası açılabilmeli");
    }

    [Fact]
    public async Task Temizle_Komut_Reddedilirse_Arayuz_Temizlenmemeli_Hata_Gosterilmeli()
    {
        // Reviewer M3: CommandBus kaydi basarisizsa harita/tablolar silinmez (proje ile arayuz tutarsizlasmaz).
        var depo = new BellekProjeDeposu();
        var (vm, alan) = await ProjeliAlan(depo: depo);
        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        depo.SonrakiYazmayiBozdur = true;

        await vm.ProjeyiTemizleCommand.ExecuteAsync(null);

        vm.StatusText.Should().StartWith("Proje temizlenemedi");
        vm.Parcels.Should().HaveCount(2);
        vm.Canvas.Scene.Polylines.Should().NotBeEmpty();
        alan.Yol.Belge.Parseller.Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void Temizle_Dugmeleri_Sol_Kart_Ve_Ust_Cubukta_Onay_Acilir_Penceresiyle_Bulunmali()
    {
        var w = Ac(Vm());

        foreach (var ad in new[] { "TemizleDugmesi", "UstTemizleDugmesi" })
        {
            var d = Bul<Button>(w, ad);
            d.IsEffectivelyVisible.Should().BeTrue();
            d.Flyout.Should().NotBeNull("yanlışlıkla temizlemeye karşı onay istenir");
        }

        Bul<Button>(w, "TemizleDugmesi").Content.Should().Be("🧹 Projeyi Temizle / Sıfırla");
        w.Close();
    }

    // ============================ 13–16. Çift tema: Stüdyo ve Pafta ============================

    [Fact]
    public void Tema_Studyo_Ve_Pafta_Ayri_Degistirilebilmeli()
    {
        var vm = Vm();
        vm.TemaEtiketi.Should().Be("☀️ Açık Stüdyo");
        vm.PaftaEtiketi.Should().Be("📄 Açık Pafta");

        vm.TemaDegistirCommand.Execute(null);
        vm.AcikStudyo.Should().BeTrue();
        vm.AcikPafta.Should().BeTrue("stüdyo değişince pafta eşlenir");
        vm.TemaEtiketi.Should().Be("🌙 Koyu Stüdyo");

        vm.PaftaDegistirCommand.Execute(null);
        vm.AcikStudyo.Should().BeTrue("pafta anahtarı stüdyo temasına dokunmaz");
        vm.AcikPafta.Should().BeFalse();
        vm.Canvas.AcikPafta.Should().BeFalse();
        vm.PaftaEtiketi.Should().Be("📄 Açık Pafta");

        vm.PaftaDegistirCommand.Execute(null);
        vm.PaftaEtiketi.Should().Be("🌑 Koyu Pafta");
    }

    [AvaloniaFact]
    public void Tema_Pafta_Anahtari_Yakinlastirma_Grubunun_Yaninda_Olmali_Ve_Uygulama_Temasini_Degistirmemeli()
    {
        var vm = Vm();
        var w = Ac(vm);
        try
        {
            var pafta = Bul<Button>(w, "PaftaAnahtari");
            var grup = pafta.GetVisualParent()!;
            grup.GetVisualChildren().OfType<Button>().Select(b => b.Content?.ToString()).Should().Contain(["−", "+"]);

            pafta.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            vm.Canvas.AcikPafta.Should().BeTrue();
            global::Avalonia.Application.Current!.RequestedThemeVariant.Should().Be(ThemeVariant.Dark, "yalnız pafta açıldı; stüdyo koyu kalır");
            pafta.Content.Should().Be("🌑 Koyu Pafta");

            Bul<Button>(w, "TemaAnahtari").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            global::Avalonia.Application.Current!.RequestedThemeVariant.Should().Be(ThemeVariant.Light);
            Bul<Button>(w, "TemaAnahtari").Content.Should().Be("🌙 Koyu Stüdyo");
        }
        finally
        {
            global::Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            w.Close();
        }
    }
}

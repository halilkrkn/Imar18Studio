using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
using Imar18.Presentation.Avalonia.Controls;
using Imar18.Presentation.Avalonia.ViewModels;
using Imar18.Presentation.Avalonia.Views;
using SkiaSharp;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// P7.3-A (SAHA-005): Girdi &amp; Sınır adımında Katman → Rol eşlemesinin doğrudan tuvaldeki çizimden yapılması — açık çizgiler
/// dahil katman isabeti, hızlı rol çipleriyle "tıkla-ata" modu, sağ tık "Bu katmanın rolü ▸" alt menüsü, katman vurgusu
/// (hover + listeden 🎯) ve mevcut <c>LayerRolesChanged → ParselGeometrileriAyarlaKomutu</c> zinciriyle projeye yazım.
/// Veri tamamen sentetiktir.
/// </summary>
public sealed class P73TuvaldenRolEslemeTests
{
    private const string Kadastro = "TK_PARSEL_GİREN";
    private const string Imar = "TK_PARSEL_OLUŞAN";
    private const string AdaKenari = "ADA_KENARLT";

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

    private static GeodeticPoint P(decimal x, decimal y) => new(X: 4_000_000m + x, Y: 500_000m + y);

    private static readonly CadPolyline KadastroKare =
        new(Kadastro, "K1", null, [P(0, 0), P(40, 0), P(40, 40), P(0, 40), P(0, 0)], true, null);

    private static readonly CadPolyline ImarKare =
        new(Imar, "101/1", null, [P(0, 0), P(20, 0), P(20, 20), P(0, 20), P(0, 0)], true, null);

    /// <summary>Açık ada kenarı çizgisi: kadastro karesinin içinden Kuzey X = 30'da Doğu boyunca geçer.</summary>
    private static readonly CadPolyline AcikCizgi = new(AdaKenari, string.Empty, null, [P(30, 0), P(30, 40)], false, null);

    private static CadLayerDocument Belge() => new(
        CadFormat.Ncz,
        [new CadLayer(0, Kadastro), new CadLayer(1, Imar), new CadLayer(2, AdaKenari)],
        [KadastroKare, ImarKare, AcikCizgi],
        [], 0, 0, 0, []);

    private static CanvasScene Sahne() => CanvasScene.From(Belge(), CadLayerRoleMap.Standard);

    private static CadCanvasViewModel TuvalVm(bool rolAtamaEtkin = true)
    {
        var vm = new CadCanvasViewModel();
        vm.SetViewport(800, 600);
        vm.LoadScene(Sahne());
        vm.RolAtamaEtkin = rolAtamaEtkin;
        return vm;
    }

    private static (double X, double Y) Ekran(CadCanvasViewModel vm, GeodeticPoint p) => vm.Camera.WorldToScreen(p);

    private static async Task<(MainWindowViewModel Vm, ProjeCalismaAlani Alan)> ProjeliAlan(Func<CadLayerDocument>? belge = null)
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik P7.3-A", zaman)).Value;
        var t = OrnekProje.Topoloji;
        var alan = new ProjeCalismaAlani(yol, new ApprovalService(yol, zaman, t),
            new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru), new SahteUretici(CiktiTuru.Cetveller), new SahteUretici(CiktiTuru.CksDopOzeti)], new BellekYazici(), t),
            OrnekProje.Muhendis);
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(belge ?? Belge)]), alan);
        await vm.LastProjeTask;
        vm.Canvas.SetViewport(800, 600);
        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        await vm.LastProjeTask;
        return (vm, alan);
    }

    // ============================ 1. Sahne: katman isabeti ============================

    [Fact]
    public void KatmanIsabeti_Acik_Cizgiye_Tolerans_Icinde_Tiklamada_Acik_Cizginin_Katmanini_Vermeli()
    {
        var isabet = Sahne().KatmanIsabeti(P(30.3m, 12), 0.5m);

        isabet.Should().NotBeNull();
        isabet!.LayerName.Should().Be(AdaKenari);
        isabet.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void KatmanIsabeti_Acik_Cizgiye_Tolerans_Disinda_Tiklamada_Kapsayan_Poligonu_Vermeli()
    {
        // 0.6 m > 0.5 m tolerans: açık çizgi yakalanmaz; nokta yalnız kadastro karesinin içindedir.
        var isabet = Sahne().KatmanIsabeti(P(30.6m, 12), 0.5m);

        isabet!.LayerName.Should().Be(Kadastro);
    }

    [Fact]
    public void KatmanIsabeti_Tam_Tolerans_Sinirinda_Acik_Cizgiyi_Yakalamali()
    {
        Sahne().KatmanIsabeti(P(30.5m, 12), 0.5m)!.LayerName.Should().Be(AdaKenari);
    }

    [Fact]
    public void KatmanIsabeti_Kenarlardan_Uzak_Ic_Noktada_En_Kucuk_Kapsayan_Poligonu_Vermeli()
    {
        Sahne().KatmanIsabeti(P(10, 10), 0.5m)!.LayerName.Should().Be(Imar);
    }

    [Fact]
    public void KatmanIsabeti_Ortak_Kenara_Yakin_Tiklamada_En_Yakin_Kenarin_Katmanini_Vermeli()
    {
        // İmar karesinin Kuzey X = 20 kenarına 0.1 m; kadastro kenarları 20 m uzakta.
        Sahne().KatmanIsabeti(P(20.1m, 5), 0.5m)!.LayerName.Should().Be(Imar);
    }

    [Fact]
    public void KatmanIsabeti_Esit_Mesafede_Acik_Cizgi_Kapali_Kenarla_Cakisirsa_Acik_Cizgiyi_Vermeli()
    {
        // Ada kenarı imar parselinin Kuzey X = 20 kenarının tam üstünde (Netcad'de sık görülen çizim); reviewer M1.
        var ustUste = new CadPolyline(AdaKenari, string.Empty, null, [P(20, 0), P(20, 20)], false, null);
        var sahne = CanvasScene.From(new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, Imar), new CadLayer(1, AdaKenari)],
            [ImarKare, ustUste], [], 0, 0, 0, []), CadLayerRoleMap.Standard);

        sahne.KatmanIsabeti(P(20.1m, 5), 0.5m)!.LayerName.Should().Be(AdaKenari);
        sahne.KatmanIsabeti(P(10, 10), 0.5m)!.LayerName.Should().Be(Imar, "kenardan uzak iç noktada poligon seçilir");
    }

    [Fact]
    public void KatmanIsabeti_Esit_Mesafede_Iki_Kapali_Poligondan_Kucuk_Alanliyi_Vermeli()
    {
        // İmar karesinin X = 0 kenarı kadastro karesinin X = 0 kenarıyla çakışık; eşit uzaklıkta küçük alanlı (imar) seçilir.
        Sahne().KatmanIsabeti(P(-0.1m, 5), 0.5m)!.LayerName.Should().Be(Imar);
    }

    [Fact]
    public void KatmanIsabeti_Sahne_Disinda_Ve_Bos_Sahnede_Null_Vermeli()
    {
        Sahne().KatmanIsabeti(P(500, 500), 0.5m).Should().BeNull();
        CanvasScene.Empty.KatmanIsabeti(P(0, 0), 0.5m).Should().BeNull();
    }

    [Fact]
    public void KatmanIsabeti_Negatif_Toleransta_Hata_Vermeli()
    {
        var act = () => Sahne().KatmanIsabeti(P(0, 0), -0.1m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ============================ 2. Tuval: rol atama modu ve çipler ============================

    [Fact]
    public void RolCipleri_Tum_Rolleri_Turkce_Adlariyla_Icermeli_Ve_Baslangicta_Mod_Kapali_Olmali()
    {
        var vm = TuvalVm();

        vm.RolCipleri.Select(c => c.Rol).Should().BeEquivalentTo(Enum.GetValues<CadLayerRole>());
        vm.RolCipleri.Single(c => c.Rol == CadLayerRole.RoadDedication).Ad.Should().Be("Yola Terk");
        vm.RolAtamaModu.Should().BeNull();
        vm.RolAtamaAktif.Should().BeFalse();
        vm.RolCipleri.Should().OnlyContain(c => !c.Secili);
    }

    [Fact]
    public void RolCipi_Secilince_Mod_Acilmali_Ayni_Cipe_Tekrar_Basinca_Kapanmali()
    {
        var vm = TuvalVm();
        var yol = vm.RolCipleri.Single(c => c.Rol == CadLayerRole.RoadDedication);

        yol.SecCommand.Execute(null);
        vm.RolAtamaModu.Should().Be(CadLayerRole.RoadDedication);
        yol.Secili.Should().BeTrue();
        vm.RolAtamaMetni.Should().Contain("Yola Terk").And.Contain("Esc");

        vm.RolCipleri.Single(c => c.Rol == CadLayerRole.Park).SecCommand.Execute(null);
        vm.RolAtamaModu.Should().Be(CadLayerRole.Park);
        yol.Secili.Should().BeFalse("tek çip seçili olmalı");

        vm.RolCipleri.Single(c => c.Rol == CadLayerRole.Park).SecCommand.Execute(null);
        vm.RolAtamaModu.Should().BeNull();
        vm.RolCipleri.Should().OnlyContain(c => !c.Secili);
    }

    [Fact]
    public void RolAtamaModu_Acikken_Sol_Tik_Acik_Cizginin_Katmanina_Atama_Istemeli_Ve_Parsel_Secmemeli()
    {
        var vm = TuvalVm();
        var istekler = new List<KatmanRolAtamasi>();
        vm.KatmanRolAtamaIstendi += (_, a) => istekler.Add(a);
        vm.RolAtamaModu = CadLayerRole.RegulationBoundary;
        var (x, y) = Ekran(vm, P(30, 12));

        vm.SolTikla(x, y + 2); // 2 px kayık: tolerans (6 px) içinde

        istekler.Should().ContainSingle().Which.Should().Be(new KatmanRolAtamasi(AdaKenari, CadLayerRole.RegulationBoundary));
        vm.Selected.Should().BeNull("rol atama modunda tıklama parsel seçmez");
        vm.IsaretliKatman.Should().BeNull("işaretleme sihirbazdan gelir (atama başarılıysa); tuval tek başına işaretlemez");
        vm.RolAtamaModu.Should().Be(CadLayerRole.RegulationBoundary, "mod art arda atama için açık kalır");
    }

    [Fact]
    public void RolAtamaModu_Acikken_Bosluga_Tiklama_Atama_Istememeli()
    {
        var vm = TuvalVm();
        var sayac = 0;
        vm.KatmanRolAtamaIstendi += (_, _) => sayac++;
        vm.RolAtamaModu = CadLayerRole.Park;
        var (x, y) = Ekran(vm, P(-30, -30));

        vm.SolTikla(x, y);

        sayac.Should().Be(0);
    }

    [Fact]
    public void RolAtamaModu_Kapaliyken_Sol_Tik_Eski_Parsel_Secimi_Davranisini_Korumali()
    {
        var vm = TuvalVm();
        var sayac = 0;
        vm.KatmanRolAtamaIstendi += (_, _) => sayac++;
        var (x, y) = Ekran(vm, P(10, 10));

        vm.SolTikla(x, y);

        sayac.Should().Be(0);
        vm.Selected!.Label.Should().Be("101/1");
    }

    [Fact]
    public void RolAtama_Etkin_Degilken_Mod_Acilamamali_Ve_Kapanmali()
    {
        var vm = TuvalVm();
        vm.RolAtamaModu = CadLayerRole.Park;

        vm.RolAtamaEtkin = false;
        vm.RolAtamaModu.Should().BeNull("1. adımdan çıkınca mod kapanır");

        vm.RolAtamaModu = CadLayerRole.Park;
        vm.RolAtamaModu.Should().BeNull("etkin değilken mod açılamaz");
        vm.RolCipleri.Single(c => c.Rol == CadLayerRole.Park).SecCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void RolAtamaModundanCik_Modu_Ve_Imlec_Vurgusunu_Temizlemeli()
    {
        var vm = TuvalVm();
        vm.RolAtamaModu = CadLayerRole.Park;
        var (x, y) = Ekran(vm, P(30, 12));
        vm.PointerMoved(x, y);
        vm.ImlecKatmani.Should().Be(AdaKenari);

        vm.RolAtamaModundanCik();

        vm.RolAtamaModu.Should().BeNull();
        vm.ImlecKatmani.Should().BeNull();
        vm.VurguluKatman.Should().BeNull();
    }

    // ============================ 3. Vurgu (hover + işaretli) ve HUD ============================

    [Fact]
    public void Imlec_Rol_Modunda_Katman_Uzerindeyken_Katmani_Vurgulamali_Ve_Mevcut_Rolunu_Gostermeli()
    {
        var vm = TuvalVm();
        vm.RolAtamaModu = CadLayerRole.Park;
        var once = vm.RenderVersion;
        var (x, y) = Ekran(vm, P(30, 12));

        vm.PointerMoved(x, y);

        vm.VurguluKatman.Should().Be(AdaKenari);
        vm.RolAtamaMetni.Should().Contain(AdaKenari).And.Contain("Ada Kenarı").And.Contain("Park Alanı");
        vm.RenderVersion.Should().BeGreaterThan(once, "vurgu değişince tuval yeniden çizilir");
    }

    [Fact]
    public void Imlec_Rol_Modu_Kapaliyken_Katman_Vurgusu_Yapmamali()
    {
        var vm = TuvalVm();
        var (x, y) = Ekran(vm, P(30, 12));

        vm.PointerMoved(x, y);

        vm.ImlecKatmani.Should().BeNull();
        vm.VurguluKatman.Should().BeNull();
    }

    [Fact]
    public void Isaretli_Katman_Imlec_Yokken_Vurgulu_Katman_Olmali()
    {
        var vm = TuvalVm();

        vm.IsaretliKatman = Kadastro;

        vm.VurguluKatman.Should().Be(Kadastro);
    }

    [Fact]
    public void Vurgulu_Katman_Cizimde_Gorunur_Fark_Uretmeli()
    {
        static byte[] Ciz(CadCanvasViewModel vm)
        {
            using var bitmap = new SKBitmap(800, 600);
            using (var canvas = new SKCanvas(bitmap))
            {
                CadSceneRenderer.Render(canvas, CadRenderState.From(vm, 800, 600));
            }

            return bitmap.Bytes;
        }

        var vm = TuvalVm();
        var vurgusuz = Ciz(vm);
        vm.IsaretliKatman = AdaKenari;

        CadRenderState.From(vm, 800, 600).VurguluKatman.Should().Be(AdaKenari);
        Ciz(vm).Should().NotEqual(vurgusuz);
    }

    // ============================ 4. Sağ tık "Bu katmanın rolü ▸" alt menüsü ============================

    [Fact]
    public void SagTik_Katman_Rolu_Alt_Menusu_Tum_Rolleri_Sunmali_Ve_Secim_Atama_Istemeli()
    {
        var vm = TuvalVm();
        var istekler = new List<KatmanRolAtamasi>();
        vm.KatmanRolAtamaIstendi += (_, a) => istekler.Add(a);
        var (x, y) = Ekran(vm, P(30, 12));

        vm.SagTikla(x, y);

        var menu = vm.ContextMenuItems.Single(m => m.Id == "katmanRolu");
        menu.Title.Should().Contain(AdaKenari);
        menu.AltOgeler.Should().HaveCount(Enum.GetValues<CadLayerRole>().Length);
        menu.AltOgeler!.Single(o => o.Title.Contains("Ada Kenarı", StringComparison.Ordinal)).Title.Should().StartWith("✓", "mevcut rol işaretli");
        var park = menu.AltOgeler!.Single(o => o.Title.Contains("Park Alanı", StringComparison.Ordinal));
        park.Command.CanExecute(null).Should().BeTrue();

        park.Command.Execute(null);

        istekler.Should().ContainSingle().Which.Should().Be(new KatmanRolAtamasi(AdaKenari, CadLayerRole.Park));
    }

    [Fact]
    public void SagTik_Bosluga_Yapilinca_Katman_Rolu_Alt_Ogeleri_Pasif_Olmali()
    {
        var vm = TuvalVm();
        var (x, y) = Ekran(vm, P(-30, -30));

        vm.SagTikla(x, y);

        var menu = vm.ContextMenuItems.Single(m => m.Id == "katmanRolu");
        menu.AltOgeler!.Should().OnlyContain(o => !o.Command.CanExecute(null));
    }

    [Fact]
    public void SagTik_Rol_Atama_Etkin_Degilken_Alt_Ogeler_Gerekceyle_Pasif_Olmali()
    {
        var vm = TuvalVm(rolAtamaEtkin: false);
        var (x, y) = Ekran(vm, P(30, 12));

        vm.SagTikla(x, y);

        var menu = vm.ContextMenuItems.Single(m => m.Id == "katmanRolu");
        menu.AltOgeler!.Should().OnlyContain(o => !o.Command.CanExecute(null));
        menu.DisabledReason.Should().Contain("1. adım");
    }

    [Fact]
    public void Parsel_Islemleri_Karti_Katman_Rolu_Alt_Menusunu_Dugme_Olarak_Gostermemeli()
    {
        TuvalVm().ParselIslemleri.Should().NotContain(m => m.Id == "katmanRolu");
    }

    // ============================ 5. Sihirbaz + proje entegrasyonu ============================

    [Fact]
    public void Wizard_RolAta_Var_Olan_Katmanda_Rolu_Degistirmeli_Bilinmeyende_False_Vermeli()
    {
        var w = new WizardViewModel();
        w.LoadLayers([new CadLayer(0, Kadastro), new CadLayer(1, AdaKenari)]);
        var degisim = 0;
        w.LayerRolesChanged += (_, _) => degisim++;

        w.RolAta(AdaKenari, CadLayerRole.RegulationBoundary).Should().BeTrue();
        w.RolAta(AdaKenari, CadLayerRole.RegulationBoundary).Should().BeTrue("aynı rol: başarı, ama yeniden uygulama yok");
        w.RolAta("YOK_BOYLE_KATMAN", CadLayerRole.Park).Should().BeFalse();

        w.LayerRoles.Single(r => r.LayerName == AdaKenari).Role.Should().Be(CadLayerRole.RegulationBoundary);
        degisim.Should().Be(1);
        w.VurguluKatmanAdi.Should().Be(AdaKenari);
        w.LayerRoles.Single(r => r.LayerName == AdaKenari).Vurgulu.Should().BeTrue();
        w.LayerRoles.Single(r => r.LayerName == Kadastro).Vurgulu.Should().BeFalse();
    }

    [Fact]
    public void Katman_Satiri_Hedef_Dugmesi_Vurguyu_Acip_Kapatmali()
    {
        var w = new WizardViewModel();
        w.LoadLayers([new CadLayer(0, Kadastro), new CadLayer(1, Imar)]);
        var kadastro = w.LayerRoles.Single(r => r.LayerName == Kadastro);
        var imar = w.LayerRoles.Single(r => r.LayerName == Imar);

        kadastro.VurgulaCommand.Execute(null);
        w.VurguluKatmanAdi.Should().Be(Kadastro);
        kadastro.Vurgulu.Should().BeTrue();

        imar.VurgulaCommand.Execute(null);
        w.VurguluKatmanAdi.Should().Be(Imar);
        kadastro.Vurgulu.Should().BeFalse();

        imar.VurgulaCommand.Execute(null);
        w.VurguluKatmanAdi.Should().BeNull();
        imar.Vurgulu.Should().BeFalse();
    }

    [Fact]
    public async Task Tuvalden_Rol_Atamasi_Proje_Geometrisini_Tabloyu_Ve_Listeyi_Guncellemeli()
    {
        var (vm, alan) = await ProjeliAlan();
        alan.Yol.Belge.Parseller.Should().Contain(p => p.ParselId == "101/1" && p.Rol == ParselRolu.Imar);
        vm.Canvas.RolAtamaEtkin.Should().BeTrue("1. adımda katmanlar yüklüyken tuvalden atama açık");
        vm.Canvas.RolCipleri.Single(c => c.Rol == CadLayerRole.RoadDedication).SecCommand.Execute(null);
        var (x, y) = vm.Canvas.Camera.WorldToScreen(P(10, 10));

        vm.Canvas.SolTikla(x, y);
        await vm.LastReapplyTask;

        vm.Wizard.LayerRoles.Single(r => r.LayerName == Imar).Role.Should().Be(CadLayerRole.RoadDedication);
        vm.Wizard.LayerRoles.Single(r => r.LayerName == Imar).Vurgulu.Should().BeTrue("liste satırı tuvalle senkron vurgulanır");
        vm.Parcels.Single(r => r.Label == "101/1").Role.Should().Be("Yola Terk");
        vm.Canvas.Scene.Polylines.Single(p => p.Label == "101/1").Role.Should().Be(CadLayerRole.RoadDedication);
        alan.Yol.Belge.Parseller.Should().Contain(p => p.ParselId == "101/1" && p.Rol == ParselRolu.YolTerk);
        vm.Canvas.IsaretliKatman.Should().Be(Imar);
        vm.Canvas.RolAtamaModu.Should().Be(CadLayerRole.RoadDedication, "yeniden uygulama sonrası mod açık kalır");
    }

    [Fact]
    public async Task Sag_Tik_Alt_Menusuyle_Acik_Cizgiye_Rol_Atanmali()
    {
        var (vm, _) = await ProjeliAlan();
        var (x, y) = vm.Canvas.Camera.WorldToScreen(P(30, 12));

        vm.Canvas.SagTikla(x, y);
        vm.Canvas.ContextMenuItems.Single(m => m.Id == "katmanRolu").AltOgeler!
            .Single(o => o.Title.Contains("Düzenleme Sınırı", StringComparison.Ordinal)).Command.Execute(null);
        await vm.LastReapplyTask;

        vm.Wizard.LayerRoles.Single(r => r.LayerName == AdaKenari).Role.Should().Be(CadLayerRole.RegulationBoundary);
        vm.Canvas.Scene.Polylines.Single(p => p.LayerName == AdaKenari).Role.Should().Be(CadLayerRole.RegulationBoundary);
    }

    [Fact]
    public async Task Rol_Atamasindan_Sonra_Kamera_Gorunumu_Korunmali()
    {
        var (vm, _) = await ProjeliAlan();
        vm.Canvas.ZoomAt(400, 300, 3);
        vm.Canvas.Pan(25, -40);
        var (olcek, ofsetX, ofsetY) = (vm.Canvas.Camera.Scale, vm.Canvas.Camera.OffsetX, vm.Canvas.Camera.OffsetY);

        vm.Wizard.RolAta(Imar, CadLayerRole.Park);
        await vm.LastReapplyTask;

        vm.Canvas.Scene.Polylines.Single(p => p.Label == "101/1").Role.Should().Be(CadLayerRole.Park);
        vm.Canvas.Camera.Scale.Should().Be(olcek, "art arda atamada görünüm sıfırlanmamalı");
        vm.Canvas.Camera.OffsetX.Should().Be(ofsetX);
        vm.Canvas.Camera.OffsetY.Should().Be(ofsetY);
    }

    [Fact]
    public async Task Art_Arda_Rol_Atamasinda_Son_Esleme_Projeye_Ve_Tuvale_Yazilmali()
    {
        // Reviewer H1: hesaplar ters sırada bitse bile yalnız en son eşleme uygulanır (eskimiş sonuç atılır).
        var (vm, alan) = await ProjeliAlan();
        var gorevler = new List<Task>();
        foreach (var rol in new[] { CadLayerRole.Park, CadLayerRole.RoadDedication, CadLayerRole.Park, CadLayerRole.ZoningParcel, CadLayerRole.RoadDedication })
        {
            vm.Wizard.RolAta(Imar, rol);
            gorevler.Add(vm.LastReapplyTask);
        }

        await Task.WhenAll(gorevler);
        await vm.LastProjeTask;

        vm.Canvas.Scene.Polylines.Single(p => p.Label == "101/1").Role.Should().Be(CadLayerRole.RoadDedication);
        vm.Parcels.Single(r => r.Label == "101/1").Role.Should().Be("Yola Terk");
        alan.Yol.Belge.Parseller.Should().Contain(p => p.ParselId == "101/1" && p.Rol == ParselRolu.YolTerk);
    }

    [Fact]
    public async Task Liste_Disi_Katmana_Tuvalden_Atama_Yapilmamali_Ve_Vurgu_Kopmamali()
    {
        // Katman tablosunda olmayan (örtük) katmandaki çizgi: reviewer L1.
        var ortuk = new CadPolyline("0", string.Empty, null, [P(-10, 0), P(-10, 40)], false, null);
        var (vm, _) = await ProjeliAlan(() => Belge() with { Polylines = [KadastroKare, ImarKare, AcikCizgi, ortuk] });
        vm.Canvas.RolAtamaModu = CadLayerRole.Park;
        var (x, y) = vm.Canvas.Camera.WorldToScreen(P(-10, 20));

        vm.Canvas.SolTikla(x, y);
        await vm.LastReapplyTask;

        vm.StatusText.Should().Contain("'0'").And.Contain("rol atanmadı");
        vm.Canvas.IsaretliKatman.Should().BeNull();
        vm.Wizard.VurguluKatmanAdi.Should().BeNull();
    }

    [Fact]
    public async Task Esc_Komut_Cubugu_Acikken_Yalniz_Cubugu_Kapatmali_Sonra_Rol_Modundan_Cikmali()
    {
        var (vm, _) = await ProjeliAlan();
        vm.Canvas.RolAtamaModu = CadLayerRole.Park;
        vm.TogglePaletteCommand.Execute(null);
        vm.Palette.IsOpen.Should().BeTrue();

        vm.EscCommand.Execute(null);
        vm.Palette.IsOpen.Should().BeFalse();
        vm.Canvas.RolAtamaModu.Should().Be(CadLayerRole.Park, "ilk Esc yalnız komut çubuğunu kapatır");

        vm.EscCommand.Execute(null);
        vm.Canvas.RolAtamaModu.Should().BeNull();
    }

    [Fact]
    public async Task Eslemeyi_Uygula_Kamera_Gorunumunu_Korumali_Ilk_Ice_Aktarma_Sigdirmali()
    {
        var (vm, _) = await ProjeliAlan();
        var sigdirilmis = vm.Canvas.Camera.Scale;
        vm.Canvas.ZoomAt(400, 300, 2);
        var olcek = vm.Canvas.Camera.Scale;
        olcek.Should().NotBe(sigdirilmis);

        await vm.EslemeyiUygulaCommand.ExecuteAsync(null);
        vm.Canvas.Camera.Scale.Should().Be(olcek, "yeniden uygulama görünümü sıfırlamaz (P7.3-A davranış değişikliği)");

        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        vm.Canvas.Camera.Scale.Should().Be(sigdirilmis, "yeni içe aktarma tümünü sığdırır");
    }

    [Fact]
    public async Task Listeden_Hedeflenen_Katman_Tuvalde_Isaretli_Olmali()
    {
        var (vm, _) = await ProjeliAlan();

        vm.Wizard.LayerRoles.Single(r => r.LayerName == Kadastro).VurgulaCommand.Execute(null);
        vm.Canvas.IsaretliKatman.Should().Be(Kadastro);

        vm.Wizard.LayerRoles.Single(r => r.LayerName == Kadastro).VurgulaCommand.Execute(null);
        vm.Canvas.IsaretliKatman.Should().BeNull();
    }

    [Fact]
    public async Task Birinci_Adim_Disinda_Tuvalden_Rol_Atama_Kapali_Olmali()
    {
        var (vm, _) = await ProjeliAlan();
        vm.Canvas.RolAtamaModu = CadLayerRole.Park;

        vm.Wizard.CurrentIndex = 1;

        vm.Canvas.RolAtamaEtkin.Should().BeFalse();
        vm.Canvas.RolAtamaModu.Should().BeNull();

        vm.Wizard.CurrentIndex = 0;
        vm.Canvas.RolAtamaEtkin.Should().BeTrue();
    }

    [Fact]
    public async Task Proje_Temizlenince_Rol_Atama_Kapanmali_Ve_Vurgu_Silinmeli()
    {
        var (vm, _) = await ProjeliAlan();
        vm.Wizard.LayerRoles[0].VurgulaCommand.Execute(null);
        vm.Canvas.RolAtamaModu = CadLayerRole.Park;

        await vm.ProjeyiTemizleCommand.ExecuteAsync(null);

        vm.Canvas.RolAtamaEtkin.Should().BeFalse("katman yokken atama yapılamaz");
        vm.Canvas.RolAtamaModu.Should().BeNull();
        vm.Canvas.IsaretliKatman.Should().BeNull();
        vm.Wizard.VurguluKatmanAdi.Should().BeNull();
    }

    // ============================ 6. Görünüm (headless XAML) ============================

    private static MainWindow Ac(MainWindowViewModel vm)
    {
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    [AvaloniaFact]
    public async Task Gorunum_Rol_Ciplerini_Ve_Katman_Satiri_Hedef_Dugmelerini_Gostermeli()
    {
        var (vm, _) = await ProjeliAlan();
        var w = Ac(vm);

        var cipler = w.FindControl<ItemsControl>("RolCipListesi") ?? throw new InvalidOperationException("RolCipListesi yok");
        cipler.GetVisualDescendants().OfType<ToggleButton>().Should().HaveCount(Enum.GetValues<CadLayerRole>().Length);

        var liste = w.FindControl<ItemsControl>("KatmanRolListesi")!;
        liste.GetVisualDescendants().OfType<Button>().Count(b => Equals(b.Content, "🎯")).Should().Be(vm.Wizard.LayerRoles.Count);

        cipler.GetVisualDescendants().OfType<ToggleButton>().First(b => (b.DataContext as RolCipi)?.Rol == CadLayerRole.Park)
            .Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        vm.Canvas.RolAtamaModu.Should().Be(CadLayerRole.Park);
        w.FindControl<Border>("RolAtamaHud")!.IsVisible.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task Gorunum_Tuvalde_Esc_Rol_Atama_Modundan_Cikarmali()
    {
        var (vm, _) = await ProjeliAlan();
        var w = Ac(vm);
        vm.Canvas.RolAtamaModu = CadLayerRole.Park;
        var tuval = w.FindControl<CadCanvasControl>("Tuval")!;
        tuval.Focus();
        Dispatcher.UIThread.RunJobs();

        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        vm.Canvas.RolAtamaModu.Should().BeNull();
    }
}

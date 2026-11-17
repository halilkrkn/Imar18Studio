using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Presentation.Avalonia.ViewModels;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>P4.3 5 adimli sihirbaz, Ctrl+K kural tabanli komut cubugu ve ana pencere ice aktarma akisi.</summary>
public sealed class WizardPaletteMainTests
{
    private static WizardViewModel GecerliSihirbaz()
    {
        var w = new WizardViewModel();
        w.LoadLayers([new CadLayer(0, "TK_PARSEL_GİREN"), new CadLayer(1, "@SM"), new CadLayer(2, "YAZI")]);
        w.Datum = GeodetikDatum.Itrf96;
        w.OrtaMeridyen = 30;
        w.EpokText = "2005.00";
        w.OlcekPaydasi = 1000;
        w.TecvizOlcekPaydasi = 1000;
        w.TecvizUretimYontemi = "Sayısallaştırma";
        w.TecvizKoordinatKalitesi = KoordinatKalitesi.ArsivOnayli;
        w.TecvizSiniriText = "21,02";
        w.TecvizKaynakNotu = "Mühendis girdisi";
        w.LayerRoles.Single(r => r.LayerName == "@SM").Role = CadLayerRole.ZoningParcel;
        return w;
    }

    [Fact]
    public void Sihirbaz_Bes_Adimi_Roadmap_Sirasiyla_Sunmali()
    {
        new WizardViewModel().Steps.Should().Equal("1. Girdi & Sınır", "2. DOP & Özet", "3. Ada Bölümleme", "4. Tahsis & Hisse", "5. Cetvel & Çıktı");
    }

    [Fact]
    public void Sihirbaz_Katman_Rollerini_Standart_Esleme_Ile_Onerip_Muhendis_Secimini_Haritaya_Islemeli()
    {
        var w = GecerliSihirbaz();

        w.LayerRoles.Single(r => r.LayerName == "TK_PARSEL_GİREN").Role.Should().Be(CadLayerRole.CadastralParcel);
        w.LayerRoles.Single(r => r.LayerName == "YAZI").Role.Should().Be(CadLayerRole.Unassigned);
        w.RoleMap.Resolve("@SM").Should().Be(CadLayerRole.ZoningParcel);
    }

    [Fact]
    public void Sihirbaz_Gecerli_Ilk_Adimda_CRS_Ve_TecvizGirdisi_Uretip_Ilerlemeli()
    {
        var w = GecerliSihirbaz();

        w.Validate().Should().BeTrue(string.Join("; ", w.Errors));
        w.Crs!.CrsId.Should().Be("ITRF96/TM3-30@2005.00");
        w.Tecviz!.TecvizSiniriM2.Should().Be(21.02m);
        w.NextCommand.CanExecute(null).Should().BeTrue();
        w.NextCommand.Execute(null);
        w.CurrentIndex.Should().Be(1);
        w.BackCommand.Execute(null);
        w.CurrentIndex.Should().Be(0);
    }

    [Fact]
    public void Sihirbaz_Gecersiz_CRS_Tecviz_Veya_Eksik_Rolde_Ilerlememeli_Ve_Turkce_Hata_Gostermeli()
    {
        var w = GecerliSihirbaz();
        w.OrtaMeridyen = 28;
        w.TecvizSiniriText = "abc";
        w.LayerRoles.Single(r => r.LayerName == "@SM").Role = CadLayerRole.Unassigned;

        w.Validate().Should().BeFalse();
        w.NextCommand.CanExecute(null).Should().BeFalse();
        w.Errors.Should().Contain(e => e.Contains("Koordinat sistemi", StringComparison.Ordinal));
        w.Errors.Should().Contain(e => e.Contains("Tecviz", StringComparison.Ordinal));
        w.Errors.Should().Contain(e => e.Contains("imar parseli", StringComparison.Ordinal));
        w.Errors.Should().NotContain(e => e.Contains("Invalid", StringComparison.Ordinal), "kullaniciya teknik mesaj gosterilmez");
    }

    [Fact]
    public void Sihirbaz_Girdi_Veya_Rol_Degisince_Dogrulamayi_Dusurmeli_Ve_Rol_Olayi_Yaymali()
    {
        var w = GecerliSihirbaz();
        var rolOlayi = 0;
        w.LayerRolesChanged += (_, _) => rolOlayi++;
        w.Validate().Should().BeTrue();

        w.OrtaMeridyen = 33;
        w.IsInputValid.Should().BeFalse();
        w.NextCommand.CanExecute(null).Should().BeFalse();

        w.Validate().Should().BeTrue();
        w.TecvizSiniriText = "30";
        w.IsInputValid.Should().BeFalse();

        w.Validate().Should().BeTrue();
        w.LayerRoles.Single(r => r.LayerName == "YAZI").Role = CadLayerRole.Park;
        w.IsInputValid.Should().BeFalse();
        rolOlayi.Should().Be(1);
    }

    [Fact]
    public void Sihirbaz_ED50_Icin_Bos_Epoku_Kabul_Etmeli()
    {
        var w = GecerliSihirbaz();
        w.Datum = GeodetikDatum.Ed50;
        w.EpokText = string.Empty;

        w.Validate().Should().BeTrue(string.Join("; ", w.Errors));
        w.Crs!.CrsId.Should().Be("ED50/TM3-30");
    }

    [Fact]
    public void Sihirbaz_Son_Adimdan_Ileri_Ilk_Adimdan_Geri_Gitmemeli()
    {
        var w = GecerliSihirbaz();
        w.Validate();
        for (var i = 0; i < 10; i++)
        {
            if (w.NextCommand.CanExecute(null))
            {
                w.NextCommand.Execute(null);
            }
        }

        w.CurrentIndex.Should().Be(4);
        w.NextCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void KomutCubugu_Turkce_Karakter_Duyarsiz_Arayip_Onek_Eslesmesini_One_Almali()
    {
        var p = new CommandPaletteViewModel();
        var calisan = new List<string>();
        p.Register(new PaletteCommand("fit", "Tümünü Sığdır", "zoom extents", () => calisan.Add("fit")));
        p.Register(new PaletteCommand("swipe0", "Kadastro Görünümü", "eski sinirlandirma", () => calisan.Add("k")));
        p.Register(new PaletteCommand("import", "CAD Dosyası Aç", "ncz dxf dwg", () => calisan.Add("i")));

        p.Query = "sigdir";
        p.Results.Select(r => r.Id).Should().Equal("fit");
        p.Query = "DWG";
        p.Results.Select(r => r.Id).Should().Equal("import");
        p.Query = "ka";
        p.Results.First().Id.Should().Be("swipe0", "baslik oneki anahtar kelimeden once gelir");
        p.Query = string.Empty;
        p.Results.Should().HaveCount(3);
    }

    [Fact]
    public void KomutCubugu_Ctrl_K_Ac_Kapa_Secim_Ve_Calistirma_Akisini_Desteklemeli()
    {
        var p = new CommandPaletteViewModel();
        var calisan = new List<string>();
        p.Register(new PaletteCommand("a", "Alfa", "", () => calisan.Add("a")));
        p.Register(new PaletteCommand("b", "Beta", "", () => calisan.Add("b")));
        p.Register(new PaletteCommand("c", "Gama", "", () => calisan.Add("c"), () => false));

        p.Toggle();
        p.IsOpen.Should().BeTrue();
        p.MoveSelection(1);
        p.ExecuteSelected();

        calisan.Should().Equal("b");
        p.IsOpen.Should().BeFalse();
        p.Query.Should().BeEmpty();

        p.Toggle();
        p.Query = "gama";
        p.ExecuteSelected();
        calisan.Should().Equal("b");
        p.IsOpen.Should().BeTrue("calistirilamayan komut cubugu kapatmaz");
    }

    [Fact]
    public void KomutCubugu_Secim_Sinirlarda_Kalmali_Ve_Bos_Sonucta_Calistirmamali()
    {
        var p = new CommandPaletteViewModel();
        p.Register(new PaletteCommand("a", "Alfa", "", () => { }));

        p.MoveSelection(-5);
        p.SelectedIndex.Should().Be(0);
        p.MoveSelection(5);
        p.SelectedIndex.Should().Be(0);
        p.Query = "zzz";
        p.SelectedIndex.Should().Be(-1);
        ((Action)p.ExecuteSelected).Should().NotThrow();
    }

    private sealed class SahteAdaptor(CadFormat format, Func<Result<CadLayerDocument>> sonuc) : ICadFormatAdapter
    {
        public CadFormat Format => format;

        public bool SupportsExtension(string extension) =>
            string.Equals(extension.TrimStart('.'), format.ToString(), StringComparison.OrdinalIgnoreCase);

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) => sonuc();
    }

    private static GeodeticPoint P(decimal x, decimal y) => new(X: 4_000_000m + x, Y: 500_000m + y);

    private static CadLayerDocument DxfBelgesi() => new(
        CadFormat.Dxf,
        [new CadLayer(0, "TK_PARSEL_OLUŞAN"), new CadLayer(1, "YAZI")],
        [new CadPolyline("TK_PARSEL_OLUŞAN", "", null, [P(0, 0), P(20, 0), P(20, 20), P(0, 20), P(0, 0)], true, null)],
        [new CadText("YAZI", "101/1", P(10, 10))],
        0, 0, 0,
        [ErrorCatalog.Get(ErrorCodes.GirdZKoduIndirgendi)]);

    [Fact]
    public async Task AnaPencere_DXF_Iceaktariminda_Etiketleri_Eslestirip_Sahneyi_Ve_Katmanlari_Yuklemeli()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(CadFormat.Dxf, () => Result<CadLayerDocument>.Success(DxfBelgesi()))]));
        vm.Canvas.SetViewport(500, 500);

        await vm.ImportAsync(new MemoryStream([1, 2, 3]), ".dxf");

        vm.IsBusy.Should().BeFalse();
        vm.ProgressPercent.Should().Be(100);
        vm.Canvas.Scene.Polylines.Should().ContainSingle().Which.Label.Should().Be("101/1");
        vm.Parcels.Should().ContainSingle(r => r.Label == "101/1" && r.AreaM2 == 400.00m);
        vm.Wizard.LayerRoles.Select(r => r.LayerName).Should().Equal("TK_PARSEL_OLUŞAN", "YAZI");
        vm.Warnings.Should().Contain(w => w.Contains("Z", StringComparison.Ordinal));
        vm.StatusText.Should().Contain("1 poligon");
    }

    [Fact]
    public async Task AnaPencere_Ust_Uste_Kadastro_Ve_Imar_Poligonlarini_Rol_Bazli_Etiketlemeli_Ve_Rol_Degisince_Yeniden_Eslestirmeli()
    {
        CadLayerDocument belge = new(
            CadFormat.Dxf,
            [new CadLayer(0, "TK_PARSEL_GİREN"), new CadLayer(1, "@SM"), new CadLayer(2, "YAZI")],
            [
                new CadPolyline("TK_PARSEL_GİREN", "", null, [P(0, 0), P(40, 0), P(40, 40), P(0, 40), P(0, 0)], true, null),
                new CadPolyline("@SM", "", null, [P(0, 0), P(20, 0), P(20, 20), P(0, 20), P(0, 0)], true, null),
            ],
            [new CadText("YAZI", "101/1", P(10, 10)), new CadText("YAZI", "902/1", P(30, 30))],
            0, 0, 0, []);
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(CadFormat.Dxf, () => Result<CadLayerDocument>.Success(belge))]));

        await vm.ImportAsync(new MemoryStream([1]), "dxf");
        vm.Canvas.Scene.Polylines[0].Label.Should().BeEmpty("kadastro poligonu iki farkli yazi icerir");

        vm.Wizard.LayerRoles.Single(r => r.LayerName == "@SM").Role = CadLayerRole.ZoningParcel;
        await vm.LastReapplyTask;

        vm.Canvas.Scene.Polylines[1].Label.Should().Be("101/1");
        vm.Canvas.Scene.Polylines[0].Label.Should().BeEmpty();
    }

    [Fact]
    public async Task AnaPencere_Hatali_Dosyada_Kullanici_Mesaji_Gostermeli_Ve_Mesgul_Kalmamali()
    {
        var hata = ErrorCatalog.Get(ErrorCodes.GirdNczOkunamadi);
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(CadFormat.Ncz, () => Result<CadLayerDocument>.Failure(hata))]));

        await vm.ImportAsync(new MemoryStream([1]), "ncz");

        vm.IsBusy.Should().BeFalse();
        vm.StatusText.Should().Contain(hata.UserMessage);
        vm.StatusText.Should().NotContain("block parse failure", "teknik mesaj kullaniciya gosterilmez");
    }

    [Fact]
    public async Task AnaPencere_Iptal_Komutu_Uzun_Ice_Aktarmayi_Durdurmali()
    {
        using var serbest = new ManualResetEventSlim(false);
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(CadFormat.Dwg, () =>
        {
            serbest.Wait(TimeSpan.FromSeconds(10));
            return Result<CadLayerDocument>.Success(DxfBelgesi());
        })]));

        var gorev = vm.ImportAsync(new MemoryStream([1]), "dwg");
        vm.IsBusy.Should().BeTrue();
        vm.CancelImportCommand.CanExecute(null).Should().BeTrue();
        vm.CancelImportCommand.Execute(null);
        await gorev;

        vm.IsBusy.Should().BeFalse();
        vm.StatusText.Should().Contain("iptal");
        serbest.Set();
    }

    [Fact]
    public void AnaPencere_Komut_Cubugu_Temel_Komutlari_Kayitli_Olmali()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(CadFormat.Ncz, () => Result<CadLayerDocument>.Success(DxfBelgesi()))]));

        vm.Palette.Results.Select(r => r.Id).Should().Contain(["openFile", "fitAll", "swipeCadastre", "swipeZoning", "toggleCutHandle", "nextStep", "previousStep", "cancelImport"]);
        vm.Palette.Query = "imar";
        vm.Palette.ExecuteSelected();
        vm.Canvas.SwipePercent.Should().Be(100);
        vm.Palette.Query = "kadastro";
        vm.Palette.ExecuteSelected();
        vm.Canvas.SwipePercent.Should().Be(0);
    }
}

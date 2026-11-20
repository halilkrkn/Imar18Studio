using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.Gorunum;
using Imar18.Presentation.Avalonia.ViewModels;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// SAHA-010 / SORU-054 arayüz zinciri: 1. adımda İmar Adası / İmar Parseli / Yoksay rol ataması → proje geometrisi → 3. adım
/// R2 otomatik değerinin anında güncellenmesi ve "Uyarı &amp; Denetim" sekmesindeki Türkçe çift sayım uyarısı. Sentetik referans:
/// ada 20 × 50 = 1000.00 m²; parseller A1 12 × 50 = 600.00 + A2 8 × 50 = 400.00 = 1000.00 m² (R2 hiçbir durumda 2000 olmamalı).
/// </summary>
public sealed class Saha010ImarAdasiR2Tests
{
    private const string Kadastro = "TK_PARSEL_GİREN";
    private const string ImarParseli = "TK_PARSEL_OLUŞAN";
    private const string Yol = "TK_PARSEL_YOLATERK";
    private const string ImarAdasi = "IMAR_ADASI";

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

    private static CadPolyline Serit(string katman, string etiket, decimal y1, decimal y2)
    {
        static GeodeticPoint P(decimal x, decimal y) => new(OrnekProje.X0 + x, OrnekProje.Y0 + y);
        return new CadPolyline(katman, etiket, null, [P(0m, y1), P(0m, y2), P(50m, y2), P(50m, y1), P(0m, y1)], true, null);
    }

    private static CadLayerDocument Belge() => new(
        CadFormat.Ncz,
        [new CadLayer(0, Kadastro), new CadLayer(1, ImarAdasi), new CadLayer(2, ImarParseli), new CadLayer(3, Yol)],
        [Serit(Kadastro, "K1", 0m, 25m), Serit(ImarAdasi, "ADA-1", 0m, 20m), Serit(ImarParseli, "A1", 0m, 12m), Serit(ImarParseli, "A2", 12m, 20m), Serit(Yol, "Y1", 20m, 25m)],
        [], 0, 0, 0, []);

    private static async Task<(MainWindowViewModel Vm, ProjeCalismaAlani Alan)> ProjeliAlan()
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik SAHA-010", zaman)).Value;
        var t = OrnekProje.Topoloji;
        var alan = new ProjeCalismaAlani(yol, new ApprovalService(yol, zaman, t),
            new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru), new SahteUretici(CiktiTuru.Cetveller), new SahteUretici(CiktiTuru.CksDopOzeti)], new BellekYazici(), t),
            OrnekProje.Muhendis);
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(Belge)]), alan);
        await vm.LastProjeTask;
        vm.Canvas.SetViewport(800, 600);
        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        await vm.LastProjeTask;
        return (vm, alan);
    }

    private static async Task RolAtaAsync(MainWindowViewModel vm, string katman, CadLayerRole rol)
    {
        vm.Wizard.RolAta(katman, rol).Should().BeTrue();
        await vm.LastReapplyTask;
        await vm.LastProjeTask;
    }

    [Theory]
    [InlineData(CadLayerRole.ZoningBlock, "İmar Adası")]
    [InlineData(CadLayerRole.Ignored, "Yoksay")]
    public void TurkceAdlar_Yeni_Roller_Turkce_Gosterilmeli(CadLayerRole rol, string beklenen)
    {
        TurkceAdlar.Ad(rol).Should().Be(beklenen);
    }

    [Fact]
    public void Rol_Secenekleri_Imar_Adasi_Ve_Yoksay_Icermeli_Ve_Ada_Ada_Kenari_Rengiyle_Cizilmeli()
    {
        new WizardViewModel().Roller.Should().Contain([CadLayerRole.ZoningBlock, CadLayerRole.Ignored]);
        foreach (var acik in new[] { false, true })
        {
            CadSceneRenderer.RolRengi(CadLayerRole.ZoningBlock, acik).Should().Be(CadSceneRenderer.RolRengi(CadLayerRole.BlockEdge, acik));
            CadSceneRenderer.RolRengi(CadLayerRole.Ignored, acik).Should().Be(CadSceneRenderer.RolRengi(CadLayerRole.Unassigned, acik));
        }
    }

    [Fact]
    public async Task Ada_Ve_Parsel_Birlikteyken_R2_1000_Olmali_Ve_Uyari_Denetim_Sekmesinde_Turkce_Uyarmali()
    {
        var (vm, alan) = await ProjeliAlan();

        await RolAtaAsync(vm, ImarAdasi, CadLayerRole.ZoningBlock);

        vm.Hesap!.R2Otomatik.Should().Be(1000.00m, "R2 yalnız İmar Parseli alanlarından (600 + 400); 2000 değil");
        vm.Warnings.Should().Contain(ProjeGeometriEslemesi.AdaVeParselBirlikteUyarisi);
        vm.DenetimUyariSayisi.Should().BeGreaterThanOrEqualTo(1);
        alan.Yol.Belge.Parseller.Should().NotContain(p => p.ParselId == "ADA-1");
        alan.Yol.Denetim.Should().Contain(k => k.Islem == "ParselGeometrileriAyarla" && k.Ayrinti == "parselSayisi=4;ciftSayimHaric=1",
            "dışlanan poligon sayısı denetim izine yalnız sayı olarak yazılır");
        vm.Canvas.Scene.Polylines.Should().Contain(p => p.Label == "ADA-1" && p.Role == CadLayerRole.ZoningBlock, "ada tuvalde görsel referans olarak kalır");
    }

    [Fact]
    public async Task Imar_Parseli_Katmani_Yoksay_Rolune_Alininca_R2_Anlik_Adadan_Guncellenmeli()
    {
        var (vm, alan) = await ProjeliAlan();
        await RolAtaAsync(vm, ImarAdasi, CadLayerRole.ZoningBlock);

        await RolAtaAsync(vm, ImarParseli, CadLayerRole.Ignored);

        vm.Hesap!.R2Otomatik.Should().Be(1000.00m, "İmar Parseli yokken R2 ada alanından");
        vm.Warnings.Should().NotContain(ProjeGeometriEslemesi.AdaVeParselBirlikteUyarisi);
        alan.Yol.Belge.Parseller.Where(p => p.Rol == ParselRolu.Imar).Select(p => p.ParselId).Should().Equal("ADA-1");
        vm.Parcels.Should().Contain(r => r.Label == "A1" && r.Role == "Yoksay");
    }

    [Fact]
    public async Task Ada_Yanlislikla_Imar_Parseli_Rolune_Atansa_Da_R2_Sismemeli()
    {
        var (vm, _) = await ProjeliAlan();

        await RolAtaAsync(vm, ImarAdasi, CadLayerRole.ZoningParcel);

        vm.Hesap!.R2Otomatik.Should().Be(1000.00m);
        vm.Warnings.Should().Contain(w => w.Contains("ADA-1", StringComparison.Ordinal) && w.Contains("kapsayan", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Yalniz_Imar_Adasi_Atanmis_Projede_Sihirbaz_Imar_Rolu_Hatasi_Vermemeli()
    {
        var (vm, _) = await ProjeliAlan();
        await RolAtaAsync(vm, ImarAdasi, CadLayerRole.ZoningBlock);
        await RolAtaAsync(vm, ImarParseli, CadLayerRole.Ignored);

        vm.Wizard.Validate();

        vm.Wizard.Errors.Should().NotContain(WizardViewModel.ImarRoluHatasi);
    }

    [Fact]
    public async Task Kayitli_DOP_R2_Guncel_Otomatik_R2den_Farkliysa_3_Adimda_Uyarmali()
    {
        var (vm, alan) = await ProjeliAlan();
        await RolAtaAsync(vm, ImarAdasi, CadLayerRole.ZoningBlock);
        // Düzeltme öncesi (çift sayımla) uygulanmış eski DOP girdisi.
        (await alan.Yol.GonderAsync(new DopGirdisiAyarlaKomutu(DopGirdisi.Olustur(2500.00m, 2000.00m).Value), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        await vm.Hesap!.YenileAsync();

        vm.Hesap.DopMesajlari.Should().Contain(m => m.Contains("2000.00", StringComparison.Ordinal) && m.Contains("1000.00", StringComparison.Ordinal)
            && m.Contains("DOP Uygula", StringComparison.Ordinal));
    }
}

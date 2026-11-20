using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Presentation.Avalonia.Gorunum;
using Imar18.Presentation.Avalonia.ViewModels;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// SAHA-013 (saha ekran görüntüsü): <c>.NCZ</c> katmanları <c>02_PARSEL_ALN_YENI</c>, <c>YOL</c>, <c>TK_OLUSAN</c> (2 poligon) ve
/// <c>İMAR</c> (1 poligon) olarak geldiğinde 1. adımda roller otomatik "Kadastro Parseli", "Yola Terk", "TK_Oluşan" ve "Yoksay"
/// gelmeli; mühendis hiçbir rol değiştirmeden "Girdileri Doğrula" 0 hatayla geçmeli ve İMAR poligonu DOP (R2) hesabına girmemeli.
/// Sentetik bağımsız referans (Kuzey 0..50 m şeritleri): K1 Doğu 0–25 m = 1250.00 m²; A1 0–12 m = 600.00 m²; A2 12–20 m =
/// 400.00 m²; Y1 20–25 m = 250.00 m²; İMAR-1 10–30 m = 1000.00 m² (sayılsaydı R2 = 2000.00). Beklenen: R1 = 1250.00,
/// R2 = 1000.00, DOPO = 250 / 1250 = 0.2000000, kamuya ayrılan 250.00 m².
/// </summary>
public sealed class Saha013TkOlusanEslemeTests
{
    private const string Kadastro = "02_PARSEL_ALN_YENI";
    private const string Yol = "YOL";
    private const string TkOlusan = "TK_OLUSAN";
    private const string Imar = "İMAR";

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

    /// <summary>Saha ekran görüntüsündeki katman sırası ve poligon sayıları (geometri sentetik).</summary>
    private static CadLayerDocument Belge() => new(
        CadFormat.Ncz,
        [new CadLayer(0, Kadastro), new CadLayer(1, Yol), new CadLayer(2, TkOlusan), new CadLayer(3, Imar)],
        [
            Serit(Kadastro, "K1", 0m, 25m),
            Serit(Yol, "Y1", 20m, 25m),
            Serit(TkOlusan, "A1", 0m, 12m),
            Serit(TkOlusan, "A2", 12m, 20m),
            Serit(Imar, "IMAR-1", 10m, 30m),
        ],
        [], 0, 0, 0, []);

    private static async Task<(MainWindowViewModel Vm, ProjeCalismaAlani Alan)> ProjeliAlan()
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik SAHA-013", zaman)).Value;
        var t = OrnekProje.Topoloji;
        var alan = new ProjeCalismaAlani(yol, new ApprovalService(yol, zaman, t),
            new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru)], new BellekYazici(), t),
            OrnekProje.Muhendis);
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(Belge)]), alan);
        await vm.LastProjeTask;
        vm.Canvas.SetViewport(800, 600);
        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        await vm.LastProjeTask;
        vm.Wizard.TecvizUretimYontemi = "Sayısallaştırma";
        vm.Wizard.TecvizSiniriText = "12.50";
        vm.Wizard.TecvizKaynakNotu = "Sentetik rapor";
        return (vm, alan);
    }

    [Fact]
    public async Task Saha_Katmanlari_Yuklenince_Roller_Otomatik_Eslenmeli_Ve_Turkce_Adla_Gorunmeli()
    {
        var (vm, _) = await ProjeliAlan();

        vm.Wizard.LayerRoles.Select(r => (r.LayerName, r.Role)).Should().Equal(
            (Kadastro, CadLayerRole.CadastralParcel),
            (Yol, CadLayerRole.RoadDedication),
            (TkOlusan, CadLayerRole.ZoningParcel),
            (Imar, CadLayerRole.Ignored));
        vm.Wizard.LayerRoles.Select(r => TurkceAdlar.Ad(r.Role)).Should().Equal("Kadastro Parseli", "Yola Terk", "TK_Oluşan", "Yoksay");
        vm.Wizard.LayerRoles.Should().NotContain(r => r.LayerName == TkOlusan && r.Role == CadLayerRole.CadastralParcel, "TK_OLUSAN asla Kadastro Parseli olmamalı");
    }

    [Fact]
    public async Task Otomatik_Eslemeyle_Girdileri_Dogrula_Rol_Degistirmeden_Sifir_Hatayla_Gecmeli()
    {
        var (vm, _) = await ProjeliAlan();

        vm.Wizard.Validate().Should().BeTrue();

        vm.Wizard.IsInputValid.Should().BeTrue();
        vm.Wizard.DogrulamaBasarili.Should().BeTrue();
        vm.Wizard.HataSayisi.Should().Be(0);
        vm.Wizard.KatmanRolHatasi.Should().BeNull("kırmızı 'TK_Oluşan veya İmar Adası' uyarısı çıkmamalı");
        vm.Wizard.Errors.Should().NotContain(WizardViewModel.ImarRoluHatasi).And.NotContain(WizardViewModel.KadastroRoluHatasi);
        vm.Wizard.NextCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Otomatik_Eslemeyle_IMAR_Katmani_DOP_R2_Hesabina_Girmemeli_Ve_DOPO_Dogru_Olmali()
    {
        var (vm, alan) = await ProjeliAlan();
        vm.Wizard.Validate().Should().BeTrue();
        await vm.LastProjeTask;
        vm.Wizard.NextCommand.Execute(null);

        var hesap = vm.Hesap!;
        await hesap.YenileAsync();
        hesap.TecvizSatirlari.Should().ContainSingle().Which.ParselId.Should().Be("K1");
        hesap.TecvizSatirlari[0].TapuAlaniText = "1250.00";
        await hesap.TecvizUygulaCommand.ExecuteAsync(null);
        await hesap.DopUygulaCommand.ExecuteAsync(null);

        alan.Yol.Belge.Parseller.Should().NotContain(p => p.ParselId == "IMAR-1", "İMAR Yoksay; projeye ve DOP'a girmez");
        alan.Yol.Belge.Parseller.Where(p => p.Rol == ParselRolu.Imar).Select(p => p.ParselId).Should().Equal("A1", "A2");
        hesap.R1.Should().Be(1250.00m);
        hesap.R2Otomatik.Should().Be(1000.00m, "R2 yalnız TK_OLUSAN alanlarından (600 + 400); İMAR sayılsaydı 2000.00");
        alan.Yol.Belge.Dop!.R2.Should().Be(1000.00m);
        hesap.DopOzeti.Should().Contain("DOPO 0.2000000").And.Contain("kamuya ayrılan 250.00");
        vm.Warnings.Should().NotContain(ProjeGeometriEslemesi.AdaVeParselBirlikteUyarisi);
        vm.Parcels.Should().Contain(r => r.Label == "IMAR-1" && r.Role == "Yoksay", "İMAR tuvalde görünür kalır, hesaba girmez");
        vm.Parcels.Should().Contain(r => r.Label == "A1" && r.Role == "TK_Oluşan");
    }

    [Fact]
    public async Task Tuval_Rol_Cipleri_TK_Olusan_Adini_Gostermeli()
    {
        var (vm, _) = await ProjeliAlan();

        vm.Canvas.RolCipleri.Single(c => c.Rol == CadLayerRole.ZoningParcel).Ad.Should().Be("TK_Oluşan");
        vm.Canvas.RolCipleri.Select(c => c.Ad).Should().NotContain("İmar Parseli");
    }

    [Fact]
    public void Imar_Rolu_Hata_Metni_Ve_Rol_Adi_TK_Olusan_Olmali()
    {
        TurkceAdlar.Ad(CadLayerRole.ZoningParcel).Should().Be("TK_Oluşan");
        WizardViewModel.ImarRoluHatasi.Should().Be("En az bir katman 'TK_Oluşan' veya 'İmar Adası' rolüne atanmalı.");
    }
}

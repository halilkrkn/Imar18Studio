using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Presentation.Avalonia.ViewModels;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// SAHA-012: mühendis 1. adımda <c>TK_PARSEL_OLUŞAN</c> katmanını "Yoksay" yapıp yalnız "İmar Adası" katmanıyla devam ettiğinde
/// "Girdileri Doğrula" geçmeli; 2.–3. adımda R2 ve DOPO yalnız ada alanından gelmeli. Sentetik bağımsız referans (50 m genişlik):
/// kadastro K1 0–25 m = 1250.00 m²; İmar Adası ADA-1 0–20 m = 1000.00 m²; yoksayılan imar parseli A1 0–12 m = 600.00 m²;
/// yol terk Y1 20–25 m = 250.00 m². Beklenen: R1 = 1250.00, R2 = 1000.00 (A1 sayılsaydı 600.00), DOPO = 250 / 1250 = 0.2000000,
/// kamuya ayrılan 250.00 m².
/// </summary>
public sealed class Saha012ImarAdasiDogrulamaTests
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
        [Serit(Kadastro, "K1", 0m, 25m), Serit(ImarAdasi, "ADA-1", 0m, 20m), Serit(ImarParseli, "A1", 0m, 12m), Serit(Yol, "Y1", 20m, 25m)],
        [], 0, 0, 0, []);

    private static async Task<(MainWindowViewModel Vm, ProjeCalismaAlani Alan)> ProjeliAlan()
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik SAHA-012", zaman)).Value;
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

    private static async Task RolAtaAsync(MainWindowViewModel vm, string katman, CadLayerRole rol)
    {
        vm.Wizard.RolAta(katman, rol).Should().BeTrue();
        await vm.LastReapplyTask;
        await vm.LastProjeTask;
    }

    [Fact]
    public async Task Imar_Parseli_Yoksay_Ve_Imar_Adasi_Atanmisken_Girdileri_Dogrula_Gecmeli_Ve_Ileri_Adim_Acilmali()
    {
        var (vm, _) = await ProjeliAlan();
        await RolAtaAsync(vm, ImarParseli, CadLayerRole.Ignored);
        await RolAtaAsync(vm, ImarAdasi, CadLayerRole.ZoningBlock);

        vm.Wizard.Validate().Should().BeTrue();

        vm.Wizard.IsInputValid.Should().BeTrue();
        vm.Wizard.DogrulamaBasarili.Should().BeTrue();
        vm.Wizard.HataSayisi.Should().Be(0);
        vm.Wizard.KatmanRolHatasi.Should().BeNull();
        vm.Wizard.NextCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Imar_Parseli_Yoksay_Iken_R2_Ve_DOPO_Yalniz_Imar_Adasi_Alanindan_Hesaplanmali()
    {
        var (vm, alan) = await ProjeliAlan();
        await RolAtaAsync(vm, ImarParseli, CadLayerRole.Ignored);
        await RolAtaAsync(vm, ImarAdasi, CadLayerRole.ZoningBlock);
        vm.Wizard.Validate().Should().BeTrue();
        await vm.LastProjeTask;
        vm.Wizard.NextCommand.Execute(null);

        var hesap = vm.Hesap!;
        await hesap.YenileAsync();
        hesap.TecvizSatirlari.Should().ContainSingle().Which.ParselId.Should().Be("K1");
        hesap.TecvizSatirlari[0].TapuAlaniText = "1250.00";
        await hesap.TecvizUygulaCommand.ExecuteAsync(null);
        await hesap.DopUygulaCommand.ExecuteAsync(null);

        alan.Yol.Belge.Parseller.Where(p => p.Rol == ParselRolu.Imar).Select(p => p.ParselId).Should().Equal(["ADA-1"], "TK_PARSEL_OLUŞAN (A1) Yoksay; hesaba katılmaz");
        hesap.R1.Should().Be(1250.00m);
        hesap.R2Otomatik.Should().Be(1000.00m, "R2 yalnız İmar Adası alanından (A1 sayılsaydı 600.00)");
        alan.Yol.Belge.Dop!.R2.Should().Be(1000.00m);
        hesap.DopOzeti.Should().Contain("DOPO 0.2000000").And.Contain("kamuya ayrılan 250.00");
    }

    [Fact]
    public async Task Imar_Rolu_Yokken_Hata_Yeni_Metinle_Gosterilmeli_Ada_Ataninca_Bayat_Hata_Hemen_Kalkmali()
    {
        var (vm, _) = await ProjeliAlan();
        await RolAtaAsync(vm, ImarParseli, CadLayerRole.Ignored);

        vm.Wizard.Validate().Should().BeFalse();
        vm.Wizard.Errors.Should().Contain(WizardViewModel.ImarRoluHatasi);
        WizardViewModel.ImarRoluHatasi.Should().Be("En az bir katman 'İmar Parseli (Oluşan)' veya 'İmar Adası' rolüne atanmalı.");

        await RolAtaAsync(vm, ImarAdasi, CadLayerRole.ZoningBlock);

        vm.Wizard.Errors.Should().NotContain(WizardViewModel.ImarRoluHatasi, "rol değişince doğrulama kartındaki eski katman hatası kalmamalı");
        vm.Wizard.KatmanRolHatasi.Should().BeNull();
        vm.Wizard.HataSayisi.Should().Be(0);
        vm.Wizard.Validate().Should().BeTrue();
    }

    [Fact]
    public async Task Rol_Degisince_Hala_Eksik_Imar_Rolu_Dogrulama_Kartinda_Kalmali()
    {
        var (vm, _) = await ProjeliAlan();
        await RolAtaAsync(vm, ImarParseli, CadLayerRole.Ignored);
        vm.Wizard.Validate().Should().BeFalse();

        await RolAtaAsync(vm, ImarAdasi, CadLayerRole.Park);

        vm.Wizard.Errors.Should().ContainSingle(e => e == WizardViewModel.ImarRoluHatasi);
        vm.Wizard.KatmanRolHatasi.Should().Be(WizardViewModel.ImarRoluHatasi);
        vm.Wizard.IsInputValid.Should().BeFalse();
    }
}

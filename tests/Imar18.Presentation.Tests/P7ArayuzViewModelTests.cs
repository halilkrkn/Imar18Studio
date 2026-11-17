using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Presentation.Avalonia.ViewModels;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// P7 UI revizyonu ViewModel davranışları: tıklanabilir sihirbaz adımları, bağlama göre sağ panel, Koyu/Açık Pafta teması,
/// açılır-kapanır paneller, Türkçe tablo değerleri, kısa ve kopyalanabilir RevisionId rozeti ve onay durumu metni.
/// </summary>
public sealed class P7ArayuzViewModelTests
{
    private sealed class SahteUretici(CiktiTuru tur) : ICiktiUretici
    {
        public CiktiTuru Tur => tur;

        public string Uzanti => ".bin";

        public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default) => Result<byte[]>.Success([1, 2, 3]);
    }

    private sealed class BellekYazici : ICiktiYazici
    {
        public Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SilAsync(string yol, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SahteAdaptor(CadLayerDocument belge) : ICadFormatAdapter
    {
        public CadFormat Format => belge.Format;

        public bool SupportsExtension(string extension) => true;

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) => Result<CadLayerDocument>.Success(belge);
    }

    private static ProjeCalismaAlani Alan(ProjeKomutYolu yol, SabitZaman zaman) => new(
        yol,
        new ApprovalService(yol, zaman, OrnekProje.Topoloji),
        new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru), new SahteUretici(CiktiTuru.Cetveller), new SahteUretici(CiktiTuru.CksDopOzeti)], new BellekYazici(), OrnekProje.Topoloji),
        OrnekProje.Muhendis);

    private static GeodeticPoint P(decimal x, decimal y) => new(X: 4_000_000m + x, Y: 500_000m + y);

    private static CadLayerDocument NczBelgesi() => new(
        CadFormat.Ncz,
        [new CadLayer(0, "TK_PARSEL_GİREN"), new CadLayer(1, "TK_PARSEL_OLUŞAN")],
        [
            new CadPolyline("TK_PARSEL_GİREN", "436/14", null, [P(0, 0), P(20, 0), P(20, 20), P(0, 20), P(0, 0)], true, null),
            new CadPolyline("TK_PARSEL_OLUŞAN", "101/1", null, [P(0, 0), P(10, 0), P(10, 10), P(0, 10), P(0, 0)], true, null),
        ],
        [], 0, 0, 0, []);

    private static WizardViewModel GecerliSihirbaz()
    {
        var w = new WizardViewModel();
        w.LoadLayers([new CadLayer(0, "TK_PARSEL_GİREN"), new CadLayer(1, "TK_PARSEL_OLUŞAN")]);
        w.TecvizUretimYontemi = "Sayısallaştırma";
        w.TecvizKoordinatKalitesi = KoordinatKalitesi.ArsivOnayli;
        w.TecvizSiniriText = "21,02";
        w.TecvizKaynakNotu = "Mühendis girdisi";
        return w;
    }

    [Fact]
    public void Sihirbaz_Adim_Ogeleri_Aktif_Ve_Tamamlanan_Adimi_Isaretlemeli()
    {
        var w = GecerliSihirbaz();

        w.AdimOgeleri.Select(a => a.Baslik).Should().Equal(w.Steps);
        w.AdimOgeleri[0].IsActive.Should().BeTrue();
        w.Validate().Should().BeTrue(string.Join("; ", w.Errors));
        w.NextCommand.Execute(null);

        w.AdimOgeleri[1].IsActive.Should().BeTrue();
        w.AdimOgeleri[0].IsActive.Should().BeFalse();
        w.AdimOgeleri[0].IsTamam.Should().BeTrue();
    }

    [Fact]
    public void Sihirbaz_Adima_Tiklama_Gecersiz_Girdide_Ileri_Atlamamali_Geri_Donmeye_Izin_Vermeli()
    {
        var w = GecerliSihirbaz();

        w.AdimaGitCommand.CanExecute(w.AdimOgeleri[3]).Should().BeFalse("1. adım doğrulanmadan ileri atlanamaz");
        w.Validate().Should().BeTrue();
        w.AdimaGitCommand.CanExecute(w.AdimOgeleri[3]).Should().BeTrue();
        w.AdimaGitCommand.Execute(w.AdimOgeleri[3]);
        w.CurrentIndex.Should().Be(3);

        w.TecvizSiniriText = "22"; // girdi değişti: doğrulama düşer
        w.AdimaGitCommand.CanExecute(w.AdimOgeleri[4]).Should().BeFalse();
        w.AdimaGitCommand.CanExecute(w.AdimOgeleri[0]).Should().BeTrue("geri dönüş her zaman serbest");
        w.AdimaGitCommand.Execute(w.AdimOgeleri[0]);
        w.CurrentIndex.Should().Be(0);
    }

    [Fact]
    public void Sihirbaz_Sag_Panel_Onay_Modu_Yalnizca_Besinci_Adimda_Acik_Olmali()
    {
        var w = GecerliSihirbaz();
        w.SagPanelOnayModu.Should().BeFalse();
        w.SagPanelOzetModu.Should().BeTrue();

        w.Validate();
        w.AdimaGitCommand.Execute(w.AdimOgeleri[4]);

        w.SagPanelOnayModu.Should().BeTrue();
        w.SagPanelOzetModu.Should().BeFalse();
    }

    [Fact]
    public void AnaPencere_Tema_Anahtari_Acik_Paftaya_Gecip_Tuvale_Yansimali()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(NczBelgesi())]));
        vm.AcikPafta.Should().BeFalse();
        vm.TemaEtiketi.Should().Be("☀️ Açık Pafta");

        vm.TemaDegistirCommand.Execute(null);

        vm.AcikPafta.Should().BeTrue();
        vm.Canvas.AcikPafta.Should().BeTrue();
        vm.TemaEtiketi.Should().Be("🌙 Koyu Stüdyo");
    }

    [Fact]
    public void AnaPencere_Sol_Sag_Ve_Alt_Paneller_Acilip_Kapanmali()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(NczBelgesi())]));
        vm.SolPanelAcik.Should().BeTrue();
        vm.SagPanelAcik.Should().BeTrue();
        vm.AltPanelAcik.Should().BeTrue();

        vm.SolPanelDegistirCommand.Execute(null);
        vm.SagPanelDegistirCommand.Execute(null);
        vm.AltPanelDegistirCommand.Execute(null);

        vm.SolPanelAcik.Should().BeFalse();
        vm.SagPanelAcik.Should().BeFalse();
        vm.AltPanelAcik.Should().BeFalse();
        vm.AltPanelEtiketi.Should().Be("▲ Cetveli Göster");
    }

    [Fact]
    public void AnaPencere_Komut_Cubugunda_Tema_Ve_Panel_Komutlari_Turkce_Bulunmali()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(NczBelgesi())]));

        vm.Palette.Query = "pafta";

        vm.Palette.Results.Should().Contain(c => c.Id == "toggleTheme");
    }

    [Fact]
    public async Task AnaPencere_Parsel_Tablosu_Ve_Durum_Metni_Rolleri_Turkce_Gostermeli()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(NczBelgesi())]));
        vm.Canvas.SetViewport(500, 500);

        await vm.ImportAsync(new MemoryStream([1]), ".ncz");

        vm.Parcels.Select(p => p.Role).Should().BeEquivalentTo("Kadastro Parseli", "İmar Parseli");
        vm.StatusText.Should().StartWith("NCZ (Netcad):");
    }

    [Fact]
    public async Task OnayCikti_Revizyon_Rozeti_Kisa_Olmali_Ve_Tam_Deger_Panoya_Kopyalanmali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var vm = new OnayCiktiViewModel(Alan(yol, zaman));
        string? kopyalanan = null;
        vm.PanoyaKopyalaIstendi += (_, metin) => kopyalanan = metin;

        vm.RevizyonTam.Should().Be(vm.Ekran!.RevizyonId);
        vm.RevizyonKisa.Should().HaveLength(9).And.EndWith("…");
        vm.RevizyonuKopyalaCommand.Execute(null);

        kopyalanan.Should().Be(vm.Ekran.RevizyonId);
    }

    [Fact]
    public async Task OnayCikti_Onay_Durumu_Metni_Onaya_Gore_Degismeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var vm = new OnayCiktiViewModel(Alan(yol, zaman)) { OnayMetniOkundu = true };
        vm.OnayDurumu.Should().Be("⏳ Mühendis Onayı Bekleniyor");

        await vm.OnaylaCommand.ExecuteAsync(null);

        vm.OnayDurumu.Should().StartWith("✅ Onaylı");
    }

    [Fact]
    public async Task OnayCikti_Uretilen_Cikti_Turu_Turkce_Yazilmali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var vm = new OnayCiktiViewModel(Alan(yol, zaman)) { OnayMetniOkundu = true };
        await vm.OnaylaCommand.ExecuteAsync(null);

        (await vm.UretAsync(CiktiTuru.FenKlasoru, "fen.bin")).Should().BeTrue(vm.DurumMetni);

        vm.DurumMetni.Should().StartWith("Fen Klasörü üretildi").And.NotContain("FenKlasoru");
        vm.OnerilenDosyaAdi(CiktiTuru.CksDopOzeti).Should().Be("Imar18_CKS_DOP_Ozeti.bin");
    }
}

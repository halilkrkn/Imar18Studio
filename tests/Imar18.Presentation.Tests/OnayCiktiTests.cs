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
/// P5.3 — Sihirbaz 5. adım (Cetvel &amp; Çıktı) ve P4'ten kalan bağlantılar: onay ekranı (spec 11.3), onay metni onay kutusu,
/// onay/geri alma ve çıktı komutlarının CommandBus/ApprovalService/CiktiServisi üzerinden çalışması; CAD içe aktarma ve CRS
/// doğrulamasının komut yoluyla projeye yazılması; bağlam menüsündeki Fen Klasörü öğesinin onaya bağlı etkinleşmesi.
/// </summary>
public sealed class OnayCiktiTests
{
    private sealed class SahteUretici(CiktiTuru tur) : ICiktiUretici
    {
        public CiktiTuru Tur => tur;

        public string Uzanti => ".bin";

        public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default) => Result<byte[]>.Success([1, 2, 3]);
    }

    private sealed class BellekYazici : ICiktiYazici
    {
        public Dictionary<string, byte[]> Dosyalar { get; } = new(StringComparer.Ordinal);

        public Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default)
        {
            Dosyalar[yol] = icerik.ToArray();
            return Task.CompletedTask;
        }

        public Task SilAsync(string yol, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SahteAdaptor(CadLayerDocument belge) : ICadFormatAdapter
    {
        public CadFormat Format => CadFormat.Ncz;

        public bool SupportsExtension(string extension) => true;

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) => Result<CadLayerDocument>.Success(belge);
    }

    private static readonly CadLayerDocument BosBelge = new(CadFormat.Ncz, [], [], [], 0, 0, 0, []);

    private static ProjeCalismaAlani Alan(ProjeKomutYolu yol, SabitZaman zaman, BellekYazici yazici) => new(
        yol,
        new ApprovalService(yol, zaman, OrnekProje.Topoloji),
        new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru), new SahteUretici(CiktiTuru.Cetveller), new SahteUretici(CiktiTuru.CksDopOzeti)], yazici, OrnekProje.Topoloji),
        OrnekProje.Muhendis);

    [Fact]
    public async Task OnayCikti_Onay_Metni_Isaretlenmeden_Onay_Komutu_Pasif_Olmali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var vm = new OnayCiktiViewModel(Alan(yol, zaman, new BellekYazici()));

        vm.Ekran!.OnayVerilebilir.Should().BeTrue();
        vm.OnayMetni.Should().Be(ApprovalService.OnayMetni);
        vm.OnaylaCommand.CanExecute(null).Should().BeFalse();
        vm.OnayMetniOkundu = true;
        vm.OnaylaCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task OnayCikti_Onay_Ve_GeriAlma_CommandBus_Uzerinden_Denetlenmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var vm = new OnayCiktiViewModel(Alan(yol, zaman, new BellekYazici())) { OnayMetniOkundu = true };

        await vm.OnaylaCommand.ExecuteAsync(null);

        vm.GecerliOnayVar.Should().BeTrue();
        yol.Denetim[^1].Islem.Should().Be("OnayVer");
        vm.GeriAlCommand.CanExecute(null).Should().BeTrue();
        await vm.GeriAlCommand.ExecuteAsync(null);
        vm.GecerliOnayVar.Should().BeFalse();
        yol.Denetim[^1].Islem.Should().Be("OnayGeriAl");
        vm.OnayMetniOkundu.Should().BeFalse("yeni onay için metin yeniden okunmalı");
    }

    [Fact]
    public async Task OnayCikti_Kritik_Hatada_Engeller_Gosterilmeli_Ve_Onay_Pasif_Olmali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        await yol.GonderAsync(new DopGirdisiAyarlaKomutu(Domain.Mevzuat.Dop.DopGirdisi.Olustur(200.00m, 100.00m).Value), OrnekProje.Muhendis);
        var vm = new OnayCiktiViewModel(Alan(yol, zaman, new BellekYazici())) { OnayMetniOkundu = true };

        vm.Engeller.Should().NotBeEmpty();
        vm.OnaylaCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task OnayCikti_Onaysiz_Cikti_Istegi_Reddedilip_Denetlenmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var yazici = new BellekYazici();
        var vm = new OnayCiktiViewModel(Alan(yol, zaman, yazici));

        var sonuc = await vm.UretAsync(CiktiTuru.FenKlasoru, "fen.pdf");

        sonuc.Should().BeFalse();
        yazici.Dosyalar.Should().BeEmpty();
        vm.DurumMetni.Should().Contain("onay");
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.OnaysizGirisim);
    }

    [Fact]
    public async Task OnayCikti_Onayli_Cikti_Uretip_Checksum_Gostermeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var yazici = new BellekYazici();
        var vm = new OnayCiktiViewModel(Alan(yol, zaman, yazici)) { OnayMetniOkundu = true };
        await vm.OnaylaCommand.ExecuteAsync(null);
        CiktiTuru? istenen = null;
        vm.KayitYoluIstendi += (_, t) => istenen = t;

        vm.FenKlasoruIsteCommand.Execute(null);
        var sonuc = await vm.UretAsync(CiktiTuru.FenKlasoru, "fen.pdf");

        istenen.Should().Be(CiktiTuru.FenKlasoru);
        sonuc.Should().BeTrue();
        yazici.Dosyalar.Should().ContainKeys("fen.pdf", "fen.pdf.sha256");
        vm.DurumMetni.Should().Contain("SHA-256");
    }

    [Fact]
    public async Task AnaPencere_Iceaktarma_Ve_CRS_Dogrulamasi_CommandBus_Ile_Projeye_Yazilmali()
    {
        var depo = new BellekProjeDeposu();
        var zaman = new SabitZaman(DateTimeOffset.UnixEpoch);
        var yol = (await ProjeKomutYolu.AcAsync(depo, "UI Projesi", zaman)).Value;
        var g = OrnekProje.Geometri();
        CadLayerDocument belge = new(
            CadFormat.Ncz,
            [new CadLayer(0, "TK_PARSEL_GİREN"), new CadLayer(1, "TK_PARSEL_OLUŞAN")],
            [
                new CadPolyline("TK_PARSEL_GİREN", "K1", null, [.. g[0].Koseler, g[0].Koseler[0]], true, null),
                new CadPolyline("TK_PARSEL_OLUŞAN", "A1", null, [.. g[1].Koseler, g[1].Koseler[0]], true, null),
            ],
            [], 0, 0, 0, []);
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(belge)]), Alan(yol, zaman, new BellekYazici()));

        await vm.ImportAsync(new MemoryStream([1]), "ncz");
        await vm.LastProjeTask;

        yol.Belge.Parseller.Select(p => (p.ParselId, p.Rol)).Should().Equal(("K1", ParselRolu.Kadastro), ("A1", ParselRolu.Imar));
        yol.Denetim[^1].Islem.Should().Be("ParselGeometrileriAyarla");

        vm.Wizard.TecvizUretimYontemi = "Sayısallaştırma";
        vm.Wizard.TecvizSiniriText = "1.00";
        vm.Wizard.TecvizKaynakNotu = "Sentetik";
        vm.Wizard.Validate().Should().BeTrue(string.Join(" | ", vm.Wizard.Errors));
        await vm.LastProjeTask;

        yol.Belge.Crs.Should().NotBeNull();
        yol.Denetim[^1].Islem.Should().Be("ProjeCrsAyarla");
        vm.OnayCikti.Should().NotBeNull();
        vm.OnayCikti!.Ekran!.ParselSayisi.Should().Be(2);
    }

    [Fact]
    public async Task AnaPencere_Fen_Klasoru_Baglam_Menusu_Onaya_Bagli_Etkin_Olmali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(BosBelge)]), Alan(yol, zaman, new BellekYazici()));
        var fen = () => vm.Canvas.ContextMenuItems.Single(i => i.Id == "fenPreview");

        fen().Command.CanExecute(null).Should().BeFalse("onay yokken");
        vm.OnayCikti!.OnayMetniOkundu = true;
        await vm.OnayCikti.OnaylaCommand.ExecuteAsync(null);

        fen().Command.CanExecute(null).Should().BeTrue();
        vm.Palette.Results.Select(r => r.Id).Should().Contain(["approve", "revokeApproval", "exportXlsx", "exportPdf", "exportCks"]);
    }

    [Fact]
    public void AnaPencere_Proje_Olmadan_P4_Davranisi_Korunmali()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(BosBelge)]));

        vm.OnayCikti.Should().BeNull();
        vm.Canvas.ContextMenuItems.Single(i => i.Id == "fenPreview").Command.CanExecute(null).Should().BeFalse();
    }
}

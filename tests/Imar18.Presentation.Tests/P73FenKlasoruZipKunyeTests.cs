using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Girdi;
using Imar18.Application.KoordinatDonusumu;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Revizyon;
using Imar18.GeometryEngine.Crs;
using Imar18.Presentation.Avalonia.Gorunum;
using Imar18.Presentation.Avalonia.ViewModels;
using Imar18.Presentation.Avalonia.Views;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// P7.3-C1/C2: 1. adım "Proje Künyesi &amp; Dönüşüm" kartı (künye kaydı, webtapu önerisi, ortak nokta metni, MK, Helmert/Afin
/// önizleme, CommandBus + onay düşmesi) ve 5. adım "📦 Fen Klasörü (.zip) Üret" düğmesi.
/// </summary>
public sealed class P73FenKlasoruZipKunyeTests
{
    private sealed class SahteUretici(CiktiTuru tur) : ICiktiUretici
    {
        public CiktiTuru Tur => tur;

        public string Uzanti => tur == CiktiTuru.FenKlasoruZip ? ".zip" : ".bin";

        public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default) => Result<byte[]>.Success([1]);
    }

    private sealed class BellekYazici : ICiktiYazici
    {
        public Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SilAsync(string yol, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class BosAdaptor : ICadFormatAdapter
    {
        public CadFormat Format => CadFormat.Ncz;

        public bool SupportsExtension(string extension) => true;

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) =>
            Result<CadLayerDocument>.Failure(ErrorCatalog.Get(ErrorCodes.GirdProjeGirdisiGecersiz));
    }

    private const string Noktalar =
        "N1;499900;4399900;499895.00;4399910.00\nN2;500200;4399900;500195.00;4399910.00\nN3;500200;4400200;500195.00;4400210.00";

    private static async Task<(ProjeCalismaAlani Alan, KunyeDonusumViewModel Vm, SabitZaman Zaman)> Hazirla(bool donusumServisi = true)
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var alan = new ProjeCalismaAlani(
            yol,
            new ApprovalService(yol, zaman, OrnekProje.Topoloji),
            new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoruZip), new SahteUretici(CiktiTuru.FenKlasoru)], new BellekYazici(), OrnekProje.Topoloji),
            OrnekProje.Muhendis)
        {
            Donusum = donusumServisi ? new DonusumServisi(new HelmertAfinDonusumHesaplayici()) : null,
        };
        return (alan, new KunyeDonusumViewModel(alan), zaman);
    }

    [Fact]
    public async Task Kunye_Kaydedilince_CommandBus_Ile_Projeye_Yazilmali_Bos_Kunye_Temizlemeli()
    {
        var (alan, vm, _) = await Hazirla();
        vm.Il = "SENTETİKİL";
        vm.Ilce = "ÖRNEKİLÇE";
        vm.PaftaNolari = "N99-X-01";

        await vm.KunyeyiKaydetCommand.ExecuteAsync(null);

        alan.Yol.Belge.Kunye.Should().NotBeNull();
        alan.Yol.Belge.Kunye!.Il.Should().Be("SENTETİKİL");
        alan.Yol.Belge.Kunye.PaftaNolari.Should().Be("N99-X-01");
        alan.Yol.Denetim[^1].Islem.Should().Be("ProjeKunyesiAyarla");
        vm.KunyeDurumu.Should().Contain("kaydedildi");

        foreach (var ad in new[] { "Il", "Ilce", "KoyMahalle", "Belediye", "PaftaNolari", "KadastroId", "DatumProjeksiyon" })
        {
            typeof(KunyeDonusumViewModel).GetProperty(ad)!.SetValue(vm, string.Empty);
        }

        await vm.KunyeyiKaydetCommand.ExecuteAsync(null);
        alan.Yol.Belge.Kunye.Should().BeNull();
    }

    [Fact]
    public async Task Datum_Projeksiyon_CRSden_Onerilmeli_Webtapu_Il_Ilce_Mahalle_Bos_Alanlara_Onerilmeli()
    {
        var (alan, vm, _) = await Hazirla();
        vm.DatumProjeksiyon.Should().Be("ITRF-96 / TM3 33°");
        vm.WebtapuOnerisiVar.Should().BeFalse();

        var rehber = new MalikRehberi([], [new KadastroTapuKaydi("K1", "12", "345", "ÖRNEKİL", "ÖRNEKİLÇE", "ÇAMLIK Mah.", "901", "7", 200m)]);
        (await alan.Yol.GonderAsync(new MalikRehberiAyarlaKomutu(rehber), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        vm.Ilce = "ELLE";
        vm.OneriyiYenile();

        vm.WebtapuOnerisiVar.Should().BeTrue();
        vm.Il.Should().Be("ÖRNEKİL");
        vm.Ilce.Should().Be("ELLE", "otomatik öneri yalnız boş alanı doldurur");
        vm.KoyMahalle.Should().Be("ÇAMLIK Mah.");
        vm.WebtapudanOnerCommand.Execute(null);
        vm.Ilce.Should().Be("ÖRNEKİLÇE", "düğme öneriyi açıkça uygular");
    }

    [Fact]
    public async Task Ortak_Noktalar_Onizleme_Parametre_Satirlarini_Gostermeli_Kaydetmemeli()
    {
        var (alan, vm, _) = await Hazirla();
        vm.OrtakNoktaMetni = Noktalar;

        vm.DonusumuOnizleCommand.Execute(null);

        vm.DonusumHatasi.Should().BeEmpty();
        vm.ParametreSatirlari.Should().Contain(s => s.StartsWith("A= 1.00000000", StringComparison.Ordinal))
            .And.Contain(s => s.StartsWith("CY= -5.000", StringComparison.Ordinal))
            .And.Contain(s => s.StartsWith("Ölçek katsayısı = 1.000000000", StringComparison.Ordinal));
        alan.Yol.Belge.Donusum.Should().BeNull("önizleme projeye yazmaz");
    }

    [Fact]
    public async Task Donusum_Uygulaninca_Projeye_Yazilmali_Onay_Dusmeli_Varsayilan_MK_009_Olmali()
    {
        var (alan, vm, zaman) = await Hazirla();
        (await new ApprovalService(alan.Yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        vm.MkMetni.Should().Be("0.09");
        vm.OrtakNoktaMetni = Noktalar;
        var uygulandi = 0;
        vm.Uygulandi += (_, _) => uygulandi++;

        await vm.DonusumuUygulaCommand.ExecuteAsync(null);

        var d = alan.Yol.Belge.Donusum;
        d.Should().NotBeNull();
        d!.Model.Should().Be(DonusumModeli.Helmert2B);
        d.Mk.Should().Be(0.09m);
        d.OrtakNoktalar.Should().HaveCount(3);
        alan.Yol.Belge.GecerliOnayVar(alan.Yol.Revizyon).Should().BeFalse("dönüşüm RevisionId'ye dahildir");
        uygulandi.Should().Be(1);
        vm.DonusumDurumu.Should().Contain("Kaydedildi: 3 ortak nokta");

        vm.OrtakNoktaMetni = string.Empty;
        await vm.DonusumuUygulaCommand.ExecuteAsync(null);
        alan.Yol.Belge.Donusum.Should().BeNull("boş metin dönüşümü temizler");
    }

    [Theory]
    // P7 UX (Numpad virgülü, proje sahibi talebi 20 Kasım 2026): "0,09" artık geçerli MK'dır (P7UxKutucukluTablolarTests);
    // çoklu / binlik ayırıcılı MK reddedilmeye devam eder.
    [InlineData("0,0,9")]
    [InlineData("1.078,90")]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("abc")]
    public async Task Gecersiz_MK_Turkce_Hata_Vermeli_Projeye_Yazmamali(string mk)
    {
        var (alan, vm, _) = await Hazirla();
        vm.OrtakNoktaMetni = Noktalar;
        vm.MkMetni = mk;

        await vm.DonusumuUygulaCommand.ExecuteAsync(null);

        vm.DonusumHatasi.Should().Contain("MK");
        alan.Yol.Belge.Donusum.Should().BeNull();
    }

    [Fact]
    public async Task Hatali_Satir_Ve_Afin_Eksik_Nokta_Ve_Esdogrusal_Noktalar_Reddedilmeli()
    {
        var (alan, vm, _) = await Hazirla();
        vm.OrtakNoktaMetni = "N1;1,5;2;3;4";
        await vm.DonusumuUygulaCommand.ExecuteAsync(null);
        vm.DonusumHatasi.Should().Contain("satırı 1");

        vm.OrtakNoktaMetni = "N1;1;1;1;1\nN2;2;2;2;2";
        vm.Model = DonusumModeli.Afin2B;
        await vm.DonusumuUygulaCommand.ExecuteAsync(null);
        vm.DonusumHatasi.Should().Contain("en az 3");

        vm.OrtakNoktaMetni = "N1;500000;4400000;500000;4400000\nN2;500100;4400100;500100;4400100\nN3;500200;4400200;500200;4400200";
        await vm.DonusumuUygulaCommand.ExecuteAsync(null);
        vm.DonusumHatasi.Should().Contain("hesaplanamadı");
        alan.Yol.Belge.Donusum.Should().BeNull();
    }

    [Fact]
    public async Task Kayitli_Donusum_Yenilemede_Metne_Model_Ve_MKya_Geri_Yuklenmeli()
    {
        var (alan, _, _) = await Hazirla();
        var girdi = new DonusumGirdisi(DonusumModeli.Afin2B, 0.12m, DonusumMetni.Ayristir(Noktalar).Value);
        (await alan.Yol.GonderAsync(new DonusumGirdisiAyarlaKomutu(girdi), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        var vm = new KunyeDonusumViewModel(alan);

        vm.Model.Should().Be(DonusumModeli.Afin2B);
        vm.MkMetni.Should().Be("0.12");
        DonusumMetni.Ayristir(vm.OrtakNoktaMetni).Value.Should().Equal(girdi.OrtakNoktalar);
        vm.Modeller.Should().Equal(DonusumModeli.Helmert2B, DonusumModeli.Afin2B);
        TurkceAdlar.Gorunen(DonusumModeli.Afin2B).Should().Be("Afin (2B)");
    }

    [Fact]
    public async Task Donusum_Servisi_Yoksa_Onizleme_Pasif_Kayit_Yapilabilmeli()
    {
        var (alan, vm, _) = await Hazirla(donusumServisi: false);
        vm.OrtakNoktaMetni = Noktalar;

        vm.OnizlemeVar.Should().BeFalse();
        await vm.DonusumuUygulaCommand.ExecuteAsync(null);

        alan.Yol.Belge.Donusum.Should().NotBeNull();
        vm.ParametreSatirlari.Should().BeEmpty();
    }

    [Fact]
    public async Task OnayCikti_Zip_Komutu_FenKlasoruZip_Istemeli_Onerilen_Ad_PILOT_Icermeli()
    {
        var (alan, _, _) = await Hazirla();
        var vm = new OnayCiktiViewModel(alan);
        CiktiTuru? istenen = null;
        vm.KayitYoluIstendi += (_, t) => istenen = t;

        vm.FenKlasoruZipIsteCommand.Execute(null);

        istenen.Should().Be(CiktiTuru.FenKlasoruZip);
        vm.OnerilenDosyaAdi(CiktiTuru.FenKlasoruZip).Should().Be("Imar18_PILOT_Fen_Klasoru.zip");
        TurkceAdlar.Ad(CiktiTuru.FenKlasoruZip).Should().Be("Fen Klasörü (.zip)");
    }

    [AvaloniaFact]
    public async Task AnaPencere_Adim1_Kunye_Donusum_Karti_Ve_Adim5_Zip_Dugmesi_Gorunmeli()
    {
        var (alan, _, _) = await Hazirla();
        var vm = new MainWindowViewModel(new CadImportService([new BosAdaptor()]), alan);
        await vm.LastProjeTask;
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();

        var kart = w.FindControl<Border>("KunyeDonusumKarti")!;
        kart.IsEffectivelyVisible.Should().BeTrue("1. adımda görünür");
        kart.DataContext.Should().BeSameAs(vm.Kunye);
        w.FindControl<TextBox>("OrtakNoktaKutusu").Should().NotBeNull();
        w.FindControl<TextBox>("MkKutusu")!.Text.Should().Be("0.09");

        vm.Wizard.CurrentIndex = 4;
        Dispatcher.UIThread.RunJobs();
        var zip = w.FindControl<Button>("FenKlasoruZipDugmesi")!;
        zip.Content.Should().Be("📦 Fen Klasörü (.zip) Üret");
        // SORU-053: çıktı düğmelerinin altında varsayılanı kapalı tek .sha256 kutucuğu; VM'ye iki yönlü bağlı.
        var kutu = w.FindControl<CheckBox>("Sha256YanDosyasiKutusu")!;
        kutu.Content.Should().Be("Doğrulama özeti (.sha256) yan dosyası oluştur");
        kutu.IsEffectivelyVisible.Should().BeTrue();
        kutu.IsChecked.Should().BeFalse();
        kutu.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        vm.OnayCikti!.Sha256YanDosyasiUret.Should().BeTrue();
        kutu.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        vm.OnayCikti!.Sha256YanDosyasiUret.Should().BeFalse();
        kart.IsEffectivelyVisible.Should().BeFalse();
        vm.Palette.Results.Select(r => r.Id).Should().Contain("exportZip");
        w.Close();
    }
}

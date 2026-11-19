using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
using Imar18.GeometryEngine.Topoloji;
using Imar18.Presentation.Avalonia.Gorunum;
using Imar18.Presentation.Avalonia.ViewModels;
using Imar18.Presentation.Avalonia.Views;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// P7 UX — kutucuklu tablolar (proje sahibi + mühendis canlı testi, 20 Kasım 2026): 1. adım ortak nokta tablosu + "WebTapu PDF'ten
/// Künye Oku"; 4. adım TAKBİS ve Kroki tabloları (<c>TakbisMetni</c> / <c>KrokiMetni</c> ile iki yönlü senkron, "Metin / Toplu
/// Yapıştır" alanı); 2. adım Kadastro Parseli yönlendirmesi; sayısal hücrelerde Numpad virgülü. Proje <see cref="OrnekProje"/>
/// (K1 200 m²; A1 120 m²; Y1 yol; MLK-001/MLK-002 yarı yarıya) üzerindedir; adlar ve konumlar uydurmadır (AC-PILOT-08).
/// </summary>
public sealed class P7UxKutucukluTablolarTests
{
    private const string BinlikUyarisi = "Binlik ayırıcı kullanmayın; yalnızca ondalık ayırıcı ('.' veya ',') girin (ör. 1078.90).";
    private const string Yonlendirme = "1. adımda en az bir katmana 'Kadastro Parseli' rolü atayıp 'Girdileri Doğrula' düğmesine basın.";
    private static readonly string Nl = Environment.NewLine;

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

    private sealed class SahteTapuOkuyucu(Func<Result<TapuKaydi>> sonuc) : ITapuKaydiOkuyucu
    {
        public Result<TapuKaydi> Oku(Stream kaynak, CancellationToken cancellationToken = default) => sonuc();
    }

    private sealed class SahteKrokiAdaptoru : ICadFormatAdapter
    {
        public CadFormat Format => CadFormat.Ncz;

        public bool SupportsExtension(string extension) => true;

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) =>
            Result<CadLayerDocument>.Success(new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "KULLANIM")], [], [], 0, 0, 0, []));
    }

    private static TapuKaydi Kayit(string? il = "ÖRNEKİL", string? ilce = "ÖRNEKİLÇE", string? mahalle = "ÇAMLIK Mah.") => new(
        "100000001", il, ilce, mahalle, null, "12", "345", "901", "K1", 200.00m, "Tarla",
        [
            new("900000101", "800000001", "AYŞE YILMAZ", "ALİ", null, BigInteger.One, new BigInteger(2), 100.00m, 200.00m, "Miras", false),
            new("900000102", "800000002", "MEHMET ÇAKIR", "ALİ", null, BigInteger.One, new BigInteger(2), 100.00m, 200.00m, "Miras", false),
        ]);

    private static async Task<ProjeCalismaAlani> Alan(Func<Result<TapuKaydi>>? tapu = null, bool okuyucu = true, ProjeKomutYolu? yol = null, SabitZaman? zaman = null)
    {
        if (yol is null || zaman is null)
        {
            (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        }

        return new ProjeCalismaAlani(
            yol,
            new ApprovalService(yol, zaman, OrnekProje.Topoloji),
            new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru)], new BellekYazici(), OrnekProje.Topoloji),
            OrnekProje.Muhendis)
        {
            TapuOkuyucu = okuyucu ? new SahteTapuOkuyucu(tapu ?? (() => Result<TapuKaydi>.Success(Kayit()))) : null,
            Kesisim = new NtsParselKesisimHesaplayici(),
            Donusum = new DonusumServisi(new HelmertAfinDonusumHesaplayici()),
        };
    }

    private static async Task<(ProjeCalismaAlani Alan, HesapAdimlariViewModel Vm)> Hesap()
    {
        var alan = await Alan();
        var vm = new HesapAdimlariViewModel(alan, OrnekProje.Tecviz, new CadImportService([new SahteKrokiAdaptoru()]));
        await vm.YenileAsync();
        return (alan, vm);
    }

    private static Stream Akis() => new MemoryStream([1, 2, 3]);

    private static void Doldur(OrtakNoktaSatirVm s, string no, string y1, string x1, string y2, string x2)
    {
        s.NoktaNo = no;
        s.Y1 = y1;
        s.X1 = x1;
        s.Y2 = y2;
        s.X2 = x2;
    }

    // ============================ 1. adım: ortak nokta tablosu ============================

    [Fact]
    public async Task OrtakNokta_Tablo_Satir_Ekleme_Duzenleme_Silme_Metinle_Iki_Yonlu_Senkron_Olmali()
    {
        var vm = new KunyeDonusumViewModel(await Alan());
        vm.OrtakNoktaSatirlari.Should().BeEmpty();

        vm.OrtakNoktaEkleCommand.Execute(null);
        vm.OrtakNoktaSatirlari.Should().ContainSingle();
        vm.OrtakNoktaMetni.Should().BeEmpty("boş satır metne yazılmaz");

        Doldur(vm.OrtakNoktaSatirlari[0], "N1", "499900", "4399900", "499895,00", "4399910,5");
        vm.OrtakNoktaMetni.Should().Be("N1;499900;4399900;499895.00;4399910.5", "virgüllü hücre metne '.' ile yazılır (InvariantCulture)");

        const string Metin = "N1;499900;4399900;499895.00;4399910.00\nN2;500200;4399900;500195.00;4399910.00\nN3;500200;4400200;500195.00;4400210.00";
        vm.OrtakNoktaMetni = Metin;
        vm.OrtakNoktaSatirlari.Should().HaveCount(3);
        vm.OrtakNoktaSatirlari[1].NoktaNo.Should().Be("N2");
        vm.OrtakNoktaSatirlari[1].Y2.Should().Be("500195.00");
        vm.OrtakNoktaMetni.Should().Be(Metin, "metinden kurulan tablo metni yeniden yazmaz");

        vm.OrtakNoktaSilCommand.Execute(vm.OrtakNoktaSatirlari[1]);
        vm.OrtakNoktaSatirlari.Should().HaveCount(2);
        vm.OrtakNoktaMetni.Should().Be("N1;499900;4399900;499895.00;4399910.00" + Nl + "N3;500200;4400200;500195.00;4400210.00");
    }

    [Fact]
    public async Task OrtakNokta_Hucre_Duzenlenince_Parametre_Onizlemesi_Canli_Guncellenmeli_Projeye_Yazmamali()
    {
        var alan = await Alan();
        var vm = new KunyeDonusumViewModel(alan);
        vm.OrtakNoktaEkleCommand.Execute(null);
        Doldur(vm.OrtakNoktaSatirlari[0], "N1", "499900", "4399900", "499895,00", "4399910,00");
        vm.DonusumDurumu.Should().Contain("en az 2 ortak nokta");
        vm.ParametreSatirlari.Should().BeEmpty();

        vm.OrtakNoktaEkleCommand.Execute(null);
        var ikinci = vm.OrtakNoktaSatirlari[1];
        ikinci.NoktaNo = "N2";
        ikinci.Y1 = "500200";
        vm.DonusumDurumu.Should().Contain("tamamlayın", "eksik satır hata sayılmaz");
        vm.DonusumHatasi.Should().BeEmpty();

        ikinci.X1 = "4399900";
        ikinci.Y2 = "500195";
        ikinci.X2 = "4399910";

        vm.DonusumHatasi.Should().BeEmpty();
        vm.DonusumDurumu.Should().Contain("Canlı önizleme");
        vm.ParametreSatirlari.Should().Contain(s => s.StartsWith("A= 1.00000000", StringComparison.Ordinal))
            .And.Contain(s => s.StartsWith("CY= -5.000", StringComparison.Ordinal), "Y2 = Y1 − 5 (bağımsız referans)");
        alan.Yol.Belge.Donusum.Should().BeNull("canlı önizleme projeye yazmaz");

        vm.OrtakNoktaSilCommand.Execute(ikinci);
        vm.ParametreSatirlari.Should().BeEmpty("satır silinince önizleme de güncellenir");
    }

    [Fact]
    public async Task OrtakNokta_Binlik_Ayiricili_Hucre_Satir_Hatasi_Ve_Kirmizi_Isaret_Gostermeli()
    {
        var alan = await Alan();
        var vm = new KunyeDonusumViewModel(alan);
        vm.OrtakNoktaEkleCommand.Execute(null);
        var s = vm.OrtakNoktaSatirlari[0];
        Doldur(s, "N1", "1.078,90", "4399900", "499895", "4399910");

        s.Y1Hatali.Should().BeTrue();
        s.X1Hatali.Should().BeFalse();
        s.Hata.Should().Be("Y1: " + BinlikUyarisi);
        vm.DonusumHatasi.Should().Contain(BinlikUyarisi);
        vm.OrtakNoktaMetni.Should().Contain("1.078,90", "geçersiz hücre tahminle düzeltilmez; ham yazılır ve metin ayrıştırıcısı reddeder");

        await vm.DonusumuUygulaCommand.ExecuteAsync(null);
        alan.Yol.Belge.Donusum.Should().BeNull();

        s.Y1 = "1078,90";
        s.Y1Hatali.Should().BeFalse();
        s.Hata.Should().BeNull();
        vm.OrtakNoktaMetni.Should().Be("N1;1078.90;4399900;499895;4399910");
    }

    [Fact]
    public async Task OrtakNokta_Metindeki_Fazla_Alan_Ve_Yorum_Satiri_Tablo_Duzenlemesinde_Korunmali()
    {
        var vm = new KunyeDonusumViewModel(await Alan());
        vm.OrtakNoktaMetni = "# not\nN1;1;2;3;4;EK";

        vm.OrtakNoktaSatirlari.Should().ContainSingle();
        vm.OrtakNoktaSatirlari[0].X2 = "5";

        vm.OrtakNoktaMetni.Should().Be("# not" + Nl + "N1;1;2;3;5;EK");
    }

    [Fact]
    public async Task OrtakNokta_Tablodan_Girilen_Noktalar_Donusumu_Uygula_Ile_Projeye_Yazilmali_Virgullu_MK_Kabul_Edilmeli()
    {
        var alan = await Alan();
        var vm = new KunyeDonusumViewModel(alan);
        foreach (var (no, y1, x1, y2, x2) in new[]
                 {
                     ("N1", "499900", "4399900", "499895,00", "4399910,00"),
                     ("N2", "500200", "4399900", "500195,00", "4399910,00"),
                     ("N3", "500200", "4400200", "500195,00", "4400210,00"),
                 })
        {
            vm.OrtakNoktaEkleCommand.Execute(null);
            Doldur(vm.OrtakNoktaSatirlari[^1], no, y1, x1, y2, x2);
        }

        vm.MkMetni = "0,12";
        await vm.DonusumuUygulaCommand.ExecuteAsync(null);

        var d = alan.Yol.Belge.Donusum;
        d.Should().NotBeNull(vm.DonusumHatasi);
        d!.Mk.Should().Be(0.12m);
        d.OrtakNoktalar.Should().HaveCount(3);
        d.OrtakNoktalar[0].Y2.Should().Be(499895.00m);
        d.OrtakNoktalar[2].X2.Should().Be(4400210.00m);

        vm.MkMetni = "1.078,90";
        await vm.DonusumuUygulaCommand.ExecuteAsync(null);
        vm.DonusumHatasi.Should().Be("MK: " + BinlikUyarisi);
    }

    // ============================ 1. adım: WebTapu PDF'ten Künye Oku ============================

    [Fact]
    public async Task WebtapuPdften_Kunye_Oku_Il_Ilce_Mahalleyi_Doldurmali_Kisi_Adi_Yazmamali_Kendiliginden_Kaydetmemeli()
    {
        var alan = await Alan();
        var vm = new KunyeDonusumViewModel(alan);
        vm.Il = "ESKİ";
        vm.PaftaNolari = "N99-X-01";
        var istendi = 0;
        vm.WebtapuKunyeDosyasiIstendi += (_, _) => istendi++;

        vm.WebtapuOkuyucuVar.Should().BeTrue();
        vm.WebtapuKunyeIsteCommand.Execute(null);
        istendi.Should().Be(1, "görünüm dosya seçicisini açar");

        await vm.WebtapudanKunyeOkuAsync(Akis());

        vm.Il.Should().Be("ÖRNEKİL", "düğme açık istekle mevcut değerin üzerine yazar");
        vm.Ilce.Should().Be("ÖRNEKİLÇE");
        vm.KoyMahalle.Should().Be("ÇAMLIK Mah.");
        vm.PaftaNolari.Should().Be("N99-X-01", "yalnız İl / İlçe / Mahalle değişir");
        var gorunen = string.Join("|", vm.Il, vm.Ilce, vm.KoyMahalle, vm.Belediye, vm.PaftaNolari, vm.KadastroId, vm.DatumProjeksiyon, vm.KunyeDurumu);
        gorunen.Should().NotContain("AYŞE").And.NotContain("MEHMET").And.NotContain("800000001", "KVKK: kişi adı ve kimlik künyeye yazılmaz");
        vm.KunyeDurumu.Should().Contain("okundu");
        alan.Yol.Belge.Kunye.Should().BeNull("okuma künyeyi kendiliğinden kaydetmez");
        alan.Yol.Belge.MalikRehberi.Malikler.Should().BeEmpty("künye okuma Malik Rehberi'ne yazmaz");

        await vm.KunyeyiKaydetCommand.ExecuteAsync(null);
        alan.Yol.Belge.Kunye!.Il.Should().Be("ÖRNEKİL");
        alan.Yol.Belge.Kunye.KoyMahalle.Should().Be("ÇAMLIK Mah.");
    }

    [Fact]
    public async Task WebtapuPdften_Kunye_Oku_Okuyucu_Yoksa_Pasif_Hata_Ve_Istisnada_Turkce_Mesaj_Gostermeli()
    {
        new KunyeDonusumViewModel(await Alan(okuyucu: false)).WebtapuKunyeIsteCommand.CanExecute(null).Should().BeFalse();

        var hata = ErrorCatalog.Get(ErrorCodes.GirdPdfBozuk);
        var vm = new KunyeDonusumViewModel(await Alan(() => Result<TapuKaydi>.Failure(hata)));
        await vm.WebtapudanKunyeOkuAsync(Akis());
        vm.KunyeDurumu.Should().Be(hata.UserMessage);
        vm.Il.Should().BeEmpty();

        vm = new KunyeDonusumViewModel(await Alan(() => throw new InvalidOperationException("pdf")));
        await vm.WebtapudanKunyeOkuAsync(Akis());
        vm.KunyeDurumu.Should().Be(hata.UserMessage, "beklenmeyen istisna ERR-GIRD-003 sayılır, uygulama çökmez");
        vm.IsBusy.Should().BeFalse();

        vm = new KunyeDonusumViewModel(await Alan(() => Result<TapuKaydi>.Success(Kayit(null, null, null))));
        await vm.WebtapudanKunyeOkuAsync(Akis());
        vm.KunyeDurumu.Should().Contain("bulunamadı");
    }

    [Fact]
    public async Task Dorduncu_Adimda_Webtapu_TAKBISe_Aktarilinca_Birinci_Adimdaki_Bos_Kunye_Alanlari_Doldurulmali()
    {
        var alan = await Alan();
        (await alan.Yol.GonderAsync(new ProjeKunyesiAyarlaKomutu(new ProjeKunyesi("ELLE İL", string.Empty, string.Empty, string.Empty, "N99", string.Empty, string.Empty)),
            OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        var vm = new MainWindowViewModel(new CadImportService([new SahteKrokiAdaptoru()]), alan);
        await vm.LastProjeTask;
        vm.Kunye!.Ilce.Should().BeEmpty();

        await vm.Hesap!.WebtapuYukleAsync([Akis()]);
        await vm.Hesap.TakbiseAktarCommand.ExecuteAsync(null);

        vm.Kunye.Il.Should().Be("ELLE İL", "dolu kutuya dokunulmaz");
        vm.Kunye.Ilce.Should().Be("ÖRNEKİLÇE");
        vm.Kunye.KoyMahalle.Should().Be("ÇAMLIK Mah.");
        vm.Kunye.KunyeDurumu.Should().Contain("dolduruldu");
    }

    // ============================ 4. adım: TAKBİS ve Kroki tabloları ============================

    [Fact]
    public async Task Takbis_Tablosu_TakbisMetni_Ile_Iki_Yonlu_Senkron_Olmali_Satir_Ekle_Duzenle_Sil()
    {
        var (_, vm) = await Hesap();
        vm.TakbisMetni.Should().Be("K1;MLK-001;1/2;E" + Nl + "K1;MLK-002;1/2;H", "ön koşul: kayıtlı hak eşleştirmesi");

        vm.TakbisSatirlari.Should().HaveCount(2);
        vm.TakbisSatirlari[0].KadastroParseli.Should().Be("K1");
        vm.TakbisSatirlari[0].MalikKodu.Should().Be("MLK-001");
        vm.TakbisSatirlari[0].Hisse.Should().Be("1/2");
        vm.TakbisSatirlari[0].Muvafakat.Should().Be("E");

        vm.TakbisSatirlari[1].Muvafakat = "E";
        vm.TakbisMetni.Should().Be("K1;MLK-001;1/2;E" + Nl + "K1;MLK-002;1/2;E");

        vm.TakbisSatiriEkleCommand.Execute(null);
        vm.TakbisSatirlari.Should().HaveCount(3);
        vm.TakbisMetni.Should().Be("K1;MLK-001;1/2;E" + Nl + "K1;MLK-002;1/2;E", "boş satır metne yazılmaz");
        var yeni = vm.TakbisSatirlari[2];
        yeni.Muvafakat.Should().Be("H", "webtapu aktarımı gibi varsayılan muvafakatsiz");
        yeni.KadastroParseli = "K1";
        yeni.MalikKodu = "MLK-003";
        yeni.Hisse = "1/4";
        vm.TakbisMetni.Should().EndWith(Nl + "K1;MLK-003;1/4;H");

        vm.TakbisSatiriSilCommand.Execute(yeni);
        vm.TakbisMetni.Should().Be("K1;MLK-001;1/2;E" + Nl + "K1;MLK-002;1/2;E");

        vm.TakbisMetni = "K1;MLK-009;1/1;H";
        vm.TakbisSatirlari.Should().ContainSingle().Which.MalikKodu.Should().Be("MLK-009", "toplu yapıştırılan metin tabloya aktarılır");

        vm.TakbisSatirlari[0].Muvafakat = null!;
        vm.TakbisSatirlari[0].Muvafakat.Should().Be("H", "seçim kutusunun null geri yazması değeri silmez");
        vm.TakbisMetni.Should().Be("K1;MLK-009;1/1;H");
    }

    [Fact]
    public async Task Takbis_Tablosu_Webtapu_Aktarimiyla_Aninda_Dolmali_Ve_Tablodan_Hak_Eslestirme_Uygulanmali()
    {
        var (alan, vm) = await Hesap();
        await vm.WebtapuYukleAsync([Akis()]);

        await vm.TakbiseAktarCommand.ExecuteAsync(null);

        vm.TakbisSatirlari.Select(s => s.MalikKodu).Should().Equal("M01", "M02");
        vm.TakbisSatirlari.Should().OnlyContain(s => s.KadastroParseli == "K1" && s.Hisse == "1/2" && s.Muvafakat == "H");

        vm.KrokiSatirlari[0].MalikKodu = "M01";
        vm.KrokiSatirlari[1].MalikKodu = "M02";
        vm.TakbisSatirlari[0].Muvafakat = "E";
        await vm.HakUygulaCommand.ExecuteAsync(null);

        alan.Yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Basarili, vm.DurumMetni);
        var hak = alan.Yol.Belge.HakEslestirme!;
        hak.KadastroParselleri.Single().Malikler.Select(m => (m.MalikId, m.Muvafakatli)).Should().Equal(("M01", true), ("M02", false));
        hak.Eslestirme.Select(r => r.MalikId).Should().Equal("M01", "M02");
    }

    [Fact]
    public async Task Kroki_Tablosu_Virgullu_Alan_Metne_Nokta_Ile_Yazilmali_Ve_Hak_Eslestirme_Uygulanmali()
    {
        var (alan, vm) = await Hesap();
        vm.KrokiSatirlari.Should().HaveCount(2);
        vm.KrokiSatirlari[0].ImarParseli.Should().Be("A1");
        vm.KrokiSatirlari[0].Alan.Should().Be("60.00");

        vm.KrokiSatirlari[0].Alan = "59,99";
        vm.KrokiSatirlari[1].Alan = "60,01";

        vm.KrokiMetni.Should().Be("MLK-001;K1;A1;59.99" + Nl + "MLK-002;K1;A1;60.01");
        await vm.HakUygulaCommand.ExecuteAsync(null);
        alan.Yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Basarili, vm.DurumMetni);
        alan.Yol.Belge.HakEslestirme!.Eslestirme.Select(r => r.AlanM2).Should().Equal(59.99m, 60.01m);
    }

    [Fact]
    public async Task Kroki_Tablosu_Binlik_Ayiricili_Alan_Acik_Uyariyla_Reddedilmeli_Projeye_Yazmamali()
    {
        var (alan, vm) = await Hesap();
        var once = alan.Yol.Revizyon;
        var s = vm.KrokiSatirlari[0];

        s.Alan = "1.078,90";

        s.AlanHatali.Should().BeTrue();
        s.Hata.Should().Be("Alan (m²): " + BinlikUyarisi);
        await vm.HakUygulaCommand.ExecuteAsync(null);
        vm.KrokiHatasi.Should().Be("Kroki alanı (m²): " + BinlikUyarisi);
        alan.Yol.Revizyon.Should().Be(once, "geçersiz girdi projeye yazılmaz");

        vm.KrokiSatiriSilCommand.Execute(s);
        vm.KrokiSatirlari.Should().ContainSingle();
        vm.KrokiMetni.Should().Be("MLK-002;K1;A1;60.00");
        vm.KrokiSatiriEkleCommand.Execute(null);
        vm.KrokiSatirlari.Should().HaveCount(2);
        vm.KrokiMetni.Should().Be("MLK-002;K1;A1;60.00", "boş satır metne yazılmaz");
    }

    [Fact]
    public async Task Kroki_Tablosu_Yaklasik_Yer_Krokisi_Aktarimiyla_Aninda_Dolmali_Yorum_Satiri_Korunmali()
    {
        var (_, vm) = await Hesap();
        vm.KrokiMetni = string.Empty;
        vm.KrokiSatirlari.Should().BeEmpty();

        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");
        vm.KrokiyeAktarCommand.Execute(null);

        vm.KrokiMetni.Should().StartWith("# Yaklaşık Yer Krokisi");
        vm.KrokiSatirlari.Select(s => (s.MalikKodu, s.KadastroParseli, s.ImarParseli, s.Alan)).Should().Equal(
            ("MLK-001", "K1", "A1", "60.00"), ("MLK-002", "K1", "A1", "60.00"));

        vm.KrokiSatirlari[0].Alan = "60,00";
        vm.KrokiMetni.Should().StartWith("# Yaklaşık Yer Krokisi", "tablo düzenlemesi yorum satırını silmez");
    }

    [Fact]
    public async Task Tablo_Onerileri_Projedeki_Kadastro_Ve_Imar_Parsellerini_Icermeli()
    {
        var (_, vm) = await Hesap();

        vm.KadastroParselOnerileri.Should().Equal("K1");
        vm.ImarParselOnerileri.Should().Contain("A1");
        vm.KadastroParseliYok.Should().BeFalse();
    }

    [Fact]
    public async Task Sifirla_Tablolari_Bosaltmali()
    {
        var (_, vm) = await Hesap();

        vm.Sifirla();

        vm.TakbisSatirlari.Should().BeEmpty();
        vm.KrokiSatirlari.Should().BeEmpty();
    }

    // ============================ 2. adım: yönlendirme; sayısal girdiler ============================

    [Fact]
    public async Task Tecviz_Projede_Kadastro_Parseli_Yoksa_Net_Yonlendirme_Gostermeli()
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik Boş Proje", zaman)).Value;
        var vm = new HesapAdimlariViewModel(await Alan(yol: yol, zaman: zaman), OrnekProje.Tecviz);
        await vm.YenileAsync();

        vm.KadastroParseliYok.Should().BeTrue();
        HesapAdimlariViewModel.KadastroYonlendirmesi.Should().Be(Yonlendirme);

        await vm.TecvizUygulaCommand.ExecuteAsync(null);
        vm.DurumMetni.Should().Contain(Yonlendirme);
    }

    [Fact]
    public async Task Sayisal_Girdiler_Virgul_Ile_Kabul_Edilmeli_Binlik_Ayirici_Acik_Uyari_Vermeli()
    {
        var (alan, vm) = await Hesap();
        vm.TecvizSatirlari.Should().ContainSingle();

        vm.TecvizSatirlari[0].TapuAlaniText = "1.078,90";
        await vm.TecvizUygulaCommand.ExecuteAsync(null);
        vm.TecvizSatirlari[0].Hata.Should().Be(BinlikUyarisi);

        vm.TecvizSatirlari[0].TapuAlaniText = "200,00";
        vm.ToplamTecvizSiniriText = "8699,44";
        await vm.TecvizUygulaCommand.ExecuteAsync(null);
        alan.Yol.Belge.TecvizParselleri.Single().TapuAlaniM2.Should().Be(200.00m, vm.DurumMetni);
        alan.Yol.Belge.DuzenlemeToplamTecvizSiniriM2.Should().Be(8699.44m);

        vm.D1Text = "8,699.44";
        await vm.DopUygulaCommand.ExecuteAsync(null);
        vm.D1Hatasi.Should().Be(BinlikUyarisi);

        vm.D1Text = "0";
        vm.R2Text = "120,00";
        vm.R3Text = "0,00";
        await vm.DopUygulaCommand.ExecuteAsync(null);
        alan.Yol.Belge.Dop!.R2.Should().Be(120.00m, vm.DurumMetni);
    }

    // ============================ Reviewer bulguları (M1–M3, L4–L9) ============================

    [Fact]
    public async Task OrtakNokta_Metne_Virgullu_Yapistirilan_Satirlar_Metin_Alanindan_Cikinca_Normalize_Edilip_Onizlenmeli()
    {
        var alan = await Alan();
        var vm = new KunyeDonusumViewModel(alan);
        vm.OrtakNoktaMetni = "N1;499900;4399900;499895,00;4399910,00\nN2;500200;4399900;500195,00;4399910,00";
        vm.OrtakNoktaSatirlari.Should().HaveCount(2).And.OnlyContain(s => s.Hata == null, "virgül tabloda geçerlidir");

        vm.OrtakNoktaMetniniNormalizeEtCommand.Execute(null);

        vm.OrtakNoktaMetni.Should().Be("N1;499900;4399900;499895.00;4399910.00" + Nl + "N2;500200;4399900;500195.00;4399910.00");
        vm.ParametreSatirlari.Should().Contain(s => s.StartsWith("CY= -5.000", StringComparison.Ordinal), "metin yolu da canlı önizlenir");
        await vm.DonusumuUygulaCommand.ExecuteAsync(null);
        alan.Yol.Belge.Donusum!.OrtakNoktalar.Should().HaveCount(2, vm.DonusumHatasi);

        vm.OrtakNoktaMetni = "N1;1,5;2;3;4";
        await vm.DonusumuUygulaCommand.ExecuteAsync(null);
        vm.DonusumHatasi.Should().Contain("satırı 1", "metin alanından çıkılmadan komut çalışırsa metin kuralı ('.') aynen geçerlidir");
    }

    [Theory]
    [InlineData("N1;5", "';'")]
    [InlineData("#5", "'#'")]
    public async Task Tablo_Hucresinde_NoktaliVirgul_Veya_Diyez_Satir_Hatasi_Vermeli(string noktaNo, string beklenen)
    {
        var vm = new KunyeDonusumViewModel(await Alan());
        vm.OrtakNoktaEkleCommand.Execute(null);
        var s = vm.OrtakNoktaSatirlari[0];

        Doldur(s, noktaNo, "1", "2", "3", "4");

        s.Hata.Should().Contain("Nokta No").And.Contain(beklenen);
        vm.DonusumHatasi.Should().Contain(beklenen, "hatalı satır canlı önizlemede sessizce düşmez");

        var (_, h) = await Hesap();
        h.TakbisSatirlari[0].MalikKodu = "MLK;001";
        h.TakbisSatirlari[0].Hata.Should().Contain("Malik Kodu").And.Contain("';'");
        h.KrokiSatirlari[0].MalikKodu = "#MLK";
        h.KrokiSatirlari[0].Hata.Should().Contain("Malik Kodu").And.Contain("'#'");
    }

    [Fact]
    public async Task Ara_Yorum_Satirlari_Sirasi_Ve_Dokunulmayan_Satirlar_Aynen_Korunmali()
    {
        var vm = new KunyeDonusumViewModel(await Alan());
        vm.OrtakNoktaMetni = "# baş\nN1;1;2;3;4\n# ara\nN2 ; 5;6;7;8\n# son";

        vm.OrtakNoktaSatirlari[0].X2 = "5";
        vm.OrtakNoktaMetni.Should().Be(string.Join(Nl, "# baş", "N1;1;2;3;5", "# ara", "N2 ; 5;6;7;8", "# son"),
            "yorumlar yerinde kalır, dokunulmayan satır aynen yazılır");

        vm.OrtakNoktaSilCommand.Execute(vm.OrtakNoktaSatirlari[1]);
        vm.OrtakNoktaMetni.Should().Be(string.Join(Nl, "# baş", "N1;1;2;3;5", "# ara", "# son"), "silinen satırın yorumu kaybolmaz");
    }

    [Fact]
    public async Task Bos_Satir_Ekleme_Metni_Ve_Kroki_Onizlemesini_Degistirmemeli_Gecersiz_Muvafakat_Ve_Hisse_Isaretlenmeli()
    {
        var (_, vm) = await Hesap();
        const string Metin = "K1;MLK-001;1/2;e\nK1;MLK-002;1/2;H";
        vm.TakbisMetni = Metin;
        vm.TakbisSatirlari[0].MuvafakatHatali.Should().BeTrue();
        vm.TakbisSatirlari[0].Hata.Should().Contain("E veya H");

        vm.TakbisSatiriEkleCommand.Execute(null);
        vm.TakbisMetni.Should().Be(Metin, "boş satır eklemek metni yeniden yazmaz");

        vm.TakbisSatirlari[1].Hisse = "1/0";
        vm.TakbisSatirlari[1].HisseHatali.Should().BeTrue();
        vm.TakbisSatirlari[1].Hata.Should().Contain("Hisse");
        vm.TakbisSatirlari[1].Hisse = "abc";
        vm.TakbisSatirlari[1].HisseHatali.Should().BeTrue();
        vm.TakbisSatirlari[1].Hisse = "1/2";
        vm.TakbisSatirlari[1].HisseHatali.Should().BeFalse();
        vm.TakbisSatirlari[1].Hata.Should().BeNull();
        vm.TakbisMetni.Should().Be("K1;MLK-001;1/2;e" + Nl + "K1;MLK-002;1/2;H", "dokunulmayan satır tahminle düzeltilmez (e → E yapılmaz)");

        vm.TakbisMetni = "K1;MLK-001;1/2;E" + Nl + "K1;MLK-002;1/2;H";
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");
        vm.KrokiOnizlemesi.Should().NotBeEmpty();
        vm.TakbisSatiriEkleCommand.Execute(null);
        vm.KrokiOnizlemesi.Should().NotBeEmpty("boş satır TAKBİS'i değiştirmediği için kroki önizlemesi bayatlamaz");
    }

    [Fact]
    public async Task Tablo_Satirindaki_Fazla_Alan_Satir_Hatasi_Olarak_Gosterilmeli()
    {
        var vm = new KunyeDonusumViewModel(await Alan());

        vm.OrtakNoktaMetni = "N1;1;2;3;4;EK";

        vm.OrtakNoktaSatirlari[0].Hata.Should().Contain("fazla alan").And.Contain("EK");
    }

    [Fact]
    public async Task Kunye_Model_Degisince_Canli_Onizleme_Yenilenmeli()
    {
        var vm = new KunyeDonusumViewModel(await Alan());
        foreach (var (no, y1, x1, y2, x2) in new[]
                 {
                     ("N1", "499900", "4399900", "499895", "4399910"),
                     ("N2", "500200", "4399900", "500195", "4399910"),
                 })
        {
            vm.OrtakNoktaEkleCommand.Execute(null);
            Doldur(vm.OrtakNoktaSatirlari[^1], no, y1, x1, y2, x2);
        }

        vm.ParametreSatirlari.Should().Contain(s => s.StartsWith("Ölçek katsayısı = 1.000000000", StringComparison.Ordinal));

        vm.Model = DonusumModeli.Afin2B;

        vm.ParametreSatirlari.Should().BeEmpty();
        vm.DonusumDurumu.Should().Contain("en az 3");
        vm.DonusumHatasi.Should().BeEmpty("yetersiz nokta canlı önizlemede hata sayılmaz");
    }

    // ============================ Görünüm (Avalonia.Headless) ============================

    [AvaloniaFact]
    public async Task Gorunum_Varsayilan_Panelde_TAKBIS_Ve_Kroki_Sil_Sutunu_Gorunmeli_Tablo_Uzerinde_Tekerlek_Sihirbazi_Kaydirmali()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteKrokiAdaptoru()]), await Alan());
        await vm.LastProjeTask;
        vm.Wizard.CurrentIndex = 3;
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 640 };
        w.Show();
        Dispatcher.UIThread.RunJobs();

        foreach (var (tablo, satirlar) in new[] { ("TakbisTablosu", "TakbisSatirlari"), ("KrokiTablosu", "KrokiSatirlari") })
        {
            var kap = Bul<Control>(w, tablo);
            var sil = Bul<ItemsControl>(w, satirlar).GetVisualDescendants().OfType<Button>().Where(b => Equals(b.Content, "✕")).ToList();
            sil.Should().HaveCount(2);
            foreach (var b in sil)
            {
                var sag = b.TranslatePoint(new global::Avalonia.Point(b.Bounds.Width, 0), kap)!.Value.X;
                sag.Should().BeLessThanOrEqualTo(kap.Bounds.Width + 0.5, tablo + " varsayılan 320 px panelde ✕ sütunu dahil kaydırmasız sığmalı");
                b.IsEffectivelyVisible.Should().BeTrue();
            }
        }

        w.Close();

        // Ortak nokta tablosu dar panelde yatay kayar; üzerindeki fare tekerleği sihirbazı dikey kaydırmalı (yutulmamalı).
        var vm1 = new MainWindowViewModel(new CadImportService([new SahteKrokiAdaptoru()]), await Alan());
        await vm1.LastProjeTask;
        vm1.Kunye!.OrtakNoktaMetni = "N1;499900;4399900;499895.00;4399910.00";
        w = new MainWindow { DataContext = vm1, Width = 1440, Height = 640 };
        w.Show();
        ((Expander)Bul<Border>(w, "KunyeDonusumKarti").Child!).IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        var dis = Bul<ScrollViewer>(w, "SihirbazKaydirma");
        var ortak = Bul<ScrollViewer>(w, "OrtakNoktaTablosu");
        ortak.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        dis.Extent.Height.Should().BeGreaterThan(dis.Viewport.Height, "ön koşul: 1. adım dikey kayar");
        var once = dis.Offset.Y;
        var nokta = ortak.TranslatePoint(new global::Avalonia.Point(20, 10), w)!.Value;
        w.MouseMove(nokta);
        w.MouseWheel(nokta, new global::Avalonia.Vector(0, once > 0 ? 1 : -1));
        Dispatcher.UIThread.RunJobs();
        dis.Offset.Y.Should().NotBe(once, "tablo üzerindeki fare tekerleği sihirbazı dikey kaydırmalı");
        w.Close();
    }

    [AvaloniaFact]
    public async Task Gorunum_Oneri_Listesi_Yenilenince_Hucre_Degeri_Ve_Metin_Degismemeli()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteKrokiAdaptoru()]), await Alan());
        await vm.LastProjeTask;
        var h = vm.Hesap!;
        var takbis = h.TakbisMetni;
        var kroki = h.KrokiMetni;
        vm.Wizard.CurrentIndex = 3;
        var w = Ac(vm);

        h.KadastroParselOnerileri.Clear();
        h.KadastroParselOnerileri.Add("K9");
        h.ImarParselOnerileri.Clear();
        Dispatcher.UIThread.RunJobs();

        h.TakbisMetni.Should().Be(takbis);
        h.KrokiMetni.Should().Be(kroki);
        Bul<ItemsControl>(w, "TakbisSatirlari").GetVisualDescendants().OfType<AutoCompleteBox>().Select(a => a.Text).Should().Equal("K1", "K1");
        w.Close();
    }

    [AvaloniaFact]
    public async Task Gorunum_Ortak_Nokta_Metin_Alanindan_Cikinca_Virgul_Normalize_Edilmeli()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteKrokiAdaptoru()]), await Alan());
        await vm.LastProjeTask;
        var w = Ac(vm);
        ((Expander)Bul<Border>(w, "KunyeDonusumKarti").Child!).IsExpanded = true;
        Bul<Expander>(w, "OrtakNoktaMetinAlani").IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        OdakDegistir(Bul<TextBox>(w, "OrtakNoktaKutusu"), Bul<TextBox>(w, "KunyeIl"), "N1;499900;4399900;499895,00;4399910,00");

        vm.Kunye!.OrtakNoktaMetni.Should().Be("N1;499900;4399900;499895.00;4399910.00");
        w.Close();
    }

    private static MainWindow Ac(MainWindowViewModel vm)
    {
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static T Bul<T>(Window w, string ad) where T : Control => w.FindControl<T>(ad) ?? throw new InvalidOperationException(ad + " bulunamadı");

    /// <summary>Düğmenin XAML'de bağlanan komutunu çalıştırır (bağlama yolu ve CommandParameter birlikte doğrulanır).</summary>
    private static void Tikla(Button dugme)
    {
        dugme.Command.Should().NotBeNull(dugme.Name ?? dugme.Content?.ToString());
        dugme.Command!.CanExecute(dugme.CommandParameter).Should().BeTrue();
        dugme.Command.Execute(dugme.CommandParameter);
        Dispatcher.UIThread.RunJobs();
    }

    private static void OdakDegistir(TextBox kaynak, TextBox hedef, string metin)
    {
        kaynak.Focus();
        kaynak.Text = metin;
        hedef.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Gorunum_Birinci_Adim_Ortak_Nokta_Tablosu_Kunye_Dugmesi_Ve_Metin_Alani_Gostermeli()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteKrokiAdaptoru()]), await Alan());
        await vm.LastProjeTask;
        var w = Ac(vm);
        var kart = Bul<Border>(w, "KunyeDonusumKarti");
        ((Expander)kart.Child!).IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        var kunyeDugmesi = Bul<Button>(w, "WebtapuKunyeOkuDugmesi");
        kunyeDugmesi.Content.Should().Be("📄 WebTapu PDF'ten Künye Oku");
        kunyeDugmesi.IsEffectivelyVisible.Should().BeTrue();
        kunyeDugmesi.IsEffectivelyEnabled.Should().BeTrue();
        Bul<ScrollViewer>(w, "OrtakNoktaTablosu").IsEffectivelyVisible.Should().BeTrue("tablo varsayılan görünümdür");
        var metinAlani = Bul<Expander>(w, "OrtakNoktaMetinAlani");
        metinAlani.IsExpanded.Should().BeFalse("toplu yapıştır alanı daraltılmış gelir");
        metinAlani.Header.Should().Be("📋 Metin / Toplu Yapıştır");

        var ekle = Bul<Button>(w, "OrtakNoktaEkleDugmesi");
        ekle.Content.Should().Be("+ Ortak Nokta Ekle");
        Tikla(ekle);
        Dispatcher.UIThread.RunJobs();
        vm.Kunye!.OrtakNoktaSatirlari.Should().ContainSingle();
        var hucreler = Bul<ItemsControl>(w, "OrtakNoktaSatirlari").GetVisualDescendants().OfType<TextBox>().ToList();
        hucreler.Should().HaveCount(5);
        hucreler.Count(SayiKutusu.GetNormalize).Should().Be(4, "Y1, X1, Y2, X2 sayı kutusudur");

        OdakDegistir(hucreler[1], hucreler[0], "499895,25");
        hucreler[1].Text.Should().Be("499895.25", "odaktan çıkınca virgül noktaya normalize edilir");
        vm.Kunye.OrtakNoktaSatirlari[0].Y1.Should().Be("499895.25");

        var mk = Bul<TextBox>(w, "MkKutusu");
        OdakDegistir(mk, Bul<TextBox>(w, "KunyeIl"), "0,12");
        mk.Text.Should().Be("0.12");
        vm.Kunye.MkMetni.Should().Be("0.12");

        Tikla(Bul<ItemsControl>(w, "OrtakNoktaSatirlari").GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "✕")));
        Dispatcher.UIThread.RunJobs();
        vm.Kunye.OrtakNoktaSatirlari.Should().BeEmpty("✕ satırı siler");
        w.Close();
    }

    [AvaloniaFact]
    public async Task Gorunum_Dorduncu_Adim_TAKBIS_Ve_Kroki_Tablolari_Metni_Degistirmeden_Gostermeli()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteKrokiAdaptoru()]), await Alan());
        await vm.LastProjeTask;
        var h = vm.Hesap!;
        var takbis = h.TakbisMetni;
        var kroki = h.KrokiMetni;
        takbis.Should().NotBeEmpty();
        vm.Wizard.CurrentIndex = 3;
        var w = Ac(vm);

        Bul<Control>(w, "TakbisTablosu").IsEffectivelyVisible.Should().BeTrue();
        Bul<Control>(w, "KrokiTablosu").IsEffectivelyVisible.Should().BeTrue();
        var takbisSatirlari = Bul<ItemsControl>(w, "TakbisSatirlari");
        takbisSatirlari.GetVisualDescendants().OfType<AutoCompleteBox>().Should().HaveCount(2);
        takbisSatirlari.GetVisualDescendants().OfType<ComboBox>().Select(c => c.SelectedItem).Should().Equal("E", "H");
        Bul<ItemsControl>(w, "KrokiSatirlari").GetVisualDescendants().OfType<AutoCompleteBox>().Should().HaveCount(4);
        h.TakbisMetni.Should().Be(takbis, "görünüm yüklenirken tablo metni değiştirmemeli");
        h.KrokiMetni.Should().Be(kroki);

        Bul<Button>(w, "TakbisSatiriEkleDugmesi").Content.Should().Be("+ Hisse Satırı Ekle");
        Bul<Button>(w, "KrokiSatiriEkleDugmesi").Content.Should().Be("+ Tahsis Satırı Ekle");
        foreach (var ad in new[] { "TakbisMetinAlani", "KrokiMetinAlani" })
        {
            var e = Bul<Expander>(w, ad);
            e.IsExpanded.Should().BeFalse();
            e.Header.Should().Be("📋 Metin / Toplu Yapıştır");
        }

        var alanKutulari = Bul<ItemsControl>(w, "KrokiSatirlari").GetVisualDescendants().OfType<TextBox>().Where(SayiKutusu.GetNormalize).ToList();
        alanKutulari.Should().HaveCount(2);
        OdakDegistir(alanKutulari[0], alanKutulari[1], "59,99");
        alanKutulari[0].Text.Should().Be("59.99");
        h.KrokiMetni.Should().StartWith("MLK-001;K1;A1;59.99");

        Tikla(Bul<Button>(w, "TakbisSatiriEkleDugmesi"));
        Dispatcher.UIThread.RunJobs();
        h.TakbisSatirlari.Should().HaveCount(3);
        Tikla(takbisSatirlari.GetVisualDescendants().OfType<Button>().Where(b => Equals(b.Content, "✕")).Last());
        Dispatcher.UIThread.RunJobs();
        h.TakbisSatirlari.Should().HaveCount(2);
        h.TakbisMetni.Should().Be(takbis);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Gorunum_Ikinci_Adim_Kadastro_Parseli_Yoksa_Sari_Yonlendirme_Gostermeli()
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik Boş Proje", zaman)).Value;
        var vm = new MainWindowViewModel(new CadImportService([new SahteKrokiAdaptoru()]), await Alan(yol: yol, zaman: zaman));
        await vm.LastProjeTask;
        vm.Wizard.CurrentIndex = 1;
        var w = Ac(vm);

        Bul<Border>(w, "KadastroYonlendirmeKarti").IsEffectivelyVisible.Should().BeTrue();
        Bul<SelectableTextBlock>(w, "KadastroYonlendirmeMetni").Text.Should().Be(Yonlendirme);
        w.Close();

        var dolu = new MainWindowViewModel(new CadImportService([new SahteKrokiAdaptoru()]), await Alan());
        await dolu.LastProjeTask;
        dolu.Wizard.CurrentIndex = 1;
        w = Ac(dolu);
        Bul<Border>(w, "KadastroYonlendirmeKarti").IsEffectivelyVisible.Should().BeFalse("projede kadastro parseli varken yönlendirme gizlidir");
        w.Close();
    }
}

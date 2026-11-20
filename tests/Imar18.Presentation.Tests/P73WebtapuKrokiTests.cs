using System.Numerics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Girdi;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Topoloji;
using Imar18.Presentation.Avalonia.ViewModels;
using Imar18.Presentation.Avalonia.Views;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// P7.3-B (SAHA-006): 4. adım "Hak Eşleştirme &amp; Hisse" — B1 webtapu PDF → önizleme → TAKBİS hisseleri + Malik Rehberi
/// (Kroki'ye dokunmaz); B2 Yaklaşık Yer Krokisi (.NCZ) → önizleme → Kroki satırları (Kip 1 + Kip 2). Proje <see cref="OrnekProje"/>
/// (K1 200 m²; A1 120 m²; Y1 yol) üzerindedir; adlar uydurmadır.
/// </summary>
public sealed class P73WebtapuKrokiTests
{
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
        public int Cagri { get; private set; }

        public Result<TapuKaydi> Oku(Stream kaynak, CancellationToken cancellationToken = default)
        {
            Cagri++;
            return sonuc();
        }
    }

    private sealed class SahteKrokiAdaptoru(Func<Result<CadLayerDocument>> sonuc) : ICadFormatAdapter
    {
        public CadFormat Format => CadFormat.Ncz;

        public bool SupportsExtension(string extension) => true;

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) => sonuc();
    }

    private sealed class FirlatanKesisim : IParselKesisimHesaplayici
    {
        public decimal KesisimAlaniM2(IReadOnlyList<IReadOnlyList<GeodeticPoint>> halkalar, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("overlay failed");
    }

    private static TapuKaydi Kayit(string parsel = "K1") => new(
        "100000001", "ÖRNEKİL", "ÖRNEKİLÇE", "ÇAMLIK Mah.", null, "12", "345", "901", parsel, 200.00m, "Tarla",
        [
            new("900000101", "800000001", "AYŞE YILMAZ", "ALİ", null, BigInteger.One, new BigInteger(2), 100.00m, 200.00m, "Miras", false),
            new("900000102", "800000002", "MEHMET ÇAKIR", "ALİ", null, BigInteger.One, new BigInteger(2), 100.00m, 200.00m, "Miras", false),
        ]);

    private static GeodeticPoint P(decimal x, decimal y) => new(X: OrnekProje.X0 + x, Y: OrnekProje.Y0 + y);

    private static CadLayerDocument Kroki(params (string Etiket, decimal Y1, decimal Y2)[] kullanim) => new(
        CadFormat.Ncz,
        [new CadLayer(0, "KULLANIM")],
        kullanim.Select(k => new CadPolyline("KULLANIM", k.Etiket, null, [P(0, k.Y1), P(0, k.Y2), P(10, k.Y2), P(10, k.Y1), P(0, k.Y1)], true, null)).ToList(),
        [], 0, 0, 0, []);

    private static async Task<(ProjeCalismaAlani Alan, HesapAdimlariViewModel Vm, SahteTapuOkuyucu Okuyucu)> Hazirla(
        Func<Result<TapuKaydi>>? tapu = null, Func<Result<CadLayerDocument>>? kroki = null, bool servislerVar = true,
        IParselKesisimHesaplayici? kesisim = null)
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var okuyucu = new SahteTapuOkuyucu(tapu ?? (() => Result<TapuKaydi>.Success(Kayit())));
        var alan = new ProjeCalismaAlani(
            yol,
            new ApprovalService(yol, zaman, OrnekProje.Topoloji),
            new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru)], new BellekYazici(), OrnekProje.Topoloji),
            OrnekProje.Muhendis)
        {
            TapuOkuyucu = servislerVar ? okuyucu : null,
            Kesisim = servislerVar ? kesisim ?? new NtsParselKesisimHesaplayici() : null,
        };
        var ithal = new CadImportService([new SahteKrokiAdaptoru(kroki ?? (() => Result<CadLayerDocument>.Success(Kroki())))]);
        var vm = new HesapAdimlariViewModel(alan, OrnekProje.Tecviz, servislerVar ? ithal : null);
        await vm.YenileAsync();
        return (alan, vm, okuyucu);
    }

    private static Stream Akis() => new MemoryStream([1, 2, 3]);

    // ============================ B1: webtapu → TAKBİS + Malik Rehberi ============================

    [Fact]
    public async Task Webtapu_Yuklenince_Onizleme_Parsel_Ozeti_Malikler_Ve_Kadastro_Onerisiyle_Olusmali()
    {
        var (_, vm, _) = await Hazirla();
        var takbisOnce = vm.TakbisMetni;

        await vm.WebtapuYukleAsync([Akis()]);

        var o = vm.WebtapuOnizlemeleri.Should().ContainSingle().Subject;
        o.Baslik.Should().Contain("901/K1").And.Contain("200.00").And.Contain("Cilt 12").And.Contain("Sayfa 345").And.Contain("2 aktif malik");
        o.MalikSatirlari.Should().HaveCount(2).And.Contain(s => s.Contains("AYŞE YILMAZ") && s.Contains("1/2"));
        o.KadastroSecenekleri.Should().Equal("K1");
        o.SeciliKadastroParsel.Should().Be("K1", "webtapu parsel no proje kadastro kimliğiyle eşleşti");
        vm.TakbisMetni.Should().Be(takbisOnce, "aktarım onaylanmadan TAKBİS değişmez");
    }

    [Fact]
    public async Task Webtapu_TAKBISe_Aktar_Hisseleri_Yazmali_Rehberi_Kaydetmeli_Ve_Krokiye_Dokunmamali()
    {
        var (alan, vm, _) = await Hazirla();
        var krokiOnce = vm.KrokiMetni;
        await vm.WebtapuYukleAsync([Akis()]);

        await vm.TakbiseAktarCommand.ExecuteAsync(null);

        // SAHA-011 / SORU-055: TAKBİS'e anonim M01 yerine ekran etiketi "AD SOYAD (BABA ADI)" yazılır (eski beklenti M01/M02).
        vm.TakbisMetni.Should().Contain("K1;AYŞE YILMAZ (ALİ);1/2;H").And.Contain("K1;MEHMET ÇAKIR (ALİ);1/2;H").And.NotContain("MLK-001");
        vm.KrokiMetni.Should().Be(krokiOnce, "webtapu yalnız TAKBİS ve Malik Rehberi'ni doldurur");
        alan.Yol.Belge.MalikRehberi.Bul("AYŞE YILMAZ (ALİ)")!.Should().Match<MalikRehberKaydi>(r => r.AdSoyad == "AYŞE YILMAZ" && r.BabaAdi == "ALİ");
        alan.Yol.Belge.MalikRehberi.Parsel("K1")!.Cilt.Should().Be("12");
        vm.WebtapuOnizlemeleri.Should().BeEmpty();
        string.Join("|", alan.Yol.Denetim.Select(d => d.Ayrinti)).Should().NotContain("AYŞE").And.NotContain("800000001");
    }

    [Fact]
    public async Task Webtapu_Kadastro_Parseli_Secilmeden_Aktarilmamali()
    {
        var (alan, vm, _) = await Hazirla(tapu: () => Result<TapuKaydi>.Success(Kayit("999")));
        await vm.WebtapuYukleAsync([Akis()]);
        vm.WebtapuOnizlemeleri.Single().SeciliKadastroParsel.Should().BeNull();
        var takbisOnce = vm.TakbisMetni;

        await vm.TakbiseAktarCommand.ExecuteAsync(null);

        vm.TakbisMetni.Should().Be(takbisOnce);
        vm.TakbisHatasi.Should().Contain("kadastro parseli seçin");
        alan.Yol.Belge.MalikRehberi.Malikler.Should().BeEmpty();
    }

    [Fact]
    public async Task Webtapu_Okunamazsa_Onizleme_Olusmamali_Ve_Turkce_Mesaj_Gosterilmeli()
    {
        var hata = ErrorCatalog.Get(ErrorCodes.GirdPdfBozuk);
        var (_, vm, _) = await Hazirla(tapu: () => Result<TapuKaydi>.Failure(hata));

        await vm.WebtapuYukleAsync([Akis()]);

        vm.WebtapuOnizlemeleri.Should().BeEmpty();
        vm.TakbisHatasi.Should().Contain(hata.UserMessage);
    }

    [Fact]
    public async Task Webtapu_Coklu_Dosya_Her_Biri_Icin_Onizleme_Olusturmali()
    {
        var (_, vm, okuyucu) = await Hazirla();

        await vm.WebtapuYukleAsync([Akis(), Akis()]);

        okuyucu.Cagri.Should().Be(2);
        vm.WebtapuOnizlemeleri.Should().HaveCount(2);
    }

    [Fact]
    public async Task Webtapu_Ve_Kroki_Servisleri_Yoksa_Dugme_Komutlari_Pasif_Olmali()
    {
        var (_, vm, _) = await Hazirla(servislerVar: false);

        vm.WebtapuEtkin.Should().BeFalse();
        vm.KrokiNczEtkin.Should().BeFalse();
        vm.WebtapuIsteCommand.CanExecute(null).Should().BeFalse();
        vm.KrokiNczIsteCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Webtapu_Iste_Komutu_Dosya_Secici_Olayini_Yayinlamali()
    {
        var (_, vm, _) = await Hazirla();
        var (webtapu, kroki) = (0, 0);
        vm.WebtapuDosyasiIstendi += (_, _) => webtapu++;
        vm.KrokiDosyasiIstendi += (_, _) => kroki++;

        vm.WebtapuIsteCommand.Execute(null);
        vm.KrokiNczIsteCommand.Execute(null);

        (webtapu, kroki).Should().Be((1, 1));
    }

    // ============================ B2: Yaklaşık Yer Krokisi → Kroki ============================

    [Fact]
    public async Task Kroki_Kip2_Onizleme_Hisse_Oraninda_Satir_Uretmeli_Aktarinca_Kroki_Kutusunu_Doldurmali()
    {
        var (alan, vm, _) = await Hazirla();
        vm.KrokiMetni = string.Empty;

        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.KrokiOnizlemesi.Should().Contain("MLK-001;K1;A1;60.00").And.Contain("MLK-002;K1;A1;60.00");
        vm.KrokiMetni.Should().BeEmpty("aktarım onaylanmadan Kroki değişmez");

        vm.KrokiyeAktarCommand.Execute(null);

        vm.KrokiMetni.Should().StartWith("# Yaklaşık Yer Krokisi").And.Contain("MLK-001;K1;A1;60.00").And.Contain("MLK-002;K1;A1;60.00");
        vm.KrokiOnizlemesi.Should().BeEmpty();
        await vm.HakUygulaCommand.ExecuteAsync(null);
        alan.Yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Basarili, vm.DurumMetni);
    }

    [Fact]
    public async Task Kroki_Kip1_Etiketli_Kullanim_Poligonuyla_Malikin_Yerine_Gore_Dagitmali()
    {
        var (_, vm, _) = await Hazirla(kroki: () => Result<CadLayerDocument>.Success(Kroki(("MLK-001", 0, 10), ("MLK-002", 10, 20))));

        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");
        vm.KrokiyeAktarCommand.Execute(null);

        vm.KrokiMetni.Should().Contain("MLK-001;K1;A1;100.00").And.Contain("MLK-002;K1;A1;20.00");
        vm.KrokiOzeti.Should().Contain("2 kullanım poligonu");
    }

    [Fact]
    public async Task Kroki_TAKBIS_Gecersizken_Eslestirilmemeli_Ve_TAKBIS_Hatasi_Gosterilmeli()
    {
        var (_, vm, _) = await Hazirla();
        vm.TakbisMetni = "K1;MLK-001;1/3;H";

        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.KrokiOnizlemesi.Should().BeEmpty();
        vm.TakbisHatasi.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Kroki_Dosyasi_Okunamazsa_Kroki_Hatasi_Gosterilmeli()
    {
        var hata = ErrorCatalog.Get(ErrorCodes.GirdCadOkunamadi);
        var (_, vm, _) = await Hazirla(kroki: () => Result<CadLayerDocument>.Failure(hata));

        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.KrokiOnizlemesi.Should().BeEmpty();
        vm.KrokiHatasi.Should().Contain(hata.UserMessage);
    }

    [Fact]
    public async Task Sifirla_Onizlemeleri_Temizlemeli()
    {
        var (_, vm, _) = await Hazirla();
        await vm.WebtapuYukleAsync([Akis()]);
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.Sifirla();

        vm.WebtapuOnizlemeleri.Should().BeEmpty();
        vm.KrokiOnizlemesi.Should().BeEmpty();
        vm.KrokiOzeti.Should().BeEmpty();
    }

    // ============================ Reviewer bulguları (H1, H2, M1–M4) ============================

    [Fact]
    public async Task Webtapu_Yeni_Malik_Kodu_TAKBISte_Elle_Girilmis_Kodla_Cakismamali()
    {
        var (alan, vm, _) = await Hazirla();
        vm.TakbisMetni += Environment.NewLine + "K2;M01;1/1;H";
        await vm.WebtapuYukleAsync([Akis()]);

        await vm.TakbiseAktarCommand.ExecuteAsync(null);

        // SORU-055: yeni kimlikler ad etiketidir; elle girilmiş M01 korunur ve rehberdeki kişilere verilmez.
        vm.TakbisMetni.Should().Contain("K2;M01;1/1;H").And.Contain("K1;AYŞE YILMAZ (ALİ);1/2;H").And.Contain("K1;MEHMET ÇAKIR (ALİ);1/2;H");
        alan.Yol.Belge.MalikRehberi.Bul("M01").Should().BeNull("M01 elle girilmiş başka bir kişidir");
    }

    [Fact]
    public async Task Webtapu_Okuyucu_Istisnasinda_Cokmemeli_ERR_GIRD_003_Gosterilmeli()
    {
        var (_, vm, _) = await Hazirla(tapu: () => throw new InvalidOperationException("regex timeout"));

        await vm.WebtapuYukleAsync([Akis()]);

        vm.WebtapuOnizlemeleri.Should().BeEmpty();
        vm.TakbisHatasi.Should().Contain(ErrorCatalog.Get(ErrorCodes.GirdPdfBozuk).UserMessage);
        vm.IsBusy.Should().BeFalse();
    }

    [Theory]
    [InlineData(ErrorCodes.GirdPdfBozuk)]
    [InlineData(ErrorCodes.TapuHisseToplamiBirDegil)]
    [InlineData(ErrorCodes.TapuDagitimGirdisiGecersiz)]
    public async Task Webtapu_Okuyucu_Hata_Kodunun_Turkce_Mesaji_Gosterilmeli(string kod)
    {
        var hata = ErrorCatalog.Get(kod);
        var (_, vm, _) = await Hazirla(tapu: () => Result<TapuKaydi>.Failure(hata));

        await vm.WebtapuYukleAsync([Akis()]);

        vm.TakbisHatasi.Should().Contain(hata.UserMessage);
    }

    [Fact]
    public async Task Webtapu_Durum_Ve_Hata_Metinleri_Ad_Ve_Kisi_No_Icermemeli()
    {
        var (_, vm, _) = await Hazirla();
        var metinler = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(vm.DurumMetni) or nameof(vm.TakbisHatasi))
            {
                metinler.Add(vm.DurumMetni + "|" + vm.TakbisHatasi);
            }
        };

        await vm.WebtapuYukleAsync([Akis()]);
        await vm.TakbiseAktarCommand.ExecuteAsync(null);

        metinler.Should().NotBeEmpty();
        string.Join("|", metinler).Should().NotContain("AYŞE").And.NotContain("MEHMET").And.NotContain("800000001").And.NotContain("900000101");
    }

    [Fact]
    public async Task Webtapu_Ayni_Kadastro_Parseline_Iki_Belge_Eslenirse_Aktarilmamali()
    {
        var (alan, vm, _) = await Hazirla();
        await vm.WebtapuYukleAsync([Akis(), Akis()]);
        var takbisOnce = vm.TakbisMetni;

        await vm.TakbiseAktarCommand.ExecuteAsync(null);

        vm.TakbisMetni.Should().Be(takbisOnce);
        vm.TakbisHatasi.Should().Contain("birden çok");
        alan.Yol.Belge.MalikRehberi.Malikler.Should().BeEmpty();
    }

    [Fact]
    public async Task Webtapu_Onizleme_Sonrasi_Kadastro_Parseli_Projeden_Kalkarsa_Aktarilmamali()
    {
        var (alan, vm, _) = await Hazirla();
        await vm.WebtapuYukleAsync([Akis()]);
        (await alan.Yol.GonderAsync(new ProjeyiSifirlaKomutu(), "test")).IsSuccess.Should().BeTrue();

        await vm.TakbiseAktarCommand.ExecuteAsync(null);

        vm.TakbisHatasi.Should().Contain("projede yok");
        alan.Yol.Belge.MalikRehberi.Malikler.Should().BeEmpty();
    }

    [Fact]
    public async Task Kroki_Kesisim_Istisnasinda_Cokmemeli_Kroki_Hatasi_Gosterilmeli()
    {
        var (_, vm, _) = await Hazirla(kesisim: new FirlatanKesisim());

        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.KrokiOnizlemesi.Should().BeEmpty();
        vm.KrokiHatasi.Should().Contain(ErrorCatalog.Get(ErrorCodes.TopoAnalizYapilamadi).UserMessage);
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Kroki_Yukleme_Sirasinda_TAKBIS_Degisirse_Onizleme_Uretilmemeli()
    {
        HesapAdimlariViewModel? hedef = null;
        var (_, vm, _) = await Hazirla(kroki: () =>
        {
            hedef!.TakbisMetni = "K1;MLK-001;1/1;H";
            return Result<CadLayerDocument>.Success(Kroki());
        });
        hedef = vm;

        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.KrokiOnizlemesi.Should().BeEmpty();
        vm.KrokiyeAktarCommand.CanExecute(null).Should().BeFalse();
        vm.KrokiHatasi.Should().Contain("TAKBİS");
    }

    [Fact]
    public async Task Kroki_Onizlemesi_TAKBIS_Degisince_Temizlenmeli()
    {
        var (_, vm, _) = await Hazirla();
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");
        vm.KrokiOnizlemesi.Should().NotBeEmpty();

        vm.TakbisMetni += Environment.NewLine;

        vm.KrokiOnizlemesi.Should().BeEmpty();
        vm.KrokiOzeti.Should().BeEmpty();
        vm.KrokiyeAktarCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Kroki_Onizleme_Sonrasi_Proje_Degisirse_Aktarilmamali()
    {
        var (alan, vm, _) = await Hazirla();
        vm.KrokiMetni = string.Empty;
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");
        (await alan.Yol.GonderAsync(new MalikRehberiAyarlaKomutu(new MalikRehberi([new MalikRehberKaydi("MLK-001", "X", null, null)], [])), "test"))
            .IsSuccess.Should().BeTrue();

        vm.KrokiyeAktarCommand.Execute(null);

        vm.KrokiMetni.Should().BeEmpty();
        vm.KrokiHatasi.Should().Contain("yeniden yükleyin");
    }

    [Fact]
    public async Task Kroki_Proje_Alaniyla_Ortusmezse_Turkce_Neden_Gosterilmeli()
    {
        var uzak = new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "KULLANIM")],
            [new CadPolyline("KULLANIM", "MLK-001", null, [P(1000, 0), P(1000, 10), P(1010, 10), P(1010, 0), P(1000, 0)], true, null)], [], 0, 0, 0, []);
        var (_, vm, _) = await Hazirla(kroki: () => Result<CadLayerDocument>.Success(uzak));

        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.KrokiOnizlemesi.Should().BeEmpty();
        vm.KrokiHatasi.Should().Contain("örtüşmüyor");
    }

    [Fact]
    public async Task Kroki_TAKBIS_Bossa_Eslestirilmemeli_Ve_Yonlendirme_Mesaji_Gosterilmeli()
    {
        var (_, vm, _) = await Hazirla();
        vm.TakbisMetni = string.Empty;

        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.KrokiOnizlemesi.Should().BeEmpty();
        vm.TakbisHatasi.Should().Contain("webtapu");
    }

    [Fact]
    public async Task Kroki_Vazgec_Onizlemeyi_Ve_Ozeti_Temizlemeli()
    {
        var (_, vm, _) = await Hazirla();
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.KrokiOnizlemesiniKaldirCommand.Execute(null);

        vm.KrokiOnizlemesi.Should().BeEmpty();
        vm.KrokiOzeti.Should().BeEmpty();
    }

    // ============================ Görünüm ============================

    [AvaloniaFact]
    public async Task Gorunum_Dorduncu_Adimda_Webtapu_Ve_Yaklasik_Yer_Krokisi_Dugmelerini_Gostermeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var alan = new ProjeCalismaAlani(yol, new ApprovalService(yol, zaman, OrnekProje.Topoloji),
            new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru)], new BellekYazici(), OrnekProje.Topoloji), OrnekProje.Muhendis)
        {
            TapuOkuyucu = new SahteTapuOkuyucu(() => Result<TapuKaydi>.Success(Kayit())),
            Kesisim = new NtsParselKesisimHesaplayici(),
        };
        var vm = new MainWindowViewModel(new CadImportService([new SahteKrokiAdaptoru(() => Result<CadLayerDocument>.Success(Kroki()))]), alan);
        await vm.LastProjeTask;
        vm.Wizard.CurrentIndex = 3;
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();

        var webtapu = w.FindControl<Button>("WebtapuYukleDugmesi");
        var kroki = w.FindControl<Button>("KrokiNczYukleDugmesi");
        webtapu.Should().NotBeNull();
        kroki.Should().NotBeNull();
        webtapu!.IsEffectivelyVisible.Should().BeTrue();
        webtapu.IsEffectivelyEnabled.Should().BeTrue();
        kroki!.IsEffectivelyEnabled.Should().BeTrue();
        w.Close();
    }
}

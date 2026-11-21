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
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.GeometryEngine.Subdivision;
using Imar18.GeometryEngine.Topoloji;
using Imar18.Presentation.Avalonia.ViewModels;
using Imar18.Presentation.Avalonia.Views;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// SAHA-019: 4. adım "✏️ Müşterek Dağıtımı Düzenle" — otomatik kroki dağılımında müşterek parsel/alan değiştirme → canlı fark
/// (hak sahibi + parsel) → "✅ Düzenlemeyi Önizlemeye Uygula" (kart, TAM/Müşterek ve tuval etiketleri) → "🔄 Otomatik Dağıtıma Dön" →
/// onayla ve uygula ile özel dağılımın projeye (Kroki satırları + hak eşleştirmesi) yazılması. Sentetik proje (SAHA-015 UI ile aynı
/// geometri; adlar uydurma, konum gerçek değil; muvafakatli): 101/1 ALİ 880, 101/2 AYŞE 660, 101/3 FATMA 660, 102/1..5 × 440 (TAM).
/// Elle referans: 101/2 ve 101/3 AYŞE 330 + FATMA 330 → her biri 33000/66000 (el hesabı).
/// </summary>
public sealed class Saha019MusterekDuzenlemeTests
{
    private const decimal X0 = 4_400_000m;
    private const decimal Y0 = 500_000m;
    private const string Ayse = "AYŞE KAYA (MEHMET)";
    private const string Fatma = "FATMA DEMİR (OSMAN)";

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

    private sealed class SahteKrokiAdaptoru(CadLayerDocument belge) : ICadFormatAdapter
    {
        public CadFormat Format => CadFormat.Ncz;

        public bool SupportsExtension(string extension) => true;

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) => Result<CadLayerDocument>.Success(belge);
    }

    private static GeodeticPoint P(decimal x, decimal y) => new(X: X0 + x, Y: Y0 + y);

    private static ParselGeometrisi Dikdortgen(string id, ParselRolu rol, decimal x1, decimal x2) => new(id, rol, [P(x1, 0), P(x1, 100), P(x2, 100), P(x2, 0)]);

    private const string Takbis = """
        K1;ALİ VELİ (HASAN);1/5;E
        K1;AYŞE KAYA (MEHMET);3/20;E
        K1;FATMA DEMİR (OSMAN);3/20;E
        K1;HÜSEYİN ÇELİK (İBRAHİM);1/10;E
        K1;ZEYNEP ARSLAN (YUSUF);1/10;E
        K1;MUSTAFA ÖZTÜRK (KEMAL);1/10;E
        K1;EMİNE ŞAHİN (HALİL);1/10;E
        K1;HATİCE YILDIZ (RECEP);1/10;E
        """;

    private static CadPolyline Cizgi(decimal x1, decimal y1, decimal x2, decimal y2) => new("0", "BOLME", null, [P(x1, y1), P(x2, y2)], false, null);

    private static CadText Yazi(string metin, decimal x, decimal y) => new("0", metin, P(x, y)) { Merkez = P(x, y) };

    private static CadLayerDocument Kroki() => new(
        CadFormat.Ncz,
        [new CadLayer(0, "0")],
        [Cizgi(28, 30, 50, 36), Cizgi(28, 65, 50, 71), Cizgi(0, 18, 22, 22), Cizgi(1, 39, 21, 41), Cizgi(0, 58, 22, 62), Cizgi(0, 79, 23, 81)],
        [
            Yazi("ALİ VELİ", 39, 15), Yazi("AYŞE KAYA", 39, 50), Yazi("FATMA DEMİR", 39, 85),
            Yazi("HÜSEYİN ÇELİK", 11, 9), Yazi("ZEYNEP ARSLAN", 11, 30), Yazi("MUSTAFA ÖZTÜRK", 11, 50), Yazi("EMİNE ŞAHİN", 11, 70), Yazi("HATİCE YILDIZ", 11, 90),
        ],
        0, 0, 0, []);

    private static async Task<ProjeCalismaAlani> AlanAsync()
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik SAHA-019", zaman)).Value;
        IProjeKomutu[] komutlar =
        [
            new ProjeCrsAyarlaKomutu(OrnekProje.Crs()),
            new ParselGeometrileriAyarlaKomutu([Dikdortgen("K1", ParselRolu.Kadastro, 0m, 50m), Dikdortgen("101", ParselRolu.Imar, 28m, 50m), Dikdortgen("Y1", ParselRolu.YolTerk, 22m, 28m), Dikdortgen("102", ParselRolu.Imar, 0m, 22m)]),
            new TecvizGirdileriAyarlaKomutu([new TecvizParselGirdisi("K1", 5000.00m, 5000.00m, OrnekProje.Tecviz())], null),
            new DopGirdisiAyarlaKomutu(DopGirdisi.Olustur(5000.00m, 4400.00m).Value),
        ];
        foreach (var k in komutlar)
        {
            (await yol.GonderAsync(k, OrnekProje.Muhendis)).IsSuccess.Should().BeTrue(k.Ad);
        }

        return new ProjeCalismaAlani(
            yol,
            new ApprovalService(yol, zaman, OrnekProje.Topoloji),
            new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru)], new BellekYazici(), OrnekProje.Topoloji),
            OrnekProje.Muhendis)
        {
            Kesisim = new NtsParselKesisimHesaplayici(),
            KrokiBolucu = new KrokiBlokBolucu(),
        };
    }

    private static async Task<(ProjeCalismaAlani Alan, HesapAdimlariViewModel Vm)> TaslakliAsync()
    {
        var alan = await AlanAsync();
        var vm = new HesapAdimlariViewModel(alan, OrnekProje.Tecviz, new CadImportService([new SahteKrokiAdaptoru(Kroki())]));
        await vm.YenileAsync();
        vm.TakbisMetni = Takbis;
        await vm.YaklasikYerKrokisiYukleAsync(new MemoryStream([1]), ".ncz");
        vm.KrokiParselasyonTaslagiVar.Should().BeTrue();
        return (alan, vm);
    }

    private static MusterekSatirVm Satir(HesapAdimlariViewModel vm, string malik, string parsel) =>
        vm.MusterekSatirlari.Single(s => s.Malik!.Id == malik && s.ImarParseli == parsel);

    /// <summary>101/2 ve 101/3'ü AYŞE 330 + FATMA 330 müşterek yapar (Numpad virgülü dahil).</summary>
    private static void MusterekYap(HesapAdimlariViewModel vm)
    {
        Satir(vm, Ayse, "101/2").AlanMetni = "330,00";
        Satir(vm, Fatma, "101/3").AlanMetni = "330";
        vm.MusterekSatirEkleCommand.Execute(null);
        var y1 = vm.MusterekSatirlari[^1];
        y1.ImarParseli = "101/2";
        y1.Malik = vm.MusterekMalikSecenekleri.Single(m => m.Id == Fatma);
        y1.AlanMetni = "330";
        vm.MusterekSatirEkleCommand.Execute(null);
        var y2 = vm.MusterekSatirlari[^1];
        y2.ImarParseli = "101/3";
        y2.Malik = vm.MusterekMalikSecenekleri.Single(m => m.Id == Ayse);
        y2.AlanMetni = "330.00";
    }

    [Fact]
    public async Task Duzenle_Paneli_Otomatik_Dagitimla_Acilmali_Tum_Farklar_Sifir_Olmali()
    {
        var (_, vm) = await TaslakliAsync();

        vm.MusterekDuzenleCommand.CanExecute(null).Should().BeTrue();
        vm.MusterekDuzenleCommand.Execute(null);

        vm.MusterekDuzenlemeAcik.Should().BeTrue();
        vm.MusterekSatirlari.Should().HaveCount(8);
        vm.MusterekParselSecenekleri.Should().Equal("101/1", "101/2", "101/3", "102/1", "102/2", "102/3", "102/4", "102/5");
        vm.MusterekMalikSecenekleri.Select(m => m.Ad).Should().Contain(["ALİ VELİ", "AYŞE KAYA"], "baba adısız görünür ad");
        vm.MusterekHakOzetleri.Should().HaveCount(8).And.OnlyContain(h => h.Dengeli && h.FarkMetni == "0.00");
        vm.MusterekHakOzetleri.Single(h => h.Malik == "ALİ VELİ").NetHakMetni.Should().Be("880.00");
        vm.MusterekParselKontrolleri.Should().HaveCount(8).And.OnlyContain(p => p.Dengeli);
        vm.MusterekDengeli.Should().BeTrue();
        Satir(vm, Ayse, "101/2").KadastroKaynagi.Should().Be("K1");
        Satir(vm, Ayse, "101/2").KadastroSecenekleri.Should().Equal("K1");
    }

    [Fact]
    public async Task Alan_Degisince_Canli_Fark_Kirmizi_Olmali_Dengesizken_Uygula_Onizlemeyi_Degistirmemeli()
    {
        var (_, vm) = await TaslakliAsync();
        vm.MusterekDuzenleCommand.Execute(null);
        var once = vm.KrokiParselasyonOnizlemesi;

        Satir(vm, Ayse, "101/2").AlanMetni = "330,00";

        var ayse = vm.MusterekHakOzetleri.Single(h => h.Malik == "AYŞE KAYA");
        (ayse.DagitilanMetni, ayse.FarkMetni, ayse.Dengeli).Should().Be(("330.00", "+330.00", false));
        vm.MusterekParselKontrolleri.Single(p => p.Kod == "101/2").Metin.Should().Be("✗ 101/2: parsel 660.00 m² · Σ hisse 330.00 m² · fark +330.00 m²");
        vm.MusterekDengeli.Should().BeFalse();

        Satir(vm, Ayse, "101/2").AlanMetni = "33,0,0";
        Satir(vm, Ayse, "101/2").AlanHatali.Should().BeTrue("binlik / çoklu ayırıcı reddedilir");
        vm.MusterekDuzenlemeyiUygulaCommand.Execute(null);

        vm.MusterekDurumu.Should().StartWith("⚠ ");
        vm.KrokiParselasyonOnizlemesi.Should().BeSameAs(once, "dengesiz düzenleme önizlemeye yazılmaz");
    }

    [Fact]
    public async Task Onizlemeye_Uygula_Kart_Ve_Tuval_Etiketlerini_Musterek_Yapmali_Otomatige_Don_Geri_Almali_Proje_Degismemeli()
    {
        var (alan, vm) = await TaslakliAsync();
        var proje = alan.Yol.Belge;
        vm.MusterekDuzenleCommand.Execute(null);
        MusterekYap(vm);
        vm.MusterekDengeli.Should().BeTrue();
        vm.MusterekHakOzetleri.Should().OnlyContain(h => h.Dengeli);

        vm.MusterekDuzenlemeyiUygulaCommand.Execute(null);

        vm.MusterekElleDuzenlendi.Should().BeTrue();
        vm.KrokiParselDagilimi.Should().Contain("101/2 • 660.00 m² • Müşterek (2 hissedar: AYŞE KAYA 33000/66000, FATMA DEMİR 33000/66000)")
            .And.Contain("101/3 • 660.00 m² • Müşterek (2 hissedar: FATMA DEMİR 33000/66000, AYŞE KAYA 33000/66000)")
            .And.Contain("101/1 • 880.00 m² • Müstakil (TAM)");
        vm.ParselasyonKatmani!.Taslak.Should().BeTrue();
        vm.ParselasyonKatmani.Parseller.Single(p => p.Kod == "101/2").Etiket.Should().Be("101/2 • AYŞE KAYA +1 • 660.00 m² • Müşterek");
        vm.KrokiOnizlemesi.Should().Contain("FATMA DEMİR (OSMAN);K1;101/2;330.00").And.Contain("AYŞE KAYA (MEHMET);K1;101/3;330.00");
        vm.KrokiParselasyonOzeti.Should().Contain("6 müstakil (TAM) · 2 müşterek (pay/payda) · ✏️ elle düzenlendi");
        vm.KrokiUyarilari.Should().Contain(KrokiDagilimDuzenleyici.ElleDuzenlemeUyarisi);
        vm.KrokiUyarilari.Should().NotContain(KrokiDagilimDuzenleyici.Md17DenetlenemediUyarisi, "TAKBİS okunur; muvafakatli, ihlal yok");
        vm.KrokiUyarilari.Should().NotContain(u => u.Contains("Md. 17/1-(d)", StringComparison.Ordinal));
        alan.Yol.Belge.Should().BeSameAs(proje, "önizleme projeye yazılmaz");

        vm.OtomatikDagitimaDonCommand.Execute(null);

        vm.MusterekElleDuzenlendi.Should().BeFalse();
        vm.KrokiParselDagilimi.Should().Contain("101/2 • 660.00 m² • Müstakil (TAM)").And.Contain("101/3 • 660.00 m² • Müstakil (TAM)");
        vm.ParselasyonKatmani!.Parseller.Single(p => p.Kod == "101/2").Etiket.Should().Be("101/2 • AYŞE KAYA • 660.00 m² • TAM");
        vm.MusterekSatirlari.Should().HaveCount(8, "satırlar otomatik sonuca döner");
        vm.MusterekDurumu.Should().StartWith("🔄 Otomatik dağıtıma dönüldü");
        alan.Yol.Belge.Should().BeSameAs(proje);
    }

    [Fact]
    public async Task Onaylaninca_Ozel_Dagilim_Kroki_Ve_Hak_Eslestirmesine_Yazilmali_Hak_Bulgusuz_Olmali()
    {
        var (alan, vm) = await TaslakliAsync();
        vm.MusterekDuzenleCommand.Execute(null);
        MusterekYap(vm);
        vm.MusterekDuzenlemeyiUygulaCommand.Execute(null);

        await vm.ParselasyonCiziminiOnaylaCommand.ExecuteAsync(null);

        var b = alan.Yol.Belge;
        b.Parseller.Select(p => p.ParselId).Should().Contain(["101/1", "101/2", "101/3", "102/5"]);
        vm.KrokiMetni.Should().Contain("FATMA DEMİR (OSMAN);K1;101/2;330.00").And.Contain("AYŞE KAYA (MEHMET);K1;101/2;330.00");
        b.HakEslestirme.Should().NotBeNull();
        b.HakEslestirme!.Eslestirme.Where(e => e.ImarParselKodu == "101/2").Select(e => (e.MalikId, e.AlanM2))
            .Should().BeEquivalentTo([(Ayse, 330.00m), (Fatma, 330.00m)]);
        var hak = vm.Degerlendirme!.Hak!;
        hak.Bulgular.Where(x => x.Code.StartsWith("ERR-TAPU", StringComparison.Ordinal)).Should().BeEmpty("Σ parsel = alan, Σ malik = net hak (0.00 m²)");
        hak.ImarParselleri.Single(p => p.GeciciKod == "101/2").Paydaslar.Select(x => x.Hisse.ToString()).Should().BeEquivalentTo("33000/66000", "33000/66000");
        hak.ImarParselleri.Single(p => p.GeciciKod == "101/1").Paydaslar.Single().Hisse.ToString().Should().Be("TAM");
        vm.ParselasyonKatmani!.Taslak.Should().BeFalse();
        vm.ParselasyonKatmani.Parseller.Single(p => p.Kod == "101/3").Etiket.Should().EndWith(" • Müşterek");
        vm.DurumMetni.Should().Contain("(6 müstakil (TAM), 2 müşterek (pay/payda))");
        vm.MusterekDuzenlemeAcik.Should().BeFalse("onaydan sonra taslak ve düzenleme paneli kalkar");
        vm.MusterekDuzenleCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Onizlemeye_Uygulanmamis_Duzenleme_Varken_Onay_Reddedilmeli_Proje_Degismemeli()
    {
        var (alan, vm) = await TaslakliAsync();
        var proje = alan.Yol.Belge;
        vm.MusterekDuzenleCommand.Execute(null);
        MusterekYap(vm);

        await vm.ParselasyonCiziminiOnaylaCommand.ExecuteAsync(null);

        alan.Yol.Belge.Should().BeSameAs(proje, "uygulanmamış düzenleme sessizce atılıp otomatik dağılım yazılmaz");
        vm.DurumMetni.Should().Be(HesapAdimlariViewModel.MusterekBekleyenDuzenlemeUyarisi);
        vm.KrokiParselasyonTaslagiVar.Should().BeTrue();

        vm.MusterekDuzenlemeyiUygulaCommand.Execute(null);
        await vm.ParselasyonCiziminiOnaylaCommand.ExecuteAsync(null);
        alan.Yol.Belge.HakEslestirme!.Eslestirme.Count(e => e.ImarParselKodu == "101/2").Should().Be(2, "uygulandıktan sonra onay geçer");
    }

    [AvaloniaFact]
    public async Task Ana_Pencere_Duzenle_Dugmesi_Onayla_Yaninda_Olmali_Panel_Ve_Eylem_Dugmeleri_Turkce_Gorunmeli()
    {
        var alan = await AlanAsync();
        var mw = new MainWindowViewModel(new CadImportService([new SahteKrokiAdaptoru(Kroki())]), alan, new CadImportService([new SahteKrokiAdaptoru(Kroki())]));
        await mw.LastProjeTask;
        mw.Hesap!.TakbisMetni = Takbis;
        var w = new MainWindow { DataContext = mw, Width = 1440, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();

        await mw.Hesap.YaklasikYerKrokisiYukleAsync(new MemoryStream([1]), ".ncz");
        Dispatcher.UIThread.RunJobs();

        var duzenle = w.FindControl<Button>("MusterekDuzenleDugmesi")!;
        duzenle.Content.Should().Be("✏️ Müşterek Dağıtımı Düzenle");
        ((Grid)duzenle.Parent!).Children.OfType<Button>().Select(b => b.Name).Should().Contain("ParselasyonOnaylaDugmesi", "Onayla düğmesinin yanında");
        w.FindControl<Border>("MusterekDuzenlemePaneli")!.IsVisible.Should().BeFalse();

        duzenle.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        w.FindControl<Border>("MusterekDuzenlemePaneli")!.IsVisible.Should().BeTrue();
        w.FindControl<Button>("MusterekSatirEkleDugmesi")!.Content.Should().Be("+ Hissedar Satırı Ekle");
        w.FindControl<Button>("MusterekUygulaDugmesi")!.Content.Should().Be("✅ Düzenlemeyi Önizlemeye Uygula");
        w.FindControl<Button>("OtomatikDagitimaDonDugmesi")!.Content.Should().Be("🔄 Otomatik Dağıtıma Dön");
        w.FindControl<ItemsControl>("MusterekSatirTablosu")!.ItemCount.Should().Be(8);
        w.FindControl<ItemsControl>("MusterekHakOzetiListesi")!.ItemCount.Should().Be(8);
        w.Close();
    }
}

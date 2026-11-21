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
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.ViewModels;
using Imar18.Presentation.Avalonia.Views;
using SkiaSharp;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// SAHA-015 (SORU-044): 4. adımda Yaklaşık Yer Krokisi → kesin alanlı parselasyon taslağı → tuval önizlemesi → Onayla / İptal.
/// Sentetik proje (adlar uydurma, konum gerçek değil): K1 50 × 100 m = 5000 m²; blok 101 (2200 m²), yol Y1 (600 m²), blok 102
/// (2200 m²); DOPO 0.12. Hisseler 1/5, 3/20, 3/20, 5 × 1/10 → net hak 880, 660, 660, 5 × 440 (el hesabı).
/// </summary>
public sealed class Saha015KrokiParselasyonUiTests
{
    private const decimal X0 = 4_400_000m;
    private const decimal Y0 = 500_000m;

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

    private static IReadOnlyList<ParselGeometrisi> Parseller() =>
    [
        Dikdortgen("K1", ParselRolu.Kadastro, 0m, 50m),
        Dikdortgen("101", ParselRolu.Imar, 28m, 50m),
        Dikdortgen("Y1", ParselRolu.YolTerk, 22m, 28m),
        Dikdortgen("102", ParselRolu.Imar, 0m, 22m),
    ];

    private const string Takbis = """
        K1;ALİ VELİ (HASAN);1/5;H
        K1;AYŞE KAYA (MEHMET);3/20;H
        K1;FATMA DEMİR (OSMAN);3/20;H
        K1;HÜSEYİN ÇELİK (İBRAHİM);1/10;H
        K1;ZEYNEP ARSLAN (YUSUF);1/10;H
        K1;MUSTAFA ÖZTÜRK (KEMAL);1/10;H
        K1;EMİNE ŞAHİN (HALİL);1/10;H
        K1;HATİCE YILDIZ (RECEP);1/10;H
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

    private static async Task<ProjeCalismaAlani> AlanAsync(bool bolucuVar = true, bool dopVar = true)
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik SAHA-015", zaman)).Value;
        List<IProjeKomutu> komutlar =
        [
            new ProjeCrsAyarlaKomutu(OrnekProje.Crs()),
            new ParselGeometrileriAyarlaKomutu(Parseller()),
            new TecvizGirdileriAyarlaKomutu([new TecvizParselGirdisi("K1", 5000.00m, 5000.00m, OrnekProje.Tecviz())], null),
        ];
        if (dopVar)
        {
            komutlar.Add(new DopGirdisiAyarlaKomutu(DopGirdisi.Olustur(5000.00m, 4400.00m).Value));
        }

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
            KrokiBolucu = bolucuVar ? new KrokiBlokBolucu() : null,
        };
    }

    private static async Task<(ProjeCalismaAlani Alan, HesapAdimlariViewModel Vm)> HazirlaAsync(bool bolucuVar = true, bool dopVar = true)
    {
        var alan = await AlanAsync(bolucuVar, dopVar);
        var vm = new HesapAdimlariViewModel(alan, OrnekProje.Tecviz, new CadImportService([new SahteKrokiAdaptoru(Kroki())]));
        await vm.YenileAsync();
        vm.TakbisMetni = Takbis;
        return (alan, vm);
    }

    private static Stream Akis() => new MemoryStream([1]);

    [Fact]
    public async Task Kroki_Yuklenince_Tuvalde_Kesikli_Taslak_Katman_Ve_Etiketler_Olusmali_Proje_Degismemeli()
    {
        var (alan, vm) = await HazirlaAsync();
        var once = alan.Yol.Belge;

        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.KrokiParselasyonTaslagiVar.Should().BeTrue();
        vm.KrokiEslestirmeOnizlemesiVar.Should().BeFalse("bölme çizgili kroki Kip 1/2 satır önizlemesine düşmez");
        var katman = vm.ParselasyonKatmani!;
        katman.Taslak.Should().BeTrue();
        katman.Parseller.Select(p => p.Etiket).Should().Equal(
            "101/1 • ALİ VELİ • 880.00 m² • TAM", "101/2 • AYŞE KAYA • 660.00 m² • TAM", "101/3 • FATMA DEMİR • 660.00 m² • TAM",
            "102/1 • HÜSEYİN ÇELİK • 440.00 m² • TAM", "102/2 • ZEYNEP ARSLAN • 440.00 m² • TAM", "102/3 • MUSTAFA ÖZTÜRK • 440.00 m² • TAM",
            "102/4 • EMİNE ŞAHİN • 440.00 m² • TAM", "102/5 • HATİCE YILDIZ • 440.00 m² • TAM");
        vm.KrokiParselDagilimi.Should().HaveCount(8).And.Contain("101/1 • 880.00 m² • Müstakil (TAM)");
        vm.KrokiParselDagilimiVar.Should().BeTrue();
        vm.KrokiParselasyonOzeti.Should().Contain("2 blok · 8 parsel · Σ 4400.00 m²");
        vm.KrokiOnizlemesi.Should().Contain("ALİ VELİ (HASAN);K1;101/1;880.00");
        vm.KrokiMetni.Should().BeEmpty("onaylanmadan Kroki kutusu değişmez");
        alan.Yol.Belge.Should().BeSameAs(once, "taslak projeye yazılmaz");
        vm.ParselasyonCiziminiOnaylaCommand.CanExecute(null).Should().BeTrue();
        vm.ParselasyonTaslaginiIptalEtCommand.CanExecute(null).Should().BeTrue();
        vm.DurumMetni.Should().Contain("8 kesin alanlı parsel taslağı");
    }

    [Fact]
    public async Task Taslagi_Iptal_Et_Tuvaldeki_Katmani_Kaldirmali_Projeye_Dokunmamali()
    {
        var (alan, vm) = await HazirlaAsync();
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");
        var once = alan.Yol.Belge;

        vm.ParselasyonTaslaginiIptalEtCommand.Execute(null);

        vm.ParselasyonKatmani.Should().BeNull();
        vm.KrokiParselasyonTaslagiVar.Should().BeFalse();
        vm.KrokiOnizlemesi.Should().BeEmpty();
        alan.Yol.Belge.Should().BeSameAs(once);
        vm.DurumMetni.Should().Be("Parselasyon taslağı iptal edildi; proje geometrisi değişmedi.");
    }

    [Fact]
    public async Task Onayla_Ve_Uygula_Yeni_Parselleri_Projeye_Yazmali_Kroki_Ve_Hak_Eslestirmesini_Doldurmali()
    {
        var (alan, vm) = await HazirlaAsync();
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        await vm.ParselasyonCiziminiOnaylaCommand.ExecuteAsync(null);

        var b = alan.Yol.Belge;
        b.Parseller.Select(p => p.ParselId).Should().Equal("K1", "101/1", "101/2", "101/3", "Y1", "102/1", "102/2", "102/3", "102/4", "102/5");
        b.Parseller.Where(p => p.ParselId.Contains('/')).Should().OnlyContain(p => p.Rol == ParselRolu.Imar);
        vm.KrokiMetni.Should().Contain("ALİ VELİ (HASAN);K1;101/1;880.00").And.Contain("HATİCE YILDIZ (RECEP);K1;102/5;440.00");
        vm.KrokiSatirlari.Should().HaveCount(8, "kutucuklu Kroki tablosu metinle senkron dolar");
        b.HakEslestirme.Should().NotBeNull("DOP uygulanmış olduğundan hak eşleştirmesi de yazılır");
        b.HakEslestirme!.ImarParselleri.Select(p => p.GeciciKod).Should().BeEquivalentTo("101/1", "101/2", "101/3", "102/1", "102/2", "102/3", "102/4", "102/5");
        b.HakEslestirme.Eslestirme.Should().HaveCount(8);
        vm.ParselasyonKatmani!.Taslak.Should().BeFalse("tuval uygulanmış kesin parselasyona geçer");
        vm.ParselasyonKatmani.Parseller.Should().HaveCount(8);
        vm.KrokiParselasyonTaslagiVar.Should().BeFalse();
        vm.DurumMetni.Should().StartWith("✅ Parselasyon çizimi onaylandı ve uygulandı: 8 kesin parsel");
        var denetim = string.Join("|", alan.Yol.Denetim.Select(d => d.Ayrinti));
        denetim.Should().Contain("parselSayisi=8;blokSayisi=2;kaynak=yaklasikYerKrokisi").And.NotContain("ALİ").And.NotContain("VELİ");
    }

    [Fact]
    public async Task DOP_Yoksa_Onay_Geometriyi_Yazmali_Hak_Icin_Yonlendirmeli()
    {
        var (alan, vm) = await HazirlaAsync(dopVar: false);
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");
        vm.KrokiUyarilari.Should().Contain(u => u.StartsWith("DOP girdisi yok", StringComparison.Ordinal));

        await vm.ParselasyonCiziminiOnaylaCommand.ExecuteAsync(null);

        alan.Yol.Belge.Parseller.Should().HaveCount(10);
        alan.Yol.Belge.HakEslestirme.Should().BeNull();
        vm.DurumMetni.Should().EndWith("Hak eşleştirmesi için 3. adımda DOP'u uygulayıp \"Hak Eşleştirmeyi Uygula\" deyin.");
    }

    [Fact]
    public async Task Proje_Onizlemeden_Sonra_Degistiyse_Onay_Bayat_Taslagi_Yazmamali()
    {
        var (alan, vm) = await HazirlaAsync();
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");
        await alan.Yol.GonderAsync(new ProjeAdiDegistirKomutu("Başka Ad"), OrnekProje.Muhendis);

        await vm.ParselasyonCiziminiOnaylaCommand.ExecuteAsync(null);

        alan.Yol.Belge.Parseller.Select(p => p.ParselId).Should().Equal("K1", "101", "Y1", "102");
        vm.KrokiHatasi.Should().StartWith("Proje önizlemeden sonra değişti");
        vm.ParselasyonKatmani.Should().BeNull();
        alan.Yol.Denetim[^1].Should().Match<AuditKaydi>(d => d.Islem == "KrokiParselasyonuUygula" && d.Sonuc == AuditSonucu.Reddedildi,
            "bayat onay girişimi CommandBus'ta reddedilip denetime yazılır (reviewer M1)");
    }

    [Fact]
    public async Task Proje_Degisip_Adimlar_Yenilenince_Bayat_Taslak_Tuvalden_Kalkmali()
    {
        var (alan, vm) = await HazirlaAsync(dopVar: false);
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        await alan.Yol.GonderAsync(new DopGirdisiAyarlaKomutu(DopGirdisi.Olustur(5000.00m, 4400.00m).Value), OrnekProje.Muhendis);
        await vm.YenileAsync();

        vm.KrokiParselasyonTaslagiVar.Should().BeFalse("reviewer M3: bayat taslak onaylanabilir görünmez");
        vm.ParselasyonKatmani.Should().BeNull();
        vm.ParselasyonCiziminiOnaylaCommand.CanExecute(null).Should().BeFalse();
        vm.KrokiHatasi.Should().StartWith("Proje değişti; kroki parselasyon taslağı kaldırıldı.");
    }

    [Fact]
    public async Task Parselasyon_Basarisizsa_Kip12_Akisina_Donmeli_Nedeni_Uyari_Olarak_Gostermeli()
    {
        // Blok 101'de kirişleri blok içinde kesişen iki çizgi (reviewer H1): parselasyon reddedilir, Kip 2 satırları yine üretilir.
        var kroki = Kroki() with { Polylines = [Cizgi(28, 30, 50, 40), Cizgi(28, 40, 50, 30)] };
        var alan = await AlanAsync();
        var vm = new HesapAdimlariViewModel(alan, OrnekProje.Tecviz, new CadImportService([new SahteKrokiAdaptoru(kroki)]));
        await vm.YenileAsync();
        vm.TakbisMetni = Takbis;

        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.KrokiParselasyonTaslagiVar.Should().BeFalse();
        vm.KrokiEslestirmeOnizlemesiVar.Should().BeTrue();
        vm.KrokiUyarilari[0].Should().StartWith("Otomatik parselasyon yapılamadı: ");
        vm.KrokiHatasi.Should().BeNull();
    }

    [Fact]
    public async Task Uygulanan_Parselasyon_Proje_Parselleri_Degisince_Tuvalden_Kalkmali()
    {
        var (alan, vm) = await HazirlaAsync();
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");
        await vm.ParselasyonCiziminiOnaylaCommand.ExecuteAsync(null);

        await alan.Yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(Parseller()), OrnekProje.Muhendis); // 1. adım CAD yeniden eşlendi
        await vm.YenileAsync();

        vm.ParselasyonKatmani.Should().BeNull();
    }

    [Fact]
    public async Task Bolucu_Bagli_Degilse_Eski_Kroki_Satiri_Akisi_Calismali()
    {
        var (_, vm) = await HazirlaAsync(bolucuVar: false);

        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.KrokiParselasyonTaslagiVar.Should().BeFalse();
        vm.ParselasyonKatmani.Should().BeNull();
        vm.KrokiEslestirmeOnizlemesiVar.Should().BeTrue("Kip 2 Kroki satırları üretilir");
    }

    [Fact]
    public async Task TAKBIS_Degisince_Bayat_Taslak_Atilmali()
    {
        var (_, vm) = await HazirlaAsync();
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.TakbisMetni += Environment.NewLine;

        vm.KrokiParselasyonTaslagiVar.Should().BeFalse();
        vm.ParselasyonKatmani.Should().BeNull();
    }

    [Fact]
    public void Tuval_Parselasyon_Katmanini_Etiketleriyle_Cizmeli()
    {
        var vm = new CadCanvasViewModel();
        vm.LoadScene(CanvasScene.From(new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "TK")],
            [new CadPolyline("TK", "B", null, [P(0, 0), P(0, 100), P(50, 100), P(50, 0), P(0, 0)], true, null)], [], 0, 0, 0, []), CadLayerRoleMap.Standard));
        vm.SetViewport(800, 600);
        vm.FitAll();
        vm.ParselasyonKatmani = new ParselasyonKatmani([new ParselasyonCizimParseli("101/1", [P(0, 0), P(0, 30), P(50, 30), P(50, 0)], "101/1 • ALİ VELİ • 1500.00 m²")], Taslak: true);

        var durum = CadRenderState.From(vm, 800, 600);
        var etiketler = CadSceneRenderer.ParselasyonEtiketleri(durum);
        using var bitmap = new SKBitmap(800, 600);
        using (var canvas = new SKCanvas(bitmap))
        {
            CadSceneRenderer.Render(canvas, durum);
        }

        etiketler.Should().ContainSingle().Which.Metin.Should().Be("101/1 • ALİ VELİ • 1500.00 m²");
        var e = etiketler[0];
        bitmap.GetPixel((int)e.X, (int)e.Y).Should().NotBe(CadSceneRenderer.IzgaraRenkleri(false).Arkaplan, "etiket kutusu çizildi");
        CadSceneRenderer.ParselasyonRengi(true, false).Should().NotBe(CadSceneRenderer.ParselasyonRengi(false, false), "taslak ve uygulanmış ayırt edilir");
    }

    [AvaloniaFact]
    public async Task Ana_Pencere_Taslagi_Tuvale_Aktarmali_Bilgi_Seridi_Ve_Dugmeleri_Turkce_Gostermeli()
    {
        var alan = await AlanAsync();
        var mw = new MainWindowViewModel(new CadImportService([new SahteKrokiAdaptoru(Kroki())]), alan, new CadImportService([new SahteKrokiAdaptoru(Kroki())]));
        await mw.LastProjeTask;
        mw.Hesap!.TakbisMetni = Takbis;
        var w = new MainWindow { DataContext = mw, Width = 1440, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();

        await mw.Hesap.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");
        Dispatcher.UIThread.RunJobs();

        mw.Canvas.ParselasyonKatmani.Should().BeSameAs(mw.Hesap.ParselasyonKatmani);
        mw.Canvas.ParselasyonKatmani!.Parseller.Should().HaveCount(8);
        w.FindControl<Border>("ParselasyonSeridi")!.IsVisible.Should().BeTrue();
        w.FindControl<Button>("SeritOnaylaDugmesi")!.Content.Should().Be("✅ Parselasyon Çizimini Onayla ve Uygula");
        w.FindControl<Button>("SeritIptalDugmesi")!.Content.Should().Be("✕ Taslağı İptal Et");
        w.FindControl<Button>("ParselasyonOnaylaDugmesi")!.Content.Should().Be("✅ Parselasyon Çizimini Onayla ve Uygula");
        w.FindControl<Button>("ParselasyonIptalDugmesi")!.Content.Should().Be("✕ Taslağı İptal Et");

        w.FindControl<Button>("SeritIptalDugmesi")!.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        mw.Canvas.ParselasyonKatmani.Should().BeNull();
        w.FindControl<Border>("ParselasyonSeridi")!.IsVisible.Should().BeFalse();
        w.Close();
    }

    /// <summary>
    /// SAHA-016: blok 101'e 880 + 440 + 440 = 1760 m², blok 102'ye 660 + 660 + 3 × 440 = 2640 m² hak düşen TAKBİS (muvafakatli).
    /// Açık kapasite 101/1'de toplanır (1320 m²); blok 102'nin artanları oraya müşterek aktarılır, diğer 7 parsel müstakil kalır.
    /// </summary>
    private const string FarkliTakbis = """
        K1;ALİ VELİ (HASAN);1/5;E
        K1;AYŞE KAYA (MEHMET);1/10;E
        K1;FATMA DEMİR (OSMAN);1/10;E
        K1;HÜSEYİN ÇELİK (İBRAHİM);3/20;E
        K1;ZEYNEP ARSLAN (YUSUF);3/20;E
        K1;MUSTAFA ÖZTÜRK (KEMAL);1/10;E
        K1;EMİNE ŞAHİN (HALİL);1/10;E
        K1;HATİCE YILDIZ (RECEP);1/10;E
        """;

    [AvaloniaFact]
    public async Task Saha016_Onizleme_Karti_Mustakil_Ve_Musterek_Dagilimi_Tuval_Etiketleri_Durumu_Gostermeli()
    {
        var (alan, vm) = await HazirlaAsync();
        vm.TakbisMetni = FarkliTakbis;
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        vm.KrokiParselDagilimi.Should().HaveCount(8);
        vm.KrokiParselDagilimi[0].Should().StartWith("101/1 • 1320.00 m² • Müşterek (6 hissedar: ALİ VELİ 88000/132000, ");
        vm.KrokiParselDagilimi.Skip(1).Should().OnlyContain(d => d.EndsWith(" • Müstakil (TAM)", StringComparison.Ordinal));
        vm.ParselasyonKatmani!.Parseller[0].Etiket.Should().Be("101/1 • ALİ VELİ +5 • 1320.00 m² • Müşterek");
        vm.KrokiParselasyonOzeti.Should().Contain("7 müstakil (TAM) · 1 müşterek (pay/payda)");
        var main = new MainWindow { DataContext = new MainWindowViewModel(new CadImportService([new SahteKrokiAdaptoru(Kroki())]), alan), Width = 1440, Height = 900 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        main.FindControl<StackPanel>("ParselDagilimPaneli").Should().NotBeNull("kart dağılım paneli görünümde tanımlı");
        main.Close();
        alan.Yol.Belge.HakEslestirme.Should().BeNull("taslak projeye yazılmaz");
    }

    [AvaloniaFact]
    public async Task Saha016_Onaylaninca_Hak_Eslestirme_Kritik_Bulgusuz_Gecmeli_Tescilde_TAM_Ve_PayPayda_Olmali()
    {
        var (alan, vm) = await HazirlaAsync();
        vm.TakbisMetni = FarkliTakbis;
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");

        await vm.ParselasyonCiziminiOnaylaCommand.ExecuteAsync(null);

        var b = alan.Yol.Belge;
        b.HakEslestirme.Should().NotBeNull();
        b.HakEslestirme!.Eslestirme.Where(e => e.ImarParselKodu == "101/1").Should().HaveCount(6);
        var hak = vm.Degerlendirme!.Hak!;
        hak.Bulgular.Where(x => x.Code.StartsWith("ERR-TAPU", StringComparison.Ordinal)).Should().BeEmpty("Σ parsel = alan, Σ malik = net hak (0.00 m²)");
        hak.ImarParselleri.Where(p => p.GeciciKod != "101/1").Should().OnlyContain(p => p.Mustakil && p.Paydaslar[0].Hisse.ToString() == "TAM");
        hak.ImarParselleri.Single(p => p.GeciciKod == "101/1").Paydaslar.Single(x => x.MalikId == "ALİ VELİ (HASAN)").Hisse.ToString().Should().Be("88000/132000");
        vm.ParselasyonKatmani!.Taslak.Should().BeFalse();
        vm.ParselasyonKatmani.Parseller[0].Etiket.Should().EndWith(" • Müşterek");
        vm.KrokiParselDagilimi.Should().HaveCount(8, "onaydan sonra uygulanan dağılım kartta kalır");
        vm.DurumMetni.Should().Contain("(7 müstakil (TAM), 1 müşterek (pay/payda))");
    }

    [AvaloniaFact]
    public async Task Saha016_Onay_Sonrasi_Hak_Eslestirme_Degisince_Dagilim_Karti_Ve_Etiket_Guncellenmeli()
    {
        // Reviewer O5: onay anındaki kopya değil, projedeki güncel eşleştirme esas alınır.
        var (alan, vm) = await HazirlaAsync();
        vm.TakbisMetni = FarkliTakbis;
        await vm.YaklasikYerKrokisiYukleAsync(Akis(), ".ncz");
        await vm.ParselasyonCiziminiOnaylaCommand.ExecuteAsync(null);
        var b = alan.Yol.Belge;
        var e = b.HakEslestirme!;

        // 101/2 (Hüseyin, 440) ile 101/3 (Zeynep, 440) satırlarını tek parselde birleştir: 101/2 müşterek, 101/3 satırsız görünmeli.
        var yeni = e with
        {
            Eslestirme = [.. e.Eslestirme.Select(x => x.ImarParselKodu == "101/3" ? x with { ImarParselKodu = "101/2" } : x)],
        };
        (await alan.Yol.GonderAsync(new HakEslestirmeAyarlaKomutu(yeni), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        await vm.YenileAsync();

        vm.KrokiParselDagilimi.Should().Contain(d => d.StartsWith("101/2 • 440.00 m² • Müşterek (2 hissedar: ", StringComparison.Ordinal))
            .And.Contain("101/3 • 440.00 m² • Hak eşleştirmesinde satır yok");
        vm.ParselasyonKatmani!.Parseller.Single(p => p.Kod == "101/2").Etiket.Should().EndWith(" • Müşterek");
    }
}

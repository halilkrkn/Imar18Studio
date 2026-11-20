using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.KoordinatDonusumu;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Crs;
using Imar18.Presentation.Avalonia.ViewModels;
using Imar18.Presentation.Avalonia.Views;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// SAHA-014: 1. adım "📂 Dönüşüm Dosyası (.DNS) Yükle" — tablo ve parametre önizlemesi, "Dönüşümü Uygula (Deprem Öncesi →
/// Sonrası)" ile CommandBus ileri dönüşümü ve yeşil durum, çift dönüşüm koruması, "Dönüşümü Kaldır", elle tablo değişikliğinde
/// DNS'in bırakılması, tuvalin Deprem Sonrası koordinatlarla yenilenmesi. Sahte okuyucu sentetik katsayı/nokta döndürür
/// (Presentation testleri Infrastructure'a başvurmaz); beklenen değerler test içindeki bağımsız ileri formülle bulunur.
/// </summary>
public sealed class Saha014DnsDonusumTests
{
    // Sentetik Helmert (Netcad düzeni): Y2 = A·Y1 + C·X1 + CY ; X2 = B·Y1 + D·X1 + CX.
    private static readonly DonusumKatsayilari K = new(A: 0.99999m, B: -0.00001m, C: 0.00001m, D: 0.99999m, Cy: -25.5m, Cx: 18.25m);

    // Gözlenen II. sistem = hesaplanan − v (v: N1 (+0.010, −0.020), N2 (−0.010, 0), N3 (0, +0.020), N4 (0, 0)).
    private static readonly DonusumOrtakNoktasi[] Noktalar =
    [
        new("N1", 500_000.000m, 4_000_000.000m, 500_009.490m, 3_999_973.270m),
        new("N2", 501_000.000m, 4_000_000.000m, 501_009.500m, 3_999_973.240m),
        new("N3", 501_000.000m, 4_001_000.000m, 501_009.500m, 4_000_973.210m),
        new("N4", 500_000.000m, 4_001_000.000m, 500_009.510m, 4_000_973.240m),
    ];

    private sealed class SahteDnsOkuyucu(Result<DnsDosyasi> sonuc) : IDnsDosyaOkuyucu
    {
        public int Cagri { get; private set; }

        public Result<DnsDosyasi> Oku(Stream kaynak, CancellationToken cancellationToken = default)
        {
            Cagri++;
            return sonuc;
        }
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

    private sealed class SahteAdaptor(CadLayerDocument belge) : ICadFormatAdapter
    {
        public CadFormat Format => belge.Format;

        public bool SupportsExtension(string extension) => true;

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) => Result<CadLayerDocument>.Success(belge);
    }

    private static DnsDosyasi Dns() => new(K, Noktalar, DonusumParametreleri.Katsayilardan(K, Noktalar));

    private static GeodeticPoint Ileri(GeodeticPoint p) => new(
        X: Math.Round((K.B * p.Y) + (K.D * p.X) + K.Cx, 6, MidpointRounding.AwayFromZero),
        Y: Math.Round((K.A * p.Y) + (K.C * p.X) + K.Cy, 6, MidpointRounding.AwayFromZero));

    private static ProjeCalismaAlani Alan(ProjeKomutYolu yol, SabitZaman zaman, IDnsDosyaOkuyucu? okuyucu) => new(
        yol,
        new ApprovalService(yol, zaman, OrnekProje.Topoloji),
        new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru)], new BellekYazici(), OrnekProje.Topoloji),
        OrnekProje.Muhendis)
    {
        Donusum = new DonusumServisi(new HelmertAfinDonusumHesaplayici()),
        DnsOkuyucu = okuyucu,
    };

    private static async Task<(ProjeCalismaAlani Alan, KunyeDonusumViewModel Vm)> Hazirla(IDnsDosyaOkuyucu? okuyucu = null)
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var alan = Alan(yol, zaman, okuyucu ?? new SahteDnsOkuyucu(Result<DnsDosyasi>.Success(Dns())));
        return (alan, new KunyeDonusumViewModel(alan));
    }

    [Fact]
    public async Task Dns_Yuklenince_Ortak_Nokta_Tablosu_Ve_Parametre_Onizlemesi_Dolmali_Projeye_Yazilmamali()
    {
        var (alan, vm) = await Hazirla();
        var onceki = alan.Yol.Belge.RevizyonKimligi();

        await vm.DnsYukleAsync(new MemoryStream([1]));

        vm.OrtakNoktaSatirlari.Select(s => (s.NoktaNo, s.Y1, s.X1, s.Y2, s.X2)).Should().Equal(
            ("N1", "500000.000", "4000000.000", "500009.490", "3999973.270"),
            ("N2", "501000.000", "4000000.000", "501009.500", "3999973.240"),
            ("N3", "501000.000", "4001000.000", "501009.500", "4000973.210"),
            ("N4", "500000.000", "4001000.000", "500009.510", "4000973.240"));
        vm.Model.Should().Be(DonusumModeli.Helmert2B);
        vm.DnsParametreSatirlari.Should().Equal(
            "A= 0.99999   B= -0.00001",
            "C= 0.00001   D= 0.99999",
            "CY= -25.5   CX= 18.25",
            "Ölçek katsayısı = 0.999990000050",
            "Dönüklük = 0.000636626 grad",
            "Ortalama hata = 0.016 m",
            "4 ortak nokta · Helmert (2B, 4 parametre) · en büyük |V| = 0.022 m");
        vm.DnsYuklendi.Should().BeTrue();
        vm.DnsDurumu.Should().Contain("4 ortak nokta").And.Contain("Dönüşümü Uygula (Deprem Öncesi → Sonrası)");
        vm.DnsHatasi.Should().BeEmpty();
        vm.DnsDonusumunuUygulaCommand.CanExecute(null).Should().BeTrue();
        vm.DonusumuUygulaCommand.CanExecute(null).Should().BeFalse("DNS yüklüyken elle ters kip uygulanmaz (architect K1)");
        alan.Yol.Belge.RevizyonKimligi().Should().Be(onceki, "yükleme önizlemedir; projeye yazmaz");
        alan.Yol.Belge.IleriDonusum.Should().BeNull();
    }

    [Fact]
    public async Task Dns_Uygulaninca_Yesil_Durum_Gorunmeli_Proje_Deprem_Sonrasi_Koordinatlara_Donusmeli_Onay_Dusmeli()
    {
        var (alan, vm) = await Hazirla();
        var degisti = 0;
        vm.IleriDonusumDegisti += (_, _) => degisti++;
        await vm.DnsYukleAsync(new MemoryStream([1]));

        await vm.DnsDonusumunuUygulaCommand.ExecuteAsync(null);

        vm.IleriDurumMetni.Should().Be("✅ Dönüşüm uygulandı (4 ortak nokta) — Proje Deprem Sonrası koordinatlarla devam ediyor.");
        vm.IleriDonusumUygulandi.Should().BeTrue();
        vm.DnsYuklendi.Should().BeFalse();
        vm.DnsHatasi.Should().BeEmpty();
        vm.DnsBilgiNotu.Should().Contain("2–4. adım", "örnek projede tecviz / DOP / hak girdileri var; alanlar yeniden hesaplandı");
        degisti.Should().Be(1);
        var b = alan.Yol.Belge;
        b.IleriDonusum.Should().Be(K);
        b.Parseller.Single(p => p.ParselId == "K1").Koseler.Should().Equal(OrnekProje.Geometri()[0].Koseler.Select(Ileri));
        b.DepremOncesiParseller!.Single(p => p.ParselId == "K1").Koseler.Should().Equal(OrnekProje.Geometri()[0].Koseler);
        vm.DonusumuKaldirCommand.CanExecute(null).Should().BeTrue();
        vm.DonusumuUygulaCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Ayni_Dns_Iki_Kez_Yuklenip_Uygulaninca_Koordinatlar_Ikinci_Kez_Kaymamali()
    {
        var (alan, vm) = await Hazirla();
        await vm.DnsYukleAsync(new MemoryStream([1]));
        await vm.DnsDonusumunuUygulaCommand.ExecuteAsync(null);
        var bir = alan.Yol.Belge;

        await vm.DnsYukleAsync(new MemoryStream([1]));
        await vm.DnsDonusumunuUygulaCommand.ExecuteAsync(null);

        var iki = alan.Yol.Belge;
        iki.Parseller.SelectMany(p => p.Koseler).Should().Equal(bir.Parseller.SelectMany(p => p.Koseler));
        iki.RevizyonKimligi().Should().Be(bir.RevizyonKimligi());
        vm.IleriDurumMetni.Should().Be(KunyeDonusumViewModel.UygulandiMetni(4));
    }

    [Fact]
    public async Task Donusum_Kaldirilinca_Proje_Deprem_Oncesi_CAD_Koordinatlarina_Donmeli()
    {
        var (alan, vm) = await Hazirla();
        await vm.DnsYukleAsync(new MemoryStream([1]));
        await vm.DnsDonusumunuUygulaCommand.ExecuteAsync(null);

        await vm.DonusumuKaldirCommand.ExecuteAsync(null);

        alan.Yol.Belge.IleriDonusum.Should().BeNull();
        alan.Yol.Belge.Donusum.Should().BeNull();
        alan.Yol.Belge.Parseller.SelectMany(p => p.Koseler).Should().Equal(OrnekProje.Geometri().SelectMany(p => p.Koseler));
        vm.IleriDonusumUygulandi.Should().BeFalse();
        vm.OrtakNoktaSatirlari.Should().BeEmpty();
        vm.DnsDurumu.Should().Contain("Deprem Öncesi");
    }

    [Fact]
    public async Task Dns_Okunamazsa_Turkce_Hata_Gosterilmeli_Proje_Degismemeli()
    {
        var hata = ErrorCatalog.Get(ErrorCodes.GirdProjeGirdisiGecersiz) with { UserMessage = "Netcad dönüşüm dosyası (.DNS) okunamadı: $SON yok." };
        var (alan, vm) = await Hazirla(new SahteDnsOkuyucu(Result<DnsDosyasi>.Failure(hata)));
        var onceki = alan.Yol.Belge.RevizyonKimligi();

        await vm.DnsYukleAsync(new MemoryStream([1]));

        vm.DnsHatasi.Should().Be("Netcad dönüşüm dosyası (.DNS) okunamadı: $SON yok.");
        vm.DnsYuklendi.Should().BeFalse();
        vm.DnsDonusumunuUygulaCommand.CanExecute(null).Should().BeFalse();
        alan.Yol.Belge.RevizyonKimligi().Should().Be(onceki);
    }

    [Fact]
    public async Task Dns_Yuklendikten_Sonra_Tablo_Elle_Degisirse_DNS_Birakilmali_Elle_Kipe_Donulmeli()
    {
        var (_, vm) = await Hazirla();
        await vm.DnsYukleAsync(new MemoryStream([1]));

        vm.OrtakNoktaSatirlari[0].Y2 = "500009.500";

        vm.DnsYuklendi.Should().BeFalse("katsayılar artık tablodaki noktalara ait değildir (birebir basım ilkesi)");
        vm.DnsParametreSatirlari.Should().BeEmpty();
        vm.DnsDurumu.Should().Contain("tablosu değiştirildi");
        vm.DonusumuUygulaCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Dns_Uygulanmisken_Yeni_DNS_Yuklenince_Yesil_Kart_Uygulanan_Durumu_Korumali_Tablo_Farki_Uyarilmali()
    {
        // Reviewer M3: yeşil kart yalnız projede uygulanmış dönüşümü gösterir; bekleyen DNS / tablo bilgisi ayrı metindedir.
        var (alan, vm) = await Hazirla();
        await vm.DnsYukleAsync(new MemoryStream([1]));
        await vm.DnsDonusumunuUygulaCommand.ExecuteAsync(null);
        var uygulanan = vm.IleriDurumMetni;

        await vm.DnsYukleAsync(new MemoryStream([1]));

        vm.IleriDurumMetni.Should().Be(uygulanan);
        vm.DnsDurumu.Should().Contain(".DNS okundu");
        vm.DnsYuklendi.Should().BeTrue();

        vm.OrtakNoktaSatirlari[0].Y2 = "500009.500";
        vm.DnsYuklendi.Should().BeFalse();
        vm.IleriDurumMetni.Should().Be(uygulanan, "proje hâlâ ileri kipte");
        alan.Yol.Belge.IleriDonusum.Should().Be(K);

        // Uygulanmış kipte (bekleyen DNS yokken) tablo düzenlemesi kayıtlı noktalardan ayrışırsa uyarılır, proje değişmez.
        vm.OrtakNoktaSatirlari[1].Y2 = "501009.400";
        vm.DnsDurumu.Should().Contain("kayıtlı .DNS noktalarından farklı");
        alan.Yol.Belge.Donusum!.OrtakNoktalar.Should().Equal(Noktalar);
    }

    [Fact]
    public async Task Elle_Ters_Donusum_Kayitliyken_DNS_Uygula_Pasif_Olmali_Kaldirinca_Bekleyen_DNS_Uygulanabilmeli()
    {
        // Reviewer M1: elle (ters) kipten DNS ileri kipine sessiz geçiş (çift kayma) yok; önce "Dönüşümü Kaldır".
        var (alan, vm) = await Hazirla();
        (await alan.Yol.GonderAsync(new DonusumGirdisiAyarlaKomutu(new DonusumGirdisi(DonusumModeli.Helmert2B, 0.09m, Noktalar)), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        vm.Yenile();
        vm.ElleTersKayitli.Should().BeTrue();
        vm.KaldirGorunur.Should().BeTrue();

        await vm.DnsYukleAsync(new MemoryStream([1]));

        vm.DnsYuklendi.Should().BeTrue();
        vm.DnsDonusumunuUygulaCommand.CanExecute(null).Should().BeFalse();
        vm.DnsDurumu.Should().Contain("Dönüşümü Kaldır");

        await vm.DonusumuKaldirCommand.ExecuteAsync(null);

        alan.Yol.Belge.Donusum.Should().BeNull();
        vm.ElleTersKayitli.Should().BeFalse();
        vm.DnsYuklendi.Should().BeTrue("bekleyen .DNS kaldırma sonrası korunur");
        vm.DnsDurumu.Should().Contain("koordinatları değişmedi");
        vm.DnsDonusumunuUygulaCommand.CanExecute(null).Should().BeTrue();
        await vm.DnsDonusumunuUygulaCommand.ExecuteAsync(null);
        alan.Yol.Belge.Parseller.Single(p => p.ParselId == "K1").Koseler.Should().Equal(OrnekProje.Geometri()[0].Koseler.Select(Ileri));
    }

    [Fact]
    public async Task Dns_Okuyucusu_Yoksa_Yukle_Dugmesi_Pasif_Olmali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var vm = new KunyeDonusumViewModel(Alan(yol, zaman, null));

        vm.DnsOkuyucuVar.Should().BeFalse();
        vm.DnsDosyasiIsteCommand.CanExecute(null).Should().BeFalse();
    }

    private static GeodeticPoint P(decimal x, decimal y) => new(X: 4_000_000m + x, Y: 500_000m + y);

    private static CadLayerDocument Belge() => new(
        CadFormat.Ncz,
        [new CadLayer(0, "TK_PARSEL_GİREN"), new CadLayer(1, "TK_PARSEL_OLUŞAN")],
        [
            new CadPolyline("TK_PARSEL_GİREN", "436/14", null, [P(0, 0), P(40, 0), P(40, 40), P(0, 40), P(0, 0)], true, null),
            new CadPolyline("TK_PARSEL_OLUŞAN", "101/1", null, [P(0, 0), P(20, 0), P(20, 20), P(0, 20), P(0, 0)], true, null),
        ],
        [], 0, 0, 0, []);

    [AvaloniaFact]
    public async Task AnaPencere_DNS_Dugmesi_Gorunmeli_Uygulaninca_Tuval_Ve_Proje_Deprem_Sonrasi_Olmali_Rol_Degisiminde_Cift_Kaymamali()
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik SAHA-014", zaman)).Value;
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor(Belge())]), Alan(yol, zaman, new SahteDnsOkuyucu(Result<DnsDosyasi>.Success(Dns()))));
        await vm.LastProjeTask;
        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        await vm.LastProjeTask;
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();

        var dugme = w.FindControl<Button>("DnsYukleDugmesi")!;
        dugme.IsEffectivelyVisible.Should().BeTrue("düğme kartın üstünde, kart kapalıyken de görünür");
        dugme.Content.Should().Be("📂 Dönüşüm Dosyası (.DNS) Yükle");
        w.FindControl<Button>("DnsUygulaDugmesi")!.IsEffectivelyVisible.Should().BeFalse("DNS yüklenmeden uygula düğmesi gizli");

        await vm.Kunye!.DnsYukleAsync(new MemoryStream([1]));
        Dispatcher.UIThread.RunJobs();
        var uygula = w.FindControl<Button>("DnsUygulaDugmesi")!;
        uygula.IsEffectivelyVisible.Should().BeTrue();
        uygula.Content.Should().Be("Dönüşümü Uygula (Deprem Öncesi → Sonrası)");

        await vm.Kunye.DnsDonusumunuUygulaCommand.ExecuteAsync(null);
        await vm.LastReapplyTask;
        await vm.LastProjeTask;
        Dispatcher.UIThread.RunJobs();

        w.FindControl<Border>("DnsBasariKarti")!.IsEffectivelyVisible.Should().BeTrue();
        w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text)
            .Should().Contain("✅ Dönüşüm uygulandı (4 ortak nokta) — Proje Deprem Sonrası koordinatlarla devam ediyor.");
        var olusan = vm.Canvas.Scene.Polylines.Single(p => p.LayerName == "TK_PARSEL_OLUŞAN");
        olusan.Vertices[0].Should().Be(Ileri(P(0, 0)), "tuval Deprem Sonrası koordinatlarla yenilenir");
        var imar = yol.Belge.Parseller.Single(p => p.Rol == Imar18.Domain.Geometry.ParselRolu.Imar);
        imar.Koseler.Should().Contain(Ileri(P(0, 0)));
        yol.Belge.DepremOncesiParseller!.SelectMany(p => p.Koseler).Should().Contain(P(0, 0));

        // Rol değişiminde CAD'den yeniden eşleme: geometri Deprem Öncesi'nden yeniden dönüştürülür (çift kayma yok).
        var once = yol.Belge.Parseller.SelectMany(p => p.Koseler).ToArray();
        vm.Wizard.LayerRoles.First(r => r.LayerName == "TK_PARSEL_GİREN").Role = CadLayerRole.CadastralParcel;
        await vm.EslemeyiUygulaCommand.ExecuteAsync(null);
        await vm.LastReapplyTask;
        await vm.LastProjeTask;
        yol.Belge.Parseller.SelectMany(p => p.Koseler).Should().Equal(once);
        vm.Canvas.Scene.Polylines.Single(p => p.LayerName == "TK_PARSEL_OLUŞAN").Vertices[0].Should().Be(Ileri(P(0, 0)));
        w.Close();
    }
}

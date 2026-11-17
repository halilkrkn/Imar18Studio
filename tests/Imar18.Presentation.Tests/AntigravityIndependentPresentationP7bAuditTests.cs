using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.Gorunum;
using Imar18.Presentation.Avalonia.ViewModels;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// Antigravity Bağımsız Denetim ve Sınır Testleri (Faz P7.0b — Saha ve UI Cilası 16 Madde).
/// src/ üretim kodlarına dokunulmadan UI/UX regresyon ve sınır koşullarını doğrular:
/// 1. Stüdyo ve Pafta bağımsız 4 tema kombinasyonu ve RenderVersion senkronizasyonu.
/// 2. CommandPaletteViewModel çalıştırılamayan komut mesajının sorgu/kapanma ile temizlenmesi ve null koruması.
/// 3. OSNAP tüm kiplerin kültürden bağımsız etiket/pano biçimi ve PointerLeft sonrasında SecilenNokta'nın korunması.
/// 4. Stepper AdimaGitCommand ileri doğrulama kapısı ve geri serbest geçiş kuralı.
/// 5. WizardViewModel.Sifirla ve HesapAdimlariViewModel.Sifirla tam alan hatası temizliği.
/// 6. Rutin/Info içe aktarma notlarının uyarı sayacına girmemesi, gerçek uyarıların girmesi.
/// 7. Imar18Logo sınır boyut kontrolü ve geçerli PNG başlık doğrulaması.
/// </summary>
public sealed class AntigravityIndependentPresentationP7bAuditTests
{
    private sealed class SahteAdaptor(params AppError[] uyarilar) : ICadFormatAdapter
    {
        public CadFormat Format => CadFormat.Ncz;

        public bool SupportsExtension(string extension) => true;

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) =>
            Result<CadLayerDocument>.Success(new CadLayerDocument(
                CadFormat.Ncz,
                [new CadLayer(0, "TK_PARSEL_GİREN"), new CadLayer(1, "TK_PARSEL_OLUŞAN")],
                [
                    new CadPolyline(
                        "TK_PARSEL_GİREN",
                        "K1",
                        null,
                        [
                            new GeodeticPoint(4_000_000m, 500_000m),
                            new GeodeticPoint(4_000_020m, 500_000m),
                            new GeodeticPoint(4_000_020m, 500_020m),
                            new GeodeticPoint(4_000_000m, 500_020m),
                            new GeodeticPoint(4_000_000m, 500_000m),
                        ],
                        true,
                        null),
                ],
                [],
                0,
                0,
                0,
                uyarilar));
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

    [Fact]
    public void Tema_Dort_Studyo_Pafta_Kombinasyonu_Bagimsiz_Ve_Tutarli_Calismali()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor()]));

        // 1. Başlangıç: Koyu Stüdyo + Koyu Pafta
        vm.AcikStudyo.Should().BeFalse();
        vm.AcikPafta.Should().BeFalse();
        vm.Canvas.AcikPafta.Should().BeFalse();
        vm.TemaEtiketi.Should().Be("☀️ Açık Stüdyo");
        vm.PaftaEtiketi.Should().Be("📄 Açık Pafta");

        // 2. Koyu Stüdyo + Açık Pafta (yalnız çizim açılır)
        var v1 = vm.Canvas.RenderVersion;
        vm.PaftaDegistirCommand.Execute(null);
        vm.AcikStudyo.Should().BeFalse();
        vm.AcikPafta.Should().BeTrue();
        vm.Canvas.AcikPafta.Should().BeTrue();
        vm.Canvas.RenderVersion.Should().BeGreaterThan(v1);
        vm.PaftaEtiketi.Should().Be("🌑 Koyu Pafta");

        // 3. Açık Stüdyo + Açık Pafta (stüdyo değişince pafta da eşlenir)
        vm.TemaDegistirCommand.Execute(null);
        vm.AcikStudyo.Should().BeTrue();
        vm.AcikPafta.Should().BeTrue();
        vm.TemaEtiketi.Should().Be("🌙 Koyu Stüdyo");

        // 4. Açık Stüdyo + Koyu Pafta (pafta tekrar bağımsız kapatılır)
        var v2 = vm.Canvas.RenderVersion;
        vm.PaftaDegistirCommand.Execute(null);
        vm.AcikStudyo.Should().BeTrue();
        vm.AcikPafta.Should().BeFalse();
        vm.Canvas.AcikPafta.Should().BeFalse();
        vm.Canvas.RenderVersion.Should().BeGreaterThan(v2);
    }

    [Fact]
    public void Komut_Cubugu_Calistirilamayan_Komut_Mesaji_Sorgu_Degisince_Ve_Kapaninca_Temizlenmeli()
    {
        var p = new CommandPaletteViewModel();
        var pasif = new PaletteCommand("pasif", "Onay Ver", "approve", () => { }, () => false);
        p.Register(pasif);
        p.Toggle();

        p.Calistir(null).Should().BeFalse();
        p.Calistir(pasif).Should().BeFalse();
        p.IsOpen.Should().BeTrue();
        p.Mesaj.Should().Contain("Onay Ver").And.Contain("şu anda kullanılamaz");

        // Sorgu değişince önceki hata mesajı silinir
        p.Query = "onay";
        p.Mesaj.Should().BeEmpty();

        // Tekrar engellenip Toggle ile kapatılınca hem Query hem Mesaj temizlenir
        p.Calistir(pasif).Should().BeFalse();
        p.Mesaj.Should().NotBeEmpty();
        p.Toggle();
        p.IsOpen.Should().BeFalse();
        p.Query.Should().BeEmpty();
        p.Mesaj.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OsnapKipi.Uc, "Uç")]
    [InlineData(OsnapKipi.Orta, "Orta")]
    [InlineData(OsnapKipi.Dik, "Dik 90°")]
    [InlineData(OsnapKipi.Kesisim, "Kesişim")]
    public void Osnap_Tum_Kiplerde_Kulturden_Bagimsiz_Nokta_Ve_Etiket_Metni_Uretmeli(OsnapKipi kip, string kipAdi)
    {
        var sonuc = new OsnapSonucu(new GeodeticPoint(X: 4_123_456.789m, Y: 543_210.125m), kip);

        CadCanvasViewModel.NoktaMetni(sonuc).Should().Be($"Y=543210.125 X=4123456.789 ({kipAdi})");
        CadCanvasViewModel.OsnapEtiketi(sonuc).Should().Be($"{kipAdi} · Y 543210.125 · X 4123456.789");
    }

    [Fact]
    public void Osnap_Imlec_Tuvalden_Ayrilinca_Yakalama_Silinmeli_Fakat_SecilenNokta_Korunmali()
    {
        var kare = new CadPolyline(
            "TK_PARSEL_OLUŞAN",
            "101/1",
            null,
            [
                new GeodeticPoint(4_000_000m, 500_000m),
                new GeodeticPoint(4_000_000m, 500_050m),
                new GeodeticPoint(4_000_050m, 500_050m),
                new GeodeticPoint(4_000_050m, 500_000m),
                new GeodeticPoint(4_000_000m, 500_000m),
            ],
            true,
            null);
        var vm = new CadCanvasViewModel();
        vm.SetViewport(600, 400);
        vm.LoadScene(CanvasScene.From(new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "TK_PARSEL_OLUŞAN")], [kare], [], 0, 0, 0, []), CadLayerRoleMap.Standard));

        var kose = vm.Camera.WorldToScreen(new GeodeticPoint(4_000_050m, 500_050m));
        vm.PointerMoved(kose.X, kose.Y);
        vm.YakalamayiSec().Should().BeTrue();
        var secili = vm.SecilenNokta;
        secili.Should().NotBeNull();

        vm.PointerLeft();

        vm.Yakalama.Should().BeNull();
        vm.SecilenNokta.Should().Be(secili, "seçilen OSNAP noktası imleç tuvalden ayrılsa bile işaretli kalmalıdır");
    }

    [Fact]
    public void Stepper_AdimaGit_Ileri_Dogrulamasiz_Engellenmeli_Geri_Serbest_Olmali()
    {
        var w = new WizardViewModel();

        // Doğrulama yapılmamışken ileri adım yasak
        w.AdimaGitCommand.CanExecute(w.AdimOgeleri[0]).Should().BeFalse("aktif adıma tekrar gitmek pasiftir");
        w.AdimaGitCommand.CanExecute(w.AdimOgeleri[1]).Should().BeFalse();
        w.AdimaGitCommand.CanExecute(w.AdimOgeleri[4]).Should().BeFalse();

        // Geçerli girdiyle doğrula -> tüm ileri adımlar açılır
        w.LoadLayers([new CadLayer(0, "TK_PARSEL_GİREN"), new CadLayer(1, "TK_PARSEL_OLUŞAN")]);
        w.TecvizUretimYontemi = "Sayısallaştırma";
        w.TecvizSiniriText = "12.50";
        w.TecvizKaynakNotu = "Sentetik rapor";
        w.Validate().Should().BeTrue();

        w.AdimaGitCommand.CanExecute(w.AdimOgeleri[3]).Should().BeTrue();
        w.AdimaGitCommand.Execute(w.AdimOgeleri[3]);
        w.CurrentIndex.Should().Be(3);
        w.AdimOgeleri[0].RozetMetni.Should().Be("✓");
        w.AdimOgeleri[2].RozetMetni.Should().Be("✓");
        w.AdimOgeleri[3].RozetMetni.Should().Be("4");

        // Doğrulama sonradan düşse bile geri adım serbesttir
        w.TecvizSiniriText = "";
        w.IsInputValid.Should().BeFalse();
        w.AdimaGitCommand.CanExecute(w.AdimOgeleri[4]).Should().BeFalse();
        w.AdimaGitCommand.CanExecute(w.AdimOgeleri[1]).Should().BeTrue();
        w.AdimaGitCommand.Execute(w.AdimOgeleri[1]);
        w.CurrentIndex.Should().Be(1);
    }

    [Fact]
    public void Wizard_Sifirla_Tum_Alan_Hatalarini_Ve_Katmanlari_Varsayilana_Dondurmeli()
    {
        var w = new WizardViewModel
        {
            Datum = GeodetikDatum.Ed50,
            OrtaMeridyen = 36,
            EpokText = "hatali",
            OlcekPaydasi = 0,
            TecvizOlcekPaydasi = -1,
            TecvizSiniriText = "0",
        };
        w.Validate().Should().BeFalse();
        w.DogrulamaYapildi.Should().BeTrue();
        w.EpokHatasi.Should().NotBeNull();
        w.TecvizSiniriHatasi.Should().NotBeNull();

        w.Sifirla();

        w.Datum.Should().Be(GeodetikDatum.Itrf96);
        w.OrtaMeridyen.Should().Be(30);
        w.EpokText.Should().Be("2005.00");
        w.OlcekPaydasi.Should().Be(1000);
        w.TecvizOlcekPaydasi.Should().Be(1000);
        w.TecvizSiniriText.Should().BeEmpty();
        w.DogrulamaYapildi.Should().BeFalse();
        w.CurrentIndex.Should().Be(0);
        w.Errors.Should().BeEmpty();
        w.EpokHatasi.Should().BeNull();
        w.OlcekHatasi.Should().BeNull();
        w.CrsHatasi.Should().BeNull();
        w.TecvizOlcekHatasi.Should().BeNull();
        w.TecvizUretimYontemiHatasi.Should().BeNull();
        w.TecvizSiniriHatasi.Should().BeNull();
        w.TecvizKaynakNotuHatasi.Should().BeNull();
        w.KatmanRolHatasi.Should().BeNull();
    }

    [Fact]
    public async Task UyariFiltresi_Info_Ve_Rutin_Notlar_BilgiNotlarina_Gercek_Uyarilar_Warnings_Listesine_Girmeli()
    {
        var vm = new MainWindowViewModel(new CadImportService([
            new SahteAdaptor(
                ErrorCatalog.Get(ErrorCodes.GirdZKoduIndirgendi),
                ErrorCatalog.Get(ErrorCodes.GirdIzgaraDisiKose),
                ErrorCatalog.Get(ErrorCodes.TopoImarYoluAlani),
                ErrorCatalog.Get(ErrorCodes.GirdToleranslaKapatildi),
                ErrorCatalog.Get(ErrorCodes.GirdEtiketPoligonEslesmedi)),
        ]));

        await vm.ImportAsync(new MemoryStream([1]), ".ncz");

        vm.BilgiNotlari.Should().HaveCount(3);
        vm.Warnings.Should().HaveCount(2);
        vm.DenetimUyariSayisi.Should().Be(2);
        vm.StatusText.Should().Contain("2 uyarı, 3 bilgi notu");
    }

    [Fact]
    public void Imar18Logo_Gecersiz_Boyutta_Hata_Vermeli_Ve_Gecerli_Boyutlarda_Png_Imzasi_Uretmeli()
    {
        var act = () => Imar18Logo.Ciz(15);
        act.Should().Throw<ArgumentOutOfRangeException>();

        foreach (var boyut in new[] { 16, 64 })
        {
            var png = Imar18Logo.Png(boyut);
            png.Length.Should().BeGreaterThan(64);
            png[..8].Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        }
    }

    [Fact]
    public async Task HesapAdimlari_Sifirla_Tum_Alan_Hatalarini_Ve_Girdileri_Varsayilana_Dondurmeli()
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik Sıfırla", zaman)).Value;
        var alan = new ProjeCalismaAlani(
            yol,
            new ApprovalService(yol, zaman, OrnekProje.Topoloji),
            new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru)], new BellekYazici(), OrnekProje.Topoloji),
            OrnekProje.Muhendis);
        var vm = new HesapAdimlariViewModel(alan, () => null)
        {
            ToplamTecvizSiniriText = "abc",
            R2Text = "xyz",
            D1Text = "a",
            D2Text = "b",
            R3Text = "c",
            OrtakKatilimText = "d",
            TakbisMetni = "hatali",
            KrokiMetni = "hatali",
            KimlikMetni = "hatali",
            ToplamTecvizSiniriHatasi = "h1",
            R2Hatasi = "h2",
            D1Hatasi = "h3",
            D2Hatasi = "h4",
            R3Hatasi = "h5",
            OrtakKatilimHatasi = "h6",
            TakbisHatasi = "h7",
            KrokiHatasi = "h8",
            KimlikHatasi = "h9",
        };

        vm.Sifirla();

        vm.ToplamTecvizSiniriText.Should().BeEmpty();
        vm.R2Text.Should().BeEmpty();
        vm.D1Text.Should().Be("0");
        vm.D2Text.Should().Be("0");
        vm.R3Text.Should().Be("0");
        vm.OrtakKatilimText.Should().Be("0");
        vm.TakbisMetni.Should().BeEmpty();
        vm.KrokiMetni.Should().BeEmpty();
        vm.KimlikMetni.Should().BeEmpty();
        vm.ToplamTecvizSiniriHatasi.Should().BeNull();
        vm.R2Hatasi.Should().BeNull();
        vm.D1Hatasi.Should().BeNull();
        vm.D2Hatasi.Should().BeNull();
        vm.R3Hatasi.Should().BeNull();
        vm.OrtakKatilimHatasi.Should().BeNull();
        vm.TakbisHatasi.Should().BeNull();
        vm.KrokiHatasi.Should().BeNull();
        vm.KimlikHatasi.Should().BeNull();
    }
}

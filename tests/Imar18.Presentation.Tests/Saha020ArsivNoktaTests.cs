using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Presentation.Avalonia.ViewModels;
using Imar18.Presentation.Avalonia.Views;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// SAHA-020: 1. adım "📌 Arşiv Onaylı Kadastro Noktaları (TK_PARSEL)" kartı — kadastro köşelerinin listelenmesi, yüklenen
/// <c>.NCZ</c> tip 1 noktasından (≤ 0.02 m) varsayılan arşiv no, kutucukta düzenleme (Numpad virgülü), toplu yapıştırma /
/// <c>.ncn</c> yükleme, "Arşiv Noktalarını Uygula" (CommandBus, RevisionId) ve kaldırma. Sentetik proje: <see cref="OrnekProje"/>
/// (K1 = 4 köşe: (0,0), (0,20), (10,20), (10,0) m; konum gerçek değil). Arşiv numaraları uydurmadır (<c>901001</c>…).
/// </summary>
public sealed class Saha020ArsivNoktaTests
{
    private const decimal X0 = OrnekProje.X0;
    private const decimal Y0 = OrnekProje.Y0;

    private sealed class BosYazici : ICiktiYazici
    {
        public Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SilAsync(string yol, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SahteNczAdaptoru(CadLayerDocument belge) : ICadFormatAdapter
    {
        public CadFormat Format => CadFormat.Ncz;

        public bool SupportsExtension(string extension) => true;

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) => Result<CadLayerDocument>.Success(belge);
    }

    private static GeodeticPoint P(decimal x, decimal y) => new(X0 + x, Y0 + y);

    private static async Task<ProjeCalismaAlani> AlanAsync(bool tamProje = true)
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik SAHA-020", zaman)).Value;
        if (tamProje)
        {
            foreach (var k in OrnekProje.TamKomutlar())
            {
                (await yol.GonderAsync(k, OrnekProje.Muhendis)).IsSuccess.Should().BeTrue(k.Ad);
            }
        }

        return new ProjeCalismaAlani(yol, new ApprovalService(yol, zaman, OrnekProje.Topoloji),
            new CiktiServisi(yol, [], new BosYazici(), OrnekProje.Topoloji), OrnekProje.Muhendis);
    }

    [Fact]
    public async Task Kart_Kadastro_Koselerini_Listelemeli_Tip1_Noktasi_Varsayilan_No_Olmali()
    {
        var alan = await AlanAsync();
        var vm = new ArsivNoktalariViewModel(alan);

        vm.Satirlar.Select(s => (s.KoseEtiketi, s.NoktaNo)).Should().Equal(("1 · K1", "1"), ("2 · K1", "2"), ("3 · K1", "3"), ("4 · K1", "4"));
        vm.Satirlar[0].ArsivY.Should().Be("500000.00");
        vm.Satirlar[0].ArsivX.Should().Be("4400000.00");
        vm.Durum.Should().Contain("4 kadastro köşesi listelendi");

        vm.CadNoktalariniAyarla([new CadPoint("NOKTA", "2652001", P(0.01m, 0.01m)), new CadPoint("NOKTA", "4360117", P(10.05m, 20m))]);

        vm.Satirlar.Select(s => s.NoktaNo).Should().Equal(["2652001", "2", "3", "4"], "0.014 m içinde eşlenir; 0.05 m uzaktaki eşlenmez");
        alan.Yol.Belge.ArsivKadastroNoktalari.Should().BeEmpty("uygulanmadan proje değişmez");
    }

    [Fact]
    public async Task Kutucukta_Duzenleyip_Uygula_Projeye_Yazmali_Onay_Dusmeli_Kaldir_Temizlemeli()
    {
        var alan = await AlanAsync();
        (await alan.Onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        var vm = new ArsivNoktalariViewModel(alan);
        var uygulandi = 0;
        vm.Uygulandi += (_, _) => uygulandi++;
        for (var i = 0; i < 4; i++)
        {
            vm.Satirlar[i].NoktaNo = "90100" + (i + 1);
        }

        vm.Satirlar[0].ArsivY = "500000,01";
        vm.Satirlar[0].YHatali.Should().BeFalse("Numpad virgülü ondalık kabul edilir");

        await vm.UygulaCommand.ExecuteAsync(null);

        var b = alan.Yol.Belge;
        b.ArsivKadastroNoktalari.Select(k => k.NoktaNo).Should().Equal("901001", "901002", "901003", "901004");
        b.ArsivKadastroNoktalari[0].ArsivY.Should().Be(500000.01m);
        b.ArsivKadastroNoktalari[0].Kose.Should().Be(P(0, 0), "eşleme anahtarı CAD köşesidir");
        b.GecerliOnayVar(b.RevizyonKimligi()).Should().BeFalse("AC-PILOT-06: arşiv noktası değişince onay düşer");
        vm.Durum.Should().StartWith("✅ 4 arşiv onaylı kadastro noktası projeye kaydedildi");
        vm.KayitliVar.Should().BeTrue();
        uygulandi.Should().Be(1);

        await vm.KaldirCommand.ExecuteAsync(null);

        alan.Yol.Belge.ArsivKadastroNoktalari.Should().BeEmpty();
        vm.Satirlar.Select(s => s.NoktaNo).Should().Equal("1", "2", "3", "4");
    }

    [Fact]
    public async Task Yinelenen_Ya_Da_Gecersiz_Nokta_No_Uygulanmamali_Hata_Gostermeli()
    {
        var alan = await AlanAsync();
        var vm = new ArsivNoktalariViewModel(alan);
        vm.Satirlar[1].NoktaNo = "1";

        await vm.UygulaCommand.ExecuteAsync(null);

        vm.Hata.Should().Be("Aynı arşiv nokta no birden çok köşede: 1");
        alan.Yol.Belge.ArsivKadastroNoktalari.Should().BeEmpty();

        vm.Satirlar[1].NoktaNo = "AB*12";
        vm.Satirlar[1].NoHatali.Should().BeTrue();
        await vm.UygulaCommand.ExecuteAsync(null);
        vm.Hata.Should().StartWith("Köşe 2: arşiv nokta no");
        alan.Yol.Belge.ArsivKadastroNoktalari.Should().BeEmpty();
    }

    [Fact]
    public async Task Toplu_Yapistir_Bosluk_Noktali_Virgul_Ve_Ncn_Satirlarini_Koordinat_Veya_No_Ile_Eslemeli()
    {
        var alan = await AlanAsync();
        var vm = new ArsivNoktalariViewModel(alan);
        vm.Satirlar[3].NoktaNo = "4360117"; // mühendisin kutucukta verdiği no
        vm.TopluMetin = string.Join('\n',
            "# arşiv listesi",
            "901001 500000.000 4400000.000 0.00",          // Netcad .ncn: kot alanı yok sayılır; köşe 1
            "901002;500020,005;4400000,010",               // noktalı virgül + virgül ondalık; köşe 2'ye 0.011 m
            "901003\t500020.00\t4400010.00",               // sekme; köşe 3
            "4360117 500000.50 4400010.40",                 // köşe 4'e 0.64 m → verilmiş no ile eşlenir (arşiv Y, X satırdan)
            "2 500099 4400099",                             // uzak; "2" köşe 2'nin varsayılan no'su → eşlenmez
            "901999 1 2",                                   // eşleşmez
            "BOZUK SATIR");

        vm.TopluAktarCommand.Execute(null);

        vm.Satirlar.Select(s => s.NoktaNo).Should().Equal("901001", "901002", "901003", "4360117");
        vm.Satirlar[1].ArsivY.Should().Be("500020.005");
        vm.Satirlar[3].ArsivY.Should().Be("500000.50");
        vm.Durum.Should().StartWith("📋 4 arşiv noktası aktarıldı");
        vm.Hata.Should().Contain("Satır 8: en az 3 alan gerekli").And.Contain("2 satır hiçbir kadastro köşesiyle").And.Contain("901999");
        alan.Yol.Belge.ArsivKadastroNoktalari.Should().BeEmpty("aktarım projeye yazmaz");

        await vm.UygulaCommand.ExecuteAsync(null);
        alan.Yol.Belge.ArsivKadastroNoktalari.Select(k => k.NoktaNo).Should().Equal("901001", "901002", "901003", "4360117");
    }

    [Fact]
    public async Task YX_Yer_Degistirmis_Arsiv_Koordinati_Uygulanmamali_Hata_Gostermeli()
    {
        var alan = await AlanAsync();
        var vm = new ArsivNoktalariViewModel(alan);
        (vm.Satirlar[0].ArsivY, vm.Satirlar[0].ArsivX) = (vm.Satirlar[0].ArsivX, vm.Satirlar[0].ArsivY);

        await vm.UygulaCommand.ExecuteAsync(null);

        vm.Hata.Should().Be("Köşe 1: arşiv Y, X köşeden 100 m'den uzak — Y ile X yer değiştirmiş olabilir.");
        alan.Yol.Belge.ArsivKadastroNoktalari.Should().BeEmpty();
    }

    [Fact]
    public async Task Ncn_Dosyasi_Yukleme_Toplu_Alana_Okuyup_Aktarmali()
    {
        var alan = await AlanAsync();
        var vm = new ArsivNoktalariViewModel(alan);
        var ncn = Encoding.ASCII.GetBytes("901001 500000.00 4400000.00\r\n901004 500000.00 4400010.00\r\n");

        await vm.NcnYukleAsync(new MemoryStream(ncn));

        vm.TopluMetin.Should().Contain("901004");
        vm.Satirlar.Select(s => s.NoktaNo).Should().Equal("901001", "2", "3", "901004");
    }

    [Fact]
    public async Task Yenile_Koseler_Degismediyse_Kaydedilmemis_Duzenlemeyi_Korumali_Sifirla_Bosaltmali()
    {
        var alan = await AlanAsync();
        var vm = new ArsivNoktalariViewModel(alan);
        vm.Satirlar[0].NoktaNo = "777";

        vm.Yenile();
        vm.Satirlar[0].NoktaNo.Should().Be("777");

        (await alan.Yol.GonderAsync(new ProjeyiSifirlaKomutu(), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        vm.Sifirla();
        vm.Satirlar.Should().BeEmpty();
        vm.SatirVar.Should().BeFalse();
        vm.Durum.Should().StartWith("Kadastro (TK_PARSEL) parseli yok");
    }

    [AvaloniaFact]
    public async Task Ana_Pencere_Ncz_Ice_Aktariminda_Tip1_Noktalarini_Karta_Aktarmali_Kart_Turkce_Gorunmeli()
    {
        var alan = await AlanAsync(tamProje: false);
        GeodeticPoint[] k1 = [P(0, 0), P(0, 20), P(10, 20), P(10, 0)];
        var belge = new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "TK_PARSEL_GİREN"), new CadLayer(1, "NOKTA")],
            [new CadPolyline("TK_PARSEL_GİREN", "901/7", null, [.. k1, k1[0]], true, 200m)], [], 0, 0, 0, [])
        {
            Points = [new CadPoint("NOKTA", "2652001", P(0, 0)), new CadPoint("NOKTA", "2652003", P(10.01m, 20m))],
        };
        var mw = new MainWindowViewModel(new CadImportService([new SahteNczAdaptoru(belge)]), alan);
        var w = new MainWindow { DataContext = mw, Width = 1440, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();

        await mw.ImportAsync(new MemoryStream([1]), ".ncz");
        await mw.LastProjeTask;
        Dispatcher.UIThread.RunJobs();

        alan.Yol.Belge.Parseller.Should().ContainSingle(p => p.Rol == ParselRolu.Kadastro);
        mw.Arsiv!.Satirlar.Select(s => s.NoktaNo).Should().Equal("2652001", "2", "2652003", "4");
        var kart = w.FindControl<Border>("ArsivNoktalariKarti")!;
        kart.IsVisible.Should().BeTrue();
        kart.Child.Should().BeOfType<Expander>().Which.Header.Should().Be("📌 Arşiv Onaylı Kadastro Noktaları (TK_PARSEL)");
        w.FindControl<Button>("ArsivUygulaDugmesi")!.Content.Should().Be("📌 Arşiv Noktalarını Uygula");
        w.FindControl<Expander>("ArsivTopluAlani")!.Header.Should().Be("📋 Toplu Yapıştır / .NCN Yükle");
        w.FindControl<Button>("ArsivNcnYukleDugmesi")!.Content.Should().Be("📂 .NCN Yükle");

        await mw.ProjeyiTemizleCommand.ExecuteAsync(null);
        mw.Arsiv.Satirlar.Should().BeEmpty("Projeyi Sıfırla kartı boşaltır");
        w.Close();
    }
}

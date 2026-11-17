using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Presentation.Avalonia.ViewModels;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// P6 — Sihirbaz 2–4. adımları (Tecviz, DOP &amp; Y1, Hak Eşleştirme) canlı motora ve CommandBus'a bağlıdır. Senaryo
/// OrnekProje'nin (K1 200 m², A1 120 m², Y1 80 m², DOPO 0.4) yalnızca geometri + CRS ile açılıp kalan girdilerin arayüzden
/// girilmesidir; bağımsız referans: aynı girdileri doğrudan komutlarla gönderen <see cref="OrnekProje.TamProjeAsync"/>'in
/// RevisionId'si ve elle hesaplanan DOPO.
/// </summary>
public sealed class HesapAdimlariTests
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

    private sealed class SahteAdaptor : ICadFormatAdapter
    {
        public CadFormat Format => CadFormat.Ncz;

        public bool SupportsExtension(string extension) => true;

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) =>
            Result<CadLayerDocument>.Success(new CadLayerDocument(CadFormat.Ncz, [], [], [], 0, 0, 0, []));
    }

    private static ProjeCalismaAlani Alan(ProjeKomutYolu yol, SabitZaman zaman) => new(
        yol,
        new ApprovalService(yol, zaman, OrnekProje.Topoloji),
        new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru)], new BellekYazici(), OrnekProje.Topoloji),
        OrnekProje.Muhendis);

    /// <summary>OrnekProje ile aynı ad; yalnız CRS ve geometri komutları gönderilmiş.</summary>
    private static async Task<(ProjeCalismaAlani Alan, HesapAdimlariViewModel Vm)> GeometriliAsync(Func<TecvizGirdisi?>? tecviz = null)
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik P5 Projesi", zaman)).Value;
        (await yol.GonderAsync(new ProjeCrsAyarlaKomutu(OrnekProje.Crs()), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(OrnekProje.Geometri()), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        var alan = Alan(yol, zaman);
        var vm = new HesapAdimlariViewModel(alan, tecviz ?? OrnekProje.Tecviz);
        await vm.YenileAsync();
        return (alan, vm);
    }

    [Fact]
    public async Task HesapAdimlari_Tecviz_DOP_Ve_Hak_Arayuzden_Girilince_Dogrudan_Komutlarla_Ayni_Revizyon_Olusmali()
    {
        var (alan, vm) = await GeometriliAsync();

        vm.TecvizSatirlari.Should().ContainSingle().Which.Should().Match<TecvizSatiri>(r => r.ParselId == "K1" && r.HesapAlaniM2 == 200.00m);
        vm.TecvizSatirlari[0].TapuAlaniText = "200,00"; // virgül ondalık ayıracı
        await vm.TecvizUygulaCommand.ExecuteAsync(null);

        vm.R1.Should().Be(200.00m);
        vm.R2Otomatik.Should().Be(120.00m, "Y1 dengelemesi sonrası imar toplamı");
        vm.DengelemeOzeti.Should().Contain("80.00");
        await vm.DopUygulaCommand.ExecuteAsync(null);
        vm.DopOzeti.Should().Contain("DOPO 0.4000000").And.Contain("kamuya ayrılan 80.00");

        vm.TakbisMetni = "K1;MLK-001;1/2;E\nK1;MLK-002;1/2;H";
        vm.KrokiMetni = "MLK-001;K1;A1;60.00\r\nMLK-002;K1;A1;60.00";
        vm.KimlikMetni = "A1;101/1\nY1;YOL";
        await vm.HakUygulaCommand.ExecuteAsync(null);

        var (referans, _, _) = await OrnekProje.TamProjeAsync();
        alan.Yol.Revizyon.Should().Be(referans.Revizyon, "arayüz girdileri doğrudan komutlarla aynı proje durumunu üretmeli");
        vm.HakSatirlari.Should().HaveCount(2).And.Contain(s => s.Contains("MLK-001") && s.Contains("net hak 60.00"));
        alan.Onay.OnayEkraniHazirla().OnayVerilebilir.Should().BeTrue();
        alan.Yol.Denetim.Select(d => d.Islem).Should().Contain(["TecvizGirdileriAyarla", "DopGirdisiAyarla", "HakEslestirmeAyarla", "ParselKimlikEslemesiAyarla"]);
    }

    [Fact]
    public async Task HesapAdimlari_Gecersiz_Tapu_Alani_Komut_Gondermemeli_Turkce_Mesaj_Gostermeli()
    {
        var (alan, vm) = await GeometriliAsync();
        var sayi = alan.Yol.Denetim.Count;
        vm.TecvizSatirlari[0].TapuAlaniText = "iki yüz";

        await vm.TecvizUygulaCommand.ExecuteAsync(null);

        vm.DurumMetni.Should().Contain("K1").And.Contain("tapu alanı");
        alan.Yol.Denetim.Should().HaveCount(sayi);
    }

    [Fact]
    public async Task HesapAdimlari_Tecviz_Girdisi_Dogrulanmadan_Tecviz_Uygulanamamali()
    {
        var (alan, vm) = await GeometriliAsync(() => null);
        vm.TecvizSatirlari[0].TapuAlaniText = "200";

        await vm.TecvizUygulaCommand.ExecuteAsync(null);

        vm.DurumMetni.Should().Contain("1. adımda");
        alan.Yol.Belge.TecvizParselleri.Should().BeEmpty();
    }

    [Fact]
    public async Task HesapAdimlari_DOP_Tecvizden_Once_Uygulanamamali()
    {
        var (alan, vm) = await GeometriliAsync();

        await vm.DopUygulaCommand.ExecuteAsync(null);

        vm.DurumMetni.Should().Contain("2. adımda");
        alan.Yol.Belge.Dop.Should().BeNull();
    }

    [Fact]
    public async Task HesapAdimlari_TC_Kimlik_No_Benzeri_Malik_Kodu_Reddedilmeli_KVKK()
    {
        var (alan, vm) = await GeometriliAsync();
        vm.TecvizSatirlari[0].TapuAlaniText = "200.00";
        await vm.TecvizUygulaCommand.ExecuteAsync(null);
        await vm.DopUygulaCommand.ExecuteAsync(null);
        var sayi = alan.Yol.Denetim.Count;

        vm.TakbisMetni = "K1;12345678901;1/1;E";
        vm.KrokiMetni = "12345678901;K1;A1;120.00";
        await vm.HakUygulaCommand.ExecuteAsync(null);

        vm.DurumMetni.Should().StartWith("K1: ");
        alan.Yol.Belge.HakEslestirme.Should().BeNull();
        alan.Yol.Denetim.Should().HaveCount(sayi, "geçersiz girdi CommandBus'a ulaşmaz");
        alan.Yol.Denetim.Should().NotContain(d => (d.Ayrinti ?? string.Empty).Contains("12345678901"));
    }

    [Fact]
    public async Task HesapAdimlari_Kimlik_Eslemesi_Gecersizse_Hak_Eslestirmesi_Kismen_Yazilmamali()
    {
        var (alan, vm) = await GeometriliAsync();
        vm.TecvizSatirlari[0].TapuAlaniText = "200.00";
        await vm.TecvizUygulaCommand.ExecuteAsync(null);
        await vm.DopUygulaCommand.ExecuteAsync(null);
        vm.TakbisMetni = "K1;MLK-001;1/2;E\nK1;MLK-002;1/2;H";
        vm.KrokiMetni = "MLK-001;K1;A1;60.00\nMLK-002;K1;A1;60.00";
        vm.KimlikMetni = "A1;101/1\nA1;101/2"; // aynı geçici kod iki tescile: geçersiz eşleme

        await vm.HakUygulaCommand.ExecuteAsync(null);

        alan.Yol.Belge.HakEslestirme.Should().BeNull("eşleme reddedilince hak eşleştirmesi de yazılmaz (yarım proje yok)");
        alan.Yol.Belge.KimlikCiftleri.Should().BeEmpty();
        vm.DurumMetni.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task HesapAdimlari_Kayitli_Projede_Girdiler_Metin_Kutularina_Yuklenmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var vm = new HesapAdimlariViewModel(Alan(yol, zaman), OrnekProje.Tecviz);

        await vm.YenileAsync();

        vm.TecvizSatirlari.Single().TapuAlaniText.Should().Be("200.00");
        vm.R2Text.Should().Be("120.00");
        vm.TakbisMetni.Should().Contain("K1;MLK-001;1/2;E").And.Contain("K1;MLK-002;1/2;H");
        vm.KrokiMetni.Should().Contain("MLK-002;K1;A1;60.00");
        vm.KimlikMetni.Should().Contain("Y1;YOL");
        vm.HakOzeti.Should().Contain("1 imar parseli");
    }

    [Fact]
    public async Task AnaPencere_Proje_Varken_Adim_2_4_Gorunumleri_Bagli_Olmali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor()]), Alan(yol, zaman));
        await vm.LastProjeTask;

        vm.Hesap.Should().NotBeNull();
        vm.Hesap!.Degerlendirme!.Tamamlandi.Should().BeTrue();
        vm.Wizard.IsStep1.Should().BeTrue();
        vm.Wizard.CurrentIndex = 2;
        vm.Wizard.IsStep3.Should().BeTrue();
        vm.Wizard.IsStep1.Should().BeFalse();
    }

    [Fact]
    public void AnaPencere_Proje_Olmadan_Hesap_Adimlari_Null_Olmali()
    {
        new MainWindowViewModel(new CadImportService([new SahteAdaptor()])).Hesap.Should().BeNull();
    }
}

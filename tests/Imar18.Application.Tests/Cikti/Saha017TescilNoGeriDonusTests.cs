using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Imar18.Domain.Revizyon;
using Xunit;

namespace Imar18.Application.Tests.Cikti;

/// <summary>
/// SAHA-017 (mühendisin ZIP incelemesi): kroki parselasyonu imar parsellerini <c>1/1</c>, <c>2/1</c> kimliğiyle üretir ve
/// tescil no eşlemesi girilmez. Bağımsız beklenti: eşleme yoksa "ada/parsel" biçimli geçici kod tescil no'dur (Ada/Par, AdaNo,
/// Tescil Sayfası ve Rölöve bu koddan dolar); biçimsiz kod (<c>A1</c>) eskisi gibi <c>-</c> kalır ve uyarılır.
/// </summary>
public sealed class Saha017TescilNoGeriDonusTests
{
    private static async Task<CiktiPaketi> PaketAsync(string imarId, IReadOnlyList<ParselKimlikCifti> kimlikler)
    {
        var depo = new BellekProjeDeposu();
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(depo, "Sentetik SAHA-017", zaman)).Value;
        IProjeKomutu[] komutlar =
        [
            new ProjeCrsAyarlaKomutu(OrnekProje.Crs()),
            new ParselGeometrileriAyarlaKomutu(
            [
                OrnekProje.Dikdortgen("K1", ParselRolu.Kadastro, 0m, 20m),
                OrnekProje.Dikdortgen(imarId, ParselRolu.Imar, 0m, 12m),
                OrnekProje.Dikdortgen("Y1", ParselRolu.YolTerk, 12m, 20m),
            ]),
            new TecvizGirdileriAyarlaKomutu(OrnekProje.TecvizParselleri(), null),
            new DopGirdisiAyarlaKomutu(OrnekProje.Dop()),
            new HakEslestirmeAyarlaKomutu(new HakEslestirmeGirdisi(
                0.4m,
                [new KadastroParseli("K1", 200.00m, [new KadastroMaliki("MLK-001", new Fraction(1, 2), true), new KadastroMaliki("MLK-002", new Fraction(1, 2), false)])],
                [new ImarParseli(imarId, 120.00m)],
                [new HakEslestirmeSatiri("MLK-001", "K1", imarId, 60.00m), new HakEslestirmeSatiri("MLK-002", "K1", imarId, 60.00m)])),
        ];
        if (kimlikler.Count > 0)
        {
            komutlar = [.. komutlar, new ParselKimlikEslemesiAyarlaKomutu(kimlikler)]; // boş liste komutla verilemez (kroki projesi: hiç eşleme yok)
        }

        foreach (var k in komutlar)
        {
            var r = await yol.GonderAsync(k, OrnekProje.Muhendis);
            r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.TechnicalMessage : string.Empty);
        }

        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        var p = CiktiPaketiOlusturucu.Olustur(yol.Belge, ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji));
        p.IsSuccess.Should().BeTrue(p.IsFailure ? p.Error.TechnicalMessage : string.Empty);
        return p.Value;
    }

    [Fact]
    public async Task CiktiPaketi_Eslemesiz_Ada_Parsel_Bicimli_Imar_Kimligi_Tescil_No_Olarak_Kullanilmali_SAHA_017()
    {
        var p = await PaketAsync("1/1", [new ParselKimlikCifti("Y1", "YOL")]);

        p.Parseller.Single(s => s.GeciciKod == "1/1").TescilNo.Should().Be("1/1");
        p.ImarSirali.Should().NotBeEmpty().And.OnlyContain(s => s.TescilNo == "1/1");
        p.MalikSirali.Should().NotBeEmpty().And.OnlyContain(s => s.ImarParseli == "1/1");
        p.Uyarilar.Should().NotContain(u => u.Contains("Tescil no eşlemesi yok: 1/1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CiktiPaketi_Eslemede_Tescil_No_Varsa_Gecici_Koddan_Once_Gelmeli_SAHA_017()
    {
        var p = await PaketAsync("1/1", [new ParselKimlikCifti("1/1", "105/3"), new ParselKimlikCifti("Y1", "YOL")]);

        p.Parseller.Single(s => s.GeciciKod == "1/1").TescilNo.Should().Be("105/3");
    }

    [Fact]
    public async Task CiktiPaketi_Gecici_Kod_Baska_Parselin_Tescil_Nosuyla_Cakisirsa_Kullanilmamali_Uyarilmali_SAHA_017()
    {
        // Reviewer O6: Y1'e açıkça "1/1" tescil no verilmiş; imar parselinin geçici kodu "1/1" ikinci kez tescil no olamaz.
        var p = await PaketAsync("1/1", [new ParselKimlikCifti("Y1", "1/1")]);

        p.Parseller.Single(s => s.GeciciKod == "Y1").TescilNo.Should().Be("1/1");
        p.Parseller.Single(s => s.GeciciKod == "1/1").TescilNo.Should().Be("-", "yinelenen tescil no üretilmez");
        p.Uyarilar.Should().Contain(u => u.Contains("çakışıyor", StringComparison.Ordinal) && u.Contains("1/1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CiktiPaketi_Hic_Eslemesi_Olmayan_Kroki_Projesinde_Gecici_Kod_Tescil_No_Olmali_SAHA_017()
    {
        var p = await PaketAsync("1/1", []);

        p.Parseller.Single(s => s.GeciciKod == "1/1").TescilNo.Should().Be("1/1", "kroki parselasyonunda eşleme listesi boştur");
        p.Parseller.Single(s => s.GeciciKod == "Y1").TescilNo.Should().Be("-");
    }

    [Fact]
    public async Task CiktiPaketi_Eslemesiz_Bicimsiz_Imar_Kimligi_Tire_Kalmali_Ve_Uyarilmali()
    {
        var p = await PaketAsync("A1", [new ParselKimlikCifti("Y1", "YOL")]);

        p.Parseller.Single(s => s.GeciciKod == "A1").TescilNo.Should().Be("-");
        p.Uyarilar.Should().Contain(u => u.Contains("Tescil no eşlemesi yok: A1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1/1", true)]
    [InlineData(" 2 / 3 ", true)]
    [InlineData("101/12", true)]
    [InlineData("A1", false)]
    [InlineData("1/", false)]
    [InlineData("1/2/3", false)]
    [InlineData("YOL", false)]
    [InlineData("1/a", false)]
    public void AdaParselBicimiMi_Yalniz_Rakamli_Ada_Parsel_Kodunu_Kabul_Etmeli(string kod, bool beklenen) =>
        CiktiPaketiOlusturucu.AdaParselBicimiMi(kod).Should().Be(beklenen);
}

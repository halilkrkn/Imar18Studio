using System.Globalization;
using System.Numerics;
using System.Text.Json;
using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Xunit;

namespace Imar18.Domain.Tests.Mevzuat;

/// <summary>
/// P3.3 / SORU-015 — Proje-B anonim dağıtım eşleşmesi (902/1, 40 malik, 37 imar parseli, 94 satır) ile FR-F1.12 uçtan uca.
/// Bağımsız referans: resmî Tescile Esas Dağıtım Cetveli'nin "Dop/İTh" sütunundaki net hak (İTh) değerleri hisse sınıfı başına
/// (fixture'a kopyalanmadan) ve SORU-011 kuralı (Σ pay = payda: 101/1 → 353974, 105/2 → 36965).
/// Muvafakat: cetvel muvafakat bilgisi içermez; tüm malikler muvafakatli varsayılır (fixture muvafakatNotu).
/// </summary>
public sealed class ProjeBDagitimEslesmesiTests
{
    private static readonly JsonElement Json = AltinVeri.ProjeBDagitim();

    private static IEnumerable<JsonElement> Satirlar => Json.GetProperty("eslesmeler").EnumerateArray();

    private static Fraction Kesir(string s)
    {
        var p = s.Split('/');
        return new Fraction(BigInteger.Parse(p[0], CultureInfo.InvariantCulture), BigInteger.Parse(p[1], CultureInfo.InvariantCulture));
    }

    private static HakEslestirmeSonucu Calistir()
    {
        var malikler = Satirlar
            .GroupBy(r => r.GetProperty("malikKodu").GetString()!)
            .Select(g => new KadastroMaliki(g.Key, Kesir(g.First().GetProperty("girenHisse").GetString()!), Muvafakatli: true))
            .ToList();
        var imar = Satirlar
            .GroupBy(r => r.GetProperty("imarParsel").GetString()!)
            .Select(g => new ImarParseli(g.Key, g.First().Dec("imarParselAlaniM2")))
            .ToList();
        var eslestirme = Satirlar
            .Select(r => new HakEslestirmeSatiri(
                r.GetProperty("malikKodu").GetString()!, "902/1", r.GetProperty("imarParsel").GetString()!, r.Dec("tahsisAlaniM2")))
            .ToList();

        var sonuc = HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(
            Json.Dec("dopo"), [new KadastroParseli("902/1", Json.Dec("toplamKadastroAlaniM2"), malikler)], imar, eslestirme));
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : "");
        return sonuc.Value;
    }

    // ---------- Fixture bütünlüğü ----------

    [Fact]
    public void ProjeBFixture_37_Parsel_94_Satir_40_Malik_Ve_Parsel_Toplami_R2_Olmali()
    {
        Satirlar.Should().HaveCount(Json.GetProperty("toplamTahsisSatirSayisi").GetInt32()).And.HaveCount(94);
        var parseller = Satirlar.GroupBy(r => r.GetProperty("imarParsel").GetString()).ToList();
        parseller.Should().HaveCount(Json.GetProperty("imarParselSayisi").GetInt32()).And.HaveCount(37);
        parseller.Sum(g => g.First().Dec("imarParselAlaniM2")).Should().Be(Json.Dec("toplamImarTahsisAlaniM2"));
        Satirlar.Select(r => r.GetProperty("malikKodu").GetString()).Distinct().Should().HaveCount(40);
    }

    [Fact]
    public void ProjeBFixture_Her_Malik_Kodu_Tek_Hisse_Tasimali_Ve_Hisseler_Toplami_Bir_Olmali()
    {
        var hisseler = Satirlar
            .GroupBy(r => r.GetProperty("malikKodu").GetString()!)
            .Select(g => g.Select(r => r.GetProperty("girenHisse").GetString()).Distinct().ToList())
            .ToList();

        hisseler.Should().OnlyContain(h => h.Count == 1, "malik kodu tüm parsellerde aynı kişiyi temsil eder");
        Fraction.Sum(hisseler.Select(h => Kesir(h[0]!))).Should().Be(Fraction.One);
    }

    [Fact]
    public void ProjeBFixture_Tamamlanan_102_1_103_7_Ve_105_2_Satirlari_Cetvelle_Uyumlu_Olmali()
    {
        Satirlar.Count(r => r.GetProperty("imarParsel").GetString() == "102/1").Should().Be(9);
        Satirlar.Where(r => r.GetProperty("imarParsel").GetString() == "102/1")
            .Sum(r => r.Dec("tahsisAlaniM2")).Should().Be(326.37m);

        var y = Satirlar.Should().ContainSingle(r => r.GetProperty("imarParsel").GetString() == "103/7").Which;
        y.GetProperty("tescilHissesi").GetString().Should().Be("TAM");
        y.Dec("tahsisAlaniM2").Should().Be(450.00m);

        // 105/2: resmî cetvel 36964 dm² toplar (0.01 m² eksik), payda 36965
        Satirlar.Where(r => r.GetProperty("imarParsel").GetString() == "105/2")
            .Sum(r => r.Dec("tahsisAlaniM2")).Should().Be(369.64m);
    }

    // ---------- FR-F1.12 uçtan uca ----------

    [Theory]
    [InlineData("1/24", "885.93")]
    [InlineData("1/72", "295.31")]
    [InlineData("19/320", "1262.46")]
    [InlineData("1/44", "483.24")]
    [InlineData("1/48", "442.97")]
    [InlineData("1/132", "161.08")]
    [InlineData("1/176", "120.81")]
    [InlineData("1/88", "241.62")]
    [InlineData("1/16", "1328.90")]
    [InlineData("1/80", "265.78")]
    public void ProjeB_Net_Hak_Resmi_Cetvel_ITh_Degerine_Birebir_Esit_Olmali(string hisse, string beklenenNetHak)
    {
        var hisseOf = Satirlar.GroupBy(r => r.GetProperty("malikKodu").GetString()!)
            .ToDictionary(g => g.Key, g => g.First().GetProperty("girenHisse").GetString());

        var haklar = Calistir().MalikHaklari.Where(h => hisseOf[h.MalikId] == hisse).ToList();

        haklar.Should().NotBeEmpty();
        haklar.Should().OnlyContain(h => h.NetHakM2 == decimal.Parse(beklenenNetHak, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ProjeB_101_1_ve_105_2_Pay_Toplamlari_Largest_Remainder_Ile_Paydaya_Esitlenmeli()
    {
        var r = Calistir();
        BigInteger Toplam(string kod) =>
            r.ImarParselleri.Single(p => p.GeciciKod == kod).Paydaslar.Aggregate(BigInteger.Zero, (t, p) => t + p.Hisse.Pay);

        Toplam("101/1").Should().Be(Json.GetProperty("largestRemainderPayToplamlari").GetProperty("101/1").GetInt64());
        Toplam("105/2").Should().Be(Json.GetProperty("largestRemainderPayToplamlari").GetProperty("105/2").GetInt64());
        r.ImarParselleri.Should().HaveCount(37);
        r.ImarParselleri.Count(p => p.Mustakil).Should().Be(Satirlar.Count(s => s.GetProperty("tescilHissesi").GetString() == "TAM"));
    }

    [Fact]
    public void ProjeB_Kritik_Bulgu_Olmamali_Tek_Uyari_105_2_LR_Ile_Malik_11_e_Giden_1_dm2_Olmali()
    {
        // 105/2: 36964 -> 36965; en buyuk kalan en buyuk paya (3325: Malik-11, Malik-12) -> kimlik sirasi Malik-11.
        // Malik-11: 103/2 450.00 + 105/2 33.26 = 483.26, net hak 483.24 -> +2 dm² (> ±1 dm² tolerans) -> ERR-TAPU-006.
        var r = Calistir();

        r.KritikVar.Should().BeFalse();
        var uyari = r.Bulgular.Should().ContainSingle().Which;
        uyari.Code.Should().Be(ErrorCodes.TapuMalikTahsisiNetHaktanFarkli);
        uyari.TechnicalMessage.Should().Contain("#11").And.Contain("by 2 dm2");
        r.MalikHaklari.Single(h => h.MalikId == "Malik-11").FarkM2.Should().Be(0.02m);
    }
}

using System.Numerics;
using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Xunit;

namespace Imar18.Domain.Tests.Mevzuat;

/// <summary>
/// P3.3 — FR-F1.12 Hak eşleştirme (D16, spec 4.4 / 25.2 / 25.8, AC-PILOT-03 / TC-TAHSIS-001).
/// Net hak: DOP_i = round(giren_i × DOPO, 2), net = giren − DOP_i. Eşleştirme mühendis girdisidir; yazılım
/// Σ parsel = parsel alanı (±1 dm²), Σ malik = net hak ve dm² paydayı doğrular, ±1 dm² farkı Largest Remainder ile dağıtır.
/// SORU-015: sentetik fixture; beklenen değerler elle hesaplanmıştır.
/// </summary>
public sealed class HakEslestirmeServisiTests
{
    private static KadastroMaliki M(string id, int pay, int payda, bool muvafakat = false) => new(id, new Fraction(pay, payda), muvafakat);

    private static HakEslestirmeSatiri S(string malik, string kadastro, string imar, decimal alan) => new(malik, kadastro, imar, alan);

    /// <summary>K1 1000 m² (M1 1/2 muvafakatli, M2 1/4, M3 1/4) + K2 500 m² (M4 tek malik); R2 = 1200 -> DOPO 0.2.</summary>
    private static (List<KadastroParseli> Kadastro, List<ImarParseli> Imar, List<HakEslestirmeSatiri> Satirlar) Fixture(bool m1Muvafakat = true) =>
    (
        [
            new KadastroParseli("K1", 1000.00m, [M("M1", 1, 2, m1Muvafakat), M("M2", 1, 4), M("M3", 1, 4)]),
            new KadastroParseli("K2", 500.00m, [M("M4", 1, 1)]),
        ],
        [new ImarParseli("901/A1", 400.00m), new ImarParseli("901/A2", 400.00m), new ImarParseli("901/A3", 400.00m)],
        [
            S("M1", "K1", "901/A1", 400.00m),
            S("M2", "K1", "901/A2", 200.00m),
            S("M3", "K1", "901/A2", 200.00m),
            S("M4", "K2", "901/A3", 400.00m),
        ]
    );

    private static HakEslestirmeSonucu Calistir(
        decimal dopo,
        IReadOnlyList<KadastroParseli> kadastro,
        IReadOnlyList<ImarParseli> imar,
        IReadOnlyList<HakEslestirmeSatiri> satirlar)
    {
        var s = HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(dopo, kadastro, imar, satirlar));
        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.Code + " " + s.Error.TechnicalMessage : "");
        return s.Value;
    }

    // ---------- Tutarlı sentetik senaryo ----------

    [Fact]
    public void HakEslestirme_Tutarli_Senaryoda_Net_Haklar_Tahsisler_Ve_Tescil_Birebir_Olmali()
    {
        var (k, i, s) = Fixture();
        var dopo = DopCalculator.Hesapla(DopGirdisi.Olustur(1500m, 1200m).Value).Value.Dopo;
        dopo.Should().Be(0.2m);

        var r = Calistir(dopo, k, i, s);

        r.Bulgular.Should().BeEmpty();
        r.MalikHaklari.Select(h => (h.MalikId, h.GirenAlanM2, h.DopKesintisiM2, h.NetHakM2, h.TahsisEdilenM2)).Should().Equal(
            ("M1", 500.00m, 100.00m, 400.00m, 400.00m),
            ("M2", 250.00m, 50.00m, 200.00m, 200.00m),
            ("M3", 250.00m, 50.00m, 200.00m, 200.00m),
            ("M4", 500.00m, 100.00m, 400.00m, 400.00m));

        var a1 = r.ImarParselleri.Single(p => p.GeciciKod == "901/A1");
        a1.Mustakil.Should().BeTrue();
        a1.Paydaslar.Single().Hisse.ToString().Should().Be("TAM");

        var a2 = r.ImarParselleri.Single(p => p.GeciciKod == "901/A2");
        a2.Mustakil.Should().BeFalse();
        a2.Paydaslar.Select(p => p.Hisse.ToString()).Should().Equal("20000/40000", "20000/40000");
    }

    [Fact]
    public void HakEslestirme_Giren_Alan_Malik_Bazinda_2_Ondalik_AwayFromZero_Yuvarlanmali_Netcad_Cetveli_Gibi()
    {
        // Resmi dagitim cetveli (Proje-B) her malikin giren alanini ayri yuvarlar: 1000.01 x 1/3 = 333.3366.. -> 333.34 (x3, Σ 1000.02)
        var k = new List<KadastroParseli> { new("K1", 1000.01m, [M("M1", 1, 3), M("M2", 1, 3), M("M3", 1, 3)]) };
        var i = new List<ImarParseli> { new("A1", 1000.01m) };
        var s = new List<HakEslestirmeSatiri> { S("M1", "K1", "A1", 333.34m), S("M2", "K1", "A1", 333.34m), S("M3", "K1", "A1", 333.33m) };

        var r = Calistir(0m, k, i, s);

        r.MalikHaklari.Select(h => h.GirenAlanM2).Should().Equal(333.34m, 333.34m, 333.34m);
        r.MalikHaklari.Single(h => h.MalikId == "M3").FarkM2.Should().Be(-0.01m);
        r.Bulgular.Should().BeEmpty();
    }

    [Fact]
    public void HakEslestirme_Net_Hak_Spec_4_4_Parsel_Bazli_DOP_Yuvarlamasiyla_Hesaplanmali()
    {
        // giren 333.34 x 0.2015451 = 67.183044... -> DOP 67.18 -> net 266.16
        var k = new List<KadastroParseli> { new("K1", 333.34m, [M("M1", 1, 1)]) };
        var i = new List<ImarParseli> { new("A1", 266.16m) };

        var h = Calistir(0.2015451m, k, i, [S("M1", "K1", "A1", 266.16m)]).MalikHaklari.Single();

        h.DopKesintisiM2.Should().Be(67.18m);
        h.NetHakM2.Should().Be(266.16m);
        h.FarkM2.Should().Be(0m);
    }

    // ---------- Proje-B 101/1: dm² payda ve ±1 dm² (SORU-011: 353974 referans) ----------

    [Fact]
    public void HakEslestirme_ProjeB_101_1_Pay_Toplami_353974_Olmali_Ve_1_dm2_En_Kucuk_Kalanli_Paydastan_Dusulmeli()
    {
        var json = AltinVeri.ProjeB();
        var alan = json.GetProperty("blocks")[0].Dec("totalArea"); // 3539.74
        var beklenen = json.GetProperty("beklenenDogruLargestRemainderPayToplami").GetInt64();
        decimal[] girilen = [663.87m, 663.96m, 663.97m, 663.97m, 663.97m, 73.34m, 73.34m, 73.33m]; // Σ = 3539.75 (+1 dm2)
        var maliklar = girilen.Select((_, n) => $"P{n + 1}").ToArray();

        var k = new List<KadastroParseli> { new("902/1", 3539.74m, maliklar.Select(id => M(id, 1, maliklar.Length)).ToList()) };
        var i = new List<ImarParseli> { new("902/1/A1", alan) };
        var s = girilen.Select((a, n) => S(maliklar[n], "902/1", "902/1/A1", a)).ToList();

        var r = Calistir(0m, k, i, s);

        var parsel = r.ImarParselleri.Single();
        parsel.Paydaslar.Should().OnlyContain(p => p.Hisse.Payda == 353974);
        parsel.Paydaslar.Aggregate(BigInteger.Zero, (t, p) => t + p.Hisse.Pay).Should().Be(beklenen);
        var paylar = parsel.Paydaslar.ToDictionary(p => p.MalikId, p => p.Hisse.Pay);
        paylar["P5"].Should().Be(66396, "eşit kalanlı 66397'lerden kimlik sırasında sonuncusu 1 dm² kaybeder");
        for (var n = 0; n < girilen.Length; n++)
        {
            if (maliklar[n] != "P5")
            {
                paylar[maliklar[n]].Should().Be((BigInteger)(girilen[n] * 100), $"{maliklar[n]} girilen dm² aynen korunmalı");
            }
        }
        r.Bulgular.Should().NotContain(b => b.Code == ErrorCodes.TapuImarParselDagitimToplamiUyumsuz);
    }

    [Fact]
    public void HakEslestirme_Parsel_Toplami_1_dm2_Fazlaysa_Dengelenmeli_2_dm2_Eksikse_ERR_TAPU_005_Vermeli()
    {
        var (k, i, _) = Fixture();
        var fazla = Calistir(0.2m, k, i,
            [S("M1", "K1", "901/A1", 400m), S("M2", "K1", "901/A2", 200.01m), S("M3", "K1", "901/A2", 200m), S("M4", "K2", "901/A3", 400m)]);
        fazla.ImarParselleri.Single(p => p.GeciciKod == "901/A2").Paydaslar
            .Aggregate(BigInteger.Zero, (t, p) => t + p.Hisse.Pay).Should().Be(40000);
        fazla.Bulgular.Should().NotContain(b => b.Code == ErrorCodes.TapuImarParselDagitimToplamiUyumsuz);

        var eksik = Calistir(0.2m, k, i,
            [S("M1", "K1", "901/A1", 400m), S("M2", "K1", "901/A2", 199.99m), S("M3", "K1", "901/A2", 199.99m), S("M4", "K2", "901/A3", 400m)]);
        var hata = eksik.Bulgular.Should().ContainSingle(b => b.Code == ErrorCodes.TapuImarParselDagitimToplamiUyumsuz).Which;
        hata.Severity.Should().Be(ErrorSeverity.Critical);
        hata.TechnicalMessage.Should().Contain("901/A2").And.Contain("-2");
        eksik.ImarParselleri.Should().NotContain(p => p.GeciciKod == "901/A2", "tutarsız parsel için tescil üretilmez");
        eksik.KritikVar.Should().BeTrue();
    }

    [Fact]
    public void HakEslestirme_Malik_Atanmamis_Imar_Parseli_ERR_TAPU_005_Vermeli()
    {
        var (k, _, s) = Fixture();
        var i = new List<ImarParseli> { new("901/A1", 400m), new("901/A2", 400m), new("901/A3", 400m), new("901/A4", 10m) };

        Calistir(0.2m, k, i, s).Bulgular.Should().Contain(b => b.Code == ErrorCodes.TapuImarParselDagitimToplamiUyumsuz
            && b.TechnicalMessage.Contains("901/A4"));
    }

    [Fact]
    public void HakEslestirme_Malik_Tahsisi_Net_Haktan_Farkliysa_ERR_TAPU_006_Uyari_Vermeli()
    {
        var (k, i, _) = Fixture();

        var r = Calistir(0.2m, k, i,
            [S("M1", "K1", "901/A1", 400m), S("M2", "K1", "901/A2", 199m), S("M3", "K1", "901/A2", 201m), S("M4", "K2", "901/A3", 400m)]);

        r.MalikHaklari.Single(h => h.MalikId == "M2").FarkM2.Should().Be(-1.00m);
        r.MalikHaklari.Single(h => h.MalikId == "M3").FarkM2.Should().Be(1.00m);
        r.Bulgular.Where(b => b.Code == ErrorCodes.TapuMalikTahsisiNetHaktanFarkli).Should().HaveCount(2)
            .And.OnlyContain(b => b.Severity == ErrorSeverity.Warning);
        r.KritikVar.Should().BeFalse();
    }

    // ---------- Md. 17/1-(d) muvafakat ----------

    [Fact]
    public void HakEslestirme_Muvafakatsiz_Musterek_Paydas_Mustakil_Parsele_Tahsis_Edilirse_ERR_TAPU_007_Vermeli()
    {
        var (k, i, s) = Fixture(m1Muvafakat: false);

        var r = Calistir(0.2m, k, i, s);

        var hata = r.Bulgular.Should().ContainSingle(b => b.Code == ErrorCodes.TapuMuvafakatsizMustakilTahsis).Which;
        hata.Severity.Should().Be(ErrorSeverity.Critical);
        hata.TechnicalMessage.Should().Contain("901/A1").And.NotContain("M1");
    }

    [Fact]
    public void HakEslestirme_Ayni_Malik_Iki_Kadastrodan_Tek_Parsele_Atanirsa_Mustakil_TAM_Ve_17_1d_Kontrolu_Yapilmali()
    {
        // M1: K1 (cok malikli, muvafakatsiz) + K3 (tek malik) -> ayni A1'e iki satir; parsel fiilen mustakil
        var k = new List<KadastroParseli>
        {
            new("K1", 1000m, [M("M1", 1, 2), M("M2", 1, 2)]),
            new("K3", 200m, [M("M1", 1, 1)]),
        };
        var i = new List<ImarParseli> { new("A1", 700m), new("A2", 500m) };
        var s = new List<HakEslestirmeSatiri> { S("M1", "K1", "A1", 500m), S("M1", "K3", "A1", 200m), S("M2", "K1", "A2", 500m) };

        var r = Calistir(0m, k, i, s);

        var a1 = r.ImarParselleri.Single(p => p.GeciciKod == "A1");
        a1.Mustakil.Should().BeTrue();
        var satir = a1.Paydaslar.Should().ContainSingle().Which;
        satir.MalikId.Should().Be("M1");
        satir.KadastroParselleri.Should().Equal("K1", "K3");
        satir.AlanM2.Should().Be(700m);
        satir.Hisse.ToString().Should().Be("TAM");
        r.Bulgular.Should().ContainSingle(b => b.Code == ErrorCodes.TapuMuvafakatsizMustakilTahsis && b.TechnicalMessage.Contains("A1"));
        r.MalikHaklari.Where(h => h.MalikId == "M1").Select(h => h.TahsisEdilenM2).Should().Equal(500m, 200m);
    }

    [Fact]
    public void HakEslestirme_Iki_Farkli_Malikli_Parselde_Muvafakatsiz_Paydas_Hisseli_Sayilmali()
    {
        var (k, i, s) = Fixture(m1Muvafakat: false);
        s[0] = S("M1", "K1", "901/A1", 300m);
        s.Add(S("M4", "K2", "901/A1", 100m));
        s[^2] = S("M4", "K2", "901/A3", 300m);
        var i2 = new List<ImarParseli> { new("901/A1", 400m), new("901/A2", 400m), new("901/A3", 300m) };

        Calistir(0.2m, k, i2, s).Bulgular.Should().NotContain(b => b.Code == ErrorCodes.TapuMuvafakatsizMustakilTahsis);
    }

    [Fact]
    public void HakEslestirme_Tek_Malikli_Kadastro_Parselinden_Mustakil_Tahsis_Muvafakat_Gerektirmemeli()
    {
        var (k, i, s) = Fixture();

        Calistir(0.2m, k, i, s).Bulgular.Should().NotContain(b => b.Code == ErrorCodes.TapuMuvafakatsizMustakilTahsis);
    }

    // ---------- Geçersiz girdiler ----------

    [Fact]
    public void HakEslestirme_Bilinmeyen_Malik_ERR_TAPU_001_Ile_Reddedilmeli_Ve_Kimlik_Sizdirmamali()
    {
        var (k, i, s) = Fixture();
        s.Add(S("Ahmet Yılmaz", "K1", "901/A1", 1m));

        var r = HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(0.2m, k, i, s));

        r.Error.Code.Should().Be(ErrorCodes.TapuMalikEslesmedi);
        r.Error.TechnicalMessage.Should().NotContain("Ahmet");
    }

    [Fact]
    public void HakEslestirme_Bilinmeyen_Imar_Parseli_ERR_TAPU_004_Ile_Reddedilmeli()
    {
        var (k, i, s) = Fixture();
        s[0] = S("M1", "K1", "901/X9", 400m);

        HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(0.2m, k, i, s)).Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Fact]
    public void HakEslestirme_Hisse_Toplami_Bir_Degilse_ERR_TAPU_003_Ile_Reddedilmeli()
    {
        var (_, i, s) = Fixture();
        var k = new List<KadastroParseli>
        {
            new("K1", 1000m, [M("M1", 1, 2, true), M("M2", 1, 4), M("M3", 1, 5)]),
            new("K2", 500m, [M("M4", 1, 1)]),
        };

        HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(0.2m, k, i, s)).Error.Code.Should().Be(ErrorCodes.TapuHisseToplamiBirDegil);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1")]
    public void HakEslestirme_Gecersiz_DOPO_Reddedilmeli(string dopo)
    {
        var (k, i, s) = Fixture();

        HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(decimal.Parse(dopo, System.Globalization.CultureInfo.InvariantCulture), k, i, s))
            .Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Fact]
    public void HakEslestirme_Yinelenen_Satir_Veya_Pozitif_Olmayan_Alan_Reddedilmeli()
    {
        var (k, i, s) = Fixture();
        var yinelenen = s.Append(S("M1", "K1", "901/A1", 1m)).ToList();
        HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(0.2m, k, i, yinelenen)).Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);

        s[0] = S("M1", "K1", "901/A1", 0m);
        HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(0.2m, k, i, s)).Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Fact]
    public void HakEslestirme_Iki_Ondaliktan_Hassas_Alan_Reddedilmeli()
    {
        var (k, _, s) = Fixture();
        var i = new List<ImarParseli> { new("901/A1", 400.001m), new("901/A2", 400m), new("901/A3", 400m) };

        HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(0.2m, k, i, s)).Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Fact]
    public void HakEslestirme_Girdi_Sirasindan_Bagimsiz_Tekrar_Edilebilir_Olmali()
    {
        var (k, i, s) = Fixture(m1Muvafakat: false);
        var a = Calistir(0.2m, k, i, s);
        k.Reverse();
        i.Reverse();
        s.Reverse();
        var b = Calistir(0.2m, k, i, s);

        a.MalikHaklari.Should().BeEquivalentTo(b.MalikHaklari, o => o.WithStrictOrdering());
        a.ImarParselleri.Select(p => p.GeciciKod).Should().Equal(b.ImarParselleri.Select(p => p.GeciciKod));
        a.ImarParselleri.SelectMany(p => p.Paydaslar.Select(x => x.Hisse.ToString()))
            .Should().Equal(b.ImarParselleri.SelectMany(p => p.Paydaslar.Select(x => x.Hisse.ToString())));
        a.Bulgular.Should().BeEquivalentTo(b.Bulgular, o => o.WithStrictOrdering());
    }
}

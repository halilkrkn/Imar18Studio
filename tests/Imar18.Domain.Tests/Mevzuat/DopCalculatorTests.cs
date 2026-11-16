using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Numerics;
using Xunit;

namespace Imar18.Domain.Tests.Mevzuat;

/// <summary>
/// P3.2 — DopCalculator (spec 25.1, D31 Kural 1): DOPO = (R1 − D1 − R2 + D2 + R3) / (R1 − D1), 7 ondalık AwayFromZero;
/// kamuya ayrılan = R1 − R2 (türetilir); %45 tavanı (ERR-DOP-001) ve aşım karşılama sırası (spec 4.4, TC-DOP-007).
/// Beklenen değerler altın veriden (değiştirilmeden) veya elle hesaplanmıştır.
/// </summary>
public sealed class DopCalculatorTests
{
    private static DopGirdisi G(decimal r1, decimal r2, decimal d1 = 0m, decimal d2 = 0m, decimal r3 = 0m, decimal ortak = 0m)
    {
        var g = DopGirdisi.Olustur(r1, r2, d1, d2, r3, ortak);
        g.IsSuccess.Should().BeTrue(g.IsFailure ? g.Error.TechnicalMessage : "");
        return g.Value;
    }

    private static DopSonucu H(DopGirdisi g, decimal? dengeleme = null)
    {
        var s = DopCalculator.Hesapla(g, dengeleme);
        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.TechnicalMessage : "");
        return s.Value;
    }

    // ---------- Altın veri (Proje-A, B, C) ----------

    [Fact]
    public void DopCalculator_ProjeA_Altin_Veri_DOPO_Ve_Kamuya_Ayrilan_Alan_Birebir_Olmali()
    {
        var m = AltinVeri.ProjeA().GetProperty("metrics");

        var s = H(G(m.Dec("toplamKadastroAlani_R1"), m.Dec("toplamImarTahsis_R2")));

        s.Dopo.Should().Be(m.Dec("dopo"));
        s.KamuyaAyrilanAlanM2.Should().Be(m.Dec("kamuyaAyrilanYol"));
        s.Oko.Should().Be(m.Dec("oko"));
        s.TavanAsildi.Should().BeFalse();
        s.Bulgular.Should().BeEmpty();
    }

    [Fact]
    public void DopCalculator_ProjeB_Altin_Veri_DOPO_Ve_Kamuya_Ayrilan_Alan_Birebir_Olmali()
    {
        var m = AltinVeri.ProjeB().GetProperty("metrics");

        var s = H(G(m.Dec("toplamKadastroAlani_R1"), m.Dec("toplamImarTahsis_R2")));

        s.Dopo.Should().Be(m.Dec("dopo"));
        s.KamuyaAyrilanAlanM2.Should().Be(m.Dec("kamuyaAyrilanYol"));
    }

    [Fact]
    public void DopCalculator_ProjeC_DOP_Sayfasi_Referans_Olmali_Ve_004_m2_Fark_Uyari_Olarak_Raporlanmali()
    {
        var json = AltinVeri.ProjeC();
        var dop = json.GetProperty("resmiDopSayfasi");
        var dengeleme = json.GetProperty("dengelemeCetveli").Dec("imarParselToplami");

        var s = H(G(dop.Dec("R1"), dop.Dec("R2_ToplamImarAdaAlani")), dengeleme);

        s.Dopo.Should().Be(dop.Dec("DOPO"));
        s.KamuyaAyrilanAlanM2.Should().Be(dop.Dec("kamuyaAyrilanAlan"));
        s.R2DengelemeFarkiM2.Should().Be(json.GetProperty("referansKarari").Dec("dengelemeParselToplamiFarki_m2"));
        var uyari = s.Bulgular.Should().ContainSingle().Which;
        uyari.Code.Should().Be(ErrorCodes.DopR2DengelemeFarki);
        uyari.Severity.Should().Be(ErrorSeverity.Warning);
        uyari.TechnicalMessage.Should().Contain("0.04");
    }

    [Fact]
    public void DopCalculator_ProjeC_PARK_R2_Disinda_Kamuya_Ayrilan_Alanin_Icinde_Olmali()
    {
        var json = AltinVeri.ProjeC();
        var dop = json.GetProperty("resmiDopSayfasi");
        var cetvel = json.GetProperty("dengelemeCetveli");

        var s = H(G(dop.Dec("R1"), dop.Dec("R2_ToplamImarAdaAlani")));

        // kamuya ayrilan (2426.29) = Y1 (2026.25) + PARK (400.00) + R2 farki (0.04)
        s.KamuyaAyrilanAlanM2.Should().Be(cetvel.Dec("Y1") + cetvel.Dec("PARK") + 0.04m);
    }

    [Fact]
    public void DopCalculator_Dengeleme_Toplami_R2_Ye_Esitse_Uyari_Uretmemeli()
    {
        var s = H(G(8699.44m, 7620.54m), 7620.54m);

        s.R2DengelemeFarkiM2.Should().Be(0m);
        s.Bulgular.Should().BeEmpty();
    }

    // ---------- Sıfırdan farklı D1, D2, R3 (SORU-008: sentetik) ----------

    [Fact]
    public void DopCalculator_Sifirdan_Farkli_D1_D2_R3_Genisletilmis_Formulu_Uygulamali()
    {
        // (10000 - 1000 - 6000 + 500 + 200) / (10000 - 1000) = 3700 / 9000 = 0.41111...
        var s = H(G(10000m, 6000m, d1: 1000m, d2: 500m, r3: 200m));

        s.DopoTam.Should().Be(new Fraction(37, 90));
        s.Dopo.Should().Be(0.4111111m);
        s.DopMiktariM2.Should().Be(3700m);
        s.KamuyaAyrilanAlanM2.Should().Be(4000m);
    }

    [Fact]
    public void DopCalculator_OKO_Ortak_Katilim_Bolu_R1_Eksi_D1_Olmali()
    {
        // 870 / (8700 - 0) = 0.1
        H(G(8700m, 6000m, ortak: 870m)).Oko.Should().Be(0.1000000m);
    }

    [Fact]
    public void DopCalculator_DOPO_7_Ondalik_AwayFromZero_Yuvarlanmali()
    {
        // 1 / 3 = 0.33333333 -> 0.3333333 ; 2/3 -> 0.6666667 (tavan asar ama yuvarlama dogrulanir)
        H(G(3m, 2m)).Dopo.Should().Be(0.3333333m);
        H(G(3m, 1m)).Dopo.Should().Be(0.6666667m);
        // 0.00000005 tam yarim: 1 / 20000000 -> 0.0000001
        H(G(20_000_000m, 19_999_999m)).Dopo.Should().Be(0.0000001m);
    }

    // ---------- %45 tavanı ----------

    [Fact]
    public void DopCalculator_DOPO_Tam_045_Ise_Tavan_Asilmamali()
    {
        var s = H(G(10000m, 5500m));

        s.Dopo.Should().Be(0.45m);
        s.TavanAsildi.Should().BeFalse();
        s.AsimM2.Should().Be(0m);
    }

    [Fact]
    public void DopCalculator_DOPO_045_i_Asarsa_ERR_DOP_001_Critical_Vermeli()
    {
        var s = H(G(10000m, 5499.99m));

        s.TavanAsildi.Should().BeTrue();
        s.AsimM2.Should().Be(0.01m);
        var hata = s.Bulgular.Should().ContainSingle().Which;
        hata.Code.Should().Be(ErrorCodes.DopYuzde45Asildi);
        hata.Severity.Should().Be(ErrorSeverity.Critical);
        s.KritikVar.Should().BeTrue();
    }

    [Fact]
    public void DopCalculator_Tavan_Kontrolu_Yuvarlanmis_Degil_Tam_Kesir_Uzerinden_Yapilmali()
    {
        // 450000.01 / 1000000 = 0.45000001 -> 7 hanede 0.4500000 ama tam deger 0.45'i asar (SORU-031 varsayimi)
        var s = H(G(1_000_000m, 549_999.99m));

        s.Dopo.Should().Be(0.4500000m);
        s.TavanAsildi.Should().BeTrue();
    }

    // ---------- Aşım karşılama sırası (spec 4.4 / TC-DOP-007) ----------

    [Fact]
    public void DopAsim_Tescil_Harici_Belediye_Muvafakatli_Kamu_Kamulastirma_Sirasiyla_Karsilanmali()
    {
        var s = H(G(10000m, 5000m)); // DOP 5000, tavan 4500 -> asim 500
        s.AsimM2.Should().Be(500m);

        var plan = DopCalculator.AsimKarsila(s, new DopAsimKaynaklari(200m, 100m, 150m, 1000m));

        plan.IsSuccess.Should().BeTrue();
        plan.Value.Kalemler.Select(k => (k.Kaynak, k.KullanilanM2)).Should().Equal(
            (DopAsimKaynagi.TescilHarici, 200m),
            (DopAsimKaynagi.BelediyeMulkiyeti, 100m),
            (DopAsimKaynagi.MuvafakatliKamuTasinmazi, 150m),
            (DopAsimKaynagi.Kamulastirma, 50m));
        plan.Value.KarsilanamayanM2.Should().Be(0m);
        plan.Value.Bulgular.Should().BeEmpty();
    }

    [Fact]
    public void DopAsim_Kaynaklar_Yetersizse_ERR_DOP_005_Ile_Karsilanamayan_Raporlanmali()
    {
        var s = H(G(10000m, 5000m));

        var plan = DopCalculator.AsimKarsila(s, new DopAsimKaynaklari(60m, 40m, 0m, 0m)).Value;

        plan.KarsilanamayanM2.Should().Be(400m);
        plan.Kalemler.Sum(k => k.KullanilanM2).Should().Be(100m);
        plan.Bulgular.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.DopAsimKarsilanamadi);
    }

    [Fact]
    public void DopAsim_Tavan_Asilmamissa_Bos_Plan_Donmeli()
    {
        var plan = DopCalculator.AsimKarsila(H(G(8699.44m, 7620.54m)), new DopAsimKaynaklari(10m, 10m, 10m, 10m)).Value;

        plan.AsimM2.Should().Be(0m);
        plan.Kalemler.Should().OnlyContain(k => k.KullanilanM2 == 0m);
        plan.KarsilanamayanM2.Should().Be(0m);
    }

    [Fact]
    public void DopAsim_Asim_Miktari_2_Ondaliga_AwayFromZero_Yuvarlanmali()
    {
        // DOP 500.01 - 0.45 x 1000.01 (450.0045) = 50.0055 -> 50.01
        H(G(1000.01m, 500m)).AsimM2.Should().Be(50.01m);
    }

    [Fact]
    public void DopGirdisi_Ortak_Katilim_Paydayi_Asarsa_Reddedilmeli()
    {
        DopGirdisi.Olustur(100m, 50m, 10m, 0m, 0m, 90.01m).Error.Code.Should().Be(ErrorCodes.DopGirdisiGecersiz);
        DopGirdisi.Olustur(100m, 50m, 10m, 0m, 0m, 90m).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void DopAsim_Negatif_Kaynak_Reddedilmeli()
    {
        DopCalculator.AsimKarsila(H(G(10000m, 5000m)), new DopAsimKaynaklari(-1m, 0m, 0m, 0m))
            .Error.Code.Should().Be(ErrorCodes.DopGirdisiGecersiz);
    }

    // ---------- Geçersiz girdiler ----------

    [Theory]
    [InlineData("0", "0", "0", "0", "0")]       // R1 - D1 = 0
    [InlineData("100", "50", "100", "0", "0")]  // D1 = R1
    [InlineData("-1", "0", "0", "0", "0")]      // negatif R1
    [InlineData("100", "-1", "0", "0", "0")]    // negatif R2
    [InlineData("100", "50", "0", "-1", "0")]   // negatif D2
    [InlineData("100", "50", "0", "0", "-1")]   // negatif R3
    [InlineData("100", "120", "0", "0", "0")]   // R2 > R1 -> negatif DOPO
    [InlineData("100", "50", "0", "60", "0")]   // D2 > R2
    [InlineData("100", "50", "10", "0", "95")]  // R3 > R1 - D1
    public void DopGirdisi_Gecersiz_Alanlar_ERR_DOP_003_Ile_Reddedilmeli(string r1, string r2, string d1, string d2, string r3)
    {
        static decimal D(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        DopGirdisi.Olustur(D(r1), D(r2), D(d1), D(d2), D(r3), 0m).Error.Code.Should().Be(ErrorCodes.DopGirdisiGecersiz);
    }

    [Fact]
    public void DopGirdisi_Negatif_Ortak_Katilim_Reddedilmeli()
    {
        DopGirdisi.Olustur(100m, 50m, 0m, 0m, 0m, -1m).Error.Code.Should().Be(ErrorCodes.DopGirdisiGecersiz);
    }

    [Fact]
    public void DopCalculator_Negatif_Dengeleme_Toplami_Reddedilmeli()
    {
        DopCalculator.Hesapla(G(100m, 50m), -1m).Error.Code.Should().Be(ErrorCodes.DopGirdisiGecersiz);
    }

    [Fact]
    public void DopCalculator_Ayni_Girdiyle_Tekrar_Edilebilir_Olmali()
    {
        H(G(26629.48m, 21262.44m)).Should().BeEquivalentTo(H(G(26629.48m, 21262.44m)));
    }
}

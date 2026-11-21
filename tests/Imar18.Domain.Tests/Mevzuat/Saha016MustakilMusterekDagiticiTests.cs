using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Xunit;

namespace Imar18.Domain.Tests.Mevzuat;

/// <summary>
/// SAHA-016 (SORU-066): müstakil (TAM) + müşterek (dm² pay/payda) akıllı hisse dağıtımı. Sentetik kimlikler (A, B, C…); beklenen
/// değerler elle hesaplanmıştır. Değişmezler: her parselde Σ hissedar = parsel alanı, her hak sahibinde (malik, kadastro) Σ satır =
/// dengelenmiş net hak — ikisi de 0.00 m² farkla.
/// </summary>
public sealed class Saha016MustakilMusterekDagiticiTests
{
    private static DagitimParseli P(string kod, decimal alan, params (string Malik, decimal Talep)[] talepler) =>
        new(kod, alan, talepler.Select(t => new ParselTalebi(t.Malik, t.Talep)).ToList());

    private static DagitimHakki H(string malik, decimal alan, string kadastro = "K1", bool mustakilOlabilir = true) =>
        new(malik, kadastro, alan) { MustakilOlabilir = mustakilOlabilir };

    private static MustakilMusterekDagitimSonucu Dagit(IReadOnlyList<DagitimParseli> parseller, IReadOnlyList<DagitimHakki> haklar)
    {
        var s = MustakilMusterekDagitici.Dagit(parseller, haklar);
        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.Code + " " + s.Error.TechnicalMessage : string.Empty);
        DegismezleriDogrula(s.Value, parseller, haklar);
        return s.Value;
    }

    private static void DegismezleriDogrula(MustakilMusterekDagitimSonucu r, IReadOnlyList<DagitimParseli> parseller, IReadOnlyList<DagitimHakki> haklar)
    {
        foreach (var p in parseller)
        {
            r.Satirlar.Where(s => s.ParselKodu == p.Kod).Sum(s => s.AlanM2).Should().Be(p.AlanM2, "Σ parsel = parsel alanı ({0})", p.Kod);
            r.Parseller.Single(x => x.Kod == p.Kod).Hissedarlar.Sum(h => h.AlanM2).Should().Be(p.AlanM2);
        }

        foreach (var h in haklar)
        {
            r.Satirlar.Where(s => s.MalikId == h.MalikId && s.KadastroParselId == h.KadastroParselId).Sum(s => s.AlanM2)
                .Should().Be(h.AlanM2, "Σ malik = net hak ({0}/{1})", h.MalikId, h.KadastroParselId);
        }

        r.Satirlar.Should().OnlyContain(s => s.AlanM2 > 0m && decimal.Round(s.AlanM2, 2) == s.AlanM2);
    }

    private static string[] Hisseler(MustakilMusterekDagitimSonucu r, string kod) =>
        [.. r.Parseller.Single(p => p.Kod == kod).Hissedarlar.Select(h => h.MalikId + "=" + h.Hisse)];

    [Fact]
    public void MustakilMusterekDagitici_Senaryo1_Bloklar_Arasi_Fark_Artan_Hak_Acik_Kapasiteli_Parsele_Musterek_Aktarilmali()
    {
        // Blok 1 (1000): A 600 + B 600 → 500 / 500 (orantılı); Blok 2 (1000): C 400 + D 400 → açık kapasite tek parselde (600 / 400).
        var parseller = new[] { P("101/1", 500m, ("A", 600m)), P("101/2", 500m, ("B", 600m)), P("102/1", 600m, ("C", 400m)), P("102/2", 400m, ("D", 400m)) };
        var haklar = new[] { H("A", 600m), H("B", 600m), H("C", 400m), H("D", 400m) };

        var r = Dagit(parseller, haklar);

        Hisseler(r, "101/1").Should().Equal("A=TAM");
        Hisseler(r, "101/2").Should().Equal("B=TAM");
        Hisseler(r, "102/1").Should().Equal("C=40000/60000", "A=10000/60000", "B=10000/60000");
        Hisseler(r, "102/2").Should().Equal("D=TAM");
        r.Parseller.Count(p => p.Mustakil).Should().Be(3);
    }

    [Fact]
    public void MustakilMusterekDagitici_Senaryo2_Krokide_Olmayan_Dusuk_Hisseli_Malik_Buyuk_Parsele_Musterek_Yazilmali()
    {
        var r = Dagit([P("101/1", 1000m, ("X", 800m))], [H("X", 800m), H("Z", 200m)]);

        Hisseler(r, "101/1").Should().Equal("X=80000/100000", "Z=20000/100000");
        r.Satirlar.Should().Contain(new DagitimSatiri("Z", "K1", "101/1", 200.00m));
    }

    [Fact]
    public void MustakilMusterekDagitici_Senaryo3_Her_Malik_Kendi_Parseline_Tam_Uyuyorsa_Hepsi_TAM_Olmali()
    {
        var r = Dagit([P("101/1", 511.77m, ("A", 511.77m)), P("101/2", 488.23m, ("B", 488.23m))], [H("A", 511.77m), H("B", 488.23m)]);

        r.Parseller.Should().OnlyContain(p => p.Mustakil && p.Hissedarlar.Single().Hisse.Tam);
        r.Satirlar.Should().Equal(new DagitimSatiri("A", "K1", "101/1", 511.77m), new DagitimSatiri("B", "K1", "101/2", 488.23m));
    }

    [Fact]
    public void MustakilMusterekDagitici_Asiri_Talepli_Parsel_LR_Ile_Bolunmeli_Esit_Kalanda_Talep_Sirasindan_Bagimsiz_Kucuk_Kimlik_Kazanmali()
    {
        // 1000.00 / 3 = 333.333… → 333.34 (A), 333.33 (B), 333.33 (C); talepler C, A, B sırasıyla verilse de bonus küçük kimliğe (A)
        // gider (reviewer Y1). Artanlar (66.66 / 66.67 / 66.67) 101/2'ye.
        var r = Dagit(
            [P("101/1", 1000m, ("C", 400m), ("A", 400m), ("B", 400m)), P("101/2", 200m)],
            [H("A", 400m), H("B", 400m), H("C", 400m)]);

        r.Parseller[0].Hissedarlar.Select(h => (h.MalikId, h.AlanM2)).Should().Equal(new (string, decimal)[] { ("C", 333.33m), ("A", 333.34m), ("B", 333.33m) });
        r.Parseller[1].Hissedarlar.Select(h => (h.MalikId, h.AlanM2)).Should().Equal(new (string, decimal)[] { ("B", 66.67m), ("C", 66.67m), ("A", 66.66m) },
            "artanlar büyükten küçüğe, eşitte küçük kimlik önce yerleşir");
    }

    [Fact]
    public void MustakilMusterekDagitici_Artan_Hak_Once_Malikin_Kendi_Acik_Kapasiteli_Parseline_Yazilmali()
    {
        // A: 550 (275 + 275 talep). 101/1 175 → A artanı 100; 101/2 335 → A 275 + boş 60; 101/3 140 boş; C (krokide yok) 100.
        // A önce (eşit artan, küçük kimlik): kendi 101/2'sine 60, sonra en büyük boş 101/3'e 40; C 101/3'e 100 → 101/2 TAM kalır.
        var r = Dagit(
            [P("101/1", 175m, ("A", 275m)), P("101/2", 335m, ("A", 275m)), P("101/3", 140m)],
            [H("A", 550m), H("C", 100m)]);

        Hisseler(r, "101/1").Should().Equal("A=TAM");
        Hisseler(r, "101/2").Should().Equal("A=TAM");
        Hisseler(r, "101/3").Should().Equal("A=4000/14000", "C=10000/14000");
    }

    [Fact]
    public void MustakilMusterekDagitici_Cok_Kadastrolu_Malik_Her_Kaynagin_Net_Hakkini_Tam_Almali()
    {
        // A: K1 300 + K2 300 = 600; 101/1 (500) TAM, artan 100 → 102/1. Kaynaklar parsel sırasıyla (K1 önce) tüketilir.
        var r = Dagit(
            [P("101/1", 500m, ("A", 600m)), P("102/1", 500m, ("B", 400m))],
            [H("A", 300m, "K1"), H("A", 300m, "K2"), H("B", 400m, "K3")]);

        r.Satirlar.Should().Equal(
            new DagitimSatiri("A", "K1", "101/1", 300m), new DagitimSatiri("A", "K2", "101/1", 200m),
            new DagitimSatiri("B", "K3", "102/1", 400m), new DagitimSatiri("A", "K2", "102/1", 100m));
        Hisseler(r, "101/1").Should().Equal(new[] { "A=TAM" }, "aynı malikin iki kadastro kaynağı tek paydaştır");
    }

    [Fact]
    public void MustakilMusterekDagitici_Md17_Mustakil_Parsele_Once_Guvenli_Kaynak_Yazilmali_Kacinilmazsa_Raporlanmali()
    {
        // A: K9 (muvafakatsız paydaş, müstakil olamaz) 100 + K2 (tek malikli) 500. 101/1 (500) müstakil → K2'den; 102/1 müşterek → K9.
        var guvenli = Dagit(
            [P("101/1", 500m, ("A", 500m)), P("102/1", 300m, ("B", 200m))],
            [H("A", 100m, "K9", mustakilOlabilir: false), H("A", 500m, "K2"), H("B", 200m, "K3")]);

        guvenli.Satirlar.Should().Contain(new DagitimSatiri("A", "K2", "101/1", 500m)).And.Contain(new DagitimSatiri("A", "K9", "102/1", 100m));
        guvenli.Md17IhlalliParseller.Should().BeEmpty();

        // Yalnız muvafakatsız kaynaklı malik müstakil kalırsa ihlal kodla (malik adı değil) raporlanır.
        var ihlal = Dagit([P("101/1", 500m, ("A", 500m))], [H("A", 500m, "K9", mustakilOlabilir: false)]);

        ihlal.Md17IhlalliParseller.Should().Equal("101/1");
    }

    [Fact]
    public void MustakilMusterekDagitici_Artan_Hak_Tamamini_Alabilen_Ayni_Blok_Parseline_Once_Yazilmali()
    {
        // A (blok 101) artanı 50, Z (krokide yok) 50: eşit artanda A önce. Açık kapasite 102/1 (50, girdi sırasında önce) ve 101/2 (50,
        // A'nın bloğunda, artanın tamamını alır) → A 101/2'ye, Z 102/1'e (yakın tahsis; SORU-070).
        var r = Dagit(
            [P("101/1", 250m, ("A", 300m)) with { Grup = "101" }, P("102/1", 250m, ("C", 200m)) with { Grup = "102" },
             P("101/2", 250m, ("B", 200m)) with { Grup = "101" }],
            [H("A", 300m), H("B", 200m), H("C", 200m), H("Z", 50m)]);

        Hisseler(r, "101/2").Should().Equal("B=20000/25000", "A=5000/25000");
        Hisseler(r, "102/1").Should().Equal("C=20000/25000", "Z=5000/25000");
    }

    [Fact]
    public void MustakilMusterekDagitici_Girdi_Sirasindan_Bagimsiz_Ayni_Sonucu_Vermeli()
    {
        var parseller = new[] { P("101/1", 500m, ("A", 600m)), P("101/2", 500m, ("B", 600m)), P("102/1", 600m, ("C", 400m)), P("102/2", 400m, ("D", 400m)) };
        var haklar = new[] { H("A", 600m), H("B", 600m), H("C", 400m), H("D", 400m) };

        var a = Dagit(parseller, haklar);
        var b = Dagit(parseller, [.. haklar.Reverse()]);

        b.Satirlar.Should().Equal(a.Satirlar);
        b.Parseller.Select(p => string.Join("|", p.Hissedarlar.Select(h => h.MalikId + "=" + h.Hisse)))
            .Should().Equal(a.Parseller.Select(p => string.Join("|", p.Hissedarlar.Select(h => h.MalikId + "=" + h.Hisse))));
    }

    [Fact]
    public void MustakilMusterekDagitici_Sifir_Hakli_Malik_Satir_Uretmemeli()
    {
        var r = Dagit([P("101/1", 100m, ("A", 100m))], [H("A", 100m), H("S", 0m)]);

        r.Satirlar.Should().ContainSingle().Which.MalikId.Should().Be("A");
    }

    public static TheoryData<string, DagitimParseli[], DagitimHakki[]> GecersizGirdiler() => new()
    {
        { "Σ parsel ≠ Σ hak", [P("101/1", 500m, ("A", 500m))], [H("A", 500.01m)] },
        { "parsel yok", [], [H("A", 1m)] },
        { "hak yok", [P("101/1", 1m)], [] },
        { "yinelenen parsel", [P("101/1", 1m), P("101/1", 1m)], [H("A", 2m)] },
        { "sıfır alan", [P("101/1", 0m)], [H("A", 0m)] },
        { "3 ondalık", [P("101/1", 1.005m)], [H("A", 1.005m)] },
        { "negatif hak", [P("101/1", 1m)], [H("A", 2m), H("B", -1m)] },
        { "yinelenen hak sahibi", [P("101/1", 2m)], [H("A", 1m), H("A", 1m)] },
        { "bilinmeyen malik talebi", [P("101/1", 1m, ("Q", 1m))], [H("A", 1m)] },
        { "talep hakkı aşıyor", [P("101/1", 1m, ("A", 0.6m)), P("101/2", 1m, ("A", 0.6m))], [H("A", 1m), H("B", 1m)] },
        { "parselde yinelenen talep", [P("101/1", 2m, ("A", 1m), ("A", 1m))], [H("A", 2m)] },
        { "sıfır talep", [P("101/1", 1m, ("A", 0m))], [H("A", 1m)] },
    };

    [Theory]
    [MemberData(nameof(GecersizGirdiler))]
    public void MustakilMusterekDagitici_Gecersiz_Girdide_ERR_TAPU_004_Donmeli_Kimlik_Yazmamali(string durum, DagitimParseli[] parseller, DagitimHakki[] haklar)
    {
        var s = MustakilMusterekDagitici.Dagit(parseller, haklar);

        s.IsFailure.Should().BeTrue(durum);
        s.Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz, durum);
        s.Error.TechnicalMessage.Should().NotContain("\"A\"").And.NotContain("Q ", "teknik mesaja malik kimliği yazılmaz (KVKK)");
    }

    [Fact]
    public void MustakilMusterekDagitici_Buyuk_Alanlarda_Tasmadan_Tam_Kapanmali()
    {
        // 9 999 999.99 m² ölçeğinde (≈ 10^9 dm²) üç malik; LR + aktarım taşmadan tam kapanır.
        var r = Dagit(
            [P("1/1", 3_333_333.33m, ("A", 5_000_000.00m)), P("1/2", 6_666_666.66m, ("B", 4_999_999.99m))],
            [H("A", 5_000_000.00m), H("B", 4_999_999.99m)]);

        Hisseler(r, "1/1").Should().Equal("A=TAM");
        Hisseler(r, "1/2").Should().Equal("B=499999999/666666666", "A=166666667/666666666");
    }

    [Theory]
    [InlineData("1.00", 9999, 2000000, "0.00")] // 0.0049995 m² → tek adım dm² yuvarlaması 0.00 (çift yuvarlama 0.01 verirdi — architect R2)
    [InlineData("1.00", 1, 200, "0.01")] // 0.005 tam yarım → AwayFromZero
    [InlineData("2500.03", 1, 3, "833.34")]
    public void HakEslestirmeServisi_GirenAlan_Tek_Adimda_dm2_Yuvarlanmali(string tapu, int pay, int payda, string beklenen)
    {
        HakEslestirmeServisi.GirenAlanM2(decimal.Parse(tapu, System.Globalization.CultureInfo.InvariantCulture), new Fraction(pay, payda))
            .Should().Be(decimal.Parse(beklenen, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(3, true, true)]
    [InlineData(3, false, false)]
    public void HakEslestirmeServisi_Md17_Mustakil_Tahsis_Yuklemi(int malikSayisi, bool muvafakat, bool beklenen) =>
        HakEslestirmeServisi.MustakilTahsisEdilebilir(malikSayisi, muvafakat).Should().Be(beklenen);
}

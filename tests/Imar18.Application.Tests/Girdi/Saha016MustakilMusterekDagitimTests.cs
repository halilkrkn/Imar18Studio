using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Girdi;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Imar18.GeometryEngine.Subdivision;
using Xunit;

namespace Imar18.Application.Tests.Girdi;

/// <summary>
/// SAHA-016 (SORU-066): kroki parselasyonunda müstakil (TAM) + müşterek (dm² pay/payda) akıllı hisse dağıtımı — motor + gerçek
/// <see cref="KrokiBlokBolucu"/> + <see cref="HakEslestirmeServisi"/> uçtan uca. Sentetik proje (uydurma adlar, konum gerçek değil;
/// X Kuzey, Y Doğu). Beklenen değerler elle hesaplanmıştır; DOPO = R2 / tapu ile tam sayı kalır (çift yuvarlama yok).
/// </summary>
public sealed class Saha016MustakilMusterekDagitimTests
{
    private const decimal X0 = 4_400_000m;
    private const decimal Y0 = 500_000m;

    private const string A = "ALİ VELİ (HASAN)";
    private const string B = "AYŞE KAYA (MEHMET)";
    private const string C = "FATMA DEMİR (OSMAN)";
    private const string D = "HÜSEYİN ÇELİK (İBRAHİM)";

    private static readonly KrokiBlokBolucu Bolucu = new();

    private static GeodeticPoint P(decimal x, decimal y) => new(X: X0 + x, Y: Y0 + y);

    private static ParselGeometrisi Dikdortgen(string id, ParselRolu rol, decimal x1, decimal x2) => new(id, rol, [P(x1, 0), P(x1, 50), P(x2, 50), P(x2, 0)]);

    private static CadPolyline Cizgi(decimal x1, decimal y1, decimal x2, decimal y2) => new("0", "BOLME", null, [P(x1, y1), P(x2, y2)], false, null);

    private static CadText Yazi(string metin, decimal x, decimal y) => new("0", metin, P(x, y)) { Merkez = P(x, y) };

    private static CadLayerDocument Kroki(IReadOnlyList<CadPolyline> cizgiler, IReadOnlyList<CadText> yazilar) =>
        new(CadFormat.Ncz, [new CadLayer(0, "0")], cizgiler, yazilar, 0, 0, 0, []);

    private static KadastroParseli K1(decimal tapu, params (string Malik, int Pay, int Payda)[] hisseler) =>
        new("K1", tapu, hisseler.Select(h => new KadastroMaliki(h.Malik, new Fraction(h.Pay, h.Payda), Muvafakatli: true)).ToList());

    private static KrokiParselasyonOnizleme Olustur(CadLayerDocument kroki, IReadOnlyList<ParselGeometrisi> parseller, KadastroParseli takbis, decimal dopo)
    {
        var s = KrokiParselasyonMotoru.Olustur(new KrokiParselasyonGirdisi(kroki, parseller, [takbis], MalikRehberi.Bos, dopo), Bolucu);
        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.Code + " " + s.Error.TechnicalMessage : string.Empty);
        return s.Value;
    }

    /// <summary>Onaylı parselasyon hak eşleştirmesinden (ERR-TAPU-005/006/007 dahil) tek bulgu olmadan geçmeli; tescil döner.</summary>
    private static HakEslestirmeSonucu HakEslestirmeHatasizGecmeli(KrokiParselasyonOnizleme o, KadastroParseli takbis, decimal dopo)
    {
        var satirlar = KrokiGirdiDogrulayici.Dogrula(o.Satirlar, [takbis]);
        satirlar.IsSuccess.Should().BeTrue(satirlar.IsFailure ? satirlar.Error.TechnicalMessage : string.Empty);
        var s = HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(
            dopo, [takbis], o.Parseller.Select(p => new ImarParseli(p.Kod, p.GaussAlanM2)).ToList(), satirlar.Value));
        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.TechnicalMessage : string.Empty);
        s.Value.Bulgular.Should().BeEmpty("Σ parsel = parsel alanı ve Σ malik = net hak 0.00 m² farkla sağlanır");
        s.Value.MalikHaklari.Should().OnlyContain(h => h.FarkM2 == 0m);
        return s.Value;
    }

    private static void DegismezlerSaglanmali(KrokiParselasyonOnizleme o)
    {
        foreach (var p in o.Parseller)
        {
            o.Satirlar.Where(s => s.ImarParselKodu == p.Kod).Sum(s => s.AlanM2).Should().Be(p.GaussAlanM2, "Σ parsel = Gauss ({0})", p.Kod);
            p.GaussAlanM2.Should().Be(p.HedefAlanM2);
        }

        o.Farklar.Should().OnlyContain(f => f.FarkM2 == 0m, "her malik DOP sonrası net hakkını tam alır");
    }

    /// <summary>
    /// Senaryo 1: K1 2500 m²; blok 101 X ∈ [30, 50] = 1000 m², yol X ∈ [20, 30] = 500 m², blok 102 X ∈ [0, 20] = 1000 m²; DOPO 0.2.
    /// A, B hisse 3/10 (giren 750, net 600), C, D hisse 1/5 (giren 500, net 400). Blok 101 Σ 1200 &gt; 1000 → 500 / 500 (orantılı);
    /// blok 102 Σ 800 &lt; 1000 → açık kapasite en büyük (eşitte ilk) talepli 102/1'de: 600 / 400.
    /// </summary>
    [Fact]
    public void Saha016_Senaryo1_Bloklar_Arasi_Fark_Blok1_Artani_Blok2_Parseline_Musterek_Aktarilmali_HakEslestirme_Hatasiz()
    {
        var parseller = new[] { Dikdortgen("K1", ParselRolu.Kadastro, 0m, 50m), Dikdortgen("101", ParselRolu.Imar, 30m, 50m), Dikdortgen("Y1", ParselRolu.YolTerk, 20m, 30m), Dikdortgen("102", ParselRolu.Imar, 0m, 20m) };
        var kroki = Kroki(
            [Cizgi(30, 25, 50, 25), Cizgi(0, 25, 20, 25)],
            [Yazi("ALİ VELİ", 40, 12), Yazi("AYŞE KAYA", 40, 37), Yazi("FATMA DEMİR", 10, 12), Yazi("HÜSEYİN ÇELİK", 10, 37)]);
        var takbis = K1(2500.00m, (A, 3, 10), (B, 3, 10), (C, 1, 5), (D, 1, 5));

        var o = Olustur(kroki, parseller, takbis, 0.2m);

        o.Parseller.Select(p => (p.Kod, p.HedefAlanM2)).Should().Equal(("101/1", 500.00m), ("101/2", 500.00m), ("102/1", 600.00m), ("102/2", 400.00m));
        o.Parseller.Select(p => p.DagilimSatiri()).Should().Equal(
            "101/1 • 500.00 m² • Müstakil (TAM)",
            "101/2 • 500.00 m² • Müstakil (TAM)",
            "102/1 • 600.00 m² • Müşterek (3 hissedar: FATMA DEMİR 40000/60000, ALİ VELİ 10000/60000, AYŞE KAYA 10000/60000)",
            "102/2 • 400.00 m² • Müstakil (TAM)");
        o.Satirlar.Should().Contain(new KrokiEslesmeGirdisi(A, "K1", "102/1", 100.00m)).And.Contain(new KrokiEslesmeGirdisi(B, "K1", "102/1", 100.00m));
        o.Uyarilar.Should().Contain(u => u.StartsWith("Blok 101: Σ net hak 1200.00 m² ≠ blok alanı 1000.00 m²", StringComparison.Ordinal))
            .And.Contain(u => u.StartsWith("Blok 102: Σ net hak 800.00 m² ≠ blok alanı 1000.00 m²", StringComparison.Ordinal));
        o.Ozet().Should().EndWith("3 müstakil (TAM) · 1 müşterek (pay/payda)");
        o.Parseller[2].Etiket().Should().Be("102/1 • FATMA DEMİR +2 • 600.00 m² • Müşterek");
        DegismezlerSaglanmali(o);

        var h = HakEslestirmeHatasizGecmeli(o, takbis, 0.2m);
        h.ImarParselleri.Single(p => p.GeciciKod == "101/1").Paydaslar.Single().Hisse.ToString().Should().Be("TAM");
        h.ImarParselleri.Single(p => p.GeciciKod == "102/1").Paydaslar.Select(x => x.Hisse.ToString()).Should().BeEquivalentTo("10000/60000", "10000/60000", "40000/60000");
    }

    /// <summary>
    /// Senaryo 2: K1 2000 m²; blok 101 X ∈ [0, 32] = 1600 m², yol X ∈ [32, 40] = 400 m²; DOPO 0.2. X (ALİ) 1/2 → net 800, Y (AYŞE)
    /// 3/8 → net 600, Z (FATMA, düşük hisseli, krokide yok) 1/8 → net 200. Σ talep 1400 &lt; 1600 → açık kapasite en büyük talepli 101/1'de
    /// (1000); Z 101/1'e 20000/100000 müşterek yazılır, 101/2 (600) müstakil.
    /// </summary>
    [Fact]
    public void Saha016_Senaryo2_Krokide_Olmayan_Dusuk_Hisseli_Malik_Buyuk_Parsele_Musterek_Yazilmali_HakEslestirme_Hatasiz()
    {
        var parseller = new[] { Dikdortgen("K1", ParselRolu.Kadastro, 0m, 40m), Dikdortgen("101", ParselRolu.Imar, 0m, 32m), Dikdortgen("Y1", ParselRolu.YolTerk, 32m, 40m) };
        var kroki = Kroki([Cizgi(0, 28, 32, 28)], [Yazi("ALİ VELİ", 16, 14), Yazi("AYŞE KAYA", 16, 40)]);
        var takbis = K1(2000.00m, (A, 1, 2), (B, 3, 8), (C, 1, 8));

        var o = Olustur(kroki, parseller, takbis, 0.2m);

        o.Parseller.Select(p => p.DagilimSatiri()).Should().Equal(
            "101/1 • 1000.00 m² • Müşterek (2 hissedar: ALİ VELİ 80000/100000, FATMA DEMİR 20000/100000)",
            "101/2 • 600.00 m² • Müstakil (TAM)");
        o.Uyarilar.Should().Contain("TAKBİS'teki 1 malik krokide yok; hakkı açık kapasiteli parsellere müstakil/müşterek (pay/payda) olarak aktarıldı.");
        DegismezlerSaglanmali(o);

        var h = HakEslestirmeHatasizGecmeli(o, takbis, 0.2m);
        h.ImarParselleri.Single(p => p.GeciciKod == "101/1").Paydaslar.Single(x => x.MalikId == C).Hisse.ToString().Should().Be("20000/100000");
    }

    /// <summary>Senaryo 3: aynı geometri, A, B, C, D hisse 1/4 (giren 625, net 500) → her malik kendi parselini tam doldurur; hepsi TAM.</summary>
    [Fact]
    public void Saha016_Senaryo3_Her_Malik_Kendi_Parseline_Tam_Uyuyorsa_Hepsi_TAM_Olmali_HakEslestirme_Hatasiz()
    {
        var parseller = new[] { Dikdortgen("K1", ParselRolu.Kadastro, 0m, 50m), Dikdortgen("101", ParselRolu.Imar, 30m, 50m), Dikdortgen("Y1", ParselRolu.YolTerk, 20m, 30m), Dikdortgen("102", ParselRolu.Imar, 0m, 20m) };
        var kroki = Kroki(
            [Cizgi(30, 22, 50, 27), Cizgi(0, 26, 20, 24)],
            [Yazi("ALİ VELİ", 40, 10), Yazi("AYŞE KAYA", 40, 40), Yazi("FATMA DEMİR", 10, 10), Yazi("HÜSEYİN ÇELİK", 10, 40)]);
        var takbis = K1(2500.00m, (A, 1, 4), (B, 1, 4), (C, 1, 4), (D, 1, 4));

        var o = Olustur(kroki, parseller, takbis, 0.2m);

        o.Parseller.Should().OnlyContain(p => p.Mustakil && p.HedefAlanM2 == 500.00m);
        o.Parseller.Select(p => p.HisseOzeti()).Should().AllBe("Müstakil (TAM)");
        o.MusterekSayisi.Should().Be(0);
        o.Uyarilar.Should().BeEmpty();
        DegismezlerSaglanmali(o);
        HakEslestirmeHatasizGecmeli(o, takbis, 0.2m).ImarParselleri.Should().OnlyContain(p => p.Mustakil);
    }

    [Fact]
    public void Saha016_Muvafakatsiz_Paydas_Mustakil_Kalirsa_Md17_Uyarisi_Parsel_Koduyla_Vermeli_Ad_Yazmamali()
    {
        var parseller = new[] { Dikdortgen("K1", ParselRolu.Kadastro, 0m, 50m), Dikdortgen("101", ParselRolu.Imar, 30m, 50m), Dikdortgen("Y1", ParselRolu.YolTerk, 20m, 30m), Dikdortgen("102", ParselRolu.Imar, 0m, 20m) };
        var kroki = Kroki(
            [Cizgi(30, 25, 50, 25), Cizgi(0, 25, 20, 25)],
            [Yazi("ALİ VELİ", 40, 12), Yazi("AYŞE KAYA", 40, 37), Yazi("FATMA DEMİR", 10, 12), Yazi("HÜSEYİN ÇELİK", 10, 37)]);
        var takbis = K1(2500.00m, (A, 1, 4), (B, 1, 4), (C, 1, 4), (D, 1, 4)) is var k
            ? k with { Malikler = [.. k.Malikler.Select(m => m.MalikId == A ? m with { Muvafakatli = false } : m)] }
            : null!;

        var o = Olustur(kroki, parseller, takbis, 0.2m);

        o.Uyarilar.Should().Contain(u => u.StartsWith("1 müstakil parsel (101/1) AAD Md. 17/1-(d) gereği muvafakat gerektiriyor", StringComparison.Ordinal));
        string.Join("|", o.Uyarilar).Should().NotContain("ALİ").And.NotContain("VELİ", "uyarılar malik adı içermez (KVKK)");
    }

    [Fact]
    public void Saha016_Toplam_Net_Hak_Blok_Toplamindan_Birkac_dm2_Farkliysa_LR_Ile_Dengelenmeli_HakEslestirme_Uyarisiz()
    {
        // Tapu 2500.03 m², hisseler 1/3 + 1/3 + 1/3 (giren 833.34 × 3 — malik bazında yuvarlama) ve DOPO 0.2 →
        // net 666.67 × 3 = 2000.01 ≠ R2 2000.00; LR her hak sahibini en fazla 1 dm² oynatır (ERR-TAPU-006 yok).
        var parseller = new[] { Dikdortgen("K1", ParselRolu.Kadastro, 0m, 50m), Dikdortgen("101", ParselRolu.Imar, 30m, 50m), Dikdortgen("Y1", ParselRolu.YolTerk, 20m, 30m), Dikdortgen("102", ParselRolu.Imar, 0m, 20m) };
        var kroki = Kroki([Cizgi(30, 25, 50, 25), Cizgi(0, 25, 20, 25)], [Yazi("ALİ VELİ", 40, 12), Yazi("AYŞE KAYA", 40, 37), Yazi("FATMA DEMİR", 10, 30)]);
        var takbis = K1(2500.03m, (A, 1, 3), (B, 1, 3), (C, 1, 3));

        var o = Olustur(kroki, parseller, takbis, 0.2m);

        o.Uyarilar.Should().Contain(u => u.StartsWith("Σ net hak 2000.01 m² ≠ bölünen blokların alanı 2000.00 m²", StringComparison.Ordinal));
        o.Farklar.Should().OnlyContain(f => f.FarkM2 != null && Math.Abs(f.FarkM2.Value) <= 0.01m);
        o.Satirlar.GroupBy(s => s.ImarParselKodu).Should().OnlyContain(g => g.Sum(s => s.AlanM2) == o.Parseller.Single(p => p.Kod == g.Key).GaussAlanM2);
        var s = HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(0.2m, [takbis],
            o.Parseller.Select(p => new ImarParseli(p.Kod, p.GaussAlanM2)).ToList(), KrokiGirdiDogrulayici.Dogrula(o.Satirlar, [takbis]).Value));
        s.Value.Bulgular.Should().BeEmpty("|tahsis − net| ≤ 1 dm² uyarı eşiğinin içindedir");
    }

    [Fact]
    public void Saha016_Ayni_Girdi_Her_Zaman_Ayni_Dagilimi_Vermeli()
    {
        var parseller = new[] { Dikdortgen("K1", ParselRolu.Kadastro, 0m, 50m), Dikdortgen("101", ParselRolu.Imar, 30m, 50m), Dikdortgen("Y1", ParselRolu.YolTerk, 20m, 30m), Dikdortgen("102", ParselRolu.Imar, 0m, 20m) };
        var kroki = Kroki([Cizgi(30, 25, 50, 25), Cizgi(0, 25, 20, 25)], [Yazi("ALİ VELİ", 40, 12), Yazi("AYŞE KAYA", 40, 37), Yazi("FATMA DEMİR", 10, 12), Yazi("HÜSEYİN ÇELİK", 10, 37)]);
        var takbis = K1(2500.00m, (A, 3, 10), (B, 3, 10), (C, 1, 5), (D, 1, 5));
        var ters = takbis with { Malikler = [.. takbis.Malikler.Reverse()] };

        var a = Olustur(kroki, parseller, takbis, 0.2m);
        var b = Olustur(kroki, parseller, ters, 0.2m);

        b.DagilimSatirlari().Should().Equal(a.DagilimSatirlari());
        b.Satirlar.Should().BeEquivalentTo(a.Satirlar);
    }

    private static ParselGeometrisi[] IkiBlok() =>
        [Dikdortgen("K1", ParselRolu.Kadastro, 0m, 50m), Dikdortgen("101", ParselRolu.Imar, 30m, 50m), Dikdortgen("Y1", ParselRolu.YolTerk, 20m, 30m), Dikdortgen("102", ParselRolu.Imar, 0m, 20m)];

    private const string E = "ZEYNEP ARSLAN (YUSUF)";

    [Fact]
    public void Saha016_Takbis_Sirasi_Ters_Cevrilince_Cok_Malikli_Asiri_Talepli_Bolgede_Ayni_Dagilim_Olmali()
    {
        // Reviewer Y1: 101/1'de A + B (400 + 400), 101/2'de C (400) → 1200 > 1000 → 666.67 / 333.33; 101/1 içinde 333.34 / 333.33 eşit
        // kalanı küçük kimliğe (ALİ) gider. 102/1 D (800), 102/2 yazısız → kalan 200; A, B, C artanları oraya.
        var kroki = Kroki([Cizgi(30, 25, 50, 25), Cizgi(0, 25, 20, 25)],
            [Yazi("AYŞE KAYA", 40, 8), Yazi("ALİ VELİ", 40, 16), Yazi("FATMA DEMİR", 40, 37), Yazi("HÜSEYİN ÇELİK", 10, 12)]);
        var takbis = K1(2500.00m, (B, 1, 5), (A, 1, 5), (C, 1, 5), (D, 2, 5));
        var ters = takbis with { Malikler = [.. takbis.Malikler.Reverse()] };

        var a = Olustur(kroki, IkiBlok(), takbis, 0.2m);
        var b = Olustur(kroki, IkiBlok(), ters, 0.2m);

        a.Parseller[0].Hissedarlar.Single(h => h.MalikId == A).AlanM2.Should().Be(333.34m);
        b.Parseller[0].Hissedarlar.Single(h => h.MalikId == A).AlanM2.Should().Be(333.34m);
        b.Satirlar.Should().BeEquivalentTo(a.Satirlar);
        b.Parseller.Select(p => string.Join("|", p.Hissedarlar.OrderBy(h => h.MalikId, StringComparer.Ordinal).Select(h => h.MalikId + "=" + h.Hisse)))
            .Should().Equal(a.Parseller.Select(p => string.Join("|", p.Hissedarlar.OrderBy(h => h.MalikId, StringComparer.Ordinal).Select(h => h.MalikId + "=" + h.Hisse))));
        DegismezlerSaglanmali(a);
        HakEslestirmeHatasizGecmeli(a, takbis, 0.2m);
    }

    [Fact]
    public void Saha016_Kiymik_Blok_Farki_Blok_Icinde_Emilmeli_Musterek_Hisse_Uretmemeli_Uyarmali()
    {
        // Giren 625.19 × 2 (blok 101) ve 624.81 × 2 (blok 102), DOPO 0.2 → net 500.15 × 2 = 1000.30 ve 499.85 × 2 = 999.70; Σ = R.
        // Blok farkları ∓0.30 m² < 0.50 m² kıymık eşiği → blok içinde emilir: dört parsel 500.00 TAM, anlamsız dm² hisse yok.
        var kroki = Kroki([Cizgi(30, 25, 50, 25), Cizgi(0, 25, 20, 25)],
            [Yazi("ALİ VELİ", 40, 12), Yazi("AYŞE KAYA", 40, 37), Yazi("FATMA DEMİR", 10, 12), Yazi("HÜSEYİN ÇELİK", 10, 37)]);
        var takbis = K1(2500.00m, (A, 62519, 250000), (B, 62519, 250000), (C, 62481, 250000), (D, 62481, 250000));

        var o = Olustur(kroki, IkiBlok(), takbis, 0.2m);

        o.Parseller.Should().OnlyContain(p => p.Mustakil && p.HedefAlanM2 == 500.00m);
        o.Uyarilar.Should().Contain(u => u.StartsWith("Blok 101: Σ net hak 1000.30 m² ≠ blok alanı 1000.00 m² (fark -0.30 m²) kıymık eşiğinin (0.50 m²) altında", StringComparison.Ordinal))
            .And.Contain(u => u.StartsWith("Blok 102: Σ net hak 999.70 m² ≠ blok alanı 1000.00 m² (fark +0.30 m²) kıymık eşiğinin", StringComparison.Ordinal));
        o.Farklar.Select(f => f.FarkM2).Should().BeEquivalentTo(new decimal?[] { -0.15m, -0.15m, 0.15m, 0.15m }, "emilen fark malik farkı olarak raporlanır (SORU-069)");
        var h = HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(0.2m, [takbis],
            o.Parseller.Select(p => new ImarParseli(p.Kod, p.GaussAlanM2)).ToList(), KrokiGirdiDogrulayici.Dogrula(o.Satirlar, [takbis]).Value)).Value;
        h.KritikVar.Should().BeFalse("Σ parsel = alan; yalnız ERR-TAPU-006 (Warning) uyarısı beklenir");
        h.Bulgular.Should().OnlyContain(b => b.Code == ErrorCodes.TapuMalikTahsisiNetHaktanFarkli);
    }

    [Fact]
    public void Saha016_Tum_Bloklar_Kiymik_Ama_Krokide_Olmayan_Malik_Varsa_Emme_Geri_Alinmali_Malik_Yerlesmeli()
    {
        // Net 499.95 × 4 (giren 624.94) + E 0.20 (giren 0.25), tapu 2500.01, DOPO 0.2 → Σ net 2000.00 = R. Bloklar 999.90 (fark +0.10 < 0.50)
        // ama E'nin yeri yalnız açık kapasite: emme kapatılır, her bloğun 0.10'luk açık kapasitesi E'ye yazılır; tüm haklar tam.
        var kroki = Kroki([Cizgi(30, 25, 50, 25), Cizgi(0, 25, 20, 25)],
            [Yazi("ALİ VELİ", 40, 12), Yazi("AYŞE KAYA", 40, 37), Yazi("FATMA DEMİR", 10, 12), Yazi("HÜSEYİN ÇELİK", 10, 37)]);
        var takbis = K1(2500.01m, (A, 62494, 250001), (B, 62494, 250001), (C, 62494, 250001), (D, 62494, 250001), (E, 25, 250001));

        var o = Olustur(kroki, IkiBlok(), takbis, 0.2m);

        o.Uyarilar.Should().NotContain(u => u.Contains("kıymık eşiğinin"));
        o.Satirlar.Where(s => s.MalikKodu == E).Sum(s => s.AlanM2).Should().Be(0.20m);
        DegismezlerSaglanmali(o);
        HakEslestirmeHatasizGecmeli(o, takbis, 0.2m);
    }
}

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
/// SAHA-019 (mühendis talebi): kroki parselasyon önizlemesinde otomatik müstakil / müşterek dağılımın elle düzenlenmesi
/// (<see cref="KrokiDagilimDuzenleyici"/>). Sentetik proje (SAHA-016 Senaryo 2 — uydurma adlar, konum gerçek değil): K1 2000 m²,
/// blok 101 = 1600 m², yol 400 m²; DOPO 0.2. A (ALİ) 1/2 → net 800, B (AYŞE) 3/8 → net 600, C (FATMA, krokide yok) 1/8 → net 200.
/// Otomatik: 101/1 = 1000 m² (A 800 + C 200, müşterek), 101/2 = 600 m² (B, TAM). Elle referans dağılım (el hesabı):
/// 101/1 = A 800 + B 200 → 80000/100000 + 20000/100000; 101/2 = B 400 + C 200 → 40000/60000 + 20000/60000.
/// </summary>
public sealed class Saha019MusterekDuzenlemeTests
{
    private const decimal X0 = 4_400_000m;
    private const decimal Y0 = 500_000m;

    private const string A = "ALİ VELİ (HASAN)";
    private const string B = "AYŞE KAYA (MEHMET)";
    private const string C = "FATMA DEMİR (OSMAN)";

    private static GeodeticPoint P(decimal x, decimal y) => new(X: X0 + x, Y: Y0 + y);

    private static ParselGeometrisi Dikdortgen(string id, ParselRolu rol, decimal x1, decimal x2) => new(id, rol, [P(x1, 0), P(x1, 50), P(x2, 50), P(x2, 0)]);

    private static readonly KadastroParseli Takbis = new("K1", 2000.00m,
    [
        new KadastroMaliki(A, new Fraction(1, 2), Muvafakatli: true),
        new KadastroMaliki(B, new Fraction(3, 8), Muvafakatli: true),
        new KadastroMaliki(C, new Fraction(1, 8), Muvafakatli: true),
    ]);

    private static KrokiParselasyonOnizleme Otomatik()
    {
        var kroki = new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "0")],
            [new CadPolyline("0", "BOLME", null, [P(0, 28), P(32, 28)], false, null)],
            [new CadText("0", "ALİ VELİ", P(16, 14)) { Merkez = P(16, 14) }, new CadText("0", "AYŞE KAYA", P(16, 40)) { Merkez = P(16, 40) }],
            0, 0, 0, []);
        var parseller = new[] { Dikdortgen("K1", ParselRolu.Kadastro, 0m, 40m), Dikdortgen("101", ParselRolu.Imar, 0m, 32m), Dikdortgen("Y1", ParselRolu.YolTerk, 32m, 40m) };
        var s = KrokiParselasyonMotoru.Olustur(new KrokiParselasyonGirdisi(kroki, parseller, [Takbis], MalikRehberi.Bos, 0.2m), new KrokiBlokBolucu());
        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.TechnicalMessage : string.Empty);
        return s.Value;
    }

    private static KrokiEslesmeGirdisi S(string malik, string imar, decimal alan) => new(malik, "K1", imar, alan);

    private static readonly KrokiEslesmeGirdisi[] ElleDagilim = [S(A, "101/1", 800.00m), S(B, "101/1", 200.00m), S(B, "101/2", 400.00m), S(C, "101/2", 200.00m)];

    [Fact]
    public void Hedefler_Otomatik_Dagitimin_Hak_Sahibi_Net_Haklari_Olmali()
    {
        var o = Otomatik();
        o.Parseller.Select(p => p.DagilimSatiri()).Should().Equal(
            "101/1 • 1000.00 m² • Müşterek (2 hissedar: ALİ VELİ 80000/100000, FATMA DEMİR 20000/100000)",
            "101/2 • 600.00 m² • Müstakil (TAM)");

        KrokiDagilimDuzenleyici.Hedefler(o).Should().BeEquivalentTo(
            [new KrokiHakHedefi(A, "K1", 800.00m), new KrokiHakHedefi(B, "K1", 600.00m), new KrokiHakHedefi(C, "K1", 200.00m)]);
    }

    [Fact]
    public void Denetle_Musterek_Parsel_Degisince_Canli_Fark_Gostermeli()
    {
        var o = Otomatik();
        var h = KrokiDagilimDuzenleyici.Hedefler(o);
        // C'nin 200 m²'sini 101/1'den 101/2'ye taşı (henüz B azaltılmadı): 101/1 −200, 101/2 +200; haklar değişmez.
        var satirlar = o.Satirlar.Select(s => s.MalikKodu == C ? s with { ImarParselKodu = "101/2" } : s).ToList();

        var d = KrokiDagilimDuzenleyici.Denetle(o, h, satirlar);

        d.Dengeli.Should().BeFalse();
        d.Parseller.Should().Equal(("101/1", 1000.00m, 800.00m), ("101/2", 600.00m, 800.00m));
        d.Haklar.Should().OnlyContain(x => x.DagitilanM2 == x.Hedef.NetHakM2, "malik toplamları değişmedi");

        // B satırı 101/1'e 200 aktarılınca kapanır; ayrıca malik bazında fark görünür.
        var eksik = satirlar.Where(s => s.MalikKodu != B).ToList();
        var d2 = KrokiDagilimDuzenleyici.Denetle(o, h, eksik);
        d2.Haklar.Single(x => x.Hedef.MalikId == B).DagitilanM2.Should().Be(0m);
        KrokiDagilimDuzenleyici.Denetle(o, h, ElleDagilim).Dengeli.Should().BeTrue();
    }

    [Fact]
    public void Uygula_Elle_Dagilimi_Tam_Ve_PayPayda_Hisselerle_Onizlemeye_Yazmali_Geometriyi_Korumali()
    {
        var o = Otomatik();

        var r = KrokiDagilimDuzenleyici.Uygula(o, KrokiDagilimDuzenleyici.Hedefler(o), ElleDagilim);

        r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.TechnicalMessage : string.Empty);
        var e = r.Value;
        e.ElleDuzenlendi.Should().BeTrue();
        e.Parseller.Select(p => p.DagilimSatiri()).Should().Equal(
            "101/1 • 1000.00 m² • Müşterek (2 hissedar: ALİ VELİ 80000/100000, AYŞE KAYA 20000/100000)",
            "101/2 • 600.00 m² • Müşterek (2 hissedar: AYŞE KAYA 40000/60000, FATMA DEMİR 20000/60000)");
        e.Parseller.Select(p => p.Etiket()).Should().Equal("101/1 • ALİ VELİ +1 • 1000.00 m² • Müşterek", "101/2 • AYŞE KAYA +1 • 600.00 m² • Müşterek");
        e.Parseller.Select(p => (p.Kod, p.Koseler, p.HedefAlanM2, p.GaussAlanM2)).Should().Equal(o.Parseller.Select(p => (p.Kod, p.Koseler, p.HedefAlanM2, p.GaussAlanM2)),
            "geometri ve alanlar değişmez");
        e.Satirlar.Should().Equal(ElleDagilim);
        e.MusterekSayisi.Should().Be(2);
        e.MustakilSayisi.Should().Be(0);
        e.Ozet().Should().EndWith("0 müstakil (TAM) · 2 müşterek (pay/payda) · ✏️ elle düzenlendi");
        e.Uyarilar[0].Should().Be(KrokiDagilimDuzenleyici.ElleDuzenlemeUyarisi);
        e.Farklar.Should().OnlyContain(f => f.FarkM2 == 0m);
        e.KrokiMetni().Should().Contain("AYŞE KAYA (MEHMET);K1;101/1;200.00").And.Contain("FATMA DEMİR (OSMAN);K1;101/2;200.00");

        // Bağımsız denetim: elle dağılım hak eşleştirmesinden bulgusuz geçer (Σ parsel = alan, Σ malik = net hak).
        var satirlar = KrokiGirdiDogrulayici.Dogrula(e.Satirlar, [Takbis]);
        satirlar.IsSuccess.Should().BeTrue();
        var h = HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(0.2m, [Takbis], e.Parseller.Select(p => new ImarParseli(p.Kod, p.GaussAlanM2)).ToList(), satirlar.Value));
        h.IsSuccess.Should().BeTrue();
        h.Value.Bulgular.Should().BeEmpty();
        h.Value.ImarParselleri.Single(p => p.GeciciKod == "101/2").Paydaslar.Select(x => x.Hisse.ToString()).Should().BeEquivalentTo("40000/60000", "20000/60000");
    }

    [Fact]
    public void Uygula_Ayni_Satirlari_Birlestirmeli_Tek_Paydasli_Parseli_TAM_Yapmali()
    {
        var o = Otomatik();
        // 101/1: A 500 + A 300 + C 200; 101/2: B 300 + B 300 → B tek paydaş → TAM.
        KrokiEslesmeGirdisi[] satirlar = [S(A, "101/1", 500.00m), S(A, "101/1", 300.00m), S(C, "101/1", 200.00m), S(B, "101/2", 300.00m), S(B, "101/2", 300.00m)];

        var r = KrokiDagilimDuzenleyici.Uygula(o, KrokiDagilimDuzenleyici.Hedefler(o), satirlar);

        r.IsSuccess.Should().BeTrue();
        r.Value.Satirlar.Should().Equal(S(A, "101/1", 800.00m), S(C, "101/1", 200.00m), S(B, "101/2", 600.00m));
        r.Value.Parseller[1].HisseOzeti().Should().Be("Müstakil (TAM)");
        r.Value.Parseller[1].Hissedarlar.Single().Hisse.Tam.Should().BeTrue();
    }

    [Theory]
    [InlineData("parsel")]
    [InlineData("hak")]
    [InlineData("bilinmeyenParsel")]
    [InlineData("bilinmeyenMalik")]
    [InlineData("sifir")]
    [InlineData("ucOndalik")]
    [InlineData("bos")]
    public void Uygula_Dengesiz_Veya_Gecersiz_Dagilimi_ERR_TAPU_004_Ile_Reddetmeli_Malik_Adi_Yazmamali(string durum)
    {
        var o = Otomatik();
        KrokiEslesmeGirdisi[] satirlar = durum switch
        {
            "parsel" => [S(A, "101/1", 800.00m), S(B, "101/1", 300.00m), S(B, "101/2", 300.00m), S(C, "101/2", 200.00m)], // 101/1 = 1100
            "hak" => [S(A, "101/1", 700.00m), S(B, "101/1", 300.00m), S(B, "101/2", 400.00m), S(C, "101/2", 200.00m)], // A 700 ≠ 800, B 700 ≠ 600
            "bilinmeyenParsel" => [.. ElleDagilim, S(A, "999/9", 1.00m)],
            "bilinmeyenMalik" => [.. ElleDagilim, S("HAYALET KİŞİ (YOK)", "101/1", 1.00m)],
            "sifir" => [.. ElleDagilim, S(A, "101/1", 0m)],
            "ucOndalik" => [S(A, "101/1", 799.995m), S(A, "101/1", 0.005m), S(B, "101/1", 200.00m), S(B, "101/2", 400.00m), S(C, "101/2", 200.00m)],
            _ => [],
        };

        var r = KrokiDagilimDuzenleyici.Uygula(o, KrokiDagilimDuzenleyici.Hedefler(o), satirlar);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        r.Error.TechnicalMessage.Should().NotContain("ALİ").And.NotContain("AYŞE").And.NotContain("HAYALET", "KVKK: teknik mesajda malik adı yok");
    }

    [Fact]
    public void Uygula_TAKBIS_Verilince_Md17_Muvafakat_Uyarisini_Elle_Dagilim_Icin_Yeniden_Hesaplamali()
    {
        var o = Otomatik();
        var muvafakatsiz = Takbis with { Malikler = [.. Takbis.Malikler.Select(m => m with { Muvafakatli = false })] };
        // 101/1: A 800 + B 200 (müşterek); 101/2: B 400 + C 200 (müşterek) → müstakil yok → uyarı yok.
        var r1 = KrokiDagilimDuzenleyici.Uygula(o, KrokiDagilimDuzenleyici.Hedefler(o), ElleDagilim, [muvafakatsiz]);
        r1.Value.Uyarilar.Should().NotContain(u => u.Contains("Md. 17/1-(d)", StringComparison.Ordinal));

        // 101/1: A 800 + C 200 (müşterek); 101/2: B 600 TAM → muvafakatsız paydaş tek başına müstakil.
        KrokiEslesmeGirdisi[] tam = [S(A, "101/1", 800.00m), S(C, "101/1", 200.00m), S(B, "101/2", 600.00m)];
        var r2 = KrokiDagilimDuzenleyici.Uygula(o, KrokiDagilimDuzenleyici.Hedefler(o), tam, [muvafakatsiz]);
        r2.Value.Uyarilar.Should().Contain(u => u.StartsWith("1 müstakil parsel (101/2) AAD Md. 17/1-(d)", StringComparison.Ordinal));
        r2.Value.Uyarilar.Should().NotContain(KrokiDagilimDuzenleyici.Md17DenetlenemediUyarisi);

        KrokiDagilimDuzenleyici.Uygula(o, KrokiDagilimDuzenleyici.Hedefler(o), tam).Value.Uyarilar
            .Should().Contain(KrokiDagilimDuzenleyici.Md17DenetlenemediUyarisi, "TAKBİS yoksa denetlenemediği açıkça söylenir");
    }

    [Fact]
    public void Elle_Duzenlenmis_Onizlemenin_Uygula_Komutu_Denetime_Dagilim_Elle_Yazmali_Otomatik_Eskisi_Gibi_Kalmali()
    {
        var o = Otomatik();
        var e = KrokiDagilimDuzenleyici.Uygula(o, KrokiDagilimDuzenleyici.Hedefler(o), ElleDagilim).Value;
        Imar18.Domain.Revizyon.RevisionId.TryParse(new string('a', 64), out var rev).Should().BeTrue();

        new Imar18.Application.Girdi.KrokiParselasyonuUygulaKomutu(e, rev).Ayrinti.Should().Be("parselSayisi=2;blokSayisi=1;kaynak=yaklasikYerKrokisi;dagilim=elle");
        new Imar18.Application.Girdi.KrokiParselasyonuUygulaKomutu(o, rev).Ayrinti.Should().Be("parselSayisi=2;blokSayisi=1;kaynak=yaklasikYerKrokisi");
    }

    [Fact]
    public void Otomatik_Onizleme_Duzenlenmemis_Isaretli_Olmamali()
    {
        var o = Otomatik();

        o.ElleDuzenlendi.Should().BeFalse();
        o.Ozet().Should().NotContain("elle");
    }
}

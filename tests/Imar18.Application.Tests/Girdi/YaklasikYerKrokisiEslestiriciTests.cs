using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Girdi;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Imar18.GeometryEngine.Topoloji;
using Xunit;

namespace Imar18.Application.Tests.Girdi;

/// <summary>
/// P7.3-B2 (SAHA-006): Yaklaşık Yer Krokisi (.NCZ) → Kroki satırları. Bağımsız referans: eksene paralel dikdörtgenlerin elle
/// hesaplanan kesişim alanları.
/// <list type="bullet">
/// <item>Kip 1: etiketli kullanım poligonu (etiket M-kodu, rehberdeki ad (NCZ'de 15 karakterle kesilmiş olabilir) veya kişi
/// no) → malikin (K ∩ I ∩ U) alanı.</item>
/// <item>Kip 2: K ∩ I'nin kullanım poligonlarıyla karşılanmayan kalanı, K'nin TAKBİS hisse oranlarıyla bölünür.</item>
/// <item>Her imar parselinde satır toplamı, parsel alanına Largest Remainder ile 0.01 m² birimde tam eşitlenir (ERR-TAPU-005
/// koşulu); 0.50 m² altı kesişim kıymıktır.</item>
/// </list>
/// Adlar uydurmadır.
/// </summary>
public sealed class YaklasikYerKrokisiEslestiriciTests
{
    private static readonly IParselKesisimHesaplayici Kesisim = new NtsParselKesisimHesaplayici();

    private static GeodeticPoint P(decimal x, decimal y) => new(X: 4_400_000m + x, Y: 500_000m + y);

    /// <summary>Kuzey X ∈ [0, 10], Doğu Y ∈ [y1, y2] (OrnekProje.Dikdortgen ile aynı düzen).</summary>
    private static IReadOnlyList<GeodeticPoint> Serit(decimal y1, decimal y2, decimal x1 = 0m, decimal x2 = 10m) =>
        [P(x1, y1), P(x1, y2), P(x2, y2), P(x2, y1)];

    private static ParselGeometrisi Parsel(string id, ParselRolu rol, decimal y1, decimal y2) => new(id, rol, Serit(y1, y2));

    private static CadPolyline Kullanim(string etiket, decimal y1, decimal y2, decimal x1 = 0m, decimal x2 = 10m)
    {
        var h = Serit(y1, y2, x1, x2).ToList();
        h.Add(h[0]);
        return new CadPolyline("KULLANIM", etiket, null, h, true, null);
    }

    private static CadLayerDocument Kroki(IReadOnlyList<CadPolyline> poligonlar, IReadOnlyList<CadText>? yazilar = null) =>
        new(CadFormat.Ncz, [new CadLayer(0, "KULLANIM")], poligonlar, yazilar ?? [], 0, 0, 0, []);

    /// <summary>K1 = Doğu 0..20 (200 m²); A1 = 0..12 (120 m²), A2 = 12..20 (80 m²). TAKBİS: M01 1/2, M02 1/2.</summary>
    private static KrokiEslestirmeGirdisi Girdi(
        CadLayerDocument kroki, IReadOnlyList<KadastroParseli>? takbis = null, decimal? dopo = null, MalikRehberi? rehber = null,
        IReadOnlyList<ImarParseli>? imar = null, IReadOnlyList<ParselGeometrisi>? geometri = null) => new(
        kroki,
        geometri ?? [Parsel("K1", ParselRolu.Kadastro, 0, 20), Parsel("A1", ParselRolu.Imar, 0, 12), Parsel("A2", ParselRolu.Imar, 12, 20)],
        imar ?? [new ImarParseli("A1", 120.00m), new ImarParseli("A2", 80.00m)],
        takbis ?? [new KadastroParseli("K1", 200.00m, [new KadastroMaliki("M01", new Fraction(1, 2), false), new KadastroMaliki("M02", new Fraction(1, 2), false)])],
        rehber ?? MalikRehberi.Bos,
        dopo);

    private static KrokiEslestirmeSonucu Eslestir(KrokiEslestirmeGirdisi g)
    {
        var s = YaklasikYerKrokisiEslestirici.Eslestir(g, Kesisim);
        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.TechnicalMessage : string.Empty);
        return s.Value;
    }

    private static List<(string, string, string, decimal)> Satirlar(KrokiEslestirmeSonucu s) =>
        s.Satirlar.Select(r => (r.MalikKodu, r.KadastroParselKimligi, r.ImarParselKodu, r.AlanM2)).ToList();

    [Fact]
    public void Kip2_Kullanim_Poligonu_Yoksa_Kesisim_Hisse_Oraninda_Bolunmeli()
    {
        var s = Eslestir(Girdi(Kroki([])));

        Satirlar(s).Should().Equal(("M01", "K1", "A1", 60.00m), ("M02", "K1", "A1", 60.00m), ("M01", "K1", "A2", 40.00m), ("M02", "K1", "A2", 40.00m));
        s.KullanimPoligonu.Should().Be(0);
        s.Uyarilar.Should().Contain(u => u.Contains("kullanım poligonu yok", StringComparison.Ordinal));
    }

    [Fact]
    public void Kip1_Kod_Etiketli_Kullanim_Poligonlari_Malikin_Kesisim_Alanini_Vermeli()
    {
        var s = Eslestir(Girdi(Kroki([Kullanim("M01", 0, 10), Kullanim("M02", 10, 20)])));

        Satirlar(s).Should().Equal(("M01", "K1", "A1", 100.00m), ("M02", "K1", "A1", 20.00m), ("M02", "K1", "A2", 80.00m));
        s.KullanimPoligonu.Should().Be(2);
        s.CozulemeyenPoligon.Should().Be(0);
    }

    [Fact]
    public void Kip1_Ve_Kip2_Birlikte_Kalan_Alan_Hisse_Oraninda_Eklenmeli()
    {
        // M01 kullanımı Doğu 0..6 (60 m²); A1'in kalan 60 m²'si 1/2–1/2: M01 60 + 30 = 90, M02 30. A2 tamamen Kip 2: 40 / 40.
        var s = Eslestir(Girdi(Kroki([Kullanim("M01", 0, 6)])));

        Satirlar(s).Should().Equal(("M01", "K1", "A1", 90.00m), ("M02", "K1", "A1", 30.00m), ("M01", "K1", "A2", 40.00m), ("M02", "K1", "A2", 40.00m));
    }

    [Fact]
    public void Kip1_Etiket_Rehberdeki_Ad_Kesilmis_Ad_Veya_Kisi_No_Ile_Cozulmeli()
    {
        var rehber = new MalikRehberi(
            [new MalikRehberKaydi("M01", "AYŞE YILMAZ", "ALİ", "800000001"), new MalikRehberKaydi("M02", "MEHMET ÇAKIR", "ALİ", "800000002")], []);
        var kroki = Kroki(
            [Kullanim("Ayşe Yılmaz", 0, 5), Kullanim("MEHMET ÇAKI", 5, 10), Kullanim(string.Empty, 10, 15), Kullanim("800000002", 15, 20)],
            [new CadText("YAZI", "800000001", P(5, 12.5m))]);

        var s = Eslestir(Girdi(kroki, rehber: rehber));

        s.KullanimPoligonu.Should().Be(4);
        s.CozulemeyenPoligon.Should().Be(0);
        Satirlar(s).Should().Equal(("M01", "K1", "A1", 70.00m), ("M02", "K1", "A1", 50.00m), ("M01", "K1", "A2", 30.00m), ("M02", "K1", "A2", 50.00m));
    }

    [Fact]
    public void Cozulemeyen_Poligon_Sayilmali_Kalan_Kip2_ye_Gitmeli_Ve_Uyari_Etiketi_Icermemeli()
    {
        var s = Eslestir(Girdi(Kroki([Kullanim("BİLİNMEYEN KİŞİ", 0, 10)])));

        s.CozulemeyenPoligon.Should().Be(1);
        Satirlar(s).Should().Equal(("M01", "K1", "A1", 60.00m), ("M02", "K1", "A1", 60.00m), ("M01", "K1", "A2", 40.00m), ("M02", "K1", "A2", 40.00m));
        s.Uyarilar.Should().Contain(u => u.Contains("1 kullanım poligonu", StringComparison.Ordinal));
        string.Join("|", s.Uyarilar).Should().NotContain("BİLİNMEYEN");
    }

    [Fact]
    public void Kiymik_Kesisim_Yarim_Metrekarenin_Altinda_Yok_Sayilmali()
    {
        // M02 kullanımı A2'ye yalnız 0.4 m² (Doğu 11.96..12 × 10) taşar: A2 tamamen Kip 2'dir.
        var s = Eslestir(Girdi(Kroki([Kullanim("M02", 0, 12.04m)])));

        Satirlar(s).Where(r => r.Item3 == "A2").Should().Equal(("M01", "K1", "A2", 40.00m), ("M02", "K1", "A2", 40.00m));
        Satirlar(s).Where(r => r.Item3 == "A1").Should().Equal(("M02", "K1", "A1", 120.00m));
    }

    [Fact]
    public void Imar_Parseli_Toplami_Largest_Remainder_Ile_Parsel_Alanina_Tam_Esitlenmeli()
    {
        var takbis = new KadastroParseli("K1", 200.00m,
        [
            new KadastroMaliki("M01", new Fraction(1, 3), false), new KadastroMaliki("M02", new Fraction(1, 3), false),
            new KadastroMaliki("M03", new Fraction(1, 3), false),
        ]);

        var s = Eslestir(Girdi(Kroki([]), takbis: [takbis], imar: [new ImarParseli("A1", 100.01m), new ImarParseli("A2", 80.00m)]));

        var a1 = Satirlar(s).Where(r => r.Item3 == "A1").ToList();
        a1.Sum(r => r.Item4).Should().Be(100.01m);
        a1.Should().Equal(("M01", "K1", "A1", 33.34m), ("M02", "K1", "A1", 33.34m), ("M03", "K1", "A1", 33.33m));
        Satirlar(s).Where(r => r.Item3 == "A2").Sum(r => r.Item4).Should().Be(80.00m);
    }

    [Fact]
    public void Baska_Parselin_Malikinin_Kullanim_Poligonu_Bu_Parselde_Yok_Sayilmali()
    {
        // M09 yalnız K2'nin malikidir; K1 üzerindeki kullanımı K1 dağıtımını etkilemez.
        var takbis = new List<KadastroParseli>
        {
            new("K1", 200.00m, [new KadastroMaliki("M01", new Fraction(1, 2), false), new KadastroMaliki("M02", new Fraction(1, 2), false)]),
            new("K2", 100.00m, [new KadastroMaliki("M09", Fraction.One, false)]),
        };

        var s = Eslestir(Girdi(Kroki([Kullanim("M09", 0, 20)]), takbis: takbis));

        Satirlar(s).Should().NotContain(r => r.Item1 == "M09");
        Satirlar(s).Where(r => r.Item3 == "A1").Should().Equal(("M01", "K1", "A1", 60.00m), ("M02", "K1", "A1", 60.00m));
    }

    [Fact]
    public void Net_Hak_Farki_Malik_Ve_Kadastro_Parseli_Bazinda_Hesaplanmali()
    {
        // DOPO 0.4: giren 100 → DOP 40 → net 60. Kip 2'de M01: 60 + 40 = 100 → fark +40.
        var s = Eslestir(Girdi(Kroki([]), dopo: 0.4m));

        var f = s.Farklar.Single(x => x.MalikKodu == "M01");
        f.KadastroParselId.Should().Be("K1");
        f.KrokiM2.Should().Be(100.00m);
        f.NetHakM2.Should().Be(60.00m);
        f.FarkM2.Should().Be(40.00m);
    }

    [Fact]
    public void Kroki_Metni_Satirlari_Kroki_Dogrulayicisindan_Gecmeli_Ve_Yorumlari_Icermeli()
    {
        var takbis = Girdi(Kroki([])).Takbis;
        var s = Eslestir(Girdi(Kroki([Kullanim("M01", 0, 6)]), dopo: 0.4m));

        var metin = s.KrokiMetni();

        var satirlar = metin.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.Length > 0).ToList();
        satirlar[0].Should().StartWith("# Yaklaşık Yer Krokisi");
        var veri = satirlar.Where(x => !x.StartsWith('#')).Select(x => x.Split(';')).ToList();
        veri.Should().HaveCount(4);
        KrokiGirdiDogrulayici.Dogrula(
            veri.Select(v => new KrokiEslesmeGirdisi(v[0], v[1], v[2], decimal.Parse(v[3], System.Globalization.CultureInfo.InvariantCulture))).ToList(),
            takbis).IsSuccess.Should().BeTrue();
        satirlar.Should().Contain(x => x.StartsWith("# M01 · K1:", StringComparison.Ordinal) && x.Contains("fark", StringComparison.Ordinal));
    }

    [Fact]
    public void Kroki_Proje_Alaniyla_Ortusmuyorsa_ERR_TAPU_004_Vermeli()
    {
        var uzak = new CadPolyline("KULLANIM", "M01", null, [P(5000, 5000), P(5000, 5010), P(5010, 5010), P(5010, 5000), P(5000, 5000)], true, null);

        var s = YaklasikYerKrokisiEslestirici.Eslestir(Girdi(Kroki([uzak])), Kesisim);

        s.IsFailure.Should().BeTrue();
        s.Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        s.Error.TechnicalMessage.Should().Contain("does not overlap");
    }

    [Fact]
    public void TAKBIS_Bos_Veya_Imar_Parseli_Yoksa_ERR_TAPU_004_Vermeli()
    {
        YaklasikYerKrokisiEslestirici.Eslestir(Girdi(Kroki([]), takbis: []), Kesisim).Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        YaklasikYerKrokisiEslestirici.Eslestir(Girdi(Kroki([]), imar: []), Kesisim).Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Fact]
    public void Kesisimsiz_Imar_Parseli_Satir_Uretmemeli_Ve_Uyari_Vermeli()
    {
        var geometri = new[] { Parsel("K1", ParselRolu.Kadastro, 0, 20), Parsel("A1", ParselRolu.Imar, 0, 20), Parsel("A9", ParselRolu.Imar, 100, 110) };

        var s = Eslestir(Girdi(Kroki([]), imar: [new ImarParseli("A1", 200.00m), new ImarParseli("A9", 100.00m)], geometri: geometri));

        s.Satirlar.Should().NotContain(r => r.ImarParselKodu == "A9");
        s.Uyarilar.Should().Contain(u => u.Contains("A9", StringComparison.Ordinal));
    }

    [Fact]
    public void Ayni_Girdiden_Ayni_Sonucu_Uretmeli()
    {
        var g = Girdi(Kroki([Kullanim("M01", 0, 6)]), dopo: 0.4m);

        Eslestir(g).KrokiMetni().Should().Be(Eslestir(g).KrokiMetni());
    }
}

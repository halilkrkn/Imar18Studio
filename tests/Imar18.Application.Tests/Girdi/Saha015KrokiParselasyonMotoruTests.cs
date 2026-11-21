using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Girdi;
using Imar18.Application.Proje;
using Imar18.Application.Tests.KoordinatDonusumu;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Imar18.GeometryEngine.Subdivision;
using Xunit;

namespace Imar18.Application.Tests.Girdi;

/// <summary>
/// SAHA-015 (SORU-044): Yaklaşık Yer Krokisi → kesin alanlı otomatik parselasyon. Sentetik proje (adlar uydurma, konum gerçek değil):
/// K1 kadastro 50 m (Kuzey) × 100 m (Doğu) = 5000 m²; blok 101 X ∈ [28, 50] = 2200 m²; yol Y1 X ∈ [22, 28] = 600 m²; blok 102
/// X ∈ [0, 22] = 2200 m². DOPO = (5000 − 4400) / 5000 = 0.12. Hisseler: 1/5 (giren 1000, net 880), 3/20 × 2 (giren 750, net 660),
/// 1/10 × 5 (giren 500, net 440) → blok 101: 880 + 660 + 660 = 2200, blok 102: 5 × 440 = 2200 (bağımsız el hesabı).
/// </summary>
public sealed class Saha015KrokiParselasyonMotoruTests
{
    internal const decimal X0 = 4_400_000m;
    internal const decimal Y0 = 500_000m;

    internal static readonly string[] Blok101Malikleri = ["ALİ VELİ (HASAN)", "AYŞE KAYA (MEHMET)", "FATMA DEMİR (OSMAN)"];
    internal static readonly string[] Blok102Malikleri = ["HÜSEYİN ÇELİK (İBRAHİM)", "ZEYNEP ARSLAN (YUSUF)", "MUSTAFA ÖZTÜRK (KEMAL)", "EMİNE ŞAHİN (HALİL)", "HATİCE YILDIZ (RECEP)"];

    private static readonly KrokiBlokBolucu Bolucu = new();

    internal static GeodeticPoint P(decimal x, decimal y) => new(X: X0 + x, Y: Y0 + y);

    private static ParselGeometrisi Dikdortgen(string id, ParselRolu rol, decimal x1, decimal x2) => new(id, rol, [P(x1, 0), P(x1, 100), P(x2, 100), P(x2, 0)]);

    internal static IReadOnlyList<ParselGeometrisi> ProjeParselleri() =>
    [
        Dikdortgen("K1", ParselRolu.Kadastro, 0m, 50m),
        Dikdortgen("101", ParselRolu.Imar, 28m, 50m),
        Dikdortgen("Y1", ParselRolu.YolTerk, 22m, 28m),
        Dikdortgen("102", ParselRolu.Imar, 0m, 22m),
    ];

    internal static IReadOnlyList<KadastroParseli> Takbis(params (string Malik, int Pay, int Payda)[] hisseler)
    {
        var liste = hisseler.Length > 0
            ? hisseler
            : [(Blok101Malikleri[0], 1, 5), (Blok101Malikleri[1], 3, 20), (Blok101Malikleri[2], 3, 20),
               .. Blok102Malikleri.Select(m => (m, 1, 10))];
        // SAHA-016 (architect R1): çok malikli K1'in paydaşları muvafakatli — aksi halde her müstakil parsel AAD Md. 17/1-(d) uyarısı alır.
        return [new KadastroParseli("K1", 5000.00m, liste.Select(h => new KadastroMaliki(h.Malik, new Fraction(h.Pay, h.Payda), true)).ToList())];
    }

    private static CadPolyline Cizgi(decimal x1, decimal y1, decimal x2, decimal y2) => new("0", "BOLME", null, [P(x1, y1), P(x2, y2)], false, null);

    private static CadText Yazi(string metin, decimal x, decimal y) =>
        new("0", metin, P(x - 0.5m, y - 6m)) { Merkez = P(x, y) };

    /// <summary>Kroki: blok 101'de 2 eğik çizgi, blok 102'de 4 çizgi (biri kısa, biri yola taşan) ve 8 malik yazısı.</summary>
    internal static CadLayerDocument Kroki(string[]? yazilar = null)
    {
        var y = yazilar ?? ["ALİ VELİ", "ayşe kaya", "FATMA DEMİR", "HÜSEYİN ÇELİK", "ZEYNEP ARSLAN", "MUSTAFA ÖZT", "EMİNE ŞAHİN (HALİL)", "HATİCE YILDIZ"];
        decimal[] dogu101 = [15m, 50m, 85m];
        decimal[] dogu102 = [9m, 30m, 50m, 70m, 90m];
        return new CadLayerDocument(
            CadFormat.Ncz,
            [new CadLayer(0, "0"), new CadLayer(1, "TK_OLUSAN")],
            [
                Cizgi(28, 30, 50, 36), Cizgi(28, 65, 50, 71),
                Cizgi(0, 18, 22, 22), Cizgi(1, 39, 21, 41), Cizgi(0, 58, 22, 62), Cizgi(0, 79, 23, 81),
            ],
            [.. dogu101.Select((d, i) => Yazi(y[i], 39m, d)), .. dogu102.Select((d, i) => Yazi(y[3 + i], 11m, d))],
            0, 0, 0, []);
    }

    internal static KrokiParselasyonGirdisi Girdi(CadLayerDocument? kroki = null, IReadOnlyList<KadastroParseli>? takbis = null, decimal? dopo = 0.12m,
        IReadOnlyList<ParselGeometrisi>? parseller = null) =>
        new(kroki ?? Kroki(), parseller ?? ProjeParselleri(), takbis ?? Takbis(), MalikRehberi.Bos, dopo);

    private static decimal Gauss(IReadOnlyList<GeodeticPoint> k) => GaussAreaCalculator.Compute(PolygonRing.FromOpen(k).Value).AreaM2Exact;

    [Fact]
    public void KrokiParselasyonMotoru_Kroki_2_Blok_6_Cizgi_8_Malik_Sekiz_Kesin_Alanli_Parsel_Uretmeli()
    {
        var s = KrokiParselasyonMotoru.Olustur(Girdi(), Bolucu);

        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.TechnicalMessage : string.Empty);
        var o = s.Value;
        o.Parseller.Select(p => p.Kod).Should().Equal("101/1", "101/2", "101/3", "102/1", "102/2", "102/3", "102/4", "102/5");
        o.Parseller.Select(p => p.HedefAlanM2).Should().Equal(880.00m, 660.00m, 660.00m, 440.00m, 440.00m, 440.00m, 440.00m, 440.00m);
        o.Parseller.Select(p => p.Malikler.Single()).Should().Equal([.. Blok101Malikleri, .. Blok102Malikleri],
            "yazılar Türkçe büyük/küçük harf, AD SOYAD, tam etiket ve ≥ 5 karakter önekle çözülür; sıra batıdan doğuya");
        o.Parseller.Should().OnlyContain(p => Math.Abs(Gauss(p.Koseler) - p.HedefAlanM2) <= 0.01m);
        o.Parseller.Should().OnlyContain(p => p.GaussAlanM2 == p.HedefAlanM2);
        o.Parseller.Where(p => p.BlokKimligi == "101").Sum(p => Gauss(p.Koseler)).Should().BeApproximately(2200m, 0.0001m);
        o.Parseller.Where(p => p.BlokKimligi == "102").Sum(p => Gauss(p.Koseler)).Should().BeApproximately(2200m, 0.0001m);
        o.BolunenBloklar.Should().Equal("101", "102");
        o.Uyarilar.Should().BeEmpty();
        o.Satirlar.Should().HaveCount(8).And.Contain(new KrokiEslesmeGirdisi("ALİ VELİ (HASAN)", "K1", "101/1", 880.00m));
        o.Farklar.Should().OnlyContain(f => f.FarkM2 == 0m, "her malik net hakkını tam alır");
        o.KrokiMetni().Should().Contain("AYŞE KAYA (MEHMET);K1;101/2;660.00").And.StartWith("# Yaklaşık Yer Krokisi otomatik parselasyon: 2 blok · 8 parsel");
        o.Parseller[0].Etiket().Should().Be("101/1 • ALİ VELİ • 880.00 m² • TAM");
        o.Parseller.Should().OnlyContain(p => p.Mustakil && p.HisseOzeti() == "Müstakil (TAM)", "SAHA-016: her malik net hakkı kadar kendi parselini alır");
    }

    [Fact]
    public void KrokiParselasyonMotoru_Kesim_Cizgileri_Muhendisin_Kabaca_Cizdigi_Aciya_Paralel_Olmali()
    {
        var o = KrokiParselasyonMotoru.Olustur(Girdi(), Bolucu).Value;

        // 101/1 ile 101/2'nin ortak kenarı kroki çizgisi (ΔY 6, ΔX 22) doğrultusundadır.
        var ortak = o.Parseller[0].Koseler.Intersect(o.Parseller[1].Koseler).OrderBy(k => k.X).ToArray();
        ortak.Should().HaveCount(2);
        var capraz = ((ortak[1].Y - ortak[0].Y) * 22m) - ((ortak[1].X - ortak[0].X) * 6m);
        Math.Abs(capraz).Should().BeLessThan(0.0001m);
    }

    [Fact]
    public void KrokiParselasyonMotoru_Blok_Hak_Toplami_Blok_Alanindan_Farkliysa_Artan_Musterek_Aktarilmali_Net_Hak_Korunmali_Fark_Raporlanmali()
    {
        // SAHA-016 (SORU-066): blok 101'e 880 + 440 + 440 = 1760 m² hak düşer → açık kapasite 440 en büyük talepli 101/1'de (1320, 440,
        // 440); blok 102'de 660 + 660 + 3 × 440 = 2640 > 2200 → 5/6 orantılı (550, 550, 366.67, 366.67, 366.66), artan 440 m² 101/1'e.
        var takbis = Takbis((Blok101Malikleri[0], 1, 5), (Blok102Malikleri[0], 1, 10), (Blok102Malikleri[1], 1, 10),
            (Blok101Malikleri[1], 3, 20), (Blok101Malikleri[2], 3, 20), (Blok102Malikleri[2], 1, 10), (Blok102Malikleri[3], 1, 10), (Blok102Malikleri[4], 1, 10));
        var kroki = Kroki(["ALİ VELİ", "HÜSEYİN ÇELİK", "ZEYNEP ARSLAN", "AYŞE KAYA", "FATMA DEMİR", "MUSTAFA ÖZTÜRK", "EMİNE ŞAHİN", "HATİCE YILDIZ"]);

        var o = KrokiParselasyonMotoru.Olustur(Girdi(kroki, takbis), Bolucu).Value;

        o.Parseller.Where(p => p.BlokKimligi == "101").Select(p => p.HedefAlanM2).Should().Equal(1320.00m, 440.00m, 440.00m);
        o.Parseller.Where(p => p.BlokKimligi == "102").Select(p => p.HedefAlanM2).Should().Equal(550.00m, 550.00m, 366.67m, 366.67m, 366.66m);
        o.Parseller.Where(p => p.BlokKimligi == "101").Sum(p => p.HedefAlanM2).Should().Be(2200.00m);
        o.Uyarilar.Should().Contain(u => u.Contains("Blok 101: Σ net hak 1760.00 m² ≠ blok alanı 2200.00 m²"));
        o.Farklar.Should().OnlyContain(f => f.FarkM2 == 0m, "hiçbir malikin hakkı blok alanı yüzünden kırpılmaz veya şişirilmez");
        o.Parseller.Single(p => p.Kod == "101/1").Hissedarlar.Should().HaveCount(6, "Ali + blok 102'nin 5 malikinin artanı");
        o.Parseller.Where(p => p.Kod != "101/1").Should().OnlyContain(p => p.Mustakil);
    }

    [Fact]
    public void KrokiParselasyonMotoru_Malike_Eslenemeyen_Parca_Blok_Kalanini_Almali_Krokide_Olmayan_Malik_Oraya_Yazilmali_Uyarmali()
    {
        var kroki = Kroki(["ALİ VELİ", "BİLİNMEYEN KİŞİ", "FATMA DEMİR", "HÜSEYİN ÇELİK", "ZEYNEP ARSLAN", "MUSTAFA ÖZTÜRK", "EMİNE ŞAHİN", "HATİCE YILDIZ"]);

        var o = KrokiParselasyonMotoru.Olustur(Girdi(kroki), Bolucu).Value;

        o.Parseller.Should().HaveCount(8);
        // SAHA-016: blok 101'de yazılı talepler 880 + 660 = 1540 → yazısız 101/2 kalan 660'ı alır; krokide olmayan Ayşe (660) oraya TAM.
        o.Parseller[1].Malikler.Should().Equal("AYŞE KAYA (MEHMET)");
        o.Parseller[1].Mustakil.Should().BeTrue();
        o.Satirlar.Should().Contain(new KrokiEslesmeGirdisi("AYŞE KAYA (MEHMET)", "K1", "101/2", 660.00m));
        o.Uyarilar.Should().Contain(u => u.StartsWith("101/2 parseline TAKBİS malikine eşlenen yazı düşmedi", StringComparison.Ordinal))
            .And.Contain(u => u.StartsWith("1 kroki yazısı TAKBİS malikine eşlenemedi", StringComparison.Ordinal))
            .And.Contain(u => u.Contains("TAKBİS'teki 1 malik krokide yok"));
        o.Uyarilar.Should().NotContain(u => u.Contains("BİLİNMEYEN"), "uyarılar kroki yazısını (ad) içermez — KVKK");
        o.Parseller.Where(p => p.BlokKimligi == "101").Sum(p => p.HedefAlanM2).Should().Be(2200.00m);
    }

    [Fact]
    public void KrokiParselasyonMotoru_Ayni_Parcada_Iki_Malik_Hisseli_Parsel_Olmali_Ve_Ayni_Malik_Iki_Parcada_Hakki_Bolunmeli()
    {
        // Blok 102'nin 1. parçasında iki yazı (hisseli); Ali hem 101/1'de hem 102/5'te.
        var kroki = Kroki() with
        {
            Texts = [.. Kroki().Texts.Take(7), new CadText("0", "ALİ VELİ", P(11m, 90m)) { Merkez = P(11m, 90m) }, new CadText("0", "HATİCE YILDIZ", P(11m, 7m)) { Merkez = P(11m, 7m) }],
        };

        var o = KrokiParselasyonMotoru.Olustur(Girdi(kroki), Bolucu).Value;

        o.Parseller.Single(p => p.Kod == "102/1").Malikler.Should().HaveCount(2);
        o.Uyarilar.Should().Contain(u => u.StartsWith("102/1 parselinde 2 malik var", StringComparison.Ordinal))
            .And.Contain(u => u.StartsWith("1 malik birden çok parçada", StringComparison.Ordinal));
        o.Satirlar.Where(s => s.MalikKodu == "ALİ VELİ (HASAN)").Select(s => s.ImarParselKodu).Should().BeEquivalentTo(["101/1", "101/2", "102/5"],
            "taslak oranındaki iki parçası + blok 102 parçasının artanı, açık kapasitenin toplandığı 101/2'ye");
        o.Farklar.Should().OnlyContain(f => f.FarkM2 == 0m, "SAHA-016: parçalı malik de net hakkını tam alır (artan müşterek aktarılır)");
        o.Parseller.Should().OnlyContain(p => Math.Abs(Gauss(p.Koseler) - p.HedefAlanM2) <= 0.01m);
        o.Satirlar.GroupBy(s => s.ImarParselKodu).Should().OnlyContain(g => g.Sum(s => s.AlanM2) == o.Parseller.Single(p => p.Kod == g.Key).HedefAlanM2);
    }

    [Fact]
    public void KrokiParselasyonMotoru_DOPO_Yoksa_Giren_Oraniyla_Dengelemeli_Net_Hak_Bos_Kalmali_Uyarmali()
    {
        var o = KrokiParselasyonMotoru.Olustur(Girdi(dopo: null), Bolucu).Value;

        // Giren 1000 : 750 : 750 (ve 5 × 500) oranı blok alanına dengelenir.
        o.Parseller.Select(p => p.HedefAlanM2).Should().Equal(880.00m, 660.00m, 660.00m, 440.00m, 440.00m, 440.00m, 440.00m, 440.00m);
        o.Farklar.Should().OnlyContain(f => f.NetHakM2 == null);
        o.Uyarilar.Should().Contain(u => u.StartsWith("DOP girdisi yok", StringComparison.Ordinal));
    }

    [Fact]
    public void KrokiParselasyonMotoru_Bolme_Cizgisi_Yoksa_Veya_Blok_Disindaysa_Tanimli_Nedenle_Reddetmeli()
    {
        var bos = Kroki() with { Polylines = [] };
        var disarida = Kroki() with { Polylines = [Cizgi(23, 10, 27, 10)] }; // yolun içinde

        KrokiParselasyonMotoru.Olustur(Girdi(bos), Bolucu).Error.TechnicalMessage.Should().Contain(KrokiParselasyonMotoru.BolmeCizgisiYokNedeni);
        KrokiParselasyonMotoru.Olustur(Girdi(disarida), Bolucu).Error.TechnicalMessage.Should().Contain(KrokiParselasyonMotoru.BolmeCizgisiYokNedeni);
        KrokiParselasyonMotoru.Olustur(Girdi(takbis: []), Bolucu).Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        KrokiParselasyonMotoru.BolmeCizgileri(Kroki()).Should().HaveCount(6);
    }

    [Fact]
    public void KrokiParselasyonMotoru_Cizgisiz_Blok_Korunmali_Yalniz_Cizgili_Blok_Bolunmeli()
    {
        var kroki = Kroki() with { Polylines = Kroki().Polylines.Take(2).ToList() };

        var o = KrokiParselasyonMotoru.Olustur(Girdi(kroki), Bolucu).Value;

        o.BolunenBloklar.Should().Equal("101");
        o.Parseller.Should().HaveCount(3);
        o.Uyarilar.Should().Contain(u => u.StartsWith("5 kroki yazısı bölünen blokların içinde değil", StringComparison.Ordinal));
    }

    [Fact]
    public void KrokiParselasyonMotoru_Projede_Imar_Yoksa_Kroki_TK_OLUSAN_Bloklari_Bolunmeli_Ve_Projeye_Eklenmeli()
    {
        var bloklar = new[] { ("101", 28m, 50m), ("102", 0m, 22m) }
            .Select(b => new CadPolyline("TK_OLUSAN", b.Item1, null, [P(b.Item2, 0), P(b.Item2, 100), P(b.Item3, 100), P(b.Item3, 0), P(b.Item2, 0)], true, null));
        var kroki = Kroki() with { Polylines = [.. Kroki().Polylines, .. bloklar] };
        var parseller = ProjeParselleri().Where(p => p.Rol != ParselRolu.Imar).ToList();

        var o = KrokiParselasyonMotoru.Olustur(Girdi(kroki, parseller: parseller), Bolucu).Value;
        var komut = KrokiParselasyonUygulayici.KomutOlustur(new ParselGeometrileriAyarlaKomutu(parseller).Uygula(ProjeBelgesi.Yeni("S")).Value, o).Value;

        o.Parseller.Should().HaveCount(8);
        o.Uyarilar.Should().Contain(u => u.StartsWith("Projede imar parseli yok", StringComparison.Ordinal));
        komut.Parseller.Select(p => p.ParselId).Should().Equal("K1", "Y1", "101/1", "101/2", "101/3", "102/1", "102/2", "102/3", "102/4", "102/5");
    }

    [Fact]
    public void KrokiParselasyonUygulayici_Bloklari_Yerinde_Yeni_Parsellerle_Degistirmeli_Digerlerini_Korumali()
    {
        var belge = new ParselGeometrileriAyarlaKomutu(ProjeParselleri()).Uygula(ProjeBelgesi.Yeni("S")).Value;
        var o = KrokiParselasyonMotoru.Olustur(Girdi(), Bolucu).Value;

        var komut = KrokiParselasyonUygulayici.KomutOlustur(belge, o).Value;
        var yeni = komut.Uygula(belge).Value;

        komut.Ayrinti.Should().Be("parselSayisi=10;kaynak=yaklasikYerKrokisi", "denetimde yalnız sayı ve kaynak işareti; ad/koordinat yok");
        yeni.Parseller.Select(p => p.ParselId).Should().Equal("K1", "101/1", "101/2", "101/3", "Y1", "102/1", "102/2", "102/3", "102/4", "102/5");
        yeni.Parseller.Where(p => p.ParselId.Contains('/')).Should().OnlyContain(p => p.Rol == ParselRolu.Imar);
        yeni.Parseller.Single(p => p.ParselId == "K1").Should().Be(belge.Parseller[0]);
    }

    [Fact]
    public void KrokiParselasyonUygulayici_Bayat_Onizlemeyi_Reddetmeli()
    {
        var o = KrokiParselasyonMotoru.Olustur(Girdi(), Bolucu).Value;
        var baska = new ParselGeometrileriAyarlaKomutu([.. ProjeParselleri().Where(p => p.ParselId != "102"), new ParselGeometrisi("103", ParselRolu.Imar, ProjeParselleri()[3].Koseler)])
            .Uygula(ProjeBelgesi.Yeni("S")).Value;

        KrokiParselasyonUygulayici.KomutOlustur(baska, o).Error.Code.Should().Be(ErrorCodes.GirdProjeGirdisiGecersiz);
    }

    [Fact]
    public void KrokiParselasyonMotoru_DNS_Etkinken_Kroki_Ve_Proje_Ayni_Katsayilarla_Donusup_Kesin_Alanlar_Deprem_Sonrasinda_Saglanmali()
    {
        var k = Saha014IleriDonusumTests.K;
        var belge = new IleriDonusumUygulaKomutu(Saha014IleriDonusumTests.Girdi(), k)
            .Uygula(new ParselGeometrileriAyarlaKomutu(ProjeParselleri()).Uygula(ProjeBelgesi.Yeni("S")).Value).Value;
        var kroki = CadBelgesiDonusumu.Uygula(Kroki(), k);

        var o = KrokiParselasyonMotoru.Olustur(Girdi(kroki, parseller: belge.Parseller), Bolucu).Value;
        var komut = KrokiParselasyonUygulayici.KomutOlustur(belge, o).Value;
        var yeni = komut.Uygula(belge).Value;

        o.Parseller.Should().HaveCount(8);
        o.Parseller.Select(p => p.Malikler.Single()).Should().Equal([.. Blok101Malikleri, .. Blok102Malikleri], "yazı merkezleri de dönüştürüldü");
        foreach (var p in o.Parseller)
        {
            Math.Abs(Gauss(yeni.Parseller.Single(x => x.ParselId == p.Kod).Koseler) - p.HedefAlanM2).Should().BeLessThanOrEqualTo(0.01m, p.Kod);
        }

        IleriDonusumDegismezi.Dogrula(yeni).IsSuccess.Should().BeTrue("aktif parseller Deprem Öncesi orijinallerin ileri dönüşümüdür");
        yeni.DepremOncesiParseller!.Single(p => p.ParselId == "K1").Should().Be(belge.DepremOncesiParseller!.Single(p => p.ParselId == "K1"));
        // Blok köşesi (101'in Doğu–Kuzey köşesi) orijinaline birebir döner; ters dönüşüm yuvarlaması yeni köşelere sınırlıdır.
        yeni.DepremOncesiParseller!.Single(p => p.ParselId == "101/3").Koseler.Should().Contain(P(50, 100));
    }

    [Fact]
    public void KrokiParselasyonMotoru_Ayni_Girdi_Her_Zaman_Ayni_Onizlemeyi_Vermeli()
    {
        var a = KrokiParselasyonMotoru.Olustur(Girdi(), Bolucu).Value;
        var b = KrokiParselasyonMotoru.Olustur(Girdi(), Bolucu).Value;

        a.KrokiMetni().Should().Be(b.KrokiMetni());
        a.Parseller.Select(p => p.Koseler).Should().BeEquivalentTo(b.Parseller.Select(p => p.Koseler), o => o.WithStrictOrdering());
    }

    [Fact]
    public void KrokiParselasyonMotoru_Bolucu_Hatasi_Blok_Kimligiyle_Donmeli()
    {
        // Birbirine dik iki çizgi: paralel bölme değildir.
        var kroki = Kroki() with { Polylines = [Cizgi(28, 30, 50, 30), Cizgi(39, 5, 39, 95)] };

        var s = KrokiParselasyonMotoru.Olustur(Girdi(kroki), Bolucu);

        s.Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz);
        s.Error.TechnicalMessage.Should().EndWith("(block 101)");
    }

    [Fact]
    public void KrokiParselasyonMotoru_Ada_Parsel_Kimlikli_Blok_Cakismasiz_Ardisik_Kod_Uretmeli()
    {
        // Reviewer H2: "101/2" bölünür, aynı adadaki "101/1" korunur → yeni kodlar 101/2, 101/3, 101/4 ("~n" eki yok).
        var parseller = ProjeParselleri().Select(p => p.ParselId switch
        {
            "101" => p with { ParselId = "101/2" },
            "102" => p with { ParselId = "101/1" },
            _ => p,
        }).ToList();
        var kroki = Kroki() with { Polylines = Kroki().Polylines.Take(2).ToList() };

        var o = KrokiParselasyonMotoru.Olustur(Girdi(kroki, parseller: parseller), Bolucu).Value;

        o.Parseller.Select(p => p.Kod).Should().Equal("101/2", "101/3", "101/4");
        o.Parseller.Should().OnlyContain(p => !p.Kod.Contains('~'));
    }

    [Fact]
    public void KrokiParselasyonMotoru_Cok_Koseli_Acik_Cizgi_Bolme_Sayilmamali_Uyarmali()
    {
        var kirik = new CadPolyline("0", "OLCU", null, [P(30, 10), P(40, 20), P(45, 12)], false, null);
        var yalnizKirik = Kroki() with { Polylines = [kirik] };
        var karisik = Kroki() with { Polylines = [.. Kroki().Polylines, kirik] };

        KrokiParselasyonMotoru.ParselasyonaUygun(yalnizKirik, ProjeParselleri()).Should().BeFalse("yalnız 2 köşeli açık çizgi bölme çizgisidir");
        var o = KrokiParselasyonMotoru.Olustur(Girdi(karisik), Bolucu).Value;
        o.Parseller.Should().HaveCount(8);
        o.Uyarilar.Should().Contain(u => u.StartsWith("1 çok köşeli açık çizgi bölme çizgisi sayılmadı", StringComparison.Ordinal));
        KrokiParselasyonMotoru.ParselasyonaUygun(Kroki(), ProjeParselleri()).Should().BeTrue();
    }

    [Fact]
    public void KrokiParselasyonMotoru_Malik_Yazisi_Kesin_Parsel_Disinda_Kalirsa_Uyarmali()
    {
        // Blok 101 hedefleri 1100 / 550 / 550 (bkz. dengeleme testi): ilk kesim Y ≈ 47–53'e kayar; Y 35'teki ikinci yazı 101/1'de kalır.
        var takbis = Takbis((Blok101Malikleri[0], 1, 5), (Blok102Malikleri[0], 1, 10), (Blok102Malikleri[1], 1, 10),
            (Blok101Malikleri[1], 3, 20), (Blok101Malikleri[2], 3, 20), (Blok102Malikleri[2], 1, 10), (Blok102Malikleri[3], 1, 10), (Blok102Malikleri[4], 1, 10));
        var kroki = Kroki(["ALİ VELİ", "HÜSEYİN ÇELİK", "ZEYNEP ARSLAN", "AYŞE KAYA", "FATMA DEMİR", "MUSTAFA ÖZTÜRK", "EMİNE ŞAHİN", "HATİCE YILDIZ"]);
        kroki = kroki with { Texts = [.. kroki.Texts.Select((t, i) => i == 1 ? t with { Merkez = P(39m, 35m) } : t)] };

        var o = KrokiParselasyonMotoru.Olustur(Girdi(kroki, takbis), Bolucu).Value;

        o.Uyarilar.Should().Contain(u => u.StartsWith("101/2: malik yazısı kesin parsel sınırının dışında kaldı", StringComparison.Ordinal));
    }

    [Fact]
    public void KrokiParselasyonMotoru_Krokiden_Gelen_Blok_Etiketi_Sayisal_Degilse_ADA_Kimligi_Almali()
    {
        var bloklar = new[] { ("SERBEST ETİKET", 28m, 50m), ("102", 0m, 22m) }
            .Select(b => new CadPolyline("TK_OLUSAN", b.Item1, null, [P(b.Item2, 0), P(b.Item2, 100), P(b.Item3, 100), P(b.Item3, 0), P(b.Item2, 0)], true, null));
        var kroki = Kroki() with { Polylines = [.. Kroki().Polylines, .. bloklar] };

        var o = KrokiParselasyonMotoru.Olustur(Girdi(kroki, parseller: ProjeParselleri().Where(p => p.Rol != ParselRolu.Imar).ToList()), Bolucu).Value;

        o.BolunenBloklar.Should().Equal("ADA1", "102");
        string.Join("|", o.Uyarilar).Should().NotContain("SERBEST", "serbest kroki etiketi (kişisel veri olabilir) uyarıya yazılmaz");
    }

    [Fact]
    public async Task KrokiParselasyonuUygulaKomutu_Bayat_Revizyonda_Reddedilmeli_Ve_Denetime_Yazilmali()
    {
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik SAHA-015", new SabitZaman(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero)))).Value;
        (await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(ProjeParselleri()), "muhendis-01")).IsSuccess.Should().BeTrue();
        var revizyon = yol.Belge.RevizyonKimligi();
        var o = KrokiParselasyonMotoru.Olustur(Girdi(parseller: yol.Belge.Parseller), Bolucu).Value;
        await yol.GonderAsync(new ProjeAdiDegistirKomutu("Başka Ad"), "muhendis-01");

        var bayat = await yol.GonderAsync(new KrokiParselasyonuUygulaKomutu(o, revizyon), "muhendis-01");

        bayat.IsFailure.Should().BeTrue();
        bayat.Error.TechnicalMessage.Should().Contain(KrokiParselasyonuUygulaKomutu.BayatNedeni);
        var son = yol.Denetim[^1];
        son.Islem.Should().Be("KrokiParselasyonuUygula");
        son.Sonuc.Should().Be(AuditSonucu.Reddedildi, "reddedilen onay girişimi de denetime yazılır (reviewer M1)");
        son.Ayrinti.Should().Be("parselSayisi=8;blokSayisi=2;kaynak=yaklasikYerKrokisi");

        var guncel = await yol.GonderAsync(new KrokiParselasyonuUygulaKomutu(o, yol.Belge.RevizyonKimligi()), "muhendis-01");
        guncel.IsSuccess.Should().BeTrue();
        yol.Belge.Parseller.Should().HaveCount(10);
    }

    [Fact]
    public void ParselGeometrileriAyarla_Ezilen_Kroki_Parselasyonu_Denetim_Ayrintisina_Sayi_Olarak_Yazilmali()
    {
        new ParselGeometrileriAyarlaKomutu(ProjeParselleri()) { KrokiParselasyonuEzildi = 8 }.Ayrinti
            .Should().Be("parselSayisi=4;krokiParselasyonuEzildi=8");
    }
}

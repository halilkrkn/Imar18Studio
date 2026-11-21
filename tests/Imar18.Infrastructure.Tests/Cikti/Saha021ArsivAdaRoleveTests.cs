using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Girdi;
using Imar18.Application.Proje;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Imar18.GeometryEngine.Subdivision;
using Imar18.Infrastructure.Cikti.FenKlasoru;
using UglyToad.PdfPig;
using Xunit;
using static Imar18.Infrastructure.Tests.Cikti.FenKlasoruZipTests;

namespace Imar18.Infrastructure.Tests.Cikti;

/// <summary>
/// SAHA-021 (mühendisin son canlı testi): (1) mühendisin girdiği arşiv onaylı kadastro nokta no ve Y, X'i (<c>901001</c>…) hiçbir
/// koşulda ezilmez; <c>.DNS</c> varken de yokken de KOORDİNE ÖZET / RÖLÖVE "DEPREM ÖNCESİ / ARŞİV ONAYLI" (kadastro, arşiv Y, X, MK yok)
/// ve "DEPREM SONRASI" (önce yalnız yeni TK_Oluşan köşeleri MK 0.09, altına kadastro köşeleri arşiv no + dönüşmüş Y, X, MK boş; ortak
/// köşe tek) tablolarıyla basılır (SORU-075 / SORU-078 kapandı). (2) Kroki parselasyonunda etiketi <c>"1"</c>, <c>"2"</c> olan bloklar
/// <c>101/1…</c>, <c>102/1…</c> kodlarını alır. (3) RÖLÖVE / PARSELASYON HARİTASI çiziminde ada no adada bir kez büyük ve kalın, parsel
/// içinde yalnız parsel no, yol <c>Y1</c>, her köşede ○ ve kenara paralel tekil cephe uzunluğu.
/// Veri tamamen sentetiktir (<see cref="Imar18.Application.Tests.Proje.OrnekProje"/>: K1 = 4 köşe; A1 ∪ Y1 = 6 köşe, ikisi yeni).
/// </summary>
public sealed class Saha021ArsivAdaRoleveTests
{
    private static string F2(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Arşiv: 901001…901004; arşiv koordinatı köşeden kasıtlı farklı (Y + 0.01, X − 0.02) — basılan değer ayırt edilir.</summary>
    private static IReadOnlyList<ArsivNoktaKaydi> Arsiv(ProjeBelgesi b) => ArsivNoktalari.Varsayilan(b, [])
        .Select(k => k with { NoktaNo = (901000 + k.Sira).ToString(CultureInfo.InvariantCulture), ArsivY = k.KoseY + 0.01m, ArsivX = k.KoseX - 0.02m })
        .ToList();

    private static int Say(string metin, string aranan) => Regex.Matches(metin, Regex.Escape(aranan)).Count;

    private static (string Once, string Sonra) Bolumler(string bosluksuz)
    {
        var once = bosluksuz.IndexOf("KOORDİNEÖZET(DEPREMÖNCESİ/ARŞİVONAYLI)", StringComparison.Ordinal);
        var sonra = bosluksuz.IndexOf("KOORDİNEÖZET(DEPREMSONRASI)", StringComparison.Ordinal);
        once.Should().BeGreaterThan(-1, "üst tablo başlığı ITRF-96 KOORDİNE ÖZET (DEPREM ÖNCESİ / ARŞİV ONAYLI)");
        sonra.Should().BeGreaterThan(once, "alt tablo başlığı ITRF-96 KOORDİNE ÖZET (DEPREM SONRASI)");
        return (bosluksuz[once..sonra], bosluksuz[sonra..]);
    }

    /// <summary>
    /// Bağımsız beklenti: Öncesi = 4 kadastro satırı (arşiv no + arşiv Y, X; MK yok); Sonrası = 2 yeni TK_Oluşan satırı ("1", "2", MK
    /// 0.09) + 4 kadastro satırı (arşiv no + aktif Y, X, MK boş). Ortak köşe Sonrası'nda tek kez ve hiçbir zaman MK 0.09 ile değil.
    /// </summary>
    private static void TablolariDogrula(string bosluksuz, CiktiPaketi p, IReadOnlyList<ArsivNoktaKaydi> kayitlar, string belge)
    {
        var (once, sonra) = Bolumler(bosluksuz);
        once.Should().NotContain("MK", "{0}: Deprem Öncesi tablosunda MK sütunu yok", belge);
        var kadastro = p.Parseller.Single(s => s.Rol == ParselRolu.Kadastro).NoktaNolari;
        foreach (var no in kadastro)
        {
            var kayit = kayitlar.Single(k => k.NoktaNo == p.NoktaAdi(no));
            once.Should().Contain(kayit.NoktaNo + F2(kayit.ArsivY) + F2(kayit.ArsivX), "{0}: arşiv no + arşiv onaylı Y, X birebir", belge);
            var aktif = p.Koordinatlar.Single(k => k.NoktaNo == no);
            var satir = kayit.NoktaNo + F2(aktif.Y) + F2(aktif.X);
            Say(sonra, satir).Should().Be(1, "{0}: kadastro köşesi Deprem Sonrası'nda tek kez (ortak köşe iki kez yazılmaz)", belge);
            Say(sonra, satir + "0.09").Should().Be(0, "{0}: kadastro köşesinde MK boş", belge);
        }

        var yeni = p.Parseller.Where(s => s.Rol != ParselRolu.Kadastro).SelectMany(s => s.NoktaNolari).Except(kadastro).Distinct().Order().ToArray();
        yeni.Should().HaveCount(2);
        yeni.Select(p.NoktaAdi).Should().Equal("1", "2");
        foreach (var no in yeni)
        {
            var k = p.Koordinatlar.Single(x => x.NoktaNo == no);
            sonra.Should().Contain(p.NoktaAdi(no) + F2(k.Y) + F2(k.X) + "0.09", "{0}: yeni TK_Oluşan köşesi MK 0.09", belge);
        }

        Say(sonra, "0.09").Should().Be(2, "{0}: yalnız 2 yeni TK_Oluşan köşesi MK taşır", belge);
        sonra.IndexOf(p.NoktaAdi(yeni[0]) + F2(p.Koordinatlar.Single(x => x.NoktaNo == yeni[0]).Y), StringComparison.Ordinal)
            .Should().BeLessThan(sonra.IndexOf("901001", StringComparison.Ordinal), "{0}: önce TK_Oluşan, altında kadastro", belge);
    }

    // ------------------------------------------------------------------ 1. Arşiv onaylı koordinatlar

    [Fact]
    public async Task Dns_Aktifken_Arsiv_Onayli_No_Ve_YX_Ezilmeden_Iki_Tabloda_Birebir_Basilmali_SAHA_021()
    {
        IReadOnlyList<ArsivNoktaKaydi> kayitlar = [];
        var p = await PaketAsync(dns: true, arsiv: b => kayitlar = Arsiv(b));

        p.ArsivNoktalariVar.Should().BeTrue();
        foreach (var no in p.Parseller.Single(s => s.Rol == ParselRolu.Kadastro).NoktaNolari)
        {
            var kayit = kayitlar.Single(k => k.NoktaNo == p.NoktaAdi(no));
            p.ArsivKoordinatlari[no].Should().Be(new KoordinatSatiri(no, kayit.ArsivY, kayit.ArsivX), "arşiv Y, X CAD / dönüşmüş koordinatla ezilmez");
        }

        var d = Ac(Uret(p));
        TablolariDogrula(Bosluksuz(Metin(d["KOORDİNE ÖZET.pdf"])), p, kayitlar, "KOORDİNE ÖZET");
        TablolariDogrula(Bosluksuz(Metin(d["RÖLÖVE ÖLÇÜ KROKİSİ.pdf"])), p, kayitlar, "RÖLÖVE ÖLÇÜ KROKİSİ");
        foreach (var ad in d.Keys.Where(k => k.StartsWith("RÖLÖVE-", StringComparison.Ordinal)))
        {
            TablolariDogrula(Bosluksuz(Metin(d[ad])), p, kayitlar, ad);
        }
    }

    [Fact]
    public async Task Donusumsuz_Projede_Arsiv_Noktasi_Varsa_Deprem_Oncesi_Arsiv_Onayli_Tablo_Basilmali_SAHA_021()
    {
        // Mühendis hatası: ".DNS yokken PDF'te arşiv onaylı koordinat yazmıyor; benim koordinatlarıma kendi koordinatını atıyor".
        IReadOnlyList<ArsivNoktaKaydi> kayitlar = [];
        var p = await PaketAsync(donusum: false, arsiv: b => kayitlar = Arsiv(b));

        p.Donusum.Should().BeNull();
        var d = Ac(Uret(p));
        TablolariDogrula(Bosluksuz(Metin(d["KOORDİNE ÖZET.pdf"])), p, kayitlar, "KOORDİNE ÖZET");
        TablolariDogrula(Bosluksuz(Metin(d["RÖLÖVE ÖLÇÜ KROKİSİ.pdf"])), p, kayitlar, "RÖLÖVE ÖLÇÜ KROKİSİ");
    }

    [Fact]
    public async Task Arsivsiz_Donusumsuz_Projede_Tek_Koordine_Ozet_Tablosu_Ortak_Kose_Tek_Olmali_SAHA_021()
    {
        var p = await PaketAsync(donusum: false);
        var b = Bosluksuz(Metin(Ac(Uret(p))["KOORDİNE ÖZET.pdf"]));

        b.Should().Contain("ITRF-96KOORDİNEÖZET").And.NotContain("DEPREMÖNCESİ").And.NotContain("DEPREMSONRASI");
        Say(b, "0.09").Should().Be(2, "yalnız kadastroyla çakışmayan 2 yeni köşe TK_Oluşan grubunda (SORU-075)");
    }

    [Fact]
    public async Task Arsiv_Anahtari_002m_Icinde_Kaymissa_Esleme_Kopmamali_Numaralar_Kaybolmamali_SAHA_021()
    {
        // .DNS uygulanıp CAD yeniden yüklendiğinde köşe milimetrik kayabilir; 0.02 m içinde eşleme korunur.
        IReadOnlyList<ArsivNoktaKaydi> kayitlar = [];
        var p = await PaketAsync(dns: true, arsiv: b => kayitlar = Arsiv(b).Select(k => k with { KoseX = k.KoseX + 0.012m, KoseY = k.KoseY - 0.01m }).ToList());

        p.Uyarilar.Should().NotContain(u => u.Contains("eşleşmedi", StringComparison.Ordinal));
        p.Parseller.Single(s => s.Rol == ParselRolu.Kadastro).NoktaNolari.Select(p.NoktaAdi).Should().BeEquivalentTo(kayitlar.Select(k => k.NoktaNo));
        TablolariDogrula(Bosluksuz(Metin(Ac(Uret(p))["KOORDİNE ÖZET.pdf"])), p, kayitlar, "KOORDİNE ÖZET");
    }

    [Fact]
    public async Task Varsayilan_Kayitli_Arsiv_No_Ve_YX_Kose_Kaysa_Da_Sira_Numarasiyla_Ezilmemeli_SAHA_021()
    {
        var (yol, _, _) = await Imar18.Application.Tests.Proje.OrnekProje.TamProjeAsync();
        (await yol.GonderAsync(new ArsivNoktalariAyarlaKomutu(Arsiv(yol.Belge)), Imar18.Application.Tests.Proje.OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        var kayitli = yol.Belge.ArsivKadastroNoktalari;
        // Kadastro geometrisi 0.015 m kaydırılarak yeniden yüklenir (CAD yeniden okundu).
        var kayik = Imar18.Application.Tests.Proje.OrnekProje.Geometri()
            .Select(g => g.Rol == ParselRolu.Kadastro ? g with { Koseler = g.Koseler.Select(k => k with { X = k.X + 0.015m }).ToArray() } : g).ToArray();
        var belge = yol.Belge with { Parseller = kayik };

        var v = ArsivNoktalari.Varsayilan(belge, [new CadPoint("NOKTA", "777", kayik[0].Koseler[0])]);

        v.Select(k => (k.NoktaNo, k.ArsivY, k.ArsivX)).Should().Equal(kayitli.Select(k => (k.NoktaNo, k.ArsivY, k.ArsivX)),
            "mühendisin arşiv no ve Y, X'i CAD adıyla / sıra no / köşe koordinatıyla ezilmez");
        v.Select(k => k.KoseX).Should().Equal(kayik[0].Koseler.Select(k => k.X), "eşleme anahtarı yeni köşeye taşınır");
    }

    [Fact]
    public async Task Arsiv_Kayitlari_Hic_Eslesmezse_Arsiv_Onayli_Baslik_Basilmamali_Tek_Tablo_Olmali_SAHA_021()
    {
        // Reviewer D4: kayıt var ama hiçbir köşeyle (1 m uzak anahtar) eşleşmiyor → CAD koordinatı "ARŞİV ONAYLI" diye basılmaz.
        var p = await PaketAsync(donusum: false, arsiv: b => Arsiv(b).Select(k => k with { KoseX = k.KoseX + 1m }).ToList());

        p.ArsivNoktalariVar.Should().BeFalse();
        var b = Bosluksuz(Metin(Ac(Uret(p))["KOORDİNE ÖZET.pdf"]));
        b.Should().NotContain("ARŞİVONAYLI").And.NotContain("DEPREMÖNCESİ");
    }

    [Theory]
    [InlineData("0.020", true)]
    [InlineData("0.0141", true)]
    [InlineData("0.021", false)]
    [InlineData("0.05", false)]
    public void TkOlusan_Kosesi_Kadastroya_002m_Icinde_Ayni_Nokta_Disinda_Ayri_Nokta_Olmali_SAHA_021(string mesafe, bool paylasir)
    {
        var d = decimal.Parse(mesafe, CultureInfo.InvariantCulture);
        (GeodeticPoint, int)[] kadastro = [(P(0m, 0m), 7), (P(10m, 0m), 8)];

        CiktiPaketiOlusturucu.KadastroKosesineCakisir(kadastro, P(d, 0m), out var no).Should().Be(paylasir);
        no.Should().Be(paylasir ? 7 : 0);
    }

    [Fact]
    public async Task Varsayilan_Girilmemis_Arsiv_YX_Kose_Kayinca_Yeni_Koseye_Tasinmali_Girilen_Korunmali_SAHA_021()
    {
        // Reviewer O2: elle ters kipte "girilmemiş" (anahtara eşit) arşiv Y, X eski köşede kalırsa Deprem Sonrası koordinat "Öncesi"ne basılırdı.
        var (yol, _, _) = await Imar18.Application.Tests.Proje.OrnekProje.TamProjeAsync();
        var kayitlar = ArsivNoktalari.Varsayilan(yol.Belge, []).Select(k => k.Sira == 1 ? k with { NoktaNo = "901001", ArsivY = k.KoseY + 0.30m } : k with { NoktaNo = "90100" + k.Sira }).ToList();
        (await yol.GonderAsync(new ArsivNoktalariAyarlaKomutu(kayitlar), Imar18.Application.Tests.Proje.OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        var kayik = Imar18.Application.Tests.Proje.OrnekProje.Geometri()
            .Select(g => g.Rol == ParselRolu.Kadastro ? g with { Koseler = g.Koseler.Select(k => k with { X = k.X + 0.015m }).ToArray() } : g).ToArray();

        var v = ArsivNoktalari.Varsayilan(yol.Belge with { Parseller = kayik }, []);

        v[0].ArsivY.Should().Be(kayitlar[0].ArsivY, "mühendisin girdiği arşiv Y korunur");
        v.Skip(1).Should().OnlyContain(k => k.ArsivX == k.KoseX && k.ArsivY == k.KoseY, "girilmemiş varsayılan yeni köşeye taşınır");
    }

    [Fact]
    public async Task Roleve_n_Kadastro_Tablolari_Arsiv_No_Sirasiyla_Dizilmeli_SAHA_021()
    {
        // SORU-078: tablolar iç nokta sırasına göre değil basılan (arşiv) no'ya göre dizilir. Arşiv no'ları iç sıranın tersine verilir.
        IReadOnlyList<ArsivNoktaKaydi> kayitlar = [];
        var p = await PaketAsync(dns: true, arsiv: b => kayitlar = Arsiv(b).Select(k => k with { NoktaNo = (901010 - k.Sira).ToString(CultureInfo.InvariantCulture) }).ToList());

        var sirali = kayitlar.Select(k => k.NoktaNo).Order(StringComparer.Ordinal).ToList();
        var (olusan, kadastro) = FenKlasoruVerisi.NoktaGruplari(p, FenKlasoruVerisi.HesapParselleri(p), adaKapsami: false);
        kadastro.Select(k => p.NoktaAdi(k.NoktaNo)).Should().Equal(sirali, "Deprem Sonrası kadastro grubu arşiv no sırasıyla");
        FenKlasoruVerisi.OncesiNoktalar(p, kadastro).Select(k => p.NoktaAdi(k.NoktaNo)).Should().Equal(sirali, "Deprem Öncesi arşiv no sırasıyla");
        olusan.Select(k => p.NoktaAdi(k.NoktaNo)).Should().Equal("1", "2");

        // PDF (RÖLÖVE yan tablosu tek sütun): satırlar (arşiv no + Y) aynı sırada; çizim yazıları desene uymaz.
        var (once, _) = Bolumler(Bosluksuz(Metin(Ac(Uret(p))["RÖLÖVE ÖLÇÜ KROKİSİ.pdf"])));
        Regex.Matches(once, "(9010[0-9]{2})500").Select(m => m.Groups[1].Value).Should().Equal(sirali);
    }

    // ------------------------------------------------------------------ 2. Yeni ada numaraları

    private const decimal X0 = 4_400_000m;
    private const decimal Y0 = 500_000m;

    private static GeodeticPoint P(decimal x, decimal y) => new(X: X0 + x, Y: Y0 + y);

    private static ParselGeometrisi Dikdortgen(string id, ParselRolu rol, decimal x1, decimal x2) => new(id, rol, [P(x1, 0), P(x1, 100), P(x2, 100), P(x2, 0)]);

    private static readonly string[] Malikler3 = ["ALİ VELİ (HASAN)", "AYŞE KAYA (MEHMET)", "FATMA DEMİR (OSMAN)"];
    private static readonly string[] Malikler5 = ["HÜSEYİN ÇELİK (İBRAHİM)", "ZEYNEP ARSLAN (YUSUF)", "MUSTAFA ÖZTÜRK (KEMAL)", "EMİNE ŞAHİN (HALİL)", "HATİCE YILDIZ (RECEP)"];

    /// <summary>
    /// SAHA-015 sentetik düzeni (uydurma adlar): K1 50 × 100 m = 5000 m²; blok "2" X ∈ [28, 50] (3 parça), yol X ∈ [22, 28], blok "1"
    /// X ∈ [0, 22] (5 parça). DOPO 0.12 → 3 malik 880 + 660 + 660 = 2200 m², 5 malik × 440 = 2200 m².
    /// </summary>
    private static KrokiParselasyonGirdisi KrokiGirdisi(string blok3, string blok5)
    {
        CadText Yazi(string metin, decimal x, decimal y) => new("0", metin, P(x - 0.5m, y - 6m)) { Merkez = P(x, y) };
        CadPolyline Cizgi(decimal x1, decimal y1, decimal x2, decimal y2) => new("0", "BOLME", null, [P(x1, y1), P(x2, y2)], false, null);
        var kroki = new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "0")],
            [Cizgi(28, 30, 50, 36), Cizgi(28, 65, 50, 71), Cizgi(0, 18, 22, 22), Cizgi(1, 39, 21, 41), Cizgi(0, 58, 22, 62), Cizgi(0, 79, 23, 81)],
            [.. new[] { 15m, 50m, 85m }.Select((d, i) => Yazi(Malikler3[i].Split(' ')[0] + " " + Malikler3[i].Split(' ')[1], 39m, d)),
             .. new[] { 9m, 30m, 50m, 70m, 90m }.Select((d, i) => Yazi(Malikler5[i].Split(' ')[0] + " " + Malikler5[i].Split(' ')[1], 11m, d))],
            0, 0, 0, []);
        var takbis = new KadastroParseli("K1", 5000.00m,
            [.. new[] { (Malikler3[0], 1, 5), (Malikler3[1], 3, 20), (Malikler3[2], 3, 20) }.Concat(Malikler5.Select(m => (m, 1, 10)))
                .Select(h => new KadastroMaliki(h.Item1, new Fraction(h.Item2, h.Item3), true))]);
        ParselGeometrisi[] parseller =
        [
            Dikdortgen("K1", ParselRolu.Kadastro, 0m, 50m), Dikdortgen(blok3, ParselRolu.Imar, 28m, 50m),
            Dikdortgen("Y1", ParselRolu.YolTerk, 22m, 28m), Dikdortgen(blok5, ParselRolu.Imar, 0m, 22m),
        ];
        return new KrokiParselasyonGirdisi(kroki, parseller, [takbis], MalikRehberi.Bos, 0.12m);
    }

    [Fact]
    public void Kroki_Blok_Etiketleri_1_2_Ise_Parseller_101_1_101_5_Ve_102_1_102_3_Olmali_SAHA_021()
    {
        var s = KrokiParselasyonMotoru.Olustur(KrokiGirdisi(blok3: "2", blok5: "1"), new KrokiBlokBolucu());

        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.TechnicalMessage : string.Empty);
        s.Value.Parseller.Select(p => p.Kod).Should().BeEquivalentTo(["101/1", "101/2", "101/3", "101/4", "101/5", "102/1", "102/2", "102/3"]);
        s.Value.Parseller.Where(p => p.BlokKimligi == "1").Select(p => p.Kod).Should().Equal("101/1", "101/2", "101/3", "101/4", "101/5");
        s.Value.Parseller.Where(p => p.BlokKimligi == "2").Select(p => p.Kod).Should().Equal("102/1", "102/2", "102/3");
        s.Value.Satirlar.Select(x => x.ImarParselKodu).Distinct().Should().OnlyContain(k => k.StartsWith("101/", StringComparison.Ordinal) || k.StartsWith("102/", StringComparison.Ordinal));
        s.Value.Uyarilar.Should().Contain("\"1\" etiketli blok yeni imar adası 101 olarak numaralandı (Başlangıç Ada No 101; projede kullanılan ada numaraları atlanır).")
            .And.Contain(u => u.StartsWith("\"2\" etiketli blok yeni imar adası 102", StringComparison.Ordinal), "reviewer D6: yeniden numaralama uyarılır");
    }

    [Fact]
    public void Kroki_Baslangic_Ada_No_201_Verilirse_201_Ve_202_Kullanilmali_SAHA_021()
    {
        var s = KrokiParselasyonMotoru.Olustur(KrokiGirdisi("2", "1") with { BaslangicAdaNo = 201 }, new KrokiBlokBolucu());

        s.IsSuccess.Should().BeTrue();
        s.Value.Parseller.Where(p => p.BlokKimligi == "1").Select(p => p.Kod).Should().Equal("201/1", "201/2", "201/3", "201/4", "201/5");
        s.Value.Parseller.Where(p => p.BlokKimligi == "2").Select(p => p.Kod).Should().Equal("202/1", "202/2", "202/3");
    }

    [Fact]
    public void Kroki_Blok_Etiketi_100_Ve_Uzeri_Ada_Numarasiysa_Aynen_Korunmali_SAHA_021()
    {
        var s = KrokiParselasyonMotoru.Olustur(KrokiGirdisi(blok3: "436", blok5: "101"), new KrokiBlokBolucu());

        s.IsSuccess.Should().BeTrue();
        s.Value.Parseller.Where(p => p.BlokKimligi == "101").Select(p => p.Kod).Should().Equal("101/1", "101/2", "101/3", "101/4", "101/5");
        s.Value.Parseller.Where(p => p.BlokKimligi == "436").Select(p => p.Kod).Should().Equal("436/1", "436/2", "436/3");
    }

    [Fact]
    public void Kroki_Karisik_Etiketlerde_Kullanilan_Ada_Numarasi_Atlanmali_Etiketsiz_Blok_Numara_Almali_SAHA_021()
    {
        // "101" korunur; "1" bir sonraki boş ada no'yu (102) alır — iki blok aynı adaya düşmez.
        var s = KrokiParselasyonMotoru.Olustur(KrokiGirdisi(blok3: "101", blok5: "1"), new KrokiBlokBolucu());

        s.IsSuccess.Should().BeTrue();
        s.Value.Parseller.Where(p => p.BlokKimligi == "101").Select(p => p.Kod).Should().Equal("101/1", "101/2", "101/3");
        s.Value.Parseller.Where(p => p.BlokKimligi == "1").Select(p => p.Kod).Should().Equal("102/1", "102/2", "102/3", "102/4", "102/5");

        KrokiParselasyonMotoru.YeniAdaNumaralari(["ADA1", "", "5", "3"], [], 101).Should().BeEquivalentTo(
            new Dictionary<string, string> { ["3"] = "101", ["5"] = "102", ["ADA1"] = "103", [""] = "104" },
            "önce sayısal etiketler değer sırasıyla, sonra diğerleri blok sırasıyla");
        KrokiParselasyonMotoru.YeniAdaNumaralari(["1"], ["101/5", "K1", "Y1"], 101).Should().Equal(new Dictionary<string, string> { ["1"] = "102" },
            "projede (kadastro 101/5) kullanılan ada no atlanır");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10_000_000)]
    public void Kroki_Gecersiz_Baslangic_Ada_No_Reddedilmeli_SAHA_021(int baslangic)
    {
        var s = KrokiParselasyonMotoru.Olustur(KrokiGirdisi("2", "1") with { BaslangicAdaNo = baslangic }, new KrokiBlokBolucu());

        s.IsFailure.Should().BeTrue();
        s.Error.TechnicalMessage.Should().Contain("starting block number");
    }

    // ------------------------------------------------------------------ 3. RÖLÖVE / PARSELASYON HARİTASI etiketleri

    /// <summary>
    /// İki adalı sentetik paket (yerel m; Y Doğu, X Kuzey): K1 20 × 20; ada 101 (kuzey, X 10..20) 101/1 Y 0..6, 101/2 Y 6..12; ada 102
    /// (güney, X 0..10) 102/1 Y 0..4, 102/2 Y 4..8, 102/3 Y 8..12; yol Y1 Y 12..20 (tescil "YOL"). Bağımsız el sayımı: seçili parsellerde
    /// 14 tekil köşe, 21 tekil kenar (ortak kenarlar 101/1–101/2, 102/1–102/2, 102/2–102/3 birer kez; tekrarlı sayım 24).
    /// </summary>
    private static async Task<CiktiPaketi> IkiAdaliPaketAsync()
    {
        var p = await PaketAsync(donusum: false);
        var nolar = new Dictionary<GeodeticPoint, int>();
        var koordinatlar = new List<KoordinatSatiri>();
        ParselCetvelSatiri Satir(string kod, string tescil, ParselRolu rol, decimal y1, decimal y2, decimal x1, decimal x2)
        {
            GeodeticPoint[] k = [P(x1, y1), P(x1, y2), P(x2, y2), P(x2, y1)];
            var no = k.Select(v =>
            {
                if (!nolar.TryGetValue(v, out var n))
                {
                    n = nolar.Count + 1;
                    nolar[v] = n;
                    koordinatlar.Add(new KoordinatSatiri(n, v.Y, v.X));
                }

                return n;
            }).ToArray();
            var kenar = k.Select((v, i) => new CepheSatiri(no[i], v.Y, v.X, DecimalMath.KenarUzunlugu(v, k[(i + 1) % 4]))).ToArray();
            var alan = (y2 - y1) * (x2 - x1);
            return new ParselCetvelSatiri(kod, tescil, rol, no, kenar, alan, alan, HeM2Dm2.Ayir(alan), k);
        }

        ParselCetvelSatiri[] parseller =
        [
            Satir("K1", "K1", ParselRolu.Kadastro, 0, 20, 0, 20),
            Satir("101/1", "101/1", ParselRolu.Imar, 0, 6, 10, 20), Satir("101/2", "101/2", ParselRolu.Imar, 6, 12, 10, 20),
            Satir("102/1", "102/1", ParselRolu.Imar, 0, 4, 0, 10), Satir("102/2", "102/2", ParselRolu.Imar, 4, 8, 0, 10),
            Satir("102/3", "102/3", ParselRolu.Imar, 8, 12, 0, 10), Satir("Y1", "YOL", ParselRolu.YolTerk, 12, 20, 0, 20),
        ];
        return p with { Parseller = parseller, Koordinatlar = koordinatlar, ImarSirali = [], MalikSirali = [] };
    }

    private static List<string> Yazilar(string svg, int boyut) =>
        Regex.Matches(svg, "font-size=\"" + boyut.ToString(CultureInfo.InvariantCulture) + "\"[^>]*>([^<]*)</text>").Select(m => m.Groups[1].Value).ToList();

    [Fact]
    public async Task Roleve_Cizimi_Ada_No_Tekil_Parsel_No_Yol_Y1_Kose_Dairesi_Ve_Cephe_Tekil_Olmali_SAHA_021()
    {
        var p = await IkiAdaliPaketAsync();

        var svg = FenKlasoruBelgeleri.KrokiSvg(p, FenKlasoruVerisi.HesapParselleri(p));

        Yazilar(svg, 44).Should().BeEquivalentTo(["101", "102"], "her ada no adada bir kez, büyük (44) ve kalın");
        Regex.Matches(svg, "font-size=\"44\"[^>]*font-weight=\"bold\"").Count.Should().Be(2);
        Yazilar(svg, 24).Should().BeEquivalentTo(["1", "2", "1", "2", "3", "Y1"], "parsel içinde yalnız parsel no; yol terki Y1");
        svg.Should().NotContain("101/").And.NotContain("102/").And.NotContain(">YOL<").And.NotContain("YOL_TERK");
        Regex.Matches(svg, "<circle ").Count.Should().Be(14, "14 tekil köşe, her birinde bir ○");
        svg.Should().Contain("r=\"5\" fill=\"#ffffff\" stroke=\"#111111\"", "içi beyaz, dışı siyah ince halka");
        Yazilar(svg, 16).Should().HaveCount(14).And.OnlyHaveUniqueItems("her köşenin nokta no'su bir kez");

        var cephe = Yazilar(svg, 15);
        cephe.Should().HaveCount(21, "21 tekil kenar; ortak kenar tek kez (tekrarlı 24 olurdu)");
        cephe.Where(c => c == "10.00").Should().HaveCount(7, "101/1 batı, 101/1–2 ortak, 101/2 doğu, 102/1 batı, 102/1–2 ortak, 102/2–3 ortak, 102/3 doğu");
        Regex.Matches(svg, "<g transform=\"translate\\([^)]*\\) rotate\\((-?[0-9.]+)\\)\">").Select(m => m.Groups[1].Value)
            .Should().HaveCount(21).And.OnlyContain(a => a == "0" || a == "90", "eksene paralel kenarlarda yazı kenara paralel (0° / 90°)");
    }

    [Fact]
    public async Task Roleve_n_Ada_Cizimi_Yalniz_Kendi_Ada_Nosunu_Bir_Kez_Basmali_SAHA_021()
    {
        var p = await IkiAdaliPaketAsync();

        var svg = FenKlasoruBelgeleri.KrokiSvg(p, FenKlasoruVerisi.AdaParselleri(p, "102"));

        Yazilar(svg, 44).Should().Equal("102");
        Yazilar(svg, 24).Should().BeEquivalentTo(["1", "2", "3"]);
        Yazilar(svg, 15).Should().HaveCount(10, "102/1 dört + 102/2 üç + 102/3 üç tekil kenar");
    }

    [Fact]
    public async Task Parselasyon_Haritasi_Ada_No_Tekil_Parsel_No_Ve_Y1_Basmali_SAHA_021()
    {
        var p = await IkiAdaliPaketAsync();

        var (svg, _) = FenKlasoruBelgeleri.HaritaSvg(p);

        var metinler = Regex.Matches(svg, ">([^<>]+)</text>").Select(m => m.Groups[1].Value).Where(t => (!t.StartsWith('X') && !t.StartsWith('Y')) || t == "Y1").ToList();
        metinler.Count(t => t == "101").Should().Be(1);
        metinler.Count(t => t == "102").Should().Be(1);
        metinler.Count(t => t == "Y1").Should().Be(1);
        metinler.Should().NotContain(t => t.Contains('/', StringComparison.Ordinal), "parsel içine 101/1 yazılmaz");
        metinler.Where(t => t is "1" or "2" or "3").Should().BeEquivalentTo(["1", "2", "1", "2", "3"]);
    }

    [Fact]
    public async Task Roleve_Pdf_Ada_No_Parsel_No_Ve_Y1_Icermeli_101_1_Etiketi_Olmamali_SAHA_021()
    {
        var p = await PaketAsync(donusum: false);
        using var pdf = PdfDocument.Open(Ac(Uret(p))["RÖLÖVE ÖLÇÜ KROKİSİ.pdf"]);
        var kelimeler = pdf.GetPages().SelectMany(s => s.GetWords()).Select(w => w.Text).ToList();

        kelimeler.Should().Contain("101", "ada no çizimde").And.Contain("Y1", "yol terki etiketi").And.NotContain("101/1").And.NotContain("YOL");
        kelimeler.Should().Contain("12.00", "yatay cephe uzunluğu (A1 kuzey/güney kenarı)").And.Contain("8.00", "yol Y1 cephesi");
    }

    [Theory]
    [InlineData(10, 0, 0)]
    [InlineData(0, 10, 90)]
    [InlineData(-10, 0, 0)]
    [InlineData(0, -10, 90)]
    [InlineData(10, 10, 45)]
    [InlineData(-10, 10, -45)]
    [InlineData(10, -10, -45)]
    public void Kenar_Acisi_Yazi_Ters_Okunmayacak_Bicimde_Indirgenmeli_SAHA_021(int dx, int dy, int beklenen)
    {
        FenKlasoruBelgeleri.KenarAcisiDerece(dx, dy).Should().Be(beklenen);
    }
}

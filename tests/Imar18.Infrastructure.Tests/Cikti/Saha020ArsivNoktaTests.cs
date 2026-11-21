using System.Globalization;
using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Proje;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cad.Ncz;
using Imar18.Infrastructure.Cikti.FenKlasoru;
using Imar18.Infrastructure.Tests.Cad.Ncz;
using UglyToad.PdfPig;
using Xunit;
using static Imar18.Infrastructure.Tests.Cikti.FenKlasoruZipTests;

namespace Imar18.Infrastructure.Tests.Cikti;

/// <summary>
/// SAHA-020 (mühendis talebi): arşiv onaylı kadastro (TK_PARSEL) köşe noktalarının mühendisin verdiği resmî arşiv nokta no'su
/// (<c>901001</c>…) ve arşiv Y, X'i ile KOORDİNE ÖZET (Deprem Öncesi / Sonrası), RÖLÖVE (tablo + çizim), NOKTALI / CEPHELİ ALAN
/// HESABI PDF'lerine ve <c>PARSELASYON HARİTASI.NCZ</c> tip 1 nokta adlarına birebir basılması; <c>.NCZ</c> tip 1 nokta okuma.
/// Bağımsız referans: <see cref="Imar18.Application.Tests.Proje.OrnekProje"/> (K1 = 4 köşe; A1 ∪ Y1 = 6 köşe, ikisi K1 köşesi değil).
/// Arşiv koordinatı köşeden kasıtlı olarak farklıdır (Y + 0.01, X − 0.02) — Deprem Öncesi tablosunda arşiv değerinin basıldığı ayırt edilir.
/// </summary>
public sealed class Saha020ArsivNoktaTests
{
    private static string F2(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    private static IReadOnlyList<ArsivNoktaKaydi> Arsiv(ProjeBelgesi b) => ArsivNoktalari.Varsayilan(b, [])
        .Select(k => k with { NoktaNo = (901000 + k.Sira).ToString(CultureInfo.InvariantCulture), ArsivY = k.KoseY + 0.01m, ArsivX = k.KoseX - 0.02m })
        .ToList();

    private static async Task<(CiktiPaketi Paket, IReadOnlyList<ArsivNoktaKaydi> Kayitlar)> ArsivliPaketAsync(bool dns)
    {
        IReadOnlyList<ArsivNoktaKaydi> kayitlar = [];
        var p = await PaketAsync(dns: dns, donusum: dns, arsiv: b => kayitlar = Arsiv(b));
        return (p, kayitlar);
    }

    private static int Say(string metin, string aranan)
    {
        var n = 0;
        for (var i = metin.IndexOf(aranan, StringComparison.Ordinal); i >= 0; i = metin.IndexOf(aranan, i + 1, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    private static List<string> Kelimeler(byte[] pdf)
    {
        using var d = PdfDocument.Open(pdf);
        return d.GetPages().SelectMany(s => s.GetWords()).Select(w => w.Text).ToList();
    }

    [Fact]
    public async Task Paket_Kadastro_Koselerine_Arsiv_No_Yeni_Noktalara_Sirali_1_2_Vermeli_SAHA_020()
    {
        var (p, kayitlar) = await ArsivliPaketAsync(dns: true);
        kayitlar.Should().HaveCount(4, "K1'in 4 köşesi");
        var kadastroNo = p.Parseller.Single(s => s.Rol == ParselRolu.Kadastro).NoktaNolari.ToHashSet();
        var araNo = p.Parseller.Where(s => s.Rol != ParselRolu.Kadastro).SelectMany(s => s.NoktaNolari).Except(kadastroNo).Order().ToArray();

        foreach (var no in kadastroNo)
        {
            var o = p.Donusum!.DepremOncesi.Single(k => k.NoktaNo == no);
            var kayit = kayitlar.Single(k => k.KoseX == o.X && k.KoseY == o.Y);
            p.NoktaAdi(no).Should().Be(kayit.NoktaNo, "eşleme Deprem Öncesi köşe anahtarıyla yapılır");
            p.ArsivKoordinatlari[no].Should().Be(new KoordinatSatiri(no, kayit.ArsivY, kayit.ArsivX));
        }

        araNo.Select(p.NoktaAdi).Should().Equal("1", "2");
        p.Uyarilar.Should().NotContain(u => u.Contains("arşiv", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task KoordineOzet_Deprem_Oncesi_Arsiv_No_Ve_Arsiv_YX_Sonrasi_Arsiv_No_Donusmus_YX_MK_Bos_Basmali_SAHA_020()
    {
        var (p, kayitlar) = await ArsivliPaketAsync(dns: true);
        var b = Bosluksuz(Metin(Ac(Uret(p))["KOORDİNE ÖZET.pdf"]));
        var once = b.IndexOf("(DEPREMÖNCESİ/ARŞİVONAYLI)", StringComparison.Ordinal);
        var sonra = b.IndexOf("(DEPREMSONRASI)", StringComparison.Ordinal);
        sonra.Should().BeGreaterThan(once).And.BeGreaterThan(-1);
        var onceBolum = b[once..sonra];
        var sonraBolum = b[sonra..];

        foreach (var no in p.Parseller.Single(s => s.Rol == ParselRolu.Kadastro).NoktaNolari)
        {
            var o = p.Donusum!.DepremOncesi.Single(k => k.NoktaNo == no);
            var kayit = kayitlar.Single(k => k.KoseX == o.X && k.KoseY == o.Y);
            onceBolum.Should().Contain(kayit.NoktaNo + F2(kayit.ArsivY) + F2(kayit.ArsivX), "Deprem Öncesi = arşiv no + arşiv onaylı Y, X");
            onceBolum.Should().NotContain(F2(o.Y) + F2(o.X), "CAD köşesi değil arşiv koordinatı basılır");
            var s = p.Koordinatlar.Single(k => k.NoktaNo == no);
            var satir = kayit.NoktaNo + F2(s.Y) + F2(s.X);
            sonraBolum.Should().Contain(satir, "Deprem Sonrası = arşiv no + ileri dönüşmüş Y, X");
            // SAHA-021 (SORU-075 / SORU-078 kapandı): TK_Oluşan ile ortak köşe yalnız kadastro grubunda, tam bir kez ve MK boş.
            Say(sonraBolum, satir).Should().Be(1, "ortak köşe iki kez yazılmaz");
            Say(sonraBolum, satir + "0.09").Should().Be(0, "kadastro grubunda MK boş");
        }

        var ara = p.Parseller.Where(x => x.Rol != ParselRolu.Kadastro).SelectMany(x => x.NoktaNolari)
            .Except(p.Parseller.Single(x => x.Rol == ParselRolu.Kadastro).NoktaNolari).Order().First();
        var a = p.Koordinatlar.Single(k => k.NoktaNo == ara);
        sonraBolum.Should().Contain("1" + F2(a.Y) + F2(a.X) + "0.09", "yeni TK_Oluşan noktası sıralı 1 ve MK 0.09");
    }

    [Fact]
    public async Task Roleve_Tablo_Ve_Cizimde_Kadastro_Koselerinde_Arsiv_No_Yazmali_SAHA_020()
    {
        var (p, kayitlar) = await ArsivliPaketAsync(dns: true);
        var d = Ac(Uret(p));

        var roleve = Kelimeler(d["RÖLÖVE ÖLÇÜ KROKİSİ.pdf"]);
        foreach (var k in kayitlar)
        {
            roleve.Should().Contain(k.NoktaNo, "RÖLÖVE yan tabloları arşiv no'yu basar");
        }

        var svg = FenKlasoruBelgeleri.KrokiSvg(p, FenKlasoruVerisi.HesapParselleri(p));
        foreach (var k in kayitlar)
        {
            svg.Should().Contain(">" + k.NoktaNo + "</text>", "çizimdeki kadastro köşesi arşiv no taşır");
        }

        svg.Should().Contain(">1</text>").And.Contain(">2</text>", "yeni ara noktalar sıralı numaralıdır");
        d.Keys.Where(k => k.StartsWith("RÖLÖVE-", StringComparison.Ordinal)).Should().NotBeEmpty();
        foreach (var ad in d.Keys.Where(k => k.StartsWith("RÖLÖVE-", StringComparison.Ordinal)))
        {
            Kelimeler(d[ad]).Should().Contain(kayitlar[0].NoktaNo, "ada bazlı RÖLÖVE-n de arşiv no basar ({0})", ad);
        }
    }

    [Fact]
    public async Task Noktali_Ve_Cepheli_Alan_Hesabinda_Kadastro_Koselerinde_Arsiv_No_Kullanilmali_SAHA_020()
    {
        var (p, _) = await ArsivliPaketAsync(dns: true);
        var d = Ac(Uret(p));

        var alan = Bosluksuz(Metin(d["ALAN HESABI.pdf"]));
        alan.Should().Contain("NOKTALIALANHESABI");
        foreach (var s in FenKlasoruVerisi.HesapParselleri(p))
        {
            var beklenen = string.Join(",", s.NoktaNolari.Select(p.NoktaAdi));
            beklenen.Should().Contain("9010", "A1 ve Y1 kadastro köşelerini paylaşır");
            alan.Should().Contain(beklenen, "NOKTALI ALAN HESABI nokta dizisi ({0})", s.GeciciKod);
        }

        var cepheli = Kelimeler(d["ALAN HESABI-1.pdf"]);
        cepheli.Should().Contain(w => w.StartsWith("9010", StringComparison.Ordinal), "CEPHELİ ALAN HESABI kadastro köşesinde arşiv no");
    }

    [Fact]
    public async Task Donusumsuz_Tek_Tabloda_Kadastro_Arsiv_No_Basmali_Arsivsiz_Paket_Eski_Numaralari_Korumali_SAHA_020()
    {
        var (p, kayitlar) = await ArsivliPaketAsync(dns: false);
        p.Donusum.Should().BeNull();
        var k = Kelimeler(Ac(Uret(p))["KOORDİNE ÖZET.pdf"]);
        foreach (var kayit in kayitlar)
        {
            k.Should().Contain(kayit.NoktaNo);
        }

        var eski = await PaketAsync(donusum: false);
        eski.NoktaAdlari.Should().BeEmpty("arşiv noktası yoksa iç nokta no basılır (geriye uyumluluk)");
        eski.NoktaAdi(5).Should().Be("5");
        Kelimeler(Ac(Uret(eski))["KOORDİNE ÖZET.pdf"]).Should().NotContain(w => w.StartsWith("9010", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ElleTers_Kipte_Girilmemis_Arsiv_YX_Deprem_Oncesi_Ters_Donusum_Degerini_Korumali_Girilen_Basilmali_SAHA_020()
    {
        // Reviewer Y1: P7.3-C2 elle ortak noktalı kipte anahtar (aktif köşe) Deprem Sonrası'dır; varsayılan arşiv Y, X = anahtar.
        IReadOnlyList<ArsivNoktaKaydi> kayitlar = [];
        var p = await PaketAsync(donusum: true, arsiv: b => kayitlar = ArsivNoktalari.Varsayilan(b, [])
            .Select(k => k.Sira == 1 ? k with { NoktaNo = "901001", ArsivY = k.KoseY - 0.30m } : k with { NoktaNo = "90100" + k.Sira }).ToList());
        p.Donusum.Should().NotBeNull();
        p.Donusum!.IleriDonusum.Should().BeFalse();

        var b = Bosluksuz(Metin(Ac(Uret(p))["KOORDİNE ÖZET.pdf"]));
        var onceBolum = b[b.IndexOf("(DEPREMÖNCESİ/ARŞİVONAYLI)", StringComparison.Ordinal)..b.IndexOf("(DEPREMSONRASI)", StringComparison.Ordinal)];
        foreach (var no in p.Parseller.Single(s => s.Rol == ParselRolu.Kadastro).NoktaNolari)
        {
            var aktif = p.Koordinatlar.Single(k => k.NoktaNo == no);
            var kayit = kayitlar.Single(k => k.KoseX == aktif.X && k.KoseY == aktif.Y);
            var ters = p.Donusum.DepremOncesi.Single(k => k.NoktaNo == no);
            if (kayit.NoktaNo == "901001")
            {
                onceBolum.Should().Contain("901001" + F2(kayit.ArsivY) + F2(kayit.ArsivX), "mühendisin girdiği arşiv Y, X basılır");
            }
            else
            {
                p.ArsivKoordinatlari.Should().NotContainKey(no);
                onceBolum.Should().Contain(kayit.NoktaNo + F2(ters.Y) + F2(ters.X), "girilmemiş arşiv koordinatında ters dönüşüm değeri korunur");
            }
        }
    }

    [Fact]
    public async Task ElleTers_Kipte_Mm_Alti_Yuvarlanmis_Varsayilan_Arsiv_YX_Girilmemis_Sayilmali_SAHA_020()
    {
        // Reviewer 2. tur: köşe çok ondalıklıysa gösterimde yuvarlanan varsayılan (≤ 1 mm) yine "girilmemiş" sayılır.
        var p = await PaketAsync(donusum: true, arsiv: b => ArsivNoktalari.Varsayilan(b, [])
            .Select(k => k with { NoktaNo = "90100" + k.Sira, ArsivY = k.KoseY + 0.0004m, ArsivX = k.KoseX - 0.0004m }).ToList());

        p.ArsivKoordinatlari.Should().BeEmpty("Deprem Sonrası köşe Deprem Öncesi tablosuna basılmaz; ters dönüşüm değeri korunur");
        p.NoktaAdlari.Values.Should().Contain("901001");
    }

    [Fact]
    public async Task Varsayilan_Sirali_Arsiv_Nolari_Yeni_Noktalarin_Sirali_Numarasiyla_Cakismamali_SAHA_020()
    {
        // Reviewer Y2: kadastro köşeleri varsayılan "1".."4" alırsa yeni noktalar 5, 6 olur (aynı ad iki noktada basılmaz).
        var p = await PaketAsync(donusum: false, arsiv: b => ArsivNoktalari.Varsayilan(b, []));

        var adlar = p.Koordinatlar.Select(k => p.NoktaAdi(k.NoktaNo)).ToList();
        adlar.Should().OnlyHaveUniqueItems();
        adlar.Should().BeEquivalentTo(["1", "2", "3", "4", "5", "6"]);
        var kadastro = p.Parseller.Single(s => s.Rol == ParselRolu.Kadastro).NoktaNolari.ToHashSet();
        p.Koordinatlar.Where(k => !kadastro.Contains(k.NoktaNo)).Select(k => p.NoktaAdi(k.NoktaNo)).Should().Equal("5", "6");
    }

    [Fact]
    public async Task Arsiv_Koordinati_Deprem_Oncesi_Koseden_05m_Fazla_Saparsa_Uyari_Vermeli_SAHA_020()
    {
        var p = await PaketAsync(dns: true, donusum: true, arsiv: b => ArsivNoktalari.Varsayilan(b, [])
            .Select(k => k with { NoktaNo = "90100" + k.Sira, ArsivY = k.Sira == 2 ? k.KoseY + 0.60m : k.KoseY }).ToList());

        p.Uyarilar.Should().Contain("Arşiv koordinatı Deprem Öncesi kadastro köşesinden 0.50 m'den fazla farklı (rakam veya Y/X hatası olabilir; mühendis kontrol etmeli): 901002 (0.60 m)");
    }

    [Fact]
    public async Task Geometri_Degisip_Eslesmeyen_Arsiv_Kaydi_Uygulanmamali_Uyari_Vermeli_SAHA_020()
    {
        var p = await PaketAsync(donusum: false, arsiv: b => Arsiv(b).Select(k => k with { KoseX = k.KoseX + 1m }).ToList());

        p.NoktaAdlari.Should().BeEmpty("anahtar köşe projede yok — sessiz yanlış numara basılmaz");
        p.Uyarilar.Should().Contain(u => u.StartsWith("Arşiv onaylı kadastro noktaları projedeki kadastro köşeleriyle eşleşmedi", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Parselasyon_Haritasi_Ncz_Tip1_Nokta_Adlari_Koordine_Ozet_Ile_Ayni_Olmali_SAHA_020()
    {
        var (p, kayitlar) = await ArsivliPaketAsync(dns: false);
        var ncz = Ac(Uret(p)).Single(x => x.Key.EndsWith(".NCZ", StringComparison.OrdinalIgnoreCase)).Value;

        using var akis = new MemoryStream(ncz);
        var belge = new NczFormatAdapter().Read(akis);

        belge.IsSuccess.Should().BeTrue(belge.IsFailure ? belge.Error.TechnicalMessage : string.Empty);
        var adlar = belge.Value.Points.Select(n => n.Name).ToList();
        adlar.Should().Contain(["1", "2"], "yeni ara noktalar");
        adlar.Should().Contain(n => kayitlar.Any(k => k.NoktaNo == n), "kadastroyla ortak TK_Oluşan köşesi arşiv no ile yazılır");
    }

    [Fact]
    public void Ncz_Tip1_Nokta_Kaydi_Ad_Ve_Konumla_Okunmali_Kisa_Kayit_Atlanmali_SAHA_020()
    {
        var dosya = new NczSentetikDosya().Katman("NOKTA").Katman("TK_PARSEL")
            .Nokta(0, "2652001", (4_400_010.25, 500_020.5))
            .Nokta(0, " 4360117 ", (4_400_000.0, 500_000.0))
            .Nokta(1, "X", (1.0, 2.0), boyut: 60);

        using var akis = dosya.Akis();
        var belge = new NczFormatAdapter().Read(akis);

        belge.IsSuccess.Should().BeTrue();
        belge.Value.Points.Should().Equal(
            new Imar18.Application.Cad.CadPoint("NOKTA", "2652001", new GeodeticPoint(4_400_010.25m, 500_020.5m)),
            new Imar18.Application.Cad.CadPoint("NOKTA", "4360117", new GeodeticPoint(4_400_000m, 500_000m)));
        belge.Value.SkippedEntityCount.Should().Be(1, "96 bayttan kısa nokta kaydı atlanır, dosya düşmez");
        belge.Value.Polylines.Should().BeEmpty();
    }
}

using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Girdi;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Geometry;
using Imar18.Domain.Numerics;
using Imar18.Domain.Revizyon;
using Imar18.GeometryEngine.Crs;
using Imar18.Infrastructure.Cikti;
using Imar18.Infrastructure.Cikti.FenKlasoru;
using UglyToad.PdfPig;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cikti;

/// <summary>
/// P7.3-C1 (SAHA-007): Fen Klasörü .zip paketi — dosya adları/sırası, UTF-8 ad bayrağı, her PDF'te tek satır damga
/// (filigran yok), PdfPig ile başlık/sütun doğrulaması, cepheli alan 3 şerit, tescil 0300/02 biçimi, Malik Rehberi ve
/// Cilt/Sayfa, dönüşüm parametre sayfası, DOP.CKS ve tekrar üretilebilirlik. Veri sentetiktir (OrnekProje).
/// </summary>
public sealed class FenKlasoruZipTests
{
    private const decimal A = 1.00001m;
    private const decimal B = 0.00002m;
    private const decimal Tx = 10m;
    private const decimal Ty = -5m;

    private static readonly string[] TekAdaDosyalari =
    [
        "DÖNÜŞÜM PARAMETRELERİ.pdf", "KOORDİNE ÖZET.pdf", "ALAN HESABI.pdf", "ALAN HESABI-1.pdf", "KOORDİNE-ALAN.pdf", "DOP.pdf",
        "ÖZET CETVELİ.pdf", "MAL SAHİPLERİ ARAŞTIRMA VE ÖZET FORMU.pdf", "TESCİLE ESAS DAĞITIM CETVELİ.pdf", "TESCİL SAYFASI-1.pdf",
        "RÖLÖVE ÖLÇÜ KROKİSİ.pdf", "RÖLÖVE-1.pdf", "PARSELASYON HARİTASI.pdf", "DOP_PILOT.CKS", "DOP_PILOT.CKS.sha256",
    ];

    /// <summary>Bilinen Helmert (I → II): Y2 = A·Y1 + B·X1 + Ty, X2 = −B·Y1 + A·X1 + Tx (bağımsız referans).</summary>
    internal static DonusumOrtakNoktasi Nokta(string no, decimal y1, decimal x1) =>
        new(no, y1, x1, (A * y1) + (B * x1) + Ty, (-B * y1) + (A * x1) + Tx);

    internal static DonusumGirdisi Donusum() => new(DonusumModeli.Helmert2B, 0.09m,
    [
        Nokta("N1", OrnekProje.Y0 - 100m, OrnekProje.X0 - 100m), Nokta("N2", OrnekProje.Y0 + 200m, OrnekProje.X0 - 100m),
        Nokta("N3", OrnekProje.Y0 + 200m, OrnekProje.X0 + 200m), Nokta("N4", OrnekProje.Y0 - 100m, OrnekProje.X0 + 200m),
    ]);

    private static readonly ProjeKunyesi Kunye = new("SENTETİKİL", "ÖRNEKİLÇE", "DENEME MAH.", "ÖRNEK BELEDİYESİ", "N99-X-01", "99999", "ITRF-96 / TM3 33");

    private static readonly MalikRehberi Rehber = new(
        [new MalikRehberKaydi("MLK-001", "AYŞE ÖRNEKSOY", "İSMAİL", null), new MalikRehberKaydi("MLK-002", "ÇAĞRI DENEMEOĞLU", "ŞÜKRÜ", null)],
        [new KadastroTapuKaydi("K1", "14", "0", null, null, null, "901", "7", 200.00m)]);

    private static async Task<CiktiPaketi> PaketAsync(bool donusum = true, bool rehber = true)
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        if (donusum)
        {
            (await yol.GonderAsync(new DonusumGirdisiAyarlaKomutu(Donusum()), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        }

        (await yol.GonderAsync(new ProjeKunyesiAyarlaKomutu(Kunye), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        if (rehber)
        {
            (await yol.GonderAsync(new MalikRehberiAyarlaKomutu(Rehber), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        }

        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        var p = CiktiPaketiOlusturucu.Olustur(yol.Belge, ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji), new HelmertAfinDonusumHesaplayici());
        p.IsSuccess.Should().BeTrue(p.IsFailure ? p.Error.TechnicalMessage : string.Empty);
        return p.Value;
    }

    /// <summary>İkinci imar adası (102/1) eklenmiş paket: A1'in 10 m kuzeye ötelenmiş kopyası.</summary>
    private static CiktiPaketi IkiAdali(CiktiPaketi p)
    {
        var a1 = p.Parseller.Single(s => s.GeciciKod == "A1");
        var a2 = a1 with { GeciciKod = "A2", TescilNo = "102/1", Koseler = a1.Koseler.Select(k => k with { X = k.X + 10m }).ToArray() };
        var dagitim = p.ImarSirali.Where(s => s.GeciciKod == "A1").Select(s => s with { TescilNo = "102/1", GeciciKod = "A2" });
        return p with { Parseller = p.Parseller.Append(a2).ToArray(), ImarSirali = p.ImarSirali.Concat(dagitim).ToArray() };
    }

    private static Dictionary<string, byte[]> Ac(byte[] zip)
    {
        using var arsiv = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return arsiv.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        });
    }

    private static List<string> Adlar(byte[] zip)
    {
        using var arsiv = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return arsiv.Entries.Select(e => e.FullName).ToList();
    }

    private static string Bosluksuz(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>Tüm sayfaların kelimeleri (boşlukla).</summary>
    private static string Metin(byte[] pdf)
    {
        using var d = PdfDocument.Open(pdf);
        return string.Join(" ", d.GetPages().SelectMany(s => s.GetWords()).Select(w => w.Text));
    }

    private static byte[] Uret(CiktiPaketi p)
    {
        var r = new FenKlasoruZipUretici().Uret(p);
        r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.TechnicalMessage : string.Empty);
        return r.Value;
    }

    [Fact]
    public async Task FenKlasoruZip_Tek_Adada_Dosya_Adlari_Ve_Sirasi_Ornek_Paket_Duzeninde_Olmali()
    {
        var p = await PaketAsync();

        var zip = Uret(p);

        Adlar(zip).Should().Equal(TekAdaDosyalari);
        new FenKlasoruZipUretici().Tur.Should().Be(CiktiTuru.FenKlasoruZip);
        new FenKlasoruZipUretici().Uzanti.Should().Be(".zip");
    }

    [Fact]
    public async Task FenKlasoruZip_Iki_Imar_Adasinda_Ada_Basina_Tescil_Ve_Roleve_Ile_Birlesik_Dosyalar_Uretilmeli()
    {
        var p = IkiAdali(await PaketAsync());

        var adlar = Adlar(Uret(p));

        adlar.Should().ContainInOrder("TESCİLE ESAS DAĞITIM CETVELİ.pdf", "TESCİL SAYFASI-1.pdf", "TESCİL SAYFASI-2.pdf", "TESCİL-1-2.pdf",
            "RÖLÖVE ÖLÇÜ KROKİSİ.pdf", "RÖLÖVE-1.pdf", "RÖLÖVE-2.pdf", "RÖLÖVE-1-2.pdf", "PARSELASYON HARİTASI.pdf");
        adlar.Should().OnlyHaveUniqueItems();
        var dosyalar = Ac(Uret(p));
        using (var birlesik = PdfDocument.Open(dosyalar["TESCİL-1-2.pdf"]))
        {
            birlesik.NumberOfPages.Should().Be(2, "birleşik dosya ada başına tescil sayfalarının ardışık render'ıdır");
        }

        Bosluksuz(Metin(dosyalar["TESCİL SAYFASI-2.pdf"])).Should().Contain("ADANO:102");
        Bosluksuz(Metin(dosyalar["RÖLÖVE-1-2.pdf"])).Should().Contain("ADA101").And.Contain("ADA102");
    }

    [Fact]
    public async Task FenKlasoruZip_Turkce_Dosya_Adlari_UTF8_Ad_Bayragiyla_Yazilmali()
    {
        var zip = Uret(await PaketAsync());

        // Yerel dosya başlıkları: imza 50 4B 03 04, genel amaçlı bit bayrağı ofset 6; bit 11 = UTF-8 dil kodlaması.
        var bulunan = 0;
        for (var i = 0; i + 30 < zip.Length; i++)
        {
            if (zip[i] == 0x50 && zip[i + 1] == 0x4B && zip[i + 2] == 0x03 && zip[i + 3] == 0x04)
            {
                var bayrak = BitConverter.ToUInt16(zip, i + 6);
                var adUzunlugu = BitConverter.ToUInt16(zip, i + 26);
                var ad = Encoding.UTF8.GetString(zip, i + 30, adUzunlugu);
                if (ad.Any(c => c > 127))
                {
                    (bayrak & 0x0800).Should().Be(0x0800, $"'{ad}' Türkçe harf içerir; UTF-8 ad bayrağı olmalı");
                    bulunan++;
                }
            }
        }

        bulunan.Should().BeGreaterThan(5);
    }

    [Fact]
    public async Task FenKlasoruZip_Her_PDF_Sayfasinda_Tek_Satir_Damga_Ve_RevisionId_Ilk16_Olmali_Filigran_Olmamali()
    {
        var p = await PaketAsync();
        var damga = Bosluksuz(PilotDamgasi.Metin + " · RevisionId " + p.RevizyonId[..16]);

        foreach (var (ad, bayt) in Ac(Uret(p)).Where(d => d.Key.EndsWith(".pdf", StringComparison.Ordinal)))
        {
            using var pdf = PdfDocument.Open(bayt);
            foreach (var sayfa in pdf.GetPages())
            {
                var metin = Bosluksuz(string.Concat(sayfa.GetWords().Select(w => w.Text)));
                metin.Should().Contain(damga, $"{ad} sayfa {sayfa.Number} alt damgası");
                CountOf(metin, "PİLOT").Should().Be(1, $"{ad} sayfa {sayfa.Number}: büyük çapraz filigran kaldırıldı; damga yalnız alt satırda");
                sayfa.GetWords().Where(w => w.Text.Contains("PİLOT", StringComparison.Ordinal))
                    .Should().OnlyContain(w => w.BoundingBox.Bottom < 40, "damga sayfanın en altındadır");
            }
        }
    }

    private static int CountOf(string metin, string aranan)
    {
        var n = 0;
        for (var i = metin.IndexOf(aranan, StringComparison.Ordinal); i >= 0; i = metin.IndexOf(aranan, i + aranan.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    [Fact]
    public async Task FenKlasoruZip_Her_PDF_Baslik_Ve_Sutunlari_Ornek_Paketle_Ayni_Olmali()
    {
        var d = Ac(Uret(await PaketAsync()));
        string M(string ad) => Bosluksuz(Metin(d[ad]));

        M("DÖNÜŞÜM PARAMETRELERİ.pdf").Should().Contain("DÖNÜŞÜMORTAKNOKTALARI").And.Contain("I.SİSTEM<KOORDİNATLAR>").And.Contain("II.SİSTEM<KOORDİNATLAR>")
            .And.Contain("ÖLÇEKKATSAYISI").And.Contain("DÖNÜKLÜK").And.Contain("ORTALAMAHATA").And.Contain("ORTAKNOKTALARINDÖNÜŞÜMSONRASIKOORDİNATLARI")
            .And.Contain("HATALAR").And.Contain("Vy").And.Contain("Vx").And.Contain("A=").And.Contain("CY=").And.Contain("CX=")
            .And.Contain("Noktano").And.Contain("DönüşecekY").And.Contain("DönüşmüşX");
        M("KOORDİNE ÖZET.pdf").Should().Contain("ITRF-96KOORDİNEÖZET(DEPREMÖNCESİ)").And.Contain("ITRF-96KOORDİNEÖZET(DEPREMSONRASI)")
            .And.Contain("NoktaNoYXMK");
        M("ALAN HESABI.pdf").Should().Contain("DENGELEMEALANHESABI").And.Contain("AdaParselAlanDüzeltmeDeng.AlanPafta").And.Contain("TOPLAM")
            .And.Contain("TapuAlanı").And.Contain("HESAPLANAN").And.Contain("FARK").And.Contain("TECVİZ")
            .And.Contain("NOKTALIALANHESABI").And.Contain("AdaParselNoktalarHesapAlan");
        M("ALAN HESABI-1.pdf").Should().Contain("CEPHELİALANHESABI-1").And.Contain("AdaParselHesapAlanNoktaNoYXCephe");
        M("DOP.pdf").Should().Contain("ToplamTahsis").And.Contain("Toplamİmar").And.Contain("ToplamOrtakKatılımAlanı").And.Contain("(Dopal)")
            .And.Contain("Dopalma").And.Contain("Kamulaştırılan").And.Contain("Bağışlanan").And.Contain("ToplamKadastroParselAlanı")
            .And.Contain("ToplamPay").And.Contain("OrtakKatılımOranı").And.Contain("DOPO(OKOilebirlikte)").And.Contain("MaksimumDopOranı")
            .And.Contain("DüzenlemeOrtaklıkPayıHesabı");
        M("ÖZET CETVELİ.pdf").Should().Contain("ÖZETCETVELİ").And.Contain("İLİ:SENTETİKİL").And.Contain("İLÇESİ:ÖRNEKİLÇE")
            .And.Contain("KÖY/MAHALLESİ:DENEMEMAH.").And.Contain("BELEDİYESİ:ÖRNEKBELEDİYESİ")
            .And.Contain("Ada/ParAlanKamu.Y.TerkD.GirmeyenDopalDopalmaDopİ.TahsisAdaNoAlan").And.Contain("KADASTROTOPLAMLARI")
            .And.Contain("PARSELASYONHARİTASI.NCZ");
        M("MAL SAHİPLERİ ARAŞTIRMA VE ÖZET FORMU.pdf").Should().Contain("MALSAHİPLERİARAŞTIRMAVEÖZETFORMU").And.Contain("(KADASTROPARSELİNESIRALI)")
            .And.Contain("SNoPaftaAda/ParAlan/PayHisseMalik/BabaAdıKamuDop/İThAda/ParAlanHissePay").And.Contain("TOPLAMADAALANI")
            .And.Contain("O.KATILIMORANI").And.Contain("D.O.PAYIORANI").And.Contain("SAYFANO");
        M("TESCİLE ESAS DAĞITIM CETVELİ.pdf").Should().Contain("(İMARPARSELİNESIRALI)").And.Contain("101/1");
        M("TESCİL SAYFASI-1.pdf").Should().Contain("TESCİLSAYFASI").And.Contain("PAFTANO:N99-X-01").And.Contain("SAYFANO:1").And.Contain("ADANO:101")
            .And.Contain("CltSyfParNoHe.M2Dm2CinsiHisseKAdaKParCiltSyf.MalikiBabası").And.Contain("ARSA");
        M("RÖLÖVE ÖLÇÜ KROKİSİ.pdf").Should().Contain("EK-17/FR.521").And.Contain("ID:99999").And.Contain("Datum:ITRF-96")
            .And.Contain("(DEPREMÖNCESİ)").And.Contain("(DEPREMSONRASI)").And.Contain("DÜZENLEYEN").And.Contain("UYGUNDUR")
            .And.Contain("KONTROLEDEN").And.Contain("ONAYLAYAN").And.Contain("KADASTROMÜDÜRLÜĞÜ").And.Contain("ADA/PARSELNO");
        M("PARSELASYON HARİTASI.pdf").Should().Contain("PARSELASYONHARİTASI").And.Contain("Ölçek1/").And.Contain("İliSENTETİKİL")
            .And.Contain("DÜZENLEYEN").And.Contain("UYGUNDUR").And.Contain("KONTROLEDİLMİŞTİR");
    }

    [Fact]
    public async Task KoordineAlan_Birlesik_Dosyasi_Koordine_Alan_Ve_Cepheli_Sayfalarin_Ardisik_Renderi_Olmali()
    {
        var d = Ac(Uret(await PaketAsync()));
        int Sayfa(string ad)
        {
            using var pdf = PdfDocument.Open(d[ad]);
            return pdf.NumberOfPages;
        }

        Sayfa("KOORDİNE-ALAN.pdf").Should().Be(Sayfa("KOORDİNE ÖZET.pdf") + Sayfa("ALAN HESABI.pdf") + Sayfa("ALAN HESABI-1.pdf"));
        using var birlesik = PdfDocument.Open(d["KOORDİNE-ALAN.pdf"]);
        var sayfalar = birlesik.GetPages().Select(s => Bosluksuz(string.Concat(s.GetWords().Select(w => w.Text)))).ToList();
        sayfalar[0].Should().Contain("KOORDİNEÖZET");
        sayfalar[^1].Should().Contain("CEPHELİALANHESABI-1");
    }

    [Fact]
    public async Task CepheliAlan_Sayfalari_Yan_Yana_Uc_Serit_Ve_Serit_Sinirinda_Bolunmeli()
    {
        var p = await PaketAsync();

        // A1 ve Y1: 4 + 4 köşe = 8 satır; şerit 2 satır → 4 şerit → sayfa 1: 3 şerit, sayfa 2: 1 şerit.
        var sayfalar = FenKlasoruVerisi.CepheliSayfalari(p, seritSatiri: 2);

        sayfalar.Should().HaveCount(2);
        sayfalar[0].Should().HaveCount(3);
        sayfalar[1].Should().HaveCount(1);
        sayfalar.SelectMany(s => s).SelectMany(s => s).Should().HaveCount(8);
        var pdf = Ac(Uret(p))["ALAN HESABI-1.pdf"];
        using var d = PdfDocument.Open(pdf);
        d.GetPage(1).GetWords().Count(w => w.Text == "Cephe").Should().Be(3, "sayfada yan yana 3 dikey şerit başlığı vardır");
    }

    [Fact]
    public async Task CepheliAlan_Cephe_Ardisik_Koseler_Arasi_Kenar_Uzunlugu_Olmali()
    {
        var p = await PaketAsync();

        var satirlar = FenKlasoruVerisi.CepheliSayfalari(p).SelectMany(s => s).SelectMany(s => s).ToList();
        var a1 = satirlar.SkipWhile(s => s.AdaParsel != "101/1").Take(4).ToList();

        // A1 = 12 m (Doğu) × 10 m (Kuzey) dikdörtgen: (0,0)→(0,12)→(10,12)→(10,0).
        a1.Select(s => s.CepheM).Should().Equal(12.00m, 10.00m, 12.00m, 10.00m);
        a1[0].HesapAlan.Should().Be("120.00");
        a1.Skip(1).Should().OnlyContain(s => s.AdaParsel.Length == 0 && s.HesapAlan.Length == 0, "parsel bloğunun yalnız ilk satırı etiketlidir");
    }

    [Theory]
    [InlineData("1064.02", "0", "1064", "02")]
    [InlineData("300.00", "0", "0300", "00")]
    [InlineData("430.87", "0", "0430", "87")]
    [InlineData("12345.60", "1", "2345", "60")]
    [InlineData("0.01", "0", "0000", "01")]
    public void TescilSayfasi_Alan_M2_Dort_Haneli_Dm2_Iki_Haneli_Sifir_Dolgulu_Olmali(string alan, string he, string m2, string dm2)
    {
        var a = HeM2Dm2.Ayir(decimal.Parse(alan, System.Globalization.CultureInfo.InvariantCulture));

        FenKlasoruBelgeleri.TescilAlani(a).Should().Be((he, m2, dm2));
    }

    [Fact]
    public async Task TescilSayfasi_PDFte_Alan_Sifir_Dolgulu_Ve_Malik_Rehberi_Ile_Cilt_Sayfa_Basilmali()
    {
        var m = Bosluksuz(Metin(Ac(Uret(await PaketAsync()))["TESCİL SAYFASI-1.pdf"]));

        // 101/1 = 120.00 m² → He 0 | M2 0120 | Dm2 00; kadastro K1 → rehberde ada 901, parsel 7, Cilt 14, Sayfa 0.
        m.Should().Contain("1001200").And.Contain("ARSA").And.Contain("AYŞEÖRNEKSOY").And.Contain("İSMAİL")
            .And.Contain("ÇAĞRIDENEMEOĞLU").And.Contain("ŞÜKRÜ").And.Contain("9017140");
    }

    [Fact]
    public async Task MalikRehberi_Bossa_Anonim_Malik_Kodu_Basilmali_Doluysa_Ad_ve_Baba_Adi()
    {
        var bos = Bosluksuz(Metin(Ac(Uret(await PaketAsync(rehber: false)))["MAL SAHİPLERİ ARAŞTIRMA VE ÖZET FORMU.pdf"]));
        var dolu = Bosluksuz(Metin(Ac(Uret(await PaketAsync()))["MAL SAHİPLERİ ARAŞTIRMA VE ÖZET FORMU.pdf"]));

        bos.Should().Contain("MLK-001").And.NotContain("ÖRNEKSOY");
        dolu.Should().Contain("AYŞEÖRNEKSOY").And.Contain("İSMAİL").And.Contain("1/2").And.Contain("101/1");
    }

    [Fact]
    public async Task DonusumParametreleri_Girdi_Yoksa_Anlasilir_Bilgi_Notu_Basilmali()
    {
        var p = await PaketAsync(donusum: false);
        var d = Ac(Uret(p));

        Bosluksuz(Metin(d["DÖNÜŞÜM PARAMETRELERİ.pdf"])).Should().Contain("ortaknoktalıdönüşümgirdisiyok").And.Contain("NoktaNo;Y1;X1;Y2;X2");
        Bosluksuz(Metin(d["KOORDİNE ÖZET.pdf"])).Should().Contain("Dönüşümgirdisiyok");
        p.Donusum.Should().BeNull();
    }

    [Fact]
    public async Task DonusumParametreleri_Bilinen_Helmert_Parametreleri_Ve_Deprem_Oncesi_Koordinatlari_Basilmali()
    {
        var p = await PaketAsync();
        var m = Bosluksuz(Metin(Ac(Uret(p))["DÖNÜŞÜM PARAMETRELERİ.pdf"]));

        // Bağımsız referans: A = 1.00001, B = 0.00002, C = −B, D = A; artıklar sıfır.
        m.Should().Contain("A=1.00001000").And.Contain("B=0.00002000").And.Contain("C=-0.00002000").And.Contain("D=1.00001000")
            .And.Contain("CY=-5.000").And.Contain("CX=10.000");
        var k1 = p.Koordinatlar[0];
        var oncesi = p.Donusum!.DepremOncesi.Single(k => k.NoktaNo == k1.NoktaNo);
        // Ters dönüşümün bağımsız denetimi: öncesi noktayı ileri dönüştürünce sonrası (± 0.005 m yuvarlama) elde edilir.
        ((A * oncesi.Y) + (B * oncesi.X) + Ty - k1.Y).Should().BeInRange(-0.006m, 0.006m);
        ((-B * oncesi.Y) + (A * oncesi.X) + Tx - k1.X).Should().BeInRange(-0.006m, 0.006m);
        m.Should().Contain(oncesi.Y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task DopCks_Pakette_PILOT_Adli_CksUreticisiyle_Bayt_Bayt_Ayni_Ve_SHA256_Girisli_Olmali_SORU_039()
    {
        var p = await PaketAsync();
        var d = Ac(Uret(p));

        var cks = d[FenKlasoruZipUretici.CksDosyaAdi];

        FenKlasoruZipUretici.CksDosyaAdi.Should().Contain("PILOT", "CKS içinde damga yok; taslak izi dosya adındadır (SORU-039)");
        cks.Should().Equal(CksDosyaUretici.Yaz(CksOzetiEslemesi.Olustur(p.Dop).Value).Value);
        Encoding.UTF8.GetString(d["DOP_PILOT.CKS.sha256"]).Should()
            .Be(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(cks)) + "  DOP_PILOT.CKS\n");
    }

    [Fact]
    public async Task DopCks_D1_Sifirdan_Farkliysa_Pakette_Olmamali_DOP_PDF_Notu_Basilmali()
    {
        var p = await PaketAsync();
        var d1 = p with { Dop = p.Dop with { D1 = 1.00m } };

        var d = Ac(Uret(d1));

        d.Keys.Should().NotContain(FenKlasoruZipUretici.CksDosyaAdi);
        var dop = Bosluksuz(Metin(d["DOP.pdf"]));
        dop.Should().Contain("DOP.CKSüretilmedi");
        dop.Should().Contain("D1—EskiDOPO>ProjeDOPOOlanAlanlar1.00").And.Contain("D2—EskiDOPO>ProjeDOPOOlanTahsisler0.00")
            .And.Contain("ToplamKamulaştırılanAlan—").And.Contain("ToplamBağışlananAlan—", "modelde karşılığı olmayan alan 0 diye basılmaz (reviewer M2)");
    }

    [Fact]
    public async Task FenKlasoruZip_Donusum_Hesaplanamadiysa_ERR_CRS_004_Ile_Reddedilmeli()
    {
        var p = await PaketAsync();
        var hata = Imar18.Domain.Diagnostics.ErrorCatalog.Get(Imar18.Domain.Diagnostics.ErrorCodes.CrsDonusumHesaplanamadi);

        var r = new FenKlasoruZipUretici().Uret(p with { Donusum = null, DonusumHatasi = hata });

        r.Error.Code.Should().Be(Imar18.Domain.Diagnostics.ErrorCodes.CrsDonusumHesaplanamadi);
    }

    [Fact]
    public async Task FenKlasoruZip_Imar_Adasi_Yokken_Dosya_Adlari_Tekil_Ve_11_Sabit_PDF_Olmali()
    {
        var p = await PaketAsync();
        var adasiz = p with
        {
            Parseller = p.Parseller.Where(s => s.Rol != Imar18.Domain.Geometry.ParselRolu.Imar).ToArray(),
            ImarSirali = [],
        };

        var adlar = Adlar(Uret(adasiz));

        adlar.Should().OnlyHaveUniqueItems();
        adlar.Should().NotContain(a => a.StartsWith("TESCİL SAYFASI", StringComparison.Ordinal) || a.StartsWith("RÖLÖVE-", StringComparison.Ordinal));
        // Ada bağımlı dosyalar (Tescil/Rölöve-n) olmadan 11 sabit PDF kalır.
        adlar.Where(a => a.EndsWith(".pdf", StringComparison.Ordinal)).Should().Equal(
            "DÖNÜŞÜM PARAMETRELERİ.pdf", "KOORDİNE ÖZET.pdf", "ALAN HESABI.pdf", "ALAN HESABI-1.pdf", "KOORDİNE-ALAN.pdf", "DOP.pdf",
            "ÖZET CETVELİ.pdf", "MAL SAHİPLERİ ARAŞTIRMA VE ÖZET FORMU.pdf", "TESCİLE ESAS DAĞITIM CETVELİ.pdf", "RÖLÖVE ÖLÇÜ KROKİSİ.pdf",
            "PARSELASYON HARİTASI.pdf");
    }

    [Fact]
    public async Task FenKlasoruZip_Cok_Imar_Parselli_Malikte_Yerlesim_Hatasi_Olmadan_Uretilmeli()
    {
        // Reviewer M6: bir malikin 150 imar satırı tek hücrede birleşseydi QuestPDF sayfa aşımıyla tüm paketi düşürürdü.
        var p = await PaketAsync();
        var ilk = p.MalikSirali[0];
        var cok = Enumerable.Range(1, 150).Select(i => ilk with { ImarParseli = "101/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var buyuk = p with { MalikSirali = p.MalikSirali.Concat(cok).ToArray() };

        var mal = Ac(Uret(buyuk))["MAL SAHİPLERİ ARAŞTIRMA VE ÖZET FORMU.pdf"];

        using var pdf = PdfDocument.Open(mal);
        pdf.NumberOfPages.Should().BeGreaterThan(1);
        Metin(mal).Should().Contain("101/150");
    }

    [Fact]
    public async Task FenKlasoruZip_Ortak_Nokta_Sirasi_Degisince_Ayni_RevisionId_Ve_Ayni_Bayt_Uretmeli()
    {
        var (yol1, _, zaman1) = await OrnekProje.TamProjeAsync();
        var (yol2, _, zaman2) = await OrnekProje.TamProjeAsync();
        var d = Donusum();
        (await yol1.GonderAsync(new DonusumGirdisiAyarlaKomutu(d), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await yol2.GonderAsync(new DonusumGirdisiAyarlaKomutu(d with { OrtakNoktalar = d.OrtakNoktalar.Reverse().ToArray() }), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await new ApprovalService(yol1, zaman1, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        (await new ApprovalService(yol2, zaman2, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        yol1.Revizyon.Should().Be(yol2.Revizyon);
        yol2.Belge.Donusum!.OrtakNoktalar.Select(n => n.NoktaNo).Should().Equal("N1", "N2", "N3", "N4");
        byte[] Zip(ProjeKomutYolu y) => Uret(CiktiPaketiOlusturucu.Olustur(y.Belge, ProjeDegerlendirici.Degerlendir(y.Belge, OrnekProje.Topoloji), new HelmertAfinDonusumHesaplayici()).Value);
        Zip(yol1).Should().Equal(Zip(yol2));
    }

    [Fact]
    public async Task FenKlasoruZip_Ayni_Paketten_Tekrar_Uretim_Bayt_Bayt_Ayni_Olmali()
    {
        var p = await PaketAsync();

        Uret(p).Should().Equal(Uret(p));
    }

    [Theory]
    [InlineData("125", "60", 500)]
    [InlineData("100", "61", 1000)]
    [InlineData("250", "120", 1000)]
    [InlineData("251", "50", 2000)]
    [InlineData("200", "121", 2000)]
    [InlineData("1000000", "1", 100000)]
    public void ParselasyonHaritasi_Olcek_Siga_En_Kucuk_Standart_Payda_Olmali(string gen, string yuk, int beklenen)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        FenKlasoruVerisi.OlcekSec(decimal.Parse(gen, inv), decimal.Parse(yuk, inv), 250m, 120m).Should().Be(beklenen);
    }

    [Fact]
    public async Task ParselasyonHaritasi_SVG_Kadastro_Kesikli_Imar_Kalin_Kuzey_Oku_Ve_Izgara_Icermeli()
    {
        var (svg, olcek) = FenKlasoruBelgeleri.HaritaSvg(await PaketAsync());

        olcek.Should().Be(100, "20 m × 10 m çizim 250 × 120 mm alana 1/100'de sığar");
        svg.Should().Contain("stroke-dasharray").And.Contain(">K</text>").And.Contain("Y=").And.Contain("X=").And.Contain("101/1");
    }

    [Fact]
    public void ImarAdalari_Sayisal_Sirada_Ve_Tekil_Olmali()
    {
        FenKlasoruVerisi.AdaNo("101/2").Should().Be("101");
        FenKlasoruVerisi.ParselNo("101/2").Should().Be("2");
        FenKlasoruVerisi.AdaNo("YOL").Should().Be("YOL");
        FenKlasoruZipUretici.Sira(3).Should().Be("1-2-3");
    }
}

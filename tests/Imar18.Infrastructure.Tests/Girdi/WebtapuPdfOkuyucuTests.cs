using System.Numerics;
using FluentAssertions;
using Imar18.Application.Girdi;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Numerics;
using Imar18.Infrastructure.Girdi;
using Imar18.Infrastructure.Tests.Cad.Ncz;
using Xunit;

namespace Imar18.Infrastructure.Tests.Girdi;

/// <summary>
/// P7.3-B1 (SAHA-006): webtapu "Tapu Kaydı (Aktif Malikler için Detaylı)" PDF okuyucusu (PdfPig, metin katmanı). Bağımsız
/// referans: gerçek belgenin kelime konumu incelemesi (iki sütunlu başlık; satır çapası sistem no; malik hücresi
/// "(SN:n) AD SOYAD : BABA Oğlu|Kızı"; edinme sebebi alt satırlara ve sonraki sayfaya taşar; 35° filigran). Sentetik PDF bu
/// düzeni taklit eder (uydurma adlar). Gerçek PDF testi yalnız yapısal tutarlılığı doğrular (gerçek ad/ada/alan koda yazılmaz).
/// </summary>
public sealed class WebtapuPdfOkuyucuTests
{
    private static readonly WebtapuPdfOkuyucu Okuyucu = new();

    private static TapuKaydi Oku(byte[] pdf)
    {
        var sonuc = Okuyucu.Oku(new MemoryStream(pdf));
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        return sonuc.Value;
    }

    private static AppError Hata(byte[] pdf)
    {
        var sonuc = Okuyucu.Oku(new MemoryStream(pdf));
        sonuc.IsFailure.Should().BeTrue();
        return sonuc.Error;
    }

    [Fact]
    public void Webtapu_Sentetik_Belgenin_Tapu_Kayit_Bilgilerini_Okumali()
    {
        var k = Oku(SentetikWebtapuPdf.Olustur());

        k.Ada.Should().Be("901");
        k.Parsel.Should().Be("7");
        k.AtYuzolcumM2.Should().Be(1000.00m);
        k.TasinmazNo.Should().Be("100000001");
        k.Il.Should().Be("ÖRNEKİL");
        k.Ilce.Should().Be("ÖRNEKİLÇE");
        k.Mahalle.Should().Be("ÇAMLIK Mah.");
        k.Mevkii.Should().Be("Sırtlar");
        k.Cilt.Should().Be("12");
        k.Sayfa.Should().Be("345");
        k.Nitelik.Should().Be("Tarla");
    }

    [Fact]
    public void Webtapu_Sentetik_Belgenin_Maliklerini_Ad_Baba_Kisi_No_Ve_Hisseyle_Okumali()
    {
        var m = Oku(SentetikWebtapuPdf.Olustur()).Malikler;

        m.Should().HaveCount(3);
        m[0].Should().BeEquivalentTo(new
        {
            HisseSistemNo = "900000101", KisiSistemNo = "800000001", AdSoyad = "AYŞE YILMAZ", BabaAdi = "ALİ",
            ElBirligiNo = "70000001", Pay = new BigInteger(1), Payda = new BigInteger(2), MetrekareM2 = 500.00m, Terkinli = false,
        });
        m[1].AdSoyad.Should().Be("MEHMET ÇAKIR");
        m[1].BabaAdi.Should().Be("ALİ");
        m[1].ElBirligiNo.Should().BeNull("boş El Birliği hücresi");
        m[2].AdSoyad.Should().Be("ŞÜKRÜ IŞIK");
        m[2].BabaAdi.Should().Be("HASAN");
        Fraction.Sum(m.Select(x => new Fraction(x.Pay, x.Payda))).Should().Be(Fraction.One);
    }

    [Fact]
    public void Webtapu_Sayfa_Sonunu_Asan_Satir_Tek_Malik_Olarak_Birlesmeli()
    {
        var m = Oku(SentetikWebtapuPdf.Olustur()).Malikler;

        m.Should().HaveCount(3, "sonraki sayfanın başındaki devam satırı yeni malik değildir");
        m[2].EdinmeSebebi.Should().Contain("Satış").And.Contain("1316");
    }

    [Fact]
    public void Webtapu_Filigran_Harfleri_Okunan_Alanlara_Karismamali()
    {
        var filigranli = Oku(SentetikWebtapuPdf.Olustur(filigran: true));
        var filigransiz = Oku(SentetikWebtapuPdf.Olustur(filigran: false));

        filigranli.Should().BeEquivalentTo(filigransiz);
    }

    [Fact]
    public void Webtapu_Terkinli_Satiri_Terkinli_Isaretiyle_Okumali_Ve_Aktiflerden_Ayirmali()
    {
        var terkinli = new SentetikMalik("900000199", "800000099", "ESKİ MALİK", "VELİ Oğlu", null, "1/4", "250.00", "1000.00",
            ["Satış", "01-01-2010", "55"], "Satış-2020");

        var k = Oku(SentetikWebtapuPdf.Olustur(terkinli: terkinli));

        k.Malikler.Should().HaveCount(4);
        k.Malikler.Single(x => x.HisseSistemNo == "900000199").Terkinli.Should().BeTrue();
        k.AktifMalikler.Should().HaveCount(3).And.OnlyContain(x => !x.Terkinli);
    }

    [Fact]
    public void Webtapu_Hisse_Toplami_Bir_Degilse_ERR_TAPU_003_Vermeli()
    {
        var m = SentetikWebtapuPdf.VarsayilanMalikler();
        m[2] = m[2] with { Hisse = "1/8", M2 = "125.00" };

        Hata(SentetikWebtapuPdf.Olustur(m)).Code.Should().Be(ErrorCodes.TapuHisseToplamiBirDegil);
    }

    [Fact]
    public void Webtapu_Hisse_Metrekaresi_AT_Yuzolcum_Ile_Uyusmazsa_ERR_TAPU_004_Vermeli()
    {
        var m = SentetikWebtapuPdf.VarsayilanMalikler();
        m[1] = m[1] with { M2 = "251.00" };

        var e = Hata(SentetikWebtapuPdf.Olustur(m));

        e.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        e.TechnicalMessage.Should().Contain("#2").And.NotContain("MEHMET");
    }

    [Fact]
    public void Webtapu_Hisse_Metrekaresi_Bir_Kurus_Tolerans_Icinde_Kabul_Edilmeli()
    {
        var m = SentetikWebtapuPdf.VarsayilanMalikler();
        var liste = new[]
        {
            m[0] with { Hisse = "1/3", M2 = "333.33" },
            m[1] with { Hisse = "1/3", M2 = "333.34" },
            m[2] with { Hisse = "1/3", M2 = "333.33" },
        };

        Oku(SentetikWebtapuPdf.Olustur(liste)).Malikler.Should().HaveCount(3);
    }

    [Fact]
    public void Webtapu_Metin_Katmani_Olmayan_PDF_ERR_GIRD_003_Vermeli()
    {
        Hata(SentetikWebtapuPdf.Metinsiz()).Code.Should().Be(ErrorCodes.GirdPdfBozuk);
    }

    [Fact]
    public void Webtapu_Bicimi_Taninmayan_PDF_ERR_GIRD_003_Vermeli()
    {
        Hata(SentetikWebtapuPdf.BaskaBelge()).Code.Should().Be(ErrorCodes.GirdPdfBozuk);
    }

    [Fact]
    public void Webtapu_Bozuk_Bayt_Dizisi_Istisnasiz_ERR_GIRD_003_Vermeli()
    {
        Hata([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x00, 0x13, 0x37]).Code.Should().Be(ErrorCodes.GirdPdfBozuk);
        Hata([]).Code.Should().Be(ErrorCodes.GirdPdfBozuk);
    }

    [Fact]
    public void Webtapu_Hata_Mesajlari_Malik_Adi_Ve_Kimlik_Icermemeli()
    {
        var m = SentetikWebtapuPdf.VarsayilanMalikler();
        m[0] = m[0] with { M2 = "499.00" };

        var e = Hata(SentetikWebtapuPdf.Olustur(m));

        foreach (var metin in new[] { e.TechnicalMessage, e.UserMessage })
        {
            metin.Should().NotContain("AYŞE").And.NotContain("800000001").And.NotContain("900000101");
        }
    }

    [Fact]
    public void Webtapu_Ayni_Belgeden_Ayni_Sonucu_Uretmeli()
    {
        var pdf = SentetikWebtapuPdf.Olustur();

        Oku(pdf).Should().BeEquivalentTo(Oku(pdf));
    }

    // ---------------- Gerçek webtapu PDF'i (yerel, RealData) ----------------

    /// <summary>Gerçek örnek: <c>test-data/real/weptapu_ornegi.pdf</c> veya <c>test-data/real/webtapu/*.pdf</c> (ilk bulunan).</summary>
    private static string? GercekWebtapu()
    {
        if (!GercekVeri.VarMi)
        {
            return null;
        }

        var dogrudan = Path.Combine(GercekVeri.Kok, "weptapu_ornegi.pdf");
        if (File.Exists(dogrudan))
        {
            return dogrudan;
        }

        var klasor = Path.Combine(GercekVeri.Kok, "webtapu");
        return Directory.Exists(klasor) ? Directory.EnumerateFiles(klasor, "*.pdf").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault() : null;
    }

    [WebtapuGercekVeriFact]
    [Trait("Category", "RealData")]
    public void Webtapu_Gercek_Belge_Yapisal_Olarak_Tutarli_Okunmali()
    {
        var k = Oku(File.ReadAllBytes(GercekWebtapu()!));

        // Gerçek ad/ada/alan değerleri koda yazılmaz (AC-PILOT-08); yalnız yapı ve iç tutarlılık doğrulanır.
        k.Ada.Should().MatchRegex(@"^\d+$");
        k.Parsel.Should().MatchRegex(@"^\d+$");
        k.AtYuzolcumM2.Should().BePositive();
        k.Cilt.Should().MatchRegex(@"^\d+$");
        k.Sayfa.Should().MatchRegex(@"^\d+$");
        k.Il.Should().NotBeNullOrWhiteSpace();
        k.Mahalle.Should().NotBeNullOrWhiteSpace();
        k.AktifMalikler.Should().HaveCount(8);
        k.AktifMalikler.Should().OnlyContain(m => m.Pay == 1 && m.Payda == 8);
        k.AktifMalikler.Should().OnlyContain(m => m.AdSoyad.Length > 0 && !m.AdSoyad.Contains(':') && !m.AdSoyad.Contains("SN"));
        k.AktifMalikler.Should().OnlyContain(m => !string.IsNullOrEmpty(m.BabaAdi) && !m.BabaAdi!.EndsWith("Oğlu") && !m.BabaAdi.EndsWith("Kızı"));
        k.AktifMalikler.Should().OnlyContain(m => m.KisiSistemNo != null && m.KisiSistemNo.All(char.IsAsciiDigit));
        k.AktifMalikler.Select(m => m.HisseSistemNo).Should().OnlyHaveUniqueItems();
        k.AktifMalikler.Should().OnlyContain(m => Math.Abs(m.MetrekareM2 - decimal.Round(k.AtYuzolcumM2 * m.Pay.ToDecimal() / m.Payda.ToDecimal(), 2)) <= 0.01m);
        k.AktifMalikler.Should().OnlyContain(m => m.EdinmeSebebi.Length > 0 && !m.EdinmeSebebi.Contains("AMAÇLIDIR"));
    }

    /// <summary>Gerçek webtapu örneği yoksa (CI, başka makine) testi temizce atlar.</summary>
    public sealed class WebtapuGercekVeriFactAttribute : FactAttribute
    {
        public WebtapuGercekVeriFactAttribute()
        {
            if (GercekWebtapu() is null)
            {
                Skip = "Yerel gerçek webtapu örneği yok (test-data/real/weptapu_ornegi.pdf veya test-data/real/webtapu/*.pdf).";
            }
        }
    }
}

internal static class BigIntegerTestUzantisi
{
    public static decimal ToDecimal(this BigInteger v) => (decimal)v;
}

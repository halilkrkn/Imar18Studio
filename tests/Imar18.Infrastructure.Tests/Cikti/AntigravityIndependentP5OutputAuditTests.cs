using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Numerics;
using Imar18.Infrastructure.Cad.Cks;
using Imar18.Infrastructure.Cikti;
using UglyToad.PdfPig;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cikti;

/// <summary>
/// Faz P5 (P5.3) Antigravity Bağımsız Çıktı Sözleşmesi ve Biçim Doğrulama Paketi.
/// <list type="bullet">
/// <item>ClosedXML: 7 Excel cetvelinin tümünde canlı formül doğruluğu, 'He. | M2 | Dm2' ayrımı ve 'PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ' damgası.</item>
/// <item>QuestPDF: Her sayfada PdfPig ile pilot damgası ve RevisionId doğrulaması, FR.348/FR.521 düzeni ve SHA-256 determinizmi.</item>
/// <item>CKS: Netcad DOP özeti gidiş-dönüş (round-trip) tutarlılığı ve başlık damgası.</item>
/// </list>
/// </summary>
public sealed class AntigravityIndependentP5OutputAuditTests
{
    private static async Task<CiktiPaketi> OnayliPaketAsync()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var onayServisi = new ApprovalService(yol, zaman, OrnekProje.Topoloji);
        var onaySonuc = await onayServisi.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);
        onaySonuc.IsSuccess.Should().BeTrue();

        var deg = ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji);
        var paketSonuc = CiktiPaketiOlusturucu.Olustur(yol.Belge, deg);
        paketSonuc.IsSuccess.Should().BeTrue();
        return paketSonuc.Value;
    }

    private static decimal Sayi(IXLCell hucre) => (decimal)hucre.Value.GetNumber();

    // =========================================================================
    // 1. ClosedXML (.xlsx) Canlı Formül ve Damga Denetimleri
    // =========================================================================

    [Fact]
    public async Task ClosedXml_Tum_Sayfalarda_Pilot_Damgasi_Ve_RevisionId_Dogrulanmali()
    {
        var paket = await OnayliPaketAsync();
        var uretici = new ClosedXmlCetvelUretici();
        var sonuc = uretici.Uret(paket);

        sonuc.IsSuccess.Should().BeTrue();
        using var stream = new MemoryStream(sonuc.Value);
        using var workbook = new XLWorkbook(stream);

        workbook.Worksheets.Count.Should().Be(7, "Özet, Koordine Özet, Noktalı Alan, Cepheli Alan, Dengeleme Alan, Mal Sahipleri, Tescile Esas Dağıtım sayfaları mevcut olmalıdır.");

        foreach (var sayfa in workbook.Worksheets)
        {
            sayfa.Cell(1, 1).GetString().Should().Be(PilotDamgasi.Metin, $"Sayfa '{sayfa.Name}' 1. satırında pilot damgası bulunmalıdır.");
            sayfa.Cell(2, 1).GetString().Should().Contain(paket.RevizyonId, $"Sayfa '{sayfa.Name}' 2. satırında RevisionId bulunmalıdır.");
            sayfa.PageSetup.Header.Center.GetText(XLHFOccurrence.OddPages).Should().Contain(PilotDamgasi.Metin);
            sayfa.PageSetup.Footer.Center.GetText(XLHFOccurrence.OddPages).Should().Contain(paket.RevizyonId);
        }
    }

    [Fact]
    public async Task ClosedXml_Ozet_Sayfasinda_DOP_Aritmetik_Formulleri_Decimal_Motor_Sonuclariyla_Birebir_Olmali()
    {
        var paket = await OnayliPaketAsync();
        var uretici = new ClosedXmlCetvelUretici();
        using var workbook = new XLWorkbook(new MemoryStream(uretici.Uret(paket).Value));
        workbook.RecalculateAllFormulas();

        var ozet = workbook.Worksheet("Özet");

        // R1: Kadastro giren = B6 (200.00)
        Sayi(ozet.Cell("B6")).Should().Be(200.00m);
        // D1: B7 (0.00)
        Sayi(ozet.Cell("B7")).Should().Be(0.00m);
        // R2: İmar oluşan = B8 (120.00)
        Sayi(ozet.Cell("B8")).Should().Be(120.00m);

        // DOP Miktarı: B12 = B6-B7-B8+B9+B10 => 200 - 120 = 80.00
        ozet.Cell("B12").FormulaA1.Should().Be("B6-B7-B8+B9+B10");
        Math.Round(Sayi(ozet.Cell("B12")), 2).Should().Be(paket.Dop.DopMiktariM2);

        // Kamuya Ayrılan Alan: B13 = B6-B8 => 80.00
        ozet.Cell("B13").FormulaA1.Should().Be("B6-B8");
        Math.Round(Sayi(ozet.Cell("B13")), 2).Should().Be(paket.Dop.KamuyaAyrilanM2);

        // DOPO: B14 = ROUND((B6-B7-B8+B9+B10)/(B6-B7),7) => 80 / 200 = 0.4000000
        ozet.Cell("B14").FormulaA1.Should().Contain("ROUND");
        Math.Round(Sayi(ozet.Cell("B14")), 7).Should().Be(paket.Dop.Dopo);

        // DOPO + OKO: B16
        Math.Round(Sayi(ozet.Cell("B16")), 7).Should().Be(paket.Dop.DopoOkoIleBirlikte);

        // %45 Kontrolü: B18 = IF(B16<=0.45,"UYGUN","%45 AŞILDI")
        ozet.Cell("B18").Value.GetText().Should().Be("UYGUN");
    }

    [Fact]
    public async Task ClosedXml_Tescile_Esas_Dagitim_He_M2_Dm2_Kolonlari_Ve_Pay_Kapanisi_Tam_Olmali()
    {
        var paket = await OnayliPaketAsync();
        var uretici = new ClosedXmlCetvelUretici();
        using var workbook = new XLWorkbook(new MemoryStream(uretici.Uret(paket).Value));
        var tescil = workbook.Worksheet("Tescile Esas Dağıtım");

        // Başlık satırı kontrolü
        tescil.Row(ClosedXmlCetvelUretici.BaslikSatiri).Cells(3, 5).Select(c => c.GetString()).Should().Equal("He.", "M2", "Dm2");

        // A1 parseli: 120.00 m² => He=0, M2=120, Dm2=0
        var satir1 = ClosedXmlCetvelUretici.BaslikSatiri + 1;
        tescil.Cell(satir1, 1).GetString().Should().Be("101/1");
        Sayi(tescil.Cell(satir1, 3)).Should().Be(0m);
        Sayi(tescil.Cell(satir1, 4)).Should().Be(120m);
        Sayi(tescil.Cell(satir1, 5)).Should().Be(0m);

        // Malik payı: 60.00 m² / 120.00 m² => 6000/12000
        tescil.Cell(satir1, 9).GetString().Should().Be("6000/12000");
    }

    // =========================================================================
    // 2. QuestPDF (.pdf) Fen Klasörü, Filigran ve SHA-256 Determinizmi
    // =========================================================================

    [Fact]
    public async Task QuestPdf_Fen_Klasoru_Her_Sayfada_Filigran_Ve_RevisionId_Tasimali()
    {
        var paket = await OnayliPaketAsync();
        var uretici = new QuestPdfFenKlasoruUretici();
        var pdfSonuc = uretici.Uret(paket);

        pdfSonuc.IsSuccess.Should().BeTrue();
        using var pdf = PdfDocument.Open(pdfSonuc.Value);

        pdf.NumberOfPages.Should().BeGreaterThanOrEqualTo(5, "Kapak, İçindekiler, Alan Cetveli, Tescil ve Kroki sayfaları bulunmalıdır.");

        var beklenenDamga = new string(PilotDamgasi.Metin.Where(c => !char.IsWhiteSpace(c)).ToArray());

        foreach (var sayfa in pdf.GetPages())
        {
            var sayfaMetni = new string(sayfa.Text.Where(c => !char.IsWhiteSpace(c)).ToArray());
            sayfaMetni.Should().Contain(beklenenDamga, $"Sayfa {sayfa.Number} üzerinde pilot filigranı yer almalıdır.");
            sayfaMetni.Should().Contain(paket.RevizyonId, $"Sayfa {sayfa.Number} üzerinde RevisionId yer almalıdır.");
        }
    }

    [Fact]
    public async Task QuestPdf_Fen_Klasoru_SHA256_Deterministik_Olmali()
    {
        var paket = await OnayliPaketAsync();
        var uretici = new QuestPdfFenKlasoruUretici();

        var pdf1 = uretici.Uret(paket).Value;
        var pdf2 = uretici.Uret(paket).Value;

        var sha1 = Convert.ToHexStringLower(SHA256.HashData(pdf1));
        var sha2 = Convert.ToHexStringLower(SHA256.HashData(pdf2));

        sha1.Should().Be(sha2, "Aynı girdi paketi için üretilen PDF belgelerinin SHA-256 hash'leri byte-for-byte özdeş olmalıdır.");
        pdf1.Should().Equal(pdf2);
    }

    // =========================================================================
    // 3. CKS Netcad Uyumlu Format ve Round-trip Denetimi
    // =========================================================================

    [Fact]
    public async Task Cks_Netcad_Formatinda_Uretilmeli_Ve_Okuyucu_Ile_Kayiplarsiz_Geri_Okunmali()
    {
        var paket = await OnayliPaketAsync();
        var cksUretici = new CksDosyaUretici();
        var cksSonuc = cksUretici.Uret(paket);

        cksSonuc.IsSuccess.Should().BeTrue();
        // SORU-039 (proje sahibi kararı, 18 Kasım 2026): CKS Netcad karışık kodlamasıyla (1254 harf + CP437 çerçeve) yazılır ve
        // 1. satırı boştur; Pilot damgası CKS'e yazılmaz (eski beklenti: 1. satırda damga + RevisionId, UTF-8).
        var cksMetin = Imar18.Infrastructure.Cikti.Cks.NetcadCksKodlayici.Coz(cksSonuc.Value);
        cksMetin.Split("\r\n")[0].Should().BeEmpty();
        cksMetin.Should().NotContain(PilotDamgasi.Metin);

        // P4.1 CksFileReader ile geri oku
        var reader = new CksFileReader();
        var okumaSonucu = reader.Read(new MemoryStream(cksSonuc.Value));

        okumaSonucu.IsSuccess.Should().BeTrue();
        var okunan = okumaSonucu.Value;

        okunan.ToplamDuzenlemeyeGirenDopAlinanM2.Should().Be(200.00m);
        okunan.ToplamImarAdaAlaniM2.Should().Be(120.00m);
        okunan.ToplamTahsisM2.Should().Be(120.00m);
        okunan.KamuyaAyrilanAlanM2.Should().Be(80.00m);
        okunan.Dopo.Should().Be(0.4000000m);
    }
}

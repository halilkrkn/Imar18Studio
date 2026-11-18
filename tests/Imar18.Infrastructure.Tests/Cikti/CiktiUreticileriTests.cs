using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Infrastructure.Cad.Cks;
using Imar18.Infrastructure.Cad.Ncz;
using Imar18.Infrastructure.Cikti;
using UglyToad.PdfPig;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cikti;

/// <summary>
/// P5.3 — Taslak çıktı üreticileri (D31 Kural 5/6/9, AC-PILOT-04/07): ClosedXML cetvellerinde canlı formüllerin motorun decimal
/// sonuçlarıyla birebir uyuşması, QuestPDF Fen Klasörü'nün her sayfasında pilot damgası, He/M2/Dm2 sütunları, FR.348/FR.521
/// bölümleri; CKS yazıcısının P4.1 okuyucusuyla gidiş-dönüş tutarlılığı. Veri tamamen sentetiktir (OrnekProje).
/// </summary>
public sealed class CiktiUreticileriTests
{
    private static readonly string DamgaBosluksuz = new(PilotDamgasi.Metin.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static async Task<CiktiPaketi> PaketAsync()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        return CiktiPaketiOlusturucu.Olustur(yol.Belge, ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji)).Value;
    }

    private static XLWorkbook Kitap(byte[] xlsx)
    {
        var wb = new XLWorkbook(new MemoryStream(xlsx));
        wb.RecalculateAllFormulas();
        return wb;
    }

    private static decimal Sayi(IXLCell c) => (decimal)c.Value.GetNumber();

    // ---------------- ClosedXML ----------------

    [Fact]
    public async Task Excel_Cetvelleri_Yedi_Sayfa_Ve_Her_Sayfada_Pilot_Damgasi_Icermeli()
    {
        var p = await PaketAsync();

        var sonuc = new ClosedXmlCetvelUretici().Uret(p);

        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        using var wb = Kitap(sonuc.Value);
        wb.Worksheets.Select(w => w.Name).Should().Equal(
            "Özet", "Koordine Özet", "Noktalı Alan", "Cepheli Alan", "Dengeleme Alan", "Mal Sahipleri", "Tescile Esas Dağıtım");
        foreach (var ws in wb.Worksheets)
        {
            ws.Cell(1, 1).GetString().Should().Be(PilotDamgasi.Metin);
            ws.Cell(2, 1).GetString().Should().Contain(p.RevizyonId);
            ws.PageSetup.Header.Center.GetText(XLHFOccurrence.OddPages).Should().Contain(PilotDamgasi.Metin);
            ws.PageSetup.Footer.Center.GetText(XLHFOccurrence.OddPages).Should().Contain(p.RevizyonId);
        }
    }

    [Fact]
    public async Task Excel_Ozet_Canli_Formulleri_Motor_Decimal_Sonucuyla_Birebir_Olmali()
    {
        var p = await PaketAsync();
        using var wb = Kitap(new ClosedXmlCetvelUretici().Uret(p).Value);
        var ws = wb.Worksheet("Özet");

        ws.Cell("B14").FormulaA1.Should().Contain("ROUND");
        Math.Round(Sayi(ws.Cell("B12")), 2).Should().Be(p.Dop.DopMiktariM2);
        Math.Round(Sayi(ws.Cell("B13")), 2).Should().Be(p.Dop.KamuyaAyrilanM2);
        Math.Round(Sayi(ws.Cell("B14")), 7).Should().Be(p.Dop.Dopo);
        Math.Round(Sayi(ws.Cell("B15")), 7).Should().Be(p.Dop.Oko);
        Math.Round(Sayi(ws.Cell("B16")), 7).Should().Be(p.Dop.DopoOkoIleBirlikte);
        ws.Cell("B18").Value.GetText().Should().Be("UYGUN");
    }

    [Fact]
    public async Task Excel_Cepheli_Alan_SQRT_Formulu_Kenar_Uzunluklariyla_Uyusmali()
    {
        var p = await PaketAsync();
        using var wb = Kitap(new ClosedXmlCetvelUretici().Uret(p).Value);
        var ws = wb.Worksheet("Cepheli Alan");
        var k1 = p.Parseller.Single(s => s.GeciciKod == "K1");

        var ilk = ClosedXmlCetvelUretici.BaslikSatiri + 1;
        for (var i = 0; i < k1.Kenarlar.Count; i++)
        {
            ws.Cell(ilk + i, 5).FormulaA1.Should().StartWith("ROUND(SQRT(");
            Math.Round(Sayi(ws.Cell(ilk + i, 5)), 2).Should().Be(k1.Kenarlar[i].CepheM);
        }
    }

    [Fact]
    public async Task Excel_Dengeleme_ve_Dagitim_Toplamlari_Canli_Formulle_Tutmali()
    {
        var p = await PaketAsync();
        using var wb = Kitap(new ClosedXmlCetvelUretici().Uret(p).Value);

        var deng = wb.Worksheet("Dengeleme Alan");
        var toplamSatiri = ClosedXmlCetvelUretici.BaslikSatiri + 1 + p.Dengeleme!.Satirlar.Count;
        deng.Cell(toplamSatiri, 5).FormulaA1.Should().StartWith("SUM(");
        Math.Round(Sayi(deng.Cell(toplamSatiri, 5)), 2).Should().Be(p.Dengeleme.TapuAlaniM2);
        deng.Cell(toplamSatiri + 6, 3).Value.GetText().Should().Be("UYGUN");

        var mal = wb.Worksheet("Mal Sahipleri");
        var malToplam = ClosedXmlCetvelUretici.BaslikSatiri + 1 + p.MalikSirali.Count;
        Math.Round(Sayi(mal.Cell(malToplam, 7)), 2).Should().Be(p.MalikSirali.Sum(m => m.TahsisM2));
        Math.Round(Sayi(mal.Cell(ClosedXmlCetvelUretici.BaslikSatiri + 1, 5)), 2).Should().Be(p.MalikSirali[0].NetHakM2);

        var tescil = wb.Worksheet("Tescile Esas Dağıtım");
        var r = ClosedXmlCetvelUretici.BaslikSatiri + 1;
        tescil.Row(ClosedXmlCetvelUretici.BaslikSatiri).Cells(3, 5).Select(c => c.GetString()).Should().Equal("He.", "M2", "Dm2");
        tescil.Cell(r, 1).GetString().Should().Be("101/1");
        Sayi(tescil.Cell(r, 3)).Should().Be(0m);
        Sayi(tescil.Cell(r, 4)).Should().Be(120m);
        Sayi(tescil.Cell(r, 5)).Should().Be(0m);
        tescil.Cell(r, 9).GetString().Should().Be("6000/12000");
    }

    // ---------------- QuestPDF ----------------

    [Fact]
    public async Task FenKlasoru_Pdf_Her_Sayfada_Pilot_Filigrani_Ve_RevisionId_Tasimali()
    {
        var p = await PaketAsync();

        var sonuc = new QuestPdfFenKlasoruUretici().Uret(p);

        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        using var pdf = PdfDocument.Open(sonuc.Value);
        pdf.NumberOfPages.Should().BeGreaterThanOrEqualTo(5);
        foreach (var sayfa in pdf.GetPages())
        {
            var metin = Bosluksuz(sayfa.Text);
            metin.Should().Contain(DamgaBosluksuz, $"sayfa {sayfa.Number} pilot damgası taşımalı");
            metin.Should().Contain(p.RevizyonId, $"sayfa {sayfa.Number} RevisionId taşımalı");
        }
    }

    [Fact]
    public async Task FenKlasoru_Pdf_Ligatur_Kullanmamali_RevisionId_Ve_Metin_Birebir_Kopyalanabilmeli()
    {
        // P6 regresyonu: "ff"/"fb"/"ti" harf birleşimleri (ligatür) PDF metninde tek glif olunca RevisionId kopyalanamaz.
        var p = await PaketAsync() with { ProjeAdi = "Sentetik ffi ffl fb ti fi Projesi", RevizyonId = "00ff11fb22ffa3fb44ff55fb66ff77fb88ff99fbaaffbbfbccffddfbeeff00fb" };

        using var pdf = PdfDocument.Open(new QuestPdfFenKlasoruUretici().Uret(p).Value);

        var tum = Bosluksuz(string.Concat(pdf.GetPages().Select(s => s.Text)));
        tum.Should().Contain("SentetikffifflfbtifiProjesi");
        tum.Should().Contain(p.RevizyonId);
        tum.Should().NotContainAny("\uFB00", "\uFB01", "\uFB02", "\uFB03", "\uFB04");
    }

    [Fact]
    public async Task FenKlasoru_Pdf_FR348_Kapak_HeM2Dm2_Tescil_Ve_FR521_Kroki_Bolumlerini_Icermeli()
    {
        var p = await PaketAsync();
        using var pdf = PdfDocument.Open(new QuestPdfFenKlasoruUretici().Uret(p).Value);

        var tum = Bosluksuz(string.Concat(pdf.GetPages().Select(s => s.Text)));

        tum.Should().Contain("FR.348(EK-2)").And.Contain("FR.521(EK-17)").And.Contain("İÇİNDEKİLER");
        tum.Should().Contain("He.").And.Contain("Dm2").And.Contain("6000/12000").And.Contain("101/1");
        tum.Should().Contain("0.4000000", "DOPO 7 haneli basılmalı");
    }

    [Fact]
    public async Task FenKlasoru_Ayni_Paket_Ayni_Pdf_Baytlarini_Uretmeli()
    {
        var p = await PaketAsync();
        var uretici = new QuestPdfFenKlasoruUretici();

        uretici.Uret(p).Value.Should().Equal(uretici.Uret(p).Value, "checksum tekrar üretilebilir olmalı");
    }

    [Fact]
    public async Task FenKlasoru_Kroki_Svg_Tum_Parselleri_Ve_Tescil_Numaralarini_Cizmeli()
    {
        var p = await PaketAsync();

        var svg = QuestPdfFenKlasoruUretici.KrokiSvg(p);

        svg.Split("<polygon").Length.Should().Be(1 + p.Parseller.Count);
        svg.Should().Contain(">101/1<").And.Contain(">12.00<").And.Contain(">8.00<");
    }

    // ---------------- CKS ----------------

    [Fact]
    public async Task Cks_Yazilan_Dosya_P4_Okuyucusuyla_Birebir_Geri_Okunmali()
    {
        var p = await PaketAsync();

        var bytes = new CksDosyaUretici().Uret(p);

        bytes.IsSuccess.Should().BeTrue();
        var okunan = new CksFileReader().Read(new MemoryStream(bytes.Value));
        okunan.IsSuccess.Should().BeTrue(okunan.IsFailure ? okunan.Error.TechnicalMessage : string.Empty);
        okunan.Value.Should().Be(CksOzetiEslemesi.Olustur(p.Dop).Value);

        // SORU-039 (proje sahibi kararı, 18 Kasım 2026): Netcad 1. satıra yazılan metnin üstüne çerçeve çizdiği için CKS'e
        // Pilot damgası yazılmaz; 1. satır gerçek Netcad dosyasındaki gibi boştur (eski beklenti: damga + RevizyonId).
        bytes.Value.Take(2).Should().Equal((byte)0x0D, (byte)0x0A);
    }

    [Fact]
    public void Cks_Sentetik_Pilot_1_Ornegi_Okunup_Yazilinca_Ayni_Degerleri_Vermeli()
    {
        var ornek = Imar18.TestDestek.TestDataLocator.Sentetik("pilot-1-concave-parcel", "DOP_Ornek.CKS");
        var bagimsiz = new CksFileReader().Read(File.OpenRead(ornek)).Value;
        var motor = CksOzetiEslemesi.Olustur(DopOzeti.From(DopCalculator.Hesapla(DopGirdisi.Olustur(8699.44m, 7620.54m).Value).Value)).Value;

        motor.Should().Be(bagimsiz, "motor eşlemesi Netcad örnek dosyasındaki değerlerle aynı olmalı");
        var yeniden = new CksFileReader().Read(new MemoryStream(CksDosyaUretici.Yaz(motor).Value)).Value;
        yeniden.Should().Be(bagimsiz);
    }

    [Fact]
    public void Cks_Hesap_Bolumu_Tutarsizsa_Dosya_Uretilmemeli()
    {
        var ornek = CksOzetiEslemesi.Olustur(DopOzeti.From(DopCalculator.Hesapla(DopGirdisi.Olustur(8699.44m, 7620.54m).Value).Value)).Value;

        var sonuc = CksDosyaUretici.Yaz(ornek with { KamuyaAyrilanAlanM2 = 1078.91m });

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiCadDisaAktarim);
    }

    // ---------------- Atomik yazıcı ----------------

    [Fact]
    public async Task AtomikCiktiYazici_Dosyayi_Yazmali_Ve_Silmeli()
    {
        var yol = Path.Combine(Path.GetTempPath(), "imar18-p5-cikti-" + Guid.NewGuid().ToString("N"), "a.pdf");
        var yazici = new AtomikCiktiYazici();

        await yazici.YazAsync(yol, new byte[] { 1, 2, 3 });
        (await File.ReadAllBytesAsync(yol)).Should().Equal(1, 2, 3);
        await yazici.YazAsync(yol, new byte[] { 4 });
        (await File.ReadAllBytesAsync(yol)).Should().Equal(4);
        await yazici.SilAsync(yol);

        File.Exists(yol).Should().BeFalse();
        Directory.Delete(Path.GetDirectoryName(yol)!, recursive: true);
    }

    private static string Bosluksuz(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
}

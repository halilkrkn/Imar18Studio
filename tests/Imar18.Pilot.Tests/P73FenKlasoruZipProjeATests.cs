using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Infrastructure.Cikti;
using Imar18.Pilot.Tests.Destek;
using Imar18.TestDestek;
using UglyToad.PdfPig;
using Xunit;

namespace Imar18.Pilot.Tests;

/// <summary>
/// P7.3-C1 (SAHA-007) Proje-A altın değerleri: Fen Klasörü .zip CommandBus/onay/.sha256/audit kapılarıyla üretilir; 3 imar
/// adası (101, 102, 103) için ada başına Tescil/Rölöve dosyaları ve birleşikleri; DOP, Özet ve Alan Hesabı belgelerinde altın
/// R1 = 8699.44, R2 = 7620.54, Kamuya Ayrılan = 1078.90, DOPO = 0.1240195, Tapu/Hesaplanan/Fark/Tecviz = 8699.44/8699.32/0.12/21.02;
/// Tescil Sayfası-1'de 101/1 = 0 | 1064 | 02 ve 101/2 = 0 | 0300 | 00.
/// </summary>
public sealed class P73FenKlasoruZipProjeATests
{
    private static string Bosluksuz(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static async Task<(PilotCalismaAlani Alan, CiktiSonucu Sonuc, Dictionary<string, byte[]> Dosyalar, List<string> Adlar)> UretAsync(
        CiktiSecenekleri? secenekler = null)
    {
        var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeA());
        (await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        var sonuc = await alan.Cikti.UretAsync(CiktiTuru.FenKlasoruZip, PilotCalismaAlani.Muhendis, alan.CiktiYolu("Imar18_PILOT_Fen_Klasoru.zip"), secenekler);
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        using var zip = new ZipArchive(File.OpenRead(sonuc.Value.DosyaYolu));
        var adlar = zip.Entries.Select(e => e.FullName).ToList();
        var dosyalar = zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        });
        return (alan, sonuc.Value, dosyalar, adlar);
    }

    private static string Metin(byte[] pdf)
    {
        using var d = PdfDocument.Open(pdf);
        return string.Join(" ", d.GetPages().SelectMany(s => s.GetWords()).Select(w => w.Text));
    }

    [Fact]
    public async Task ProjeA_FenKlasoruZip_Uc_Imar_Adasi_Icin_Ornek_Paket_Dosya_Duzeni_Uretilmeli()
    {
        var (alan, sonuc, _, adlar) = await UretAsync();
        using var _ = alan;

        adlar.Should().ContainInOrder(
            "DÖNÜŞÜM PARAMETRELERİ.pdf", "KOORDİNE ÖZET.pdf", "ALAN HESABI.pdf", "ALAN HESABI-1.pdf", "KOORDİNE-ALAN.pdf", "DOP.pdf",
            "ÖZET CETVELİ.pdf", "MAL SAHİPLERİ ARAŞTIRMA VE ÖZET FORMU.pdf", "TESCİLE ESAS DAĞITIM CETVELİ.pdf",
            "TESCİL SAYFASI-1.pdf", "TESCİL SAYFASI-2.pdf", "TESCİL SAYFASI-3.pdf", "TESCİL-1-2-3.pdf",
            "RÖLÖVE ÖLÇÜ KROKİSİ.pdf", "RÖLÖVE-1.pdf", "RÖLÖVE-2.pdf", "RÖLÖVE-3.pdf", "RÖLÖVE-1-2-3.pdf", "PARSELASYON HARİTASI.pdf", "PARSELASYON HARİTASI.NCZ",
            "DOP_PILOT.CKS");
        adlar.Should().NotContain(a => a.EndsWith(".sha256", StringComparison.Ordinal), "SORU-053: ZIP içinde varsayılan olarak yan özet yok");
        adlar.Count(a => a.EndsWith(".pdf", StringComparison.Ordinal)).Should().BeGreaterThanOrEqualTo(19, "örnek paket 19 PDF'tir");
        sonuc.ChecksumDosyasi.Should().BeNull();
        File.Exists(sonuc.DosyaYolu + ".sha256").Should().BeFalse("SORU-053: diskte yan .sha256 dosyası varsayılan olarak yazılmaz");
        sonuc.Sha256.Should().Be(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(sonuc.DosyaYolu))));
        alan.Yol.Denetim[^1].Should().Match<AuditKaydi>(k => k.Islem == "Cikti:FenKlasoruZip" && k.Sonuc == AuditSonucu.Basarili
            && k.SonrakiRevizyon == sonuc.Revizyon.Deger && k.Ayrinti!.Contains("sha256=" + sonuc.Sha256));
    }

    [Fact]
    public async Task ProjeA_FenKlasoruZip_Sha256_Yan_Dosyasi_Istenirse_Diskte_Ve_Zip_Icinde_Ozetler_Olmali_SORU_053()
    {
        var (alan, sonuc, d, adlar) = await UretAsync(new CiktiSecenekleri(Sha256YanDosyasiUret: true));
        using var _ = alan;

        adlar.Should().ContainInOrder("PARSELASYON HARİTASI.NCZ", "PARSELASYON HARİTASI.NCZ.sha256", "DOP_PILOT.CKS", "DOP_PILOT.CKS.sha256");
        Encoding.UTF8.GetString(d["DOP_PILOT.CKS.sha256"]).Should()
            .Be(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(d["DOP_PILOT.CKS"])) + "  DOP_PILOT.CKS\n");
        sonuc.ChecksumDosyasi.Should().Be(sonuc.DosyaYolu + ".sha256");
        (await File.ReadAllTextAsync(sonuc.ChecksumDosyasi!, Encoding.UTF8)).Should().Be(sonuc.Sha256 + "  Imar18_PILOT_Fen_Klasoru.zip\n");
        alan.Yol.Denetim[^1].Should().Match<AuditKaydi>(k => k.Islem == "Cikti:FenKlasoruZip" && k.Sonuc == AuditSonucu.Basarili
            && k.Ayrinti!.Contains("sha256=" + sonuc.Sha256) && k.Ayrinti.Contains("yanDosya=evet"));
    }

    [Fact]
    public async Task ProjeA_DOP_Ozet_Ve_Alan_Hesabi_Altin_Degerleri_Basmali()
    {
        var (alan, _, d, _) = await UretAsync();
        using var _ = alan;

        Bosluksuz(Metin(d["DOP.pdf"])).Should().Contain("0.1240195").And.Contain("8699.44").And.Contain("7620.54").And.Contain("=1078.90")
            .And.Contain("MaksimumDopOranı=0.4500000").And.Contain("DüzenlemeOrtaklıkPayı=1078.90/8699.44=0.1240195", "SAHA-017 Netcad DOP düzeni");
        Bosluksuz(Metin(d["ÖZET CETVELİ.pdf"])).Should().Contain("8699.44").And.Contain("7620.54").And.Contain("1078.90").And.Contain("0.1240195");
        var alanHesabi = Metin(d["ALAN HESABI.pdf"]);
        alanHesabi.Should().Contain("Tapu Alanı 8699.44").And.Contain("HESAPLANAN 8699.32").And.Contain("FARK 0.12").And.Contain("TECVİZ 21.02");
        alanHesabi.Should().Contain("1078.78 +0.12 1078.90", "901/Y1 yol terk dengelemesi (+0.12)");
        Bosluksuz(Metin(d["MAL SAHİPLERİ ARAŞTIRMA VE ÖZET FORMU.pdf"])).Should().Contain("TOPLAMADAALANI:7620.54").And.Contain("D.O.PAYIORANI:0.1240195");
    }

    [Fact]
    public async Task ProjeA_Tescil_Sayfasi_1_Alanlari_Sifir_Dolgulu_Olmali()
    {
        var (alan, _, d, _) = await UretAsync();
        using var _ = alan;

        var t1 = Metin(d["TESCİL SAYFASI-1.pdf"]);
        t1.Should().Contain("1 0 1064 02 ARSA").And.Contain("2 0 0300 00 ARSA").And.Contain("ADANO: 101");
        Metin(d["TESCİL SAYFASI-2.pdf"]).Should().Contain("ADANO: 102").And.Contain("0 0430 87");
        using var birlesik = PdfDocument.Open(d["TESCİL-1-2-3.pdf"]);
        birlesik.NumberOfPages.Should().Be(3);
    }

    [Fact]
    public async Task ProjeA_ZIP_Icindeki_DOP_CKS_Damgasiz_Netcad_Duzeninde_Olmali_SORU_039()
    {
        var (alan, _, d, _) = await UretAsync();
        using var _ = alan;

        var cks = d["DOP_PILOT.CKS"];
        cks.Take(2).Should().Equal((byte)0x0D, (byte)0x0A);
        Imar18.Infrastructure.Cikti.Cks.NetcadCksKodlayici.Coz(cks).Should().NotContain(PilotDamgasi.Metin).And.Contain("0.1240195");
    }

    [Fact]
    public async Task ProjeA_Onaysiz_Zip_Istegi_Reddedilip_Denetime_Yazilmali_Dosya_Olusmamali()
    {
        using var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeA());
        var yol = alan.CiktiYolu("onaysiz.zip");

        var sonuc = await alan.Cikti.UretAsync(CiktiTuru.FenKlasoruZip, PilotCalismaAlani.Muhendis, yol);

        sonuc.IsFailure.Should().BeTrue();
        File.Exists(yol).Should().BeFalse();
        alan.Yol.Denetim[^1].Should().Match<AuditKaydi>(k => k.Islem == "Cikti:FenKlasoruZip" && k.Sonuc == AuditSonucu.OnaysizGirisim);
    }
}

/// <summary>
/// P7.3-C1 RealData: mühendisin yerel örnek paketindeki (test-data/real/fen-klasoru-referans, gitignore'lu) PDF adlarının
/// tamamı Proje-A ZIP'inde bulunmalı. Örnekteki yazım hatası "SASHİPLERİ" proje sahibinin talimatındaki doğru adla
/// ("SAHİPLERİ") eşlenir; cepheli alan sayfa sayısı (ALAN HESABI-n) ve ada sayısına bağlı birleşik adlar veriye bağlı
/// olduğundan yalnız ilk sayfa/ada adları karşılaştırılır.
/// </summary>
[Trait("Category", "RealData")]
public sealed class P73FenKlasoruZipGercekReferansTests
{
    [GercekVeriFact]
    public async Task Gercek_Ornek_Paketin_Sabit_Dosya_Adlari_Uretilen_Zipte_Bulunmali()
    {
        var klasor = Path.Combine(TestDataLocator.GercekVeriKoku!, "fen-klasoru-referans");
        var referans = Directory.EnumerateFiles(klasor, "*.pdf").Select(Path.GetFileName)
            .Select(a => a!.Replace("SASHİPLERİ", "SAHİPLERİ", StringComparison.Ordinal))
            .Where(a => !a.StartsWith("ALAN HESABI-", StringComparison.Ordinal) && !a.StartsWith("RÖLÖVE-1-2", StringComparison.Ordinal))
            .ToList();
        using var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeA());
        (await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        var sonuc = await alan.Cikti.UretAsync(CiktiTuru.FenKlasoruZip, PilotCalismaAlani.Muhendis, alan.CiktiYolu("gercek.zip"));
        using var zip = new ZipArchive(File.OpenRead(sonuc.Value.DosyaYolu));

        referans.Should().NotBeEmpty();
        zip.Entries.Select(e => e.FullName).Should().Contain(referans);
    }
}

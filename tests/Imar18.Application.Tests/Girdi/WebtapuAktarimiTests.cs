using System.Numerics;
using FluentAssertions;
using Imar18.Application.Girdi;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Xunit;

namespace Imar18.Application.Tests.Girdi;

/// <summary>
/// P7.3-B1 (SAHA-006): webtapu tapu kaydının TAKBİS hisselerine ve yerel Malik Rehberi'ne aktarımı. Hesap motoru anonim malik
/// koduyla (M01, M02 …) çalışır; ad/baba adı/kişi no yalnız rehberde durur (RevisionId'ye ve denetim kaydına girmez —
/// SORU-040 c). Kroki satırlarına dokunulmaz (B2'nin işi). Adlar uydurmadır.
/// </summary>
public sealed class WebtapuAktarimiTests
{
    private static TapuMalikSatiri Malik(string sistem, string? kisi, string ad, string? baba, int pay, int payda, decimal m2, bool terkin = false) =>
        new(sistem, kisi, ad, baba, null, new BigInteger(pay), new BigInteger(payda), m2, 1000.00m, "Miras", terkin);

    private static TapuKaydi Kayit(string ada, string parsel, params TapuMalikSatiri[] malikler) =>
        new("100000001", "ÖRNEKİL", "ÖRNEKİLÇE", "ÇAMLIK Mah.", "Sırtlar", "12", "345", ada, parsel, 1000.00m, "Tarla", malikler);

    private static TapuKaydi Ornek() => Kayit("901", "7",
        Malik("900000101", "800000001", "AYŞE YILMAZ", "ALİ", 1, 2, 500.00m),
        Malik("900000102", "800000002", "MEHMET ÇAKIR", "ALİ", 1, 4, 250.00m),
        Malik("900000103", "800000003", "ŞÜKRÜ IŞIK", "HASAN", 1, 4, 250.00m));

    [Fact]
    public void WebtapuAktarimi_Yeni_Maliklere_Belge_Sirasiyla_M01_den_Baslayan_Anonim_Kod_Vermeli()
    {
        var a = WebtapuAktarici.Aktar(Ornek(), "K7", MalikRehberi.Bos);

        a.Hisseler.Select(h => h.MalikKodu).Should().Equal("M01", "M02", "M03");
        a.Hisseler.Select(h => (h.Pay, h.Payda)).Should().Equal((1, 2), (1, 4), (1, 4));
        a.Hisseler.Should().OnlyContain(h => !h.Muvafakatli, "webtapu muvafakat bilgisi içermez; varsayılan H (SORU-040 a)");
        a.Rehber.Malikler.Should().ContainSingle(r => r.MalikKodu == "M01" && r.AdSoyad == "AYŞE YILMAZ" && r.BabaAdi == "ALİ" && r.KisiSistemNo == "800000001");
    }

    [Fact]
    public void WebtapuAktarimi_Uretilen_Hisseler_TAKBIS_Dogrulayicisindan_Gecmeli()
    {
        var a = WebtapuAktarici.Aktar(Ornek(), "K7", MalikRehberi.Bos);

        TakbisGirdiDogrulayici.Dogrula(new TakbisParselGirdisi("K7", 1000.00m, a.Hisseler)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void WebtapuAktarimi_Ayni_Kisi_Ikinci_Parselde_Ayni_Kodu_Almali_Yeni_Kisi_Siradaki_Kodu_Almali()
    {
        var ilk = WebtapuAktarici.Aktar(Ornek(), "K7", MalikRehberi.Bos);
        var ikinciKayit = Kayit("901", "8",
            Malik("900000201", "800000003", "ŞÜKRÜ IŞIK", "HASAN", 1, 2, 500.00m),
            Malik("900000202", "800000009", "ZEYNEP ÖZ", "MUSA", 1, 2, 500.00m));

        var ikinci = WebtapuAktarici.Aktar(ikinciKayit, "K8", ilk.Rehber);

        ikinci.Hisseler.Select(h => h.MalikKodu).Should().Equal("M03", "M04");
        ikinci.Rehber.Malikler.Should().HaveCount(4);
        ikinci.Rehber.Parseller.Select(p => p.KadastroParselId).Should().BeEquivalentTo("K7", "K8");
    }

    [Fact]
    public void WebtapuAktarimi_Kisi_No_Yoksa_Ad_Ve_Baba_Adi_Esitliginde_Ayni_Kodu_Kullanmali()
    {
        var ilk = WebtapuAktarici.Aktar(Kayit("901", "7", Malik("1", null, "AYŞE YILMAZ", "ALİ", 1, 1, 1000.00m)), "K7", MalikRehberi.Bos);

        var ikinci = WebtapuAktarici.Aktar(Kayit("901", "8", Malik("2", null, "AYŞE  YILMAZ", "ALİ", 1, 1, 1000.00m)), "K8", ilk.Rehber);

        ikinci.Hisseler.Single().MalikKodu.Should().Be("M01");
    }

    [Fact]
    public void WebtapuAktarimi_Terkinli_Satirlari_Aktarmamali()
    {
        var k = Kayit("901", "7",
            Malik("900000101", "800000001", "AYŞE YILMAZ", "ALİ", 1, 1, 1000.00m),
            Malik("900000199", "800000099", "ESKİ MALİK", "VELİ", 1, 1, 1000.00m, terkin: true));

        var a = WebtapuAktarici.Aktar(k, "K7", MalikRehberi.Bos);

        a.Hisseler.Should().ContainSingle();
        a.Rehber.Malikler.Should().ContainSingle();
    }

    [Fact]
    public void WebtapuAktarimi_Parsel_Kaydina_Cilt_Sayfa_Il_Ilce_Mahalle_Yazmali_Ve_Tekrar_Aktarimda_Guncellemeli()
    {
        var ilk = WebtapuAktarici.Aktar(Ornek(), "K7", MalikRehberi.Bos);
        var p = ilk.Rehber.Parseller.Single();
        p.Should().BeEquivalentTo(new { KadastroParselId = "K7", Cilt = "12", Sayfa = "345", Il = "ÖRNEKİL", Ilce = "ÖRNEKİLÇE", Mahalle = "ÇAMLIK Mah.", Ada = "901", Parsel = "7", AtYuzolcumM2 = 1000.00m });

        var yeniden = WebtapuAktarici.Aktar(Ornek() with { Cilt = "13" }, "K7", ilk.Rehber);

        yeniden.Rehber.Parseller.Should().ContainSingle().Which.Cilt.Should().Be("13");
        yeniden.Rehber.Malikler.Should().HaveCount(3, "aynı kişiler yeniden eklenmez");
    }

    [Fact]
    public void WebtapuAktarimi_Mevcut_Rehberdeki_En_Buyuk_Koddan_Devam_Etmeli()
    {
        var rehber = new MalikRehberi([new MalikRehberKaydi("M07", "VAR OLAN", "X", "811111111")], []);

        WebtapuAktarici.Aktar(Ornek(), "K7", rehber).Hisseler.Select(h => h.MalikKodu).Should().Equal("M08", "M09", "M10");
    }

    [Fact]
    public void TakbisMetni_Ayni_Parselin_Eski_Satirlarini_Degistirmeli_Digerlerini_Ve_Yorumlari_Korumali()
    {
        const string Mevcut = "# not\nK7;ESKI1;1/1;H\nK9;M50;1/1;E\nK7;ESKI2;0/1;H";
        var a = WebtapuAktarici.Aktar(Ornek(), "K7", MalikRehberi.Bos);

        var yeni = WebtapuAktarici.TakbisMetniniGuncelle(Mevcut, "K7", a.Hisseler);

        yeni.Split('\n').Select(s => s.TrimEnd('\r')).Should().Equal("# not", "K9;M50;1/1;E", "K7;M01;1/2;H", "K7;M02;1/4;H", "K7;M03;1/4;H");
    }

    [Theory]
    [InlineData(new[] { "K1", "901/7", "901/8" }, "901/7")]
    [InlineData(new[] { "436/7", "7", "17" }, "7")]
    [InlineData(new[] { "K1", "A/7", "17" }, "A/7")]
    [InlineData(new[] { "K1", "K2" }, null)]
    public void KadastroParseliOner_Ada_Parsel_Sonra_Parsel_No_Sonra_Bolu_Parsel_Ile_Eslemeli(string[] proje, string? beklenen)
    {
        WebtapuAktarici.KadastroParseliOner(Ornek(), proje).Should().Be(beklenen);
    }

    // ---------------- Malik Rehberi komutu (CommandBus) ----------------

    [Fact]
    public async Task MalikRehberiAyarla_Rehberi_Yazmali_RevisionId_Ve_Onayi_Degistirmemeli_Denetime_Ad_Yazmamali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        var rev = yol.Belge.RevizyonKimligi();
        var rehber = WebtapuAktarici.Aktar(Ornek(), "K1", MalikRehberi.Bos).Rehber;

        var sonuc = await yol.GonderAsync(new MalikRehberiAyarlaKomutu(rehber), OrnekProje.Muhendis);

        sonuc.IsSuccess.Should().BeTrue();
        yol.Belge.MalikRehberi.Should().BeEquivalentTo(rehber);
        yol.Belge.RevizyonKimligi().Should().Be(rev, "ad/baba adı hesabı değiştirmez (SORU-040 c)");
        yol.Belge.GecerliOnayVar(rev).Should().BeTrue("rehber değişikliği onayı düşürmez");
        var kayit = yol.Denetim[^1];
        kayit.Islem.Should().Be("MalikRehberiAyarla");
        kayit.Ayrinti.Should().Be("malik=3 parsel=1");
        string.Join("|", yol.Denetim.Select(d => d.Ayrinti)).Should().NotContain("AYŞE").And.NotContain("800000001");
    }

    [Theory]
    [InlineData("M01", "M01", "AYŞE", "duplicate")]
    [InlineData("AYŞE YILMAZ", "M02", "AYŞE", "anonymous")]
    [InlineData("M01", "M02", " ", "name")]
    public async Task MalikRehberiAyarla_Gecersiz_Rehberi_Reddetmeli_Ve_Ret_Kaydi_Yazmali(string kod1, string kod2, string ad2, string neden)
    {
        var (yol, _, _) = await OrnekProje.TamProjeAsync();
        var rehber = new MalikRehberi([new MalikRehberKaydi(kod1, "AYŞE YILMAZ", "ALİ", null), new MalikRehberKaydi(kod2, ad2, null, null)], []);

        var sonuc = await yol.GonderAsync(new MalikRehberiAyarlaKomutu(rehber), OrnekProje.Muhendis);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.GirdProjeGirdisiGecersiz);
        sonuc.Error.TechnicalMessage.Should().Contain(neden).And.NotContain("AYŞE");
        yol.Belge.MalikRehberi.Malikler.Should().BeEmpty();
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Reddedildi);
    }

    [Fact]
    public async Task ProjeyiSifirla_Malik_Rehberini_De_Bosaltmali()
    {
        var (yol, _, _) = await OrnekProje.TamProjeAsync();
        await yol.GonderAsync(new MalikRehberiAyarlaKomutu(WebtapuAktarici.Aktar(Ornek(), "K1", MalikRehberi.Bos).Rehber), OrnekProje.Muhendis);

        await yol.GonderAsync(new ProjeyiSifirlaKomutu(), OrnekProje.Muhendis);

        yol.Belge.MalikRehberi.Should().BeEquivalentTo(MalikRehberi.Bos);
    }

    [Fact]
    public void MalikRehberi_Kod_Ile_Bulmali_Bilinmeyende_Null_Vermeli()
    {
        var r = WebtapuAktarici.Aktar(Ornek(), "K7", MalikRehberi.Bos).Rehber;

        r.Bul("M02")!.AdSoyad.Should().Be("MEHMET ÇAKIR");
        r.Bul("M99").Should().BeNull();
        r.Parsel("K7")!.Cilt.Should().Be("12");
        r.Parsel("K0").Should().BeNull();
    }
}

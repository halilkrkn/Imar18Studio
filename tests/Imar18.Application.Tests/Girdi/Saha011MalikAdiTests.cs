using System.Numerics;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Girdi;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Imar18.GeometryEngine.Topoloji;
using Xunit;

namespace Imar18.Application.Tests.Girdi;

/// <summary>
/// SAHA-011 / SORU-055 (proje sahibi kararı): yerel ekranlarda malik <c>AD SOYAD (BABA ADI)</c> biçiminde görünür ve TAKBİS'te
/// kabul edilir; eski anonim kodlar (<c>M01</c>, <c>Malik-01</c>) geriye dönük geçerlidir. KVKK sınırı korunur: 11 haneli
/// T.C. Kimlik No (ayırıcılı biçimler ve ad içine gömülü olanlar dahil) reddedilir; hata mesajı ve denetim kaydı ad/numara
/// taşımaz. Tüm adlar uydurmadır (sentetik).
/// </summary>
public sealed class Saha011MalikAdiTests
{
    private static TakbisParselGirdisi Parsel(params TakbisHisseGirdisi[] h) => new("K1", 200.00m, h);

    private static TakbisHisseGirdisi H(string kod, int pay, int payda) => new(kod, pay, payda, false);

    // ============================ MalikEtiketi ============================

    [Theory]
    [InlineData("AYŞE ÖRNEKOĞLU", "ALİ", "AYŞE ÖRNEKOĞLU (ALİ)")]
    [InlineData("ayşe   örnekoğlu ", "ali", "AYŞE ÖRNEKOĞLU (ALİ)")]
    [InlineData("ŞÜKRÜ IŞIKTAŞ", null, "ŞÜKRÜ IŞIKTAŞ")]
    [InlineData("ŞÜKRÜ IŞIKTAŞ", "  ", "ŞÜKRÜ IŞIKTAŞ")]
    public void MalikEtiketi_Ad_Ve_Baba_Adindan_Turkce_Buyuk_Harf_Etiket_Uretmeli(string ad, string? baba, string beklenen)
    {
        MalikEtiketi.Olustur(ad, baba).Should().Be(beklenen);
    }

    [Theory]
    [InlineData("AYŞE ÖRNEKOĞLU (ALİ)", "AYŞE ÖRNEKOĞLU", "ALİ")]
    [InlineData("AYŞE ÖRNEKOĞLU / ALİ", "AYŞE ÖRNEKOĞLU", "ALİ")]
    [InlineData("AYŞE ÖRNEKOĞLU (ALİ) #2", "AYŞE ÖRNEKOĞLU", "ALİ")]
    [InlineData("ŞÜKRÜ IŞIKTAŞ", "ŞÜKRÜ IŞIKTAŞ", null)]
    [InlineData("M01", "M01", null)]
    public void MalikEtiketi_Etiketi_Ad_Soyad_Ve_Baba_Adina_Ayirmali(string etiket, string ad, string? baba)
    {
        MalikEtiketi.Ayristir(etiket).Should().Be((ad, baba));
    }

    [Theory]
    [InlineData("Ayşe Örnekoğlu (Ali)", "AYŞE ÖRNEKOĞLU (ALİ)")]
    [InlineData("AYŞE ÖRNEKOĞLU / ALİ", "AYŞE ÖRNEKOĞLU (ALİ)")]
    [InlineData("ayşe örnekoğlu(ali)", "AYŞE ÖRNEKOĞLU (ALİ)")]
    [InlineData("m01", "M01")]
    [InlineData("MALIK-01", "Malik-01")] // reviewer M3: NCZ'deki ASCII büyük harf etiket
    [InlineData("AYSE ORNEKOGLU (ALI)", "AYSE ORNEKOGLU (ali)")]
    public void MalikEtiketi_Anahtar_Yazim_Farklarindan_Bagimsiz_Olmali(string a, string b)
    {
        MalikEtiketi.Anahtar(a).Should().Be(MalikEtiketi.Anahtar(b));
    }

    // ============================ TAKBİS doğrulayıcı ============================

    [Theory]
    [InlineData("AYŞE ÖRNEKOĞLU (ALİ)")]
    [InlineData("AYŞE ÖRNEKOĞLU / ALİ")]
    [InlineData("Ayşe Örnekoğlu")]
    [InlineData("ÖRNEK YAPI A.Ş.")]
    [InlineData("M01")]
    [InlineData("Malik-01")]
    [InlineData("Malik 01")]
    public void Takbis_Ad_Soyad_Baba_Adi_Ve_Eski_Kodlari_Kabul_Etmeli(string kod)
    {
        var r = TakbisGirdiDogrulayici.Dogrula(Parsel(H(kod, 1, 1)));

        r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.TechnicalMessage : string.Empty);
        r.Value.Malikler.Single().MalikId.Should().Be(kod.Trim(), "malik kimliği yazıldığı gibi korunur (eski M01 projeleri bozulmaz)");
    }

    [Theory]
    [InlineData("12345678901")]
    [InlineData("123 456 789 01")]
    [InlineData("123-456-789-01")]
    [InlineData("123.456.789.01")]
    [InlineData("AYŞE ÖRNEKOĞLU 12345678901")]
    [InlineData("AYŞE ÖRNEKOĞLU (ALİ) 123 456 789 01")]
    [InlineData("TCKN:12345678901")]
    [InlineData("123/456/789/01")] // reviewer H1: her ayırıcı ve yapışık biçim
    [InlineData("123 - 456 - 789 - 01")]
    [InlineData("AYŞE (123,456,789,01)")]
    [InlineData("123  45678901")]
    [InlineData("M0112345678901")]
    public void Takbis_11_Haneli_TC_Kimlik_No_Iceren_Malik_Alanini_KVKK_Geregi_Reddetmeli_Ve_Sizdirmamali(string kod)
    {
        var hata = TakbisGirdiDogrulayici.Dogrula(Parsel(H(kod, 1, 1))).Error;

        hata.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        hata.TechnicalMessage.Should().Contain("KVKK").And.NotContain(kod).And.NotContain("12345678901").And.NotContain("ÖRNEKOĞLU");
        hata.UserMessage.Should().NotContain("ÖRNEKOĞLU");
    }

    [Theory]
    [InlineData("#AYŞE ÖRNEKOĞLU")]
    [InlineData("AYŞE;ÖRNEKOĞLU")]
    [InlineData("AYŞE‮ÖRNEKOĞLU")]
    [InlineData("AYŞE\u0001ÖRNEKOĞLU")]
    [InlineData("AYŞE\u001FÖRNEKOĞLU")]
    public void Takbis_Yorum_Ayirici_Ve_Denetim_Karakterli_Malik_Alanini_Reddetmeli(string kod)
    {
        var hata = TakbisGirdiDogrulayici.Dogrula(Parsel(H(kod, 1, 1))).Error;

        hata.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        hata.TechnicalMessage.Should().NotContain("ÖRNEKOĞLU");
    }

    [Fact]
    public void Takbis_Cok_Uzun_Malik_Alanini_Reddetmeli()
    {
        TakbisGirdiDogrulayici.Dogrula(Parsel(H(new string('A', MalikEtiketi.EnFazlaUzunluk + 1), 1, 1))).IsFailure.Should().BeTrue();
        TakbisGirdiDogrulayici.Dogrula(Parsel(H(new string('A', MalikEtiketi.EnFazlaUzunluk), 1, 1))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Takbis_Yazim_Farkli_Ayni_Malik_Ayni_Parselde_Yinelenen_Sayilmali()
    {
        TakbisGirdiDogrulayici.Dogrula(Parsel(H("AYŞE ÖRNEKOĞLU (ALİ)", 1, 2), H("ayşe örnekoğlu / ali", 1, 2))).Error.TechnicalMessage
            .Should().Contain("duplicate").And.NotContain("ÖRNEKOĞLU");
    }

    [Fact]
    public void Kroki_Dogrulayici_Yazim_Farkli_Ad_Etiketini_TAKBIS_Kimligine_Eslemeli()
    {
        var kadastro = TakbisGirdiDogrulayici.Dogrula(Parsel(H("AYŞE ÖRNEKOĞLU (ALİ)", 1, 2), H("M02", 1, 2))).Value;

        var r = KrokiGirdiDogrulayici.Dogrula(
            [new KrokiEslesmeGirdisi("Ayşe Örnekoğlu / Ali", "K1", "A1", 100.00m), new KrokiEslesmeGirdisi("m02", "K1", "A1", 20.00m)], [kadastro]);

        r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.TechnicalMessage : string.Empty);
        r.Value.Select(s => s.MalikId).Should().Equal(["AYŞE ÖRNEKOĞLU (ALİ)", "M02"], "Domain'e TAKBİS'teki kanonik kimlik gider");
    }

    [Fact]
    public void Takbis_Farkli_Parsellerde_Yazim_Farkli_Ayni_Malik_Tek_Malik_Sayilmali()
    {
        // Reviewer H2: Domain malikleri Ordinal gruplar; yazım farkı iki kişi sayılmamalı (ilk görülen yazım kanoniktir).
        var k1 = TakbisGirdiDogrulayici.Dogrula(new TakbisParselGirdisi("K1", 200.00m, [H("Ayşe Örnekoğlu (Ali)", 1, 1)])).Value;
        var k2 = TakbisGirdiDogrulayici.Dogrula(new TakbisParselGirdisi("K2", 100.00m, [H("AYŞE ÖRNEKOĞLU / ALİ", 1, 2), H("M02", 1, 2)])).Value;

        var birlesik = TakbisMalikKimlikleri.Birlestir([k1, k2]);

        birlesik.SelectMany(p => p.Malikler).Select(m => m.MalikId).Should().Equal("Ayşe Örnekoğlu (Ali)", "Ayşe Örnekoğlu (Ali)", "M02");
        birlesik[1].Malikler[0].Hisse.Should().Be(new Fraction(1, 2), "hisse değişmez");
    }

    // ============================ webtapu aktarımı ============================

    private static TapuMalikSatiri Malik(string sistem, string? kisi, string ad, string? baba, int pay, int payda, decimal m2) =>
        new(sistem, kisi, ad, baba, null, new BigInteger(pay), new BigInteger(payda), m2, 1000.00m, "Miras", false);

    private static TapuKaydi Kayit(string parsel, params TapuMalikSatiri[] malikler) =>
        new("100000001", "ÖRNEKİL", "ÖRNEKİLÇE", "ÇAMLIK Mah.", "Sırtlar", "12", "345", "901", parsel, 1000.00m, "Tarla", malikler);

    private static TapuKaydi Ornek() => Kayit("7",
        Malik("900000101", "800000001", "AYŞE ÖRNEKOĞLU", "ALİ", 1, 2, 500.00m),
        Malik("900000102", "800000002", "MEHMET ÇAKIRTEPE", null, 1, 4, 250.00m),
        Malik("900000103", "800000003", "ŞÜKRÜ IŞIKTAŞ", "HASAN", 1, 4, 250.00m));

    [Fact]
    public void Webtapu_Aktarimi_TAKBIS_Hisselerine_Ad_Soyad_Baba_Adi_Etiketi_Yazmali()
    {
        var a = WebtapuAktarici.Aktar(Ornek(), "K7", MalikRehberi.Bos);

        a.Hisseler.Select(h => h.MalikKodu).Should().Equal("AYŞE ÖRNEKOĞLU (ALİ)", "MEHMET ÇAKIRTEPE", "ŞÜKRÜ IŞIKTAŞ (HASAN)");
        a.Rehber.Bul("AYŞE ÖRNEKOĞLU (ALİ)").Should().Be(new MalikRehberKaydi("AYŞE ÖRNEKOĞLU (ALİ)", "AYŞE ÖRNEKOĞLU", "ALİ", "800000001"),
            "Malik Rehberi ad ve baba adını ayrı alanlarda tutmaya devam eder (Fen Klasörü 'Maliki' / 'Babası')");
        WebtapuAktarici.TakbisMetniniGuncelle(string.Empty, "K7", a.Hisseler).Split(Environment.NewLine)[0]
            .Should().Be("K7;AYŞE ÖRNEKOĞLU (ALİ);1/2;H");
        TakbisGirdiDogrulayici.Dogrula(new TakbisParselGirdisi("K7", 1000.00m, a.Hisseler)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Webtapu_Aktarimi_Rehberdeki_Eski_Kodlu_Kisi_Kodunu_Korumali()
    {
        var rehber = new MalikRehberi([new MalikRehberKaydi("M01", "AYŞE ÖRNEKOĞLU", "ALİ", "800000001")], []);

        var a = WebtapuAktarici.Aktar(Ornek(), "K7", rehber);

        a.Hisseler[0].MalikKodu.Should().Be("M01", "aynı kişinin diğer parsellerdeki hisse/kroki satırları bozulmaz");
        a.Hisseler[1].MalikKodu.Should().Be("MEHMET ÇAKIRTEPE");
    }

    [Fact]
    public void Webtapu_Aktarimi_Ayni_Ad_Baba_Adli_Farkli_Kisiyi_Ek_Ile_Ayirmali()
    {
        var ilk = WebtapuAktarici.Aktar(Ornek(), "K7", MalikRehberi.Bos);
        var adas = Kayit("8", Malik("900000201", "800000099", "AYŞE ÖRNEKOĞLU", "ALİ", 1, 1, 1000.00m));

        var ikinci = WebtapuAktarici.Aktar(adas, "K8", ilk.Rehber);

        ikinci.Hisseler.Single().MalikKodu.Should().Be("AYŞE ÖRNEKOĞLU (ALİ) #2", "kişi sistem no farklı: başka kişi");
        ikinci.Rehber.Bul("AYŞE ÖRNEKOĞLU (ALİ) #2")!.KisiSistemNo.Should().Be("800000099");
    }

    [Fact]
    public void Webtapu_Aktarimi_Elle_Girilmis_Ayni_Ad_Etiketini_Yeniden_Kullanmali()
    {
        var a = WebtapuAktarici.Aktar(Ornek(), "K7", MalikRehberi.Bos, ["Ayşe Örnekoğlu (Ali)"]);

        a.Hisseler[0].MalikKodu.Should().Be("Ayşe Örnekoğlu (Ali)", "elle yazılmış aynı etiket aynı kişidir; hak eşleştirme bağı kopmaz");
        a.Rehber.Bul("Ayşe Örnekoğlu (Ali)")!.AdSoyad.Should().Be("AYŞE ÖRNEKOĞLU");
    }

    [Fact]
    public void Webtapu_Aktarimi_TCKN_Iceren_Ad_Icin_Anonim_Koda_Dusmeli()
    {
        var kayit = Kayit("7", Malik("900000101", "800000001", "AYŞE 12345678901", null, 1, 1, 1000.00m));

        var a = WebtapuAktarici.Aktar(kayit, "K7", MalikRehberi.Bos);

        a.Hisseler.Single().MalikKodu.Should().Be("M01", "kimlik numarası etikete yazılamaz (KVKK)");
    }

    [Fact]
    public async Task Malik_Rehberi_Komutu_Ad_Etiketli_Kodu_Kabul_Etmeli_Denetime_Ad_Yazmamali_TCKN_Reddetmeli()
    {
        var (yol, _, _) = await OrnekProje.TamProjeAsync();
        var a = WebtapuAktarici.Aktar(Ornek(), "K1", MalikRehberi.Bos);

        (await yol.GonderAsync(new MalikRehberiAyarlaKomutu(a.Rehber), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        var tckn = new MalikRehberi([new MalikRehberKaydi("AYŞE 12345678901", "AYŞE", null, null)], []);
        (await yol.GonderAsync(new MalikRehberiAyarlaKomutu(tckn), OrnekProje.Muhendis)).IsFailure.Should().BeTrue();

        yol.Denetim.Should().NotContain(k => (k.Ayrinti ?? string.Empty).Contains("ÖRNEKOĞLU", StringComparison.Ordinal)
            || (k.Ayrinti ?? string.Empty).Contains("12345678901", StringComparison.Ordinal), "denetim kaydına ad / kimlik no yazılmaz");
    }

    // ============================ Yaklaşık Yer Krokisi ============================

    private static GeodeticPoint P(decimal x, decimal y) => new(X: 4_400_000m + x, Y: 500_000m + y);

    private static IReadOnlyList<GeodeticPoint> Serit(decimal y1, decimal y2) => [P(0, y1), P(0, y2), P(10, y2), P(10, y1)];

    private static CadPolyline Kullanim(string etiket, decimal y1, decimal y2)
    {
        var h = Serit(y1, y2).ToList();
        h.Add(h[0]);
        return new CadPolyline("KULLANIM", etiket, null, h, true, null);
    }

    [Theory]
    [InlineData("AYŞE ÖRNEKOĞLU (ALİ)")]
    [InlineData("ayşe örnekoğlu")]
    [InlineData("AYŞE ÖRNE")]
    [InlineData("M01")]
    public void Kroki_Eslestirici_Ad_Etiketli_TAKBIS_Malikini_Rehbersiz_De_Cozmeli_Ve_Ayni_Etiketle_Aktarmali(string etiket)
    {
        // TAKBİS'te biri ad etiketli, biri eski kodlu iki malik; rehber boş (elle girilmiş etiketler).
        var takbis = new[]
        {
            new KadastroParseli("K1", 200.00m, [new KadastroMaliki(etiket == "M01" ? "M01" : "AYŞE ÖRNEKOĞLU (ALİ)", new Fraction(1, 2), false),
                new KadastroMaliki("MEHMET ÇAKIRTEPE", new Fraction(1, 2), false)]),
        };
        var beklenen = etiket == "M01" ? "M01" : "AYŞE ÖRNEKOĞLU (ALİ)";
        var girdi = new KrokiEslestirmeGirdisi(
            new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "KULLANIM")], [Kullanim(etiket, 0, 10), Kullanim("Mehmet Çakırtepe", 10, 20)], [], 0, 0, 0, []),
            [new ParselGeometrisi("K1", ParselRolu.Kadastro, Serit(0, 20)), new ParselGeometrisi("A1", ParselRolu.Imar, Serit(0, 20))],
            [new ImarParseli("A1", 200.00m)],
            takbis,
            MalikRehberi.Bos,
            null);

        var s = YaklasikYerKrokisiEslestirici.Eslestir(girdi, new NtsParselKesisimHesaplayici());

        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.TechnicalMessage : string.Empty);
        s.Value.CozulemeyenPoligon.Should().Be(0);
        s.Value.Satirlar.Select(r => (r.MalikKodu, r.AlanM2)).Should().Equal((beklenen, 100.00m), ("MEHMET ÇAKIRTEPE", 100.00m));
        s.Value.KrokiMetni().Should().Contain(beklenen + ";K1;A1;100.00");
        s.Value.Uyarilar.Should().NotContain(u => u.Contains("ÖRNEKOĞLU", StringComparison.Ordinal) || u.Contains("ÇAKIRTEPE", StringComparison.Ordinal));
    }

    [Fact]
    public void Kroki_Eslestirici_Ascii_Buyuk_Harf_Etiket_Eski_Kodla_Eslesmeli()
    {
        // Reviewer M3: NCZ etiketi "MALIK-01" (ASCII I) ↔ TAKBİS "Malik-01".
        var girdi = new KrokiEslestirmeGirdisi(
            new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "KULLANIM")], [Kullanim("MALIK-01", 0, 10)], [], 0, 0, 0, []),
            [new ParselGeometrisi("K1", ParselRolu.Kadastro, Serit(0, 20)), new ParselGeometrisi("A1", ParselRolu.Imar, Serit(0, 20))],
            [new ImarParseli("A1", 200.00m)],
            [new KadastroParseli("K1", 200.00m, [new KadastroMaliki("Malik-01", new Fraction(1, 2), false), new KadastroMaliki("Malik-02", new Fraction(1, 2), false)])],
            MalikRehberi.Bos,
            null);

        var s = YaklasikYerKrokisiEslestirici.Eslestir(girdi, new NtsParselKesisimHesaplayici()).Value;

        s.CozulemeyenPoligon.Should().Be(0);
        s.Satirlar.Select(r => (r.MalikKodu, r.AlanM2)).Should().Equal(("Malik-01", 150.00m), ("Malik-02", 50.00m));
    }
}

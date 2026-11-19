using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Imar18.Application.Girdi;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Revizyon;
using Xunit;

namespace Imar18.Application.Tests.Girdi;

/// <summary>
/// SORU-046 (proje sahibi kararı, 19 Kasım 2026): Malik Rehberi doluysa içeriğinin SHA-256 özeti RevisionId'ye girer; boşsa
/// eski projelerin RevisionId'si değişmez. Özet kayıt sırasından bağımsızdır; ad, baba adı, kişi no, Cilt/Sayfa ve tapu alanları
/// değişince değişir. Denetim kaydına kişi adı yazılmaz.
/// </summary>
public sealed class MalikRehberiRevizyonTests
{
    private static readonly MalikRehberKaydi Ayse = new("M01", "AYŞE ÖRNEKSOY", "İSMAİL", "800000001");
    private static readonly MalikRehberKaydi Cagri = new("M02", "ÇAĞRI DENEMEOĞLU", null, null);
    private static readonly KadastroTapuKaydi K1 = new("K1", "14", "0", "SENTETİKİL", "ÖRNEKİLÇE", "DENEME", "901", "7", 200.00m);
    private static readonly KadastroTapuKaydi K2 = new("K2", "15", "3", null, null, null, "901", "8", null);

    private static RevisionId Rev(MalikRehberi r) => (ProjeBelgesi.Yeni("Sentetik") with { MalikRehberi = r }).RevizyonKimligi();

    [Fact]
    public void Bos_Rehber_Eski_RevisionIdyi_Korumali()
    {
        var eski = ProjeRevizyonu.Hesapla(new ProjeRevizyonGirdisi("Sentetik", null, [], null, [], null, null, []));

        Rev(MalikRehberi.Bos).Should().Be(eski, "P7.3-C öncesi rehbersiz projelerin onayı düşmemeli");
        Rev(new MalikRehberi([], [])).Should().Be(eski);
        MalikRehberi.Bos.Ozet().Should().BeNull();
    }

    [Fact]
    public void Dolu_Rehber_RevisionIdyi_Degistirmeli_Ozet_64_Hex_Olmali()
    {
        var r = new MalikRehberi([Ayse], []);

        Rev(r).Should().NotBe(Rev(MalikRehberi.Bos));
        r.Ozet().Should().MatchRegex("^[0-9a-f]{64}$");
        new MalikRehberi([], [K1]).Ozet().Should().NotBeNull("yalnız tapu kaydı da (Cilt/Sayfa) çıktıya basılır");
    }

    [Fact]
    public void Ozet_Kayit_Sirasindan_Bagimsiz_Ve_Tekrar_Edilebilir_Olmali()
    {
        var a = new MalikRehberi([Ayse, Cagri], [K1, K2]);
        var b = new MalikRehberi([Cagri, Ayse], [K2, K1]);

        a.Ozet().Should().Be(b.Ozet());
        Rev(a).Should().Be(Rev(b));
        a.Ozet().Should().Be(new MalikRehberi([Ayse, Cagri], [K1, K2]).Ozet());
    }

    [Theory]
    [InlineData("ad")]
    [InlineData("baba")]
    [InlineData("babaNull")]
    [InlineData("kisiNo")]
    [InlineData("kod")]
    [InlineData("cilt")]
    [InlineData("sayfa")]
    [InlineData("mahalle")]
    [InlineData("at")]
    [InlineData("parselEkle")]
    public void Her_Basilan_Alan_Degisince_Ozet_Ve_RevisionId_Degismeli(string alan)
    {
        var temel = new MalikRehberi([Ayse, Cagri], [K1, K2]);
        var degisik = alan switch
        {
            "ad" => temel with { Malikler = [Ayse with { AdSoyad = "AYŞE ÖRNEKSOYY" }, Cagri] },
            "baba" => temel with { Malikler = [Ayse with { BabaAdi = "İSMAİLL" }, Cagri] },
            "babaNull" => temel with { Malikler = [Ayse, Cagri with { BabaAdi = string.Empty }] },
            "kisiNo" => temel with { Malikler = [Ayse with { KisiSistemNo = "800000002" }, Cagri] },
            "kod" => temel with { Malikler = [Ayse with { MalikKodu = "M03" }, Cagri] },
            "cilt" => temel with { Parseller = [K1 with { Cilt = "41" }, K2] },
            "sayfa" => temel with { Parseller = [K1 with { Sayfa = "1" }, K2] },
            "mahalle" => temel with { Parseller = [K1 with { Mahalle = "DENEME2" }, K2] },
            "at" => temel with { Parseller = [K1 with { AtYuzolcumM2 = 200.01m }, K2] },
            _ => temel with { Parseller = [K1, K2, K2 with { KadastroParselId = "K3" }] },
        };

        degisik.Ozet().Should().NotBe(temel.Ozet(), alan);
        Rev(degisik).Should().NotBe(Rev(temel), alan);
    }

    [Fact]
    public void Ozet_Alan_Sinirlarini_Kaydirarak_Ayni_Ozeti_Uretmemeli()
    {
        // Ayırıcı enjeksiyonu: "AB" + "C" ile "A" + "BC" aynı özeti vermemeli.
        var a = new MalikRehberi([new MalikRehberKaydi("M01", "AB", "C", null)], []);
        var b = new MalikRehberi([new MalikRehberKaydi("M01", "A", "BC", null)], []);

        a.Ozet().Should().NotBe(b.Ozet());
    }

    [Fact]
    public void Ondalik_Olcegi_Ozeti_Degistirmemeli()
    {
        new MalikRehberi([], [K1 with { AtYuzolcumM2 = 200.0m }]).Ozet().Should().Be(new MalikRehberi([], [K1 with { AtYuzolcumM2 = 200.00m }]).Ozet());
    }

    [Fact]
    public async Task Onayli_Projede_Rehber_Degisince_Onay_Dusmeli_Denetimde_Ad_Ve_Ozet_Olmamali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var rehber = new MalikRehberi([Ayse, Cagri], [K1]);
        (await yol.GonderAsync(new MalikRehberiAyarlaKomutu(rehber), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        yol.Belge.GecerliOnayVar(yol.Revizyon).Should().BeTrue();

        (await yol.GonderAsync(new MalikRehberiAyarlaKomutu(rehber with { Parseller = [K1 with { Cilt = "41" }] }), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        yol.Belge.GecerliOnayVar(yol.Revizyon).Should().BeFalse("Cilt değişikliği basılan çıktıyı değiştirir (SORU-046)");
        var denetim = string.Join("|", yol.Denetim.Select(k => k.Ayrinti + ";" + k.HataKodu));
        denetim.Should().NotContain("AYŞE").And.NotContain("ÖRNEKSOY").And.NotContain("İSMAİL").And.NotContain("ÇAĞRI").And.NotContain("800000001");
        denetim.Should().NotContain(rehber.Ozet()!, "rehber özeti ayrıca denetime yazılmaz; yalnız RevisionId'nin girdisidir");
    }

    [Fact]
    public async Task Ayni_Rehber_Yeniden_Yazilinca_Onay_Korunmali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var rehber = new MalikRehberi([Ayse], [K1]);
        (await yol.GonderAsync(new MalikRehberiAyarlaKomutu(rehber), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        (await yol.GonderAsync(new MalikRehberiAyarlaKomutu(new MalikRehberi([Ayse], [K1 with { AtYuzolcumM2 = 200.0m }])), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        yol.Belge.GecerliOnayVar(yol.Revizyon).Should().BeTrue("içerik aynı (yalnız ondalık ölçeği farklı)");
    }

    [Fact]
    public void Ozet_Bagimsiz_SHA256_Referansiyla_Eslesmeli()
    {
        // Bağımsız referans: belgelenmiş kanonik biçim — "m" kayıtları kod sırasıyla, "p" kayıtları parsel sırasıyla; her alan
        // uzunluk önekli ("<uzunluk>:<değer>", null → "-"), ondalık sadeleştirilmiş kesir; UTF-8, SHA-256 küçük hex.
        var r = new MalikRehberi([Cagri, Ayse], [K2]);
        static string A(string? v) => v is null ? "-" : v.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + v;
        var kanonik = "imar18-malik-rehberi-v1\n"
            + "m" + A("M01") + A("AYŞE ÖRNEKSOY") + A("İSMAİL") + A("800000001") + "\n"
            + "m" + A("M02") + A("ÇAĞRI DENEMEOĞLU") + A(null) + A(null) + "\n"
            + "p" + A("K2") + A("15") + A("3") + A(null) + A(null) + A(null) + A("901") + A("8") + A(null) + "\n";

        r.Ozet().Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(kanonik))));
    }
}

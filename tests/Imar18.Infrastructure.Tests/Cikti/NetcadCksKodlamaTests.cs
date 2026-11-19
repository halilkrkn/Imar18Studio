using System.Text;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Infrastructure.Cad.Cks;
using Imar18.Infrastructure.Cikti;
using Imar18.Infrastructure.Cikti.Cks;
using Imar18.Infrastructure.Tests.Cad.Ncz;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cikti;

/// <summary>
/// P7.3-D (SAHA-008, SORU-039) — Netcad uyumlu <c>.CKS</c> bayt düzeni. Bağımsız referans: gerçek Netcad <c>DOP.CKS</c>
/// dosyasının bayt incelemesi — Türkçe harfler Windows-1254, çerçeve karakterleri OEM/CP437 baytı, satır 1 boş, kalan 21 satır
/// tam 87 bayt, CRLF; Pilot damgası CKS'e yazılmaz (proje sahibi kararı). Public testler sentetik Proje-A değerlerini kullanır.
/// </summary>
public sealed class NetcadCksKodlamaTests
{
    private const int SatirBayt = 87;

    private static readonly Encoding Utf8Kati = new UTF8Encoding(false, true);

    private static CksDopOzeti ProjeAOzeti() =>
        CksOzetiEslemesi.Olustur(DopOzeti.From(DopCalculator.Hesapla(DopGirdisi.Olustur(8699.44m, 7620.54m).Value).Value)).Value;

    private static List<byte[]> Satirlar(byte[] dosya)
    {
        var satirlar = new List<byte[]>();
        var bas = 0;
        for (var i = 0; i < dosya.Length - 1; i++)
        {
            if (dosya[i] == 0x0D && dosya[i + 1] == 0x0A)
            {
                satirlar.Add(dosya[bas..i]);
                bas = i + 2;
                i++;
            }
        }

        bas.Should().Be(dosya.Length, "dosya CRLF ile bitmeli, CRLF'siz artık bayt olmamalı");
        return satirlar;
    }

    // ---------------- Kodlayıcı ----------------

    [Theory]
    [InlineData('┌', 0xDA)]
    [InlineData('─', 0xC4)]
    [InlineData('┐', 0xBF)]
    [InlineData('│', 0xB3)]
    [InlineData('├', 0xC3)]
    [InlineData('┤', 0xB4)]
    [InlineData('└', 0xC0)]
    [InlineData('┘', 0xD9)]
    public void NetcadCksKodlayici_Cerceve_Karakterini_Cp437_Baytina_Cevirmeli(char karakter, int bayt)
    {
        var sonuc = NetcadCksKodlayici.Kodla(karakter.ToString());

        sonuc.IsSuccess.Should().BeTrue();
        sonuc.Value.Should().Equal((byte)bayt);
    }

    [Theory]
    [InlineData('İ', 0xDD)]
    [InlineData('ı', 0xFD)]
    [InlineData('ü', 0xFC)]
    [InlineData('ş', 0xFE)]
    [InlineData('ğ', 0xF0)]
    [InlineData('Ğ', 0xD0)]
    [InlineData('Ş', 0xDE)]
    [InlineData('Ü', 0xDC)]
    [InlineData('ç', 0xE7)]
    [InlineData('Ç', 0xC7)]
    [InlineData('ö', 0xF6)]
    [InlineData('Ö', 0xD6)]
    [InlineData('A', 0x41)]
    [InlineData('=', 0x3D)]
    [InlineData(' ', 0x20)]
    public void NetcadCksKodlayici_Turkce_Harfi_Windows1254_Baytina_Cevirmeli(char karakter, int bayt)
    {
        var sonuc = NetcadCksKodlayici.Kodla(karakter.ToString());

        sonuc.IsSuccess.Should().BeTrue();
        sonuc.Value.Should().Equal((byte)bayt);
    }

    [Theory]
    [InlineData("✓")]
    [InlineData("→")]
    [InlineData("漢")]
    [InlineData("😀")]
    public void NetcadCksKodlayici_Eslenemeyen_Karakterde_Sessiz_Soru_Isareti_Yerine_Hata_Vermeli(string metin)
    {
        var sonuc = NetcadCksKodlayici.Kodla("Toplam " + metin);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiCadDisaAktarim);
    }

    [Theory]
    [InlineData('Ú')]
    [InlineData('Ä')]
    [InlineData('¿')]
    [InlineData('³')]
    [InlineData('Ã')]
    [InlineData('´')]
    [InlineData('À')]
    [InlineData('Ù')]
    public void NetcadCksKodlayici_Cerceve_Baytina_Cakisan_1254_Harfini_Reddetmeli(char karakter)
    {
        // Bu harfler 1254'te çerçeve baytlarına (DA C4 BF B3 C3 B4 C0 D9) düşer; yazılırsa çözümde çerçeveye dönüşürdü.
        NetcadCksKodlayici.Kodla("A" + karakter).IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("\uD83D")]
    [InlineData("\t")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\0")]
    [InlineData("\u0081")]
    public void NetcadCksKodlayici_Tek_Vekil_Veya_Kontrol_Karakterinde_Hata_Vermeli(string metin)
    {
        // Satır sonlarını yazıcı ekler; içerik satırında kontrol karakteri sabit 87 bayt düzenini bozar.
        NetcadCksKodlayici.Kodla("Toplam" + metin).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NetcadCksKodlayici_1254_Tanimsiz_Bayti_Istisnasiz_Ve_Deterministik_Cozmeli()
    {
        NetcadCksKodlayici.Coz([0x81]).Should().Be("\u0081");
        NetcadCksKodlayici.Coz([0x41, 0x8D, 0xB3]).Should().Be("A\u008D│");
    }

    [Fact]
    public void NetcadCksKodlayici_Bos_Metin_Bos_Bayt_Dizisi_Vermeli()
    {
        NetcadCksKodlayici.Kodla(string.Empty).Value.Should().BeEmpty();
        NetcadCksKodlayici.Coz([]).Should().BeEmpty();
    }

    [Fact]
    public void NetcadCksKodlayici_Kodla_Coz_Gidis_Donus_Ayni_Metni_Vermeli()
    {
        const string Metin = "┌──┐│ Toplam İmar Ada Alanı Kamulaştırılan Bağışlanan Düzenleme Ortaklık Payı ÇÖÜĞŞ (DOPO) = 0.1240195 │├┤└┘";

        NetcadCksKodlayici.Coz(NetcadCksKodlayici.Kodla(Metin).Value).Should().Be(Metin);
    }

    // ---------------- Üretici bayt düzeni ----------------

    [Fact]
    public void CksUretici_Ilk_Satir_Bos_Olmali_Ve_Pilot_Damgasi_Yazilmamali_SORU_039()
    {
        var dosya = CksDosyaUretici.Yaz(ProjeAOzeti()).Value;

        dosya.Take(2).Should().Equal((byte)0x0D, (byte)0x0A);
        NetcadCksKodlayici.Coz(dosya).Should().NotContain("PİLOT").And.NotContain("Revizyon");
    }

    [Fact]
    public void CksUretici_Yirmi_Iki_Satir_Ve_Cerceve_Satirlari_Tam_87_Bayt_Olmali()
    {
        var dosya = CksDosyaUretici.Yaz(ProjeAOzeti()).Value;
        var satirlar = Satirlar(dosya);

        satirlar.Should().HaveCount(22);
        satirlar[0].Should().BeEmpty();
        satirlar.Skip(1).Should().OnlyContain(s => s.Length == SatirBayt);
        dosya.Length.Should().Be(2 + (21 * (SatirBayt + 2)));
    }

    [Fact]
    public void CksUretici_Cerceve_Koseleri_Ve_Kenarlari_Cp437_Baytlari_Olmali()
    {
        var s = Satirlar(CksDosyaUretici.Yaz(ProjeAOzeti()).Value);

        s[1][0].Should().Be(0xDA);
        s[1][1..^1].Should().OnlyContain(b => b == 0xC4);
        s[1][^1].Should().Be(0xBF);
        s[11][0].Should().Be(0xC3);
        s[11][^1].Should().Be(0xB4);
        s[21][0].Should().Be(0xC0);
        s[21][^1].Should().Be(0xD9);
        foreach (var i in new[] { 2, 3, 10, 12, 15, 17, 19, 20 })
        {
            s[i][0].Should().Be(0xB3, $"satır {i + 1} sol kenarı");
            s[i][^1].Should().Be(0xB3, $"satır {i + 1} sağ kenarı");
        }
    }

    [Fact]
    public void CksUretici_Turkce_Harfler_Windows1254_Olmali_Ve_Utf8_Dizisi_Icermemeli()
    {
        var dosya = CksDosyaUretici.Yaz(ProjeAOzeti()).Value;
        var s = Satirlar(dosya);

        // "│ Toplam İmar Ada Alanı" → İ (0xDD) içerik indeksi 8 (bayt 9), ı (0xFD) "Alanı" sonu
        s[3][9].Should().Be(0xDD);
        Encoding.ASCII.GetString(s[3], 10, 12).Should().Be("mar Ada Alan");
        s[3][22].Should().Be(0xFD);
        s[3][40].Should().Be((byte)'=', "'=' içerik indeksi 39'da (Netcad sütunu)");

        var utf8Gibi = () => Utf8Kati.GetString(dosya);
        utf8Gibi.Should().Throw<DecoderFallbackException>("dosya UTF-8 değil, Netcad karışık kodlaması olmalı");
        dosya.Should().NotContain((byte)0xE2, "UTF-8 kutu çizgisi önek baytı (E2 94 ..) olmamalı");
    }

    [Fact]
    public void CksUretici_Buyuk_Sayida_Satir_87_Bayti_Asarsa_Dosya_Uretilmemeli()
    {
        // Hesap satırı: 39 + "= " + R1 + " - " + R2 + " = " + kamu → 26 haneli alanlarla 85 karakterlik iç genişlik aşılır
        // (aritmetik tutarlı: R1 − R2 = kamu, kamu / R1 = 0.5); sütun taşması denetimi daha önce yakalar.
        var o = ProjeAOzeti() with
        {
            ToplamDuzenlemeyeGirenDopAlinanM2 = 10000000000000000000000000.00m,
            ToplamImarAdaAlaniM2 = 5000000000000000000000000.00m,
            KamuyaAyrilanAlanM2 = 5000000000000000000000000.00m,
            Dopo = 0.5000000m,
        };

        var sonuc = CksDosyaUretici.Yaz(o);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiCadDisaAktarim);
    }

    [Theory]
    [InlineData("99999999.99", true)]   // 11 karakter: "Toplam Tahsis" sütununa (11) tam sığar
    [InlineData("100000000.00", false)] // 12 karakter: sütun taşar → sütunlar kaymasın diye ERR-CIKTI-003
    public void CksUretici_Alan_Sutunu_11_Karakter_Sinirinda_Yazilmali_Asinca_Hata_Vermeli(string tahsis, bool yazilmali)
    {
        var o = ProjeAOzeti() with { ToplamTahsisM2 = decimal.Parse(tahsis, System.Globalization.CultureInfo.InvariantCulture) };

        var sonuc = CksDosyaUretici.Yaz(o);

        sonuc.IsSuccess.Should().Be(yazilmali);
        if (yazilmali)
        {
            Satirlar(sonuc.Value)[2].Should().HaveCount(SatirBayt);
        }
        else
        {
            sonuc.Error.Code.Should().Be(ErrorCodes.CiktiCadDisaAktarim);
        }
    }

    [Theory]
    [InlineData("9999999.99", true)]   // R1 ve R2 hesap satırlarındaki 10 karakterlik sütunlara tam sığar
    [InlineData("10000000.00", false)] // 11 karakter: "Kamuya" satırında R2, "DOPO" satırında R1 sütunu (10) taşar
    public void CksUretici_Hesap_Satiri_10_Karakter_Sutun_Sinirinda_Yazilmali_Asinca_Hata_Vermeli(string alanMetni, bool yazilmali)
    {
        // R1 = R2 → kamu 0, DOPO 0 (aritmetik tutarlı); yalnız sütun genişliği sınanır.
        var alan = decimal.Parse(alanMetni, System.Globalization.CultureInfo.InvariantCulture);
        var o = ProjeAOzeti() with
        {
            ToplamDuzenlemeyeGirenDopAlinanM2 = alan,
            ToplamImarAdaAlaniM2 = alan,
            ToplamTahsisM2 = alan,
            ToplamKadastroParselAlaniM2 = alan,
            ToplamPayM2 = alan,
            KamuyaAyrilanAlanM2 = 0m,
            Dopo = 0m,
            DopoOkoIleBirlikte = 0m,
        };

        CksDosyaUretici.Yaz(o).IsSuccess.Should().Be(yazilmali);
    }

    [Fact]
    public void CksUretici_Ayni_Girdiden_Ayni_Baytlari_Uretmeli()
    {
        CksDosyaUretici.Yaz(ProjeAOzeti()).Value.Should().Equal(CksDosyaUretici.Yaz(ProjeAOzeti()).Value);
    }

    [Fact]
    public void CksUretici_Cikti_Okuyucuyla_Gidis_Donus_Ayni_Ozeti_Vermeli()
    {
        var o = ProjeAOzeti();

        var okunan = new CksFileReader().Read(new MemoryStream(CksDosyaUretici.Yaz(o).Value));

        okunan.IsSuccess.Should().BeTrue(okunan.IsFailure ? okunan.Error.TechnicalMessage : string.Empty);
        okunan.Value.Should().Be(o);
    }

    [Fact]
    public void CksUretici_ProjeA_Ciktisi_Sentetik_Netcad_Bicimli_Ornekle_Bayt_Bayt_Ayni_Olmali()
    {
        // DOP_Ornek.CKS, Proje-A değerleriyle Netcad bayt düzeninde (gerçek DOP.CKS ile birebir; içerik yalnız public altın
        // JSON'daki alan toplamları) yazıcı dışı bir betikle üretildi; asıl bağımsız referans yerelde koşan RealData testidir
        // (CksUretici_ProjeA_Degerleriyle_Gercek_Netcad_DOP_CKS_Ile_Bayt_Bayt_Ayni_Olmali). Bu test CI'da bayt düzenini korur.
        var ornek = File.ReadAllBytes(Imar18.TestDestek.TestDataLocator.Sentetik("pilot-1-concave-parcel", "DOP_Ornek.CKS"));

        CksDosyaUretici.Yaz(ProjeAOzeti()).Value.Should().Equal(ornek);
    }

    // ---------------- Gerçek Netcad dosyası (yerel, RealData) ----------------

    [RealDataFact]
    [Trait("Category", "RealData")]
    public void CksUretici_ProjeA_Degerleriyle_Gercek_Netcad_DOP_CKS_Ile_Bayt_Bayt_Ayni_Olmali()
    {
        var gercek = File.ReadAllBytes(Path.Combine(GercekVeri.ProjeKlasoru("proje-1-"), "DOP.CKS"));

        var uretilen = CksDosyaUretici.Yaz(ProjeAOzeti()).Value;

        uretilen.Length.Should().Be(gercek.Length);
        uretilen.SequenceEqual(gercek).Should().BeTrue(
            "ilk farklı bayt: " + Enumerable.Range(0, Math.Min(uretilen.Length, gercek.Length)).FirstOrDefault(i => uretilen[i] != gercek[i], -1));
    }

    [RealDataFact]
    [Trait("Category", "RealData")]
    public void NetcadCksKodlayici_Gercek_DOP_CKS_Cozulunce_Dogru_Turkce_Metin_Vermeli()
    {
        var gercek = File.ReadAllBytes(Path.Combine(GercekVeri.ProjeKlasoru("proje-1-"), "DOP.CKS"));

        var metin = NetcadCksKodlayici.Coz(gercek);

        metin.Should().Contain("│ Toplam İmar Ada Alanı").And.Contain("Toplam Kamulaştırılan Alan").And.Contain("┌─").And.Contain("─┘");
    }
}

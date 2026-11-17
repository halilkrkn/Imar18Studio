using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Infrastructure.Cad.Cks;
using Imar18.Infrastructure.Tests.Cad.Ncz;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cad.Cks;

/// <summary>
/// P4.1 Netcad <c>.CKS</c> DOP ozet okuyucusu (spec §25.1, ncz-netcad skill). Netcad dosyasi Turkce harfleri
/// Windows-1254, cerceve karakterlerini OEM (cp437) baytlariyla yazar; public sentetik ornek UTF-8'dir.
/// Beklenen degerler public altin veriden (saha_proje_1_golden.json) alinir.
/// </summary>
public sealed class CksFileReaderTests
{
    private static readonly CksFileReader Okuyucu = new();

    private static readonly string[] Satirlar =
    [
        "",
        "┌" + new string('─', 85) + "┐",
        Deger("Toplam Tahsis", "7620.54"),
        Deger("Toplam İmar Ada Alanı", "7620.54"),
        Deger("Toplam Ortak Katılım Alanı", "0.00"),
        Deger("Toplam Düzenlemeye Giren (Dopal)", "8699.44"),
        Deger("Toplam Düzenlemeye Giren (Dopalma)", "0.00"),
        Deger("Toplam Kamulaştırılan Alan", "0.00"),
        Deger("Toplam Bağışlanan Alan", "0.00"),
        Deger("Toplam Kadastro Parsel Alanı", "8699.44"),
        Deger("Toplam Pay", "8699.44"),
        "├" + new string('─', 85) + "┤",
        Deger("Ortak Katılım Oranı", "0.0000000"),
        Deger("Düzenleme Ortaklık Payı Oranı (DOPO)", "0.1240195"),
        Deger("DOPO (OKO ile birlikte)", "0.1240195"),
        Deger("Maksimum Dop Oranı", "0.45"),
        "├" + new string('─', 85) + "┤",
        "│" + "Düzenleme Ortaklık Payı Hesabı".PadRight(85) + "│",
        "├" + new string('─', 85) + "┤",
        "│" + "Kamuya Ayrılan Alan".PadRight(39) + "=     8699.44 -    7620.54 =    1078.90".PadRight(46) + "│",
        "│" + "Düzenleme Ortaklık Payı".PadRight(39) + "=     1078.90 /    8699.44 =  0.1240195".PadRight(46) + "│",
        "└" + new string('─', 85) + "┘",
        "",
    ];

    private static string Deger(string etiket, string deger) =>
        "│ " + etiket.PadRight(37) + "=" + deger.PadLeft(12) + new string(' ', 85 - 1 - 37 - 1 - 12) + "│";

    /// <summary>Netcad'in karisik kodlamasi: cerceve karakterleri OEM (cp437) baytlari, harfler Windows-1254.</summary>
    private static byte[] NetcadBaytlari(IEnumerable<string> satirlar)
    {
        var oem = new Dictionary<char, byte>
        {
            ['┌'] = 0xDA, ['─'] = 0xC4, ['┐'] = 0xBF, ['│'] = 0xB3, ['├'] = 0xC3, ['┤'] = 0xB4, ['└'] = 0xC0, ['┘'] = 0xD9,
        };
        var tr = CodePagesEncodingProvider.Instance.GetEncoding(1254)!;
        var bayt = new List<byte>();
        foreach (var c in string.Join("\r\n", satirlar))
        {
            if (oem.TryGetValue(c, out var b))
            {
                bayt.Add(b);
            }
            else
            {
                bayt.AddRange(tr.GetBytes(c.ToString()));
            }
        }

        return [.. bayt];
    }

    private static CksDopOzeti Oku(byte[] bayt)
    {
        using var akis = new MemoryStream(bayt, writable: false);
        var sonuc = Okuyucu.Read(akis);
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        return sonuc.Value;
    }

    private static AppError OkuHata(byte[] bayt)
    {
        using var akis = new MemoryStream(bayt, writable: false);
        var sonuc = Okuyucu.Read(akis);
        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.GirdCksOkunamadi);
        return sonuc.Error;
    }

    private static void ProjeAAltinVeriyleEslesmeli(CksDopOzeti o)
    {
        using var altin = JsonDocument.Parse(File.ReadAllText(
            Imar18.TestDestek.TestDataLocator.Sentetik("pilot-1-concave-parcel", "saha_proje_1_golden.json")));
        var m = altin.RootElement.GetProperty("metrics");

        o.ToplamDuzenlemeyeGirenDopAlinanM2.Should().Be(m.GetProperty("toplamKadastroAlani_R1").GetDecimal());
        o.ToplamKadastroParselAlaniM2.Should().Be(m.GetProperty("toplamKadastroAlani_R1").GetDecimal());
        o.ToplamImarAdaAlaniM2.Should().Be(m.GetProperty("toplamImarTahsis_R2").GetDecimal());
        o.ToplamTahsisM2.Should().Be(m.GetProperty("toplamImarTahsis_R2").GetDecimal());
        o.KamuyaAyrilanAlanM2.Should().Be(m.GetProperty("kamuyaAyrilanYol").GetDecimal());
        o.Dopo.Should().Be(m.GetProperty("dopo").GetDecimal());
        o.DopoOkoIleBirlikte.Should().Be(m.GetProperty("dopo").GetDecimal());
        o.OrtakKatilimOrani.Should().Be(m.GetProperty("oko").GetDecimal());
        o.MaksimumDopOrani.Should().Be(0.45m);
        o.ToplamDuzenlemeyeGirenDopAlinmayanM2.Should().Be(0m);
        o.ToplamOrtakKatilimAlaniM2.Should().Be(0m);
        o.ToplamKamulastirilanAlanM2.Should().Be(0m);
        o.ToplamBagislananAlanM2.Should().Be(0m);
        o.ToplamPayM2.Should().Be(8699.44m);
    }

    [Fact]
    public void CksOkuyucu_Netcad_Karisik_Kodlamali_Dosyayi_Tum_Alanlariyla_Okumali()
    {
        ProjeAAltinVeriyleEslesmeli(Oku(NetcadBaytlari(Satirlar)));
    }

    [Fact]
    public void CksOkuyucu_Public_Sentetik_UTF8_Ornegi_Altin_Veriyle_Eslesmeli()
    {
        var bayt = File.ReadAllBytes(
            Imar18.TestDestek.TestDataLocator.Sentetik("pilot-1-concave-parcel", "DOP_Ornek.CKS"));

        ProjeAAltinVeriyleEslesmeli(Oku(bayt));
    }

    [Fact]
    public void CksOkuyucu_Duzgun_Turkce_UTF8_Metni_De_Okumali()
    {
        ProjeAAltinVeriyleEslesmeli(Oku(Encoding.UTF8.GetBytes(string.Join("\n", Satirlar))));
    }

    [Fact]
    public void CksOkuyucu_Zorunlu_Alan_Eksikse_ERR_GIRD_007_Dondurmeli()
    {
        var eksik = Satirlar.Where(s => !s.Contains("Toplam Pay", StringComparison.Ordinal)).ToArray();

        OkuHata(NetcadBaytlari(eksik)).TechnicalMessage.Should().Contain("missing");
    }

    [Fact]
    public void CksOkuyucu_Alan_Iki_Kez_Gecerse_ERR_GIRD_007_Dondurmeli()
    {
        var tekrar = Satirlar.Append(Deger("Toplam Pay", "1.00")).ToArray();

        OkuHata(NetcadBaytlari(tekrar)).TechnicalMessage.Should().Contain("duplicate");
    }

    [Theory]
    [InlineData("7620,54")]
    [InlineData("7.620.54")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1e5")]
    [InlineData("-7620.54")]
    public void CksOkuyucu_Gecersiz_Sayi_Biciminde_ERR_GIRD_007_Dondurmeli(string deger)
    {
        var bozuk = Satirlar.Select(s => s.Contains("Toplam Tahsis", StringComparison.Ordinal) ? Deger("Toplam Tahsis", deger) : s).ToArray();

        OkuHata(NetcadBaytlari(bozuk)).TechnicalMessage.Should().Contain("number");
    }

    [Fact]
    public void CksOkuyucu_Kamuya_Ayrilan_Alan_Aritmetigi_Tutmazsa_ERR_GIRD_007_Dondurmeli()
    {
        var bozuk = Satirlar.Select(s => s.Replace("=    1078.90", "=    1078.91", StringComparison.Ordinal)).ToArray();

        OkuHata(NetcadBaytlari(bozuk)).TechnicalMessage.Should().Contain("arithmetic");
    }

    [Fact]
    public void CksOkuyucu_DOPO_Bolme_Sonucu_7_Hanede_Tutmazsa_ERR_GIRD_007_Dondurmeli()
    {
        var bozuk = Satirlar.Select(s => s.Replace("=  0.1240195", "=  0.1240196", StringComparison.Ordinal)).ToArray();

        OkuHata(NetcadBaytlari(bozuk)).TechnicalMessage.Should().Contain("arithmetic");
    }

    [Fact]
    public void CksOkuyucu_DOPO_Satiri_Hesap_Satiriyla_Celisirse_ERR_GIRD_007_Dondurmeli()
    {
        var bozuk = Satirlar.Select(s => s.Contains("(DOPO)", StringComparison.Ordinal) ? Deger("Düzenleme Ortaklık Payı Oranı (DOPO)", "0.1240100") : s).ToArray();

        OkuHata(NetcadBaytlari(bozuk)).TechnicalMessage.Should().Contain("arithmetic");
    }

    [Fact]
    public void CksOkuyucu_Bos_Veya_Ilgisiz_Dosyada_ERR_GIRD_007_Dondurmeli()
    {
        OkuHata([]);
        OkuHata("bu bir CKS dosyasi degil"u8.ToArray());
        OkuHata(new byte[] { 0x01, 0x78, 0x00, 0xFF, 0xFE });
    }

    [Fact]
    public void CksOkuyucu_Boyut_Siniri_Asilirsa_Okumadan_ERR_GIRD_007_Dondurmeli()
    {
        var buyuk = new byte[CksFileReader.MaxFileSizeBytes + 1];

        OkuHata(buyuk).TechnicalMessage.Should().Contain("too large");
    }

    [Fact]
    public void CksOkuyucu_Hata_Mesajinda_Dosya_Degeri_Sizdirmamali()
    {
        var bozuk = Satirlar.Select(s => s.Contains("Toplam Tahsis", StringComparison.Ordinal) ? Deger("Toplam Tahsis", "GIZLI-99") : s).ToArray();

        OkuHata(NetcadBaytlari(bozuk)).TechnicalMessage.Should().NotContain("GIZLI");
    }

    [Fact]
    public void CksOkuyucu_Iptal_Ve_Null_Sozlesmesine_Uymali()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var akis = new MemoryStream(NetcadBaytlari(Satirlar));

        ((Action)(() => Okuyucu.Read(akis, cts.Token))).Should().Throw<OperationCanceledException>();
        ((Action)(() => Okuyucu.Read(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CksOkuyucu_Sayilar_Kultur_Bagimsiz_Ayristirilmali()
    {
        var onceki = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Oku(NetcadBaytlari(Satirlar)).ToplamTahsisM2.Should().Be(7620.54m);
        }
        finally
        {
            CultureInfo.CurrentCulture = onceki;
        }
    }

    [RealDataFact]
    [Trait("Category", "RealData")]
    public void CksOkuyucu_Gercek_Netcad_DOP_CKS_Dosyasi_Altin_Veriyle_Eslesmeli()
    {
        var dosya = Path.Combine(GercekVeri.ProjeKlasoru("proje-1-"), "DOP.CKS");

        ProjeAAltinVeriyleEslesmeli(Oku(File.ReadAllBytes(dosya)));
    }
}

using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cikti.FenKlasoru;
using UglyToad.PdfPig;
using Xunit;
using static Imar18.Infrastructure.Tests.Cikti.FenKlasoruZipTests;

namespace Imar18.Infrastructure.Tests.Cikti;

/// <summary>
/// SAHA-017 (mühendisin gerçek <c>Imar18_PILOT_Fen_Klasoru.zip</c> incelemesi): tescil no'suz kroki parselleri (<c>1/1</c>, <c>2/1</c>)
/// için ada başına Tescil/Rölöve dosyaları, Tescile Esas Dağıtım Cetveli başlığı, Koordine Özet / Rölöve nokta ayrımı
/// (Deprem Öncesi = yalnız kadastro, MK yok; Deprem Sonrası = TK_Oluşan MK 0.09 + kadastro MK boş) ve PDF görsel cilası.
/// Bağımsız referans: <see cref="OrnekProje"/> (K1 = 4 köşe; A1 ∪ Y1 = 6 köşe, ikisi K1'de olmayan y = 12 m ara noktaları).
/// </summary>
public sealed class Saha017FenKlasoruTests
{
    private static string F2(decimal v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static List<string> Kelimeler(byte[] pdf)
    {
        using var d = PdfDocument.Open(pdf);
        return d.GetPages().SelectMany(s => s.GetWords()).Select(w => w.Text).ToList();
    }

    /// <summary>Kroki parselasyonu çıktısı benzetimi: A1 → <c>1/1</c>, kopyası → <c>2/1</c> (77.77 m²); hiçbirinde tescil no yok (<c>-</c>).</summary>
    private static CiktiPaketi KrokiParselli(CiktiPaketi p)
    {
        var a1 = p.Parseller.Single(s => s.GeciciKod == "A1");
        var p11 = a1 with { GeciciKod = "1/1", TescilNo = "-" };
        var p21 = a1 with { GeciciKod = "2/1", TescilNo = "-", DengelenmisAlanM2 = 77.77m, Koseler = a1.Koseler.Select(k => k with { X = k.X + 10m }).ToArray() };
        var dagitim = p.ImarSirali.Where(s => s.GeciciKod == "A1").ToArray();
        return p with
        {
            Parseller = p.Parseller.Where(s => s.GeciciKod != "A1").Append(p11).Append(p21).ToArray(),
            ImarSirali = dagitim.Select(s => s with { TescilNo = "-", GeciciKod = "1/1" })
                .Concat(dagitim.Select(s => s with { TescilNo = "-", GeciciKod = "2/1", ParselAlaniM2 = 77.77m, ParselAlani = Imar18.Domain.Numerics.HeM2Dm2.Ayir(77.77m) })).ToArray(),
        };
    }

    [Fact]
    public async Task FenKlasoru_Tescil_Nosuz_1_1_ve_2_1_Parsellerinde_Ada_Basina_Tescil_Ve_Roleve_Uretilmeli_SAHA_017()
    {
        var p = KrokiParselli(await PaketAsync());

        var zip = Uret(p);
        var adlar = Adlar(zip);
        var d = Ac(zip);
        string M(string ad) => Bosluksuz(Metin(d[ad]));

        FenKlasoruVerisi.ImarAdalari(p).Should().Equal("1", "2");
        adlar.Should().Contain(["TESCİL SAYFASI-1.pdf", "TESCİL SAYFASI-2.pdf", "TESCİL-1-2.pdf", "RÖLÖVE-1.pdf", "RÖLÖVE-2.pdf", "RÖLÖVE-1-2.pdf"]);
        M("TESCİL SAYFASI-1.pdf").Should().Contain("ADANO:1").And.Contain("ARSA");
        M("TESCİL SAYFASI-2.pdf").Should().Contain("ADANO:2").And.Contain("0077").And.Contain("77", "2/1 alanı 77.77 m² → M2 0077, Dm2 77");
        M("RÖLÖVE-1.pdf").Should().Contain("ADA1").And.Contain("1/1");
        M("RÖLÖVE-2.pdf").Should().Contain("ADA2").And.Contain("2/1");
        M("TESCİLE ESAS DAĞITIM CETVELİ.pdf").Should().Contain("TESCİLEESASDAĞITIMCETVELİ").And.Contain("1/1").And.Contain("2/1");
        var ozet = M("ÖZET CETVELİ.pdf");
        ozet.Should().Contain(F2(p.Parseller.Single(s => s.GeciciKod == "1/1").DengelenmisAlanM2)).And.Contain("77.77", "İMAR AdaNo/Alan satırları dolu");
        Kelimeler(d["ÖZET CETVELİ.pdf"]).Should().Contain("1").And.Contain("2");
    }

    [Fact]
    public async Task TescileEsasDagitim_Basligi_Kendi_Adi_Mal_Sahipleri_Basligi_Kadastro_Sirali_Olmali_SAHA_017()
    {
        var d = Ac(Uret(await PaketAsync()));

        var tescil = Bosluksuz(Metin(d["TESCİLE ESAS DAĞITIM CETVELİ.pdf"]));
        tescil.Should().Contain("TESCİLEESASDAĞITIMCETVELİ").And.Contain("(İMARPARSELİNESIRALI)").And.NotContain("MALSAHİPLERİARAŞTIRMAVEÖZETFORMU");
        var mal = Bosluksuz(Metin(d["MAL SAHİPLERİ ARAŞTIRMA VE ÖZET FORMU.pdf"]));
        mal.Should().Contain("MALSAHİPLERİARAŞTIRMAVEÖZETFORMU").And.Contain("(KADASTROPARSELİNESIRALI)").And.NotContain("TESCİLEESAS");
    }

    [Fact]
    public async Task KoordineOzet_Deprem_Oncesi_Yalniz_Kadastro_Sonrasi_Olusan_MK_Ve_Kadastro_MK_Bos_Olmali_SAHA_017()
    {
        var p = await PaketAsync(dns: true);
        var kadastroNo = p.Parseller.Single(s => s.Rol == ParselRolu.Kadastro).NoktaNolari.ToHashSet();
        var olusanNo = p.Parseller.Where(s => s.Rol != ParselRolu.Kadastro).SelectMany(s => s.NoktaNolari).ToHashSet();
        kadastroNo.Should().HaveCount(4);
        olusanNo.Should().HaveCount(6);
        var araNokta = olusanNo.Except(kadastroNo).ToArray();
        araNokta.Should().HaveCount(2, "y = 12 m ara noktaları kadastro köşesi değildir");

        var metin = Metin(Ac(Uret(p))["KOORDİNE ÖZET.pdf"]);
        var b = Bosluksuz(metin);
        var once = b.IndexOf("(DEPREMÖNCESİ)", StringComparison.Ordinal);
        var sonra = b.IndexOf("(DEPREMSONRASI)", StringComparison.Ordinal);
        once.Should().BeGreaterThan(-1);
        sonra.Should().BeGreaterThan(once);
        var onceBolum = b[once..sonra];
        var sonraBolum = b[sonra..];

        onceBolum.Should().NotContain("MK", "Deprem Öncesi tablosunda MK sütunu yoktur");
        foreach (var no in araNokta)
        {
            var o = p.Donusum!.DepremOncesi.Single(k => k.NoktaNo == no);
            onceBolum.Should().NotContain(F2(o.Y) + F2(o.X), "TK_Oluşan ara noktası Deprem Öncesi'nde yer almaz");
        }

        foreach (var no in kadastroNo)
        {
            var o = p.Donusum!.DepremOncesi.Single(k => k.NoktaNo == no);
            onceBolum.Should().Contain(no + F2(o.Y) + F2(o.X), "kadastro köşesi Deprem Öncesi'nde");
        }

        // Deprem Sonrası: 6 TK_Oluşan satırı MK = 0.09, ardından 4 kadastro satırı MK boş (tire yok).
        var kelimeler = metin.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sonraKelime = kelimeler.SkipWhile(k => !k.Contains("SONRASI", StringComparison.Ordinal)).ToArray();
        sonraKelime.Count(k => k == "0.09").Should().Be(6);
        kelimeler.Count(k => k == "0.09").Should().Be(6, "MK yalnız TK_Oluşan noktalarındadır");
        kelimeler.Should().NotContain("-", "kadastro MK hücresi boştur");
        var ilkKadastro = p.Koordinatlar.Single(k => k.NoktaNo == kadastroNo.Min());
        var sonOlusan = p.Koordinatlar.Single(k => k.NoktaNo == olusanNo.Max());
        sonraBolum.Should().Contain(sonOlusan.NoktaNo + F2(sonOlusan.Y) + F2(sonOlusan.X) + "0.09");
        sonraBolum.LastIndexOf(ilkKadastro.NoktaNo + F2(ilkKadastro.Y) + F2(ilkKadastro.X), StringComparison.Ordinal)
            .Should().BeGreaterThan(sonraBolum.IndexOf(sonOlusan.NoktaNo + F2(sonOlusan.Y) + F2(sonOlusan.X) + "0.09", StringComparison.Ordinal),
                "kadastro köşeleri TK_Oluşan köşelerinin altındadır");
    }

    [Fact]
    public async Task KoordineOzet_Donusumsuz_Tek_Tabloda_Olusan_MK_009_Kadastro_MK_Bos_Olmali_SAHA_017()
    {
        var p = await PaketAsync(donusum: false);
        var d = Ac(Uret(p));

        var k = Kelimeler(d["KOORDİNE ÖZET.pdf"]);

        k.Count(w => w == "0.09").Should().Be(6, "dönüşüm yokken de TK_Oluşan noktalarında MK = 0.09 (DonusumGirdisi.VarsayilanMk)");
        k.Should().NotContain("-");
        Kelimeler(d["RÖLÖVE ÖLÇÜ KROKİSİ.pdf"]).Count(w => w == "0.09").Should().Be(6);
    }

    [Fact]
    public async Task Roleve_Ada_Basina_Oncesi_Kadastro_Sonrasi_Ada_Parselleri_MK_Ile_Ayrilmali_SAHA_017()
    {
        var p = await PaketAsync(dns: true);
        var d = Ac(Uret(p));

        var metin = Metin(d["RÖLÖVE-1.pdf"]);
        var k = metin.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        k.Count(w => w == "0.09").Should().Be(4, "RÖLÖVE-1: yalnız 101/1'in 4 köşesi TK_Oluşan (MK)");
        var b = Bosluksuz(metin);
        var once = b.IndexOf("(DEPREMÖNCESİ)", StringComparison.Ordinal);
        var sonra = b.IndexOf("(DEPREMSONRASI)", StringComparison.Ordinal);
        var onceBolum = b[once..sonra];
        foreach (var no in p.Parseller.Single(s => s.Rol == ParselRolu.Kadastro).NoktaNolari)
        {
            var o = p.Donusum!.DepremOncesi.Single(x => x.NoktaNo == no);
            onceBolum.Should().Contain(no + F2(o.Y) + F2(o.X));
        }

        var genel = Bosluksuz(Metin(d["RÖLÖVE ÖLÇÜ KROKİSİ.pdf"]));
        var go = genel.IndexOf("(DEPREMÖNCESİ)", StringComparison.Ordinal);
        genel[go..genel.IndexOf("(DEPREMSONRASI)", StringComparison.Ordinal)].Should().Contain("NoktaNoYX").And.NotContain("MK", "Deprem Öncesi'nde MK sütunu yok");
    }

    [Fact]
    public async Task Dop_Resmi_Netcad_Duzeninde_Esittir_Hizali_Ve_DOP_Hesabi_Kamuya_Bolu_R1_Olmali_SAHA_017()
    {
        var p = await PaketAsync();
        var b = Bosluksuz(Metin(Ac(Uret(p))["DOP.pdf"]));
        var dop = p.Dop;

        b.Should().NotContain("KalemDeğer", "2 sütunlu Kalem | Değer tablosu yerine Netcad DOP çerçevesi");
        b.Should().Contain("ToplamTahsis=" + F2(dop.R2)).And.Contain("ToplamDüzenlemeyeGiren(Dopal)=" + F2(dop.R1))
            .And.Contain("OrtakKatılımOranı=").And.Contain("DOPO(OKOilebirlikte)=").And.Contain("MaksimumDopOranı=")
            .And.Contain("DüzenlemeOrtaklıkPayıHesabı")
            .And.Contain("KamuyaAyrılanAlan=" + F2(dop.R1) + "-" + F2(dop.R2) + "=" + F2(dop.KamuyaAyrilanM2))
            .And.Contain("DüzenlemeOrtaklıkPayı=" + F2(dop.KamuyaAyrilanM2) + "/" + F2(dop.R1) + "=" + dop.Dopo.ToString("0.0000000", System.Globalization.CultureInfo.InvariantCulture));
        (dop.KamuyaAyrilanM2 / dop.R1).Should().Be(dop.Dopo, "bağımsız kontrol: D1 = D2 = R3 = 0 iken DOPO = (R1 − R2) / R1");
    }

    [Fact]
    public async Task OzetCetveli_Gelistirici_NCZ_Notu_Basilmamali_SAHA_017()
    {
        var b = Bosluksuz(Metin(Ac(Uret(await PaketAsync()))["ÖZET CETVELİ.pdf"]));

        b.Should().NotContain("deneysel").And.NotContain("SORU-042").And.NotContain("PARSELASYONHARİTASI.NCZ");
    }

    [Fact]
    public async Task ParselasyonHaritasi_Ada_Parsel_No_Kadastro_Parsellerinden_Dolmali_SAHA_017()
    {
        var p = await PaketAsync();

        FenKlasoruVerisi.KadastroAdaParselleri(p).Should().Be("901/7", "rehberdeki K1 tapu kaydı: Ada 901, Parsel 7");
        Bosluksuz(Metin(Ac(Uret(p))["PARSELASYON HARİTASI.pdf"])).Should().Contain("Ada/ParselNo901/7");
    }

    [Fact]
    public void IcNokta_U_Bicimli_Yol_Terkinde_Etiket_Kendi_Poligonunun_Icinde_Olmali_SAHA_017()
    {
        // U biçimli yol: kolların arasındaki boşluk (2..10 × 3..9) imar parselidir; ağırlık merkezi bu boşluğa düşer.
        static GeodeticPoint K(decimal x, decimal y) => new(OrnekProje.X0 + x, OrnekProje.Y0 + y);
        GeodeticPoint[] u = [K(0, 0), K(0, 12), K(10, 12), K(10, 9), K(2, 9), K(2, 3), K(10, 3), K(10, 0)];

        var ic = FenKlasoruVerisi.IcNokta(u);

        // Elle çıkarılmış iç bölge (bağımsız kahin, reviewer D9): sırt şeridi (x 0..2, y 0..12) veya alt/üst kollar (x 0..10, y 0..3 / 9..12).
        var (x, y) = (ic.X - OrnekProje.X0, ic.Y - OrnekProje.Y0);
        var icBolgede = (x > 0m && x < 2m && y > 0m && y < 12m) || (x > 0m && x < 10m && ((y > 0m && y < 3m) || (y > 9m && y < 12m)));
        icBolgede.Should().BeTrue("YOL_TERKİ etiketi yolun kendi alanında, komşu imar parselinin (boşluk) üstüne binmez");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void IcNokta_Dejenere_Bos_Iki_Noktali_Ve_Dogrusal_Girdide_Istisnasiz_Donmeli_SAHA_017(int adet)
    {
        GeodeticPoint[] dogrusal = [new(OrnekProje.X0, OrnekProje.Y0), new(OrnekProje.X0, OrnekProje.Y0 + 5m), new(OrnekProje.X0, OrnekProje.Y0 + 10m)];

        var act = () => FenKlasoruVerisi.IcNokta(dogrusal.Take(adet).ToArray());

        act.Should().NotThrow("sıfır alanlı (doğrusal) poligonda bölme sıfıra düşmez");
    }

    [Fact]
    public async Task HaritaSvg_Kadastro_Ince_Kesikli_Olusan_Kalin_Net_Etiketler_Halolu_Olmali_SAHA_017()
    {
        var (svg, _) = FenKlasoruBelgeleri.HaritaSvg(await PaketAsync());

        svg.Should().Contain("stroke=\"#6b6b6b\"").And.Contain("stroke-dasharray").And.Contain("<rect").And.Contain("fill=\"#ffffff\"").And.Contain(">YOL</text>");
        System.Text.RegularExpressions.Regex.Matches(svg, ">YOL</text>").Count.Should().Be(1, "etiket tek kez basılır (PDF metni temiz)");
    }

    [Fact]
    public async Task RoleveSvg_Kose_Dairesi_Ve_Halolu_Kaydirilmis_Nokta_Ve_Cephe_Yazilari_Olmali_SAHA_017()
    {
        var p = await PaketAsync();
        var a1 = FenKlasoruVerisi.AdaParselleri(p, "101");

        var svg = FenKlasoruBelgeleri.KrokiSvg(p, a1);

        svg.Should().Contain("<circle").And.Contain("<rect").And.Contain("font-family=\"Arial");
        System.Text.RegularExpressions.Regex.Matches(svg, "<circle").Count.Should().Be(4, "101/1'in her köşesinde ○");
        System.Text.RegularExpressions.Regex.Matches(svg, ">12\\.00</text>").Count.Should().Be(2, "iki 12.00 m cephe, her biri tek yazı (PDF metni temiz)");
    }
}

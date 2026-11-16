using System.Globalization;
using System.Numerics;
using FluentAssertions;
using Imar18.Domain.Numerics;
using Xunit;

namespace Imar18.Domain.Tests.Numerics;

/// <summary>
/// P1.1 — Fraction (BigInteger Pay/Payda) birim testleri (FR-F1.x / AC-PILOT-03 / TC: FractionTests).
/// Beklenen degerler elle/bagimsiz (EBOB, ortak payda, teleskopik toplam) hesaplanmistir;
/// mevcut kodun ciktisina uydurulmamistir.
/// </summary>
public sealed class FractionTests
{
    private static decimal D(string metin) => decimal.Parse(metin, CultureInfo.InvariantCulture);

    // ---------- Olusturma ve sadelestirme ----------

    [Fact]
    public void Fraction_Olusturma_Pay_Payda_EBOB_ile_Otomatik_Sadelesmeli()
    {
        var f = new Fraction(2, 4);

        f.Numerator.Should().Be(1);
        f.Denominator.Should().Be(2);
    }

    [Fact]
    public void Fraction_Olusturma_Sifir_Pay_Sifir_Bolu_Bir_Olarak_Normalize_Olmali()
    {
        var f = new Fraction(0, 17);

        f.Numerator.Should().Be(0);
        f.Denominator.Should().Be(1);
        f.IsZero.Should().BeTrue();
    }

    [Fact]
    public void Fraction_Olusturma_Negatif_Pay_Kabul_Edilmeli_Ve_Payda_Pozitif_Kalmali()
    {
        var f = new Fraction(-6, 8);

        f.Numerator.Should().Be(-3);
        f.Denominator.Should().Be(4);
        f.Sign.Should().Be(-1);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(0, 0)]
    [InlineData(-5, -7)]
    public void Fraction_Olusturma_Sifir_Veya_Negatif_Payda_Reddedilmeli(int pay, int payda)
    {
        var act = () => new Fraction(pay, payda);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Fraction_Default_Deger_Gecerli_Sifir_Olarak_Davranmali()
    {
        var f = default(Fraction);

        f.Numerator.Should().Be(0);
        f.Denominator.Should().Be(1);
        (f + new Fraction(1, 3)).Should().Be(new Fraction(1, 3));
    }

    [Fact]
    public void Fraction_Sabitler_Sifir_Ve_Bir_Dogru_Olmali()
    {
        Fraction.Zero.Should().Be(new Fraction(0, 1));
        Fraction.One.Should().Be(new Fraction(5, 5));
    }

    // ---------- Gercek saha kesirleri ile aritmetik ----------

    [Fact]
    public void Fraction_Toplama_Saha_Kesirleri_1_24_Arti_1_72_Sadelesmis_1_18_Vermeli()
    {
        // 3/72 + 1/72 = 4/72 = 1/18
        var sonuc = new Fraction(1, 24) + new Fraction(1, 72);

        sonuc.Should().Be(new Fraction(1, 18));
        sonuc.Numerator.Should().Be(1);
        sonuc.Denominator.Should().Be(18);
    }

    [Fact]
    public void Fraction_Toplama_Saha_Kesirleri_19_320_Arti_1_176_229_3520_Vermeli()
    {
        // EKOK(320,176)=3520 -> 209/3520 + 20/3520 = 229/3520 (229 asal)
        var sonuc = new Fraction(19, 320) + new Fraction(1, 176);

        sonuc.Numerator.Should().Be(229);
        sonuc.Denominator.Should().Be(3520);
    }

    [Fact]
    public void Fraction_Cikarma_1_24_Eksi_1_72_1_36_Vermeli()
    {
        var sonuc = new Fraction(1, 24) - new Fraction(1, 72);

        sonuc.Should().Be(new Fraction(1, 36));
    }

    [Fact]
    public void Fraction_Cikarma_Kucukten_Buyuk_Cikarilinca_Negatif_Sonuc_Vermeli()
    {
        var sonuc = new Fraction(1, 72) - new Fraction(1, 24);

        sonuc.Should().Be(new Fraction(-1, 36));
        sonuc.Denominator.Sign.Should().Be(1);
    }

    [Fact]
    public void Fraction_Carpma_1_24_Carpi_1_72_1_1728_Vermeli()
    {
        (new Fraction(1, 24) * new Fraction(1, 72)).Should().Be(new Fraction(1, 1728));
    }

    [Fact]
    public void Fraction_Carpma_Capraz_Sadelesme_Uygulanmali()
    {
        // (19/320) * (176/19) = 176/320 = 11/20
        (new Fraction(19, 320) * new Fraction(176, 19)).Should().Be(new Fraction(11, 20));
    }

    [Fact]
    public void Fraction_Bolme_19_320_Bolu_1_176_209_20_Vermeli()
    {
        // 19*176 / 320 = 3344/320 = 209/20
        var sonuc = new Fraction(19, 320) / new Fraction(1, 176);

        sonuc.Numerator.Should().Be(209);
        sonuc.Denominator.Should().Be(20);
    }

    [Fact]
    public void Fraction_Bolme_Negatif_Bolen_Ile_Payda_Pozitif_Kalmali()
    {
        var sonuc = new Fraction(1, 2) / new Fraction(-3, 4);

        sonuc.Should().Be(new Fraction(-2, 3));
        sonuc.Denominator.Should().Be(3);
    }

    [Fact]
    public void Fraction_Bolme_Sifira_Bolunce_Reddedilmeli()
    {
        var act = () => new Fraction(1, 2) / Fraction.Zero;

        act.Should().Throw<DivideByZeroException>();
    }

    [Fact]
    public void Fraction_Negasyon_Isareti_Cevirmeli()
    {
        (-new Fraction(3, 7)).Should().Be(new Fraction(-3, 7));
        (-Fraction.Zero).Should().Be(Fraction.Zero);
    }

    [Fact]
    public void Fraction_Teleskopik_Toplam_Bagimsiz_Referansla_Uyusmali()
    {
        // Bagimsiz referans: Sum_{k=1..n} 1/(k(k+1)) = n/(n+1)
        const int n = 1000;
        var toplam = Fraction.Zero;
        for (var k = 1; k <= n; k++)
        {
            toplam += new Fraction(1, (BigInteger)k * (k + 1));
        }

        toplam.Should().Be(new Fraction(n, n + 1));
    }

    [Fact]
    public void Fraction_Toplama_Degisme_Ve_Birlesme_Ozelligi_Saglamali()
    {
        var a = new Fraction(1, 24);
        var b = new Fraction(19, 320);
        var c = new Fraction(1, 176);

        (a + b).Should().Be(b + a);
        ((a + b) + c).Should().Be(a + (b + c));
    }

    // ---------- Esitlik ve karsilastirma ----------

    [Fact]
    public void Fraction_Esitlik_Deger_Esitligi_Olmali_2_4_Esittir_1_2()
    {
        var a = new Fraction(2, 4);
        var b = new Fraction(1, 2);

        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
        (a != b).Should().BeFalse();
    }

    [Fact]
    public void Fraction_Karsilastirma_Buyuk_Kucuk_Dogru_Siralamali()
    {
        var kucuk = new Fraction(1, 176);
        var buyuk = new Fraction(19, 320);

        (kucuk < buyuk).Should().BeTrue();
        (buyuk > kucuk).Should().BeTrue();
        (kucuk <= new Fraction(2, 352)).Should().BeTrue();
        (buyuk >= new Fraction(38, 640)).Should().BeTrue();
        kucuk.CompareTo(buyuk).Should().BeLessThan(0);
        buyuk.CompareTo(kucuk).Should().BeGreaterThan(0);
        kucuk.CompareTo(new Fraction(2, 352)).Should().Be(0);
    }

    [Fact]
    public void Fraction_Karsilastirma_Negatif_Degerler_Dogru_Siralamali()
    {
        (new Fraction(-1, 2) < new Fraction(-1, 3)).Should().BeTrue();
        (new Fraction(-1, 2) < Fraction.Zero).Should().BeTrue();
    }

    [Fact]
    public void Fraction_Siralama_List_Sort_Deger_Sirasina_Gore_Olmali()
    {
        var liste = new List<Fraction> { new(1, 2), new(1, 176), new(19, 320), new(-1, 3) };

        liste.Sort();

        liste.Should().Equal(new Fraction(-1, 3), new Fraction(1, 176), new Fraction(19, 320), new Fraction(1, 2));
    }

    // ---------- Yardimcilar: Floor, Abs, Sum ----------

    [Theory]
    [InlineData(7, 2, 3)]
    [InlineData(6, 2, 3)]
    [InlineData(1, 3, 0)]
    [InlineData(-1, 3, -1)]
    [InlineData(-7, 2, -4)]
    [InlineData(0, 5, 0)]
    public void Fraction_Floor_Negatif_Dahil_Tabana_Yuvarlamali(int pay, int payda, int beklenen)
    {
        new Fraction(pay, payda).Floor().Should().Be(beklenen);
    }

    [Fact]
    public void Fraction_Abs_Mutlak_Deger_Vermeli()
    {
        new Fraction(-3, 7).Abs().Should().Be(new Fraction(3, 7));
        new Fraction(3, 7).Abs().Should().Be(new Fraction(3, 7));
    }

    [Fact]
    public void Fraction_Topla_Bos_Dizi_Sifir_Vermeli()
    {
        Fraction.Sum(Array.Empty<Fraction>()).Should().Be(Fraction.Zero);
    }

    [Fact]
    public void Fraction_Topla_Saha_Kesirleri_Toplami_Dogru_Olmali()
    {
        // 1/24 + 1/72 + 19/320 + 1/176
        // EKOK(24,72,320,176) = 2^6*3^2*5*11 = 31680
        // 1320/31680 + 440/31680 + 1881/31680 + 180/31680 = 3821/31680
        var toplam = Fraction.Sum([new Fraction(1, 24), new Fraction(1, 72), new Fraction(19, 320), new Fraction(1, 176)]);

        toplam.Numerator.Should().Be(3821);
        toplam.Denominator.Should().Be(31680);
    }

    // ---------- BigInteger tasma ----------

    [Fact]
    public void Fraction_Cok_Buyuk_Pay_Payda_Ile_Tasma_Olmadan_Aritmetik_Yapmali()
    {
        var on50 = BigInteger.Pow(10, 50);
        var a = new Fraction(on50 + 1, on50);
        var b = new Fraction(on50, on50 + 1);

        (a * b).Should().Be(Fraction.One);
        (a / a).Should().Be(Fraction.One);
        (a - a).Should().Be(Fraction.Zero);
        // a + b = ((on50+1)^2 + on50^2) / (on50 * (on50+1)); EBOB = 1 (ardisik tam sayilar)
        (a + b).Numerator.Should().Be(((on50 + 1) * (on50 + 1)) + (on50 * on50));
        (a + b).Denominator.Should().Be(on50 * (on50 + 1));
    }

    [Fact]
    public void Fraction_Cok_Buyuk_Ortak_Carpanli_Pay_Payda_EBOB_ile_Sadelesmeli()
    {
        var ortak = BigInteger.Pow(7, 80);
        var f = new Fraction(3 * ortak, 5 * ortak);

        f.Numerator.Should().Be(3);
        f.Denominator.Should().Be(5);
    }

    // ---------- Decimal donusumleri ----------

    [Fact]
    public void Fraction_ToDecimal_Varsayilan_Scale_6_Ile_AwayFromZero_Yuvarlamali()
    {
        new Fraction(1, 24).ToDecimal().Should().Be(0.041667m);   // 0.0416666...
        new Fraction(1, 176).ToDecimal().Should().Be(0.005682m);  // 0.0056818...
        new Fraction(19, 320).ToDecimal().Should().Be(0.059375m); // tam
    }

    [Theory]
    [InlineData(1, 8, 2, "0.13")]
    [InlineData(-1, 8, 2, "-0.13")]
    [InlineData(1, 2, 0, "1")]
    [InlineData(3, 2, 0, "2")]
    [InlineData(5, 2, 0, "3")]
    [InlineData(-1, 2, 0, "-1")]
    [InlineData(-3, 2, 0, "-2")]
    [InlineData(-5, 2, 0, "-3")]
    [InlineData(-1, 200, 2, "-0.01")]   // -0.005 -> sifirdan uzaga
    [InlineData(-3, 200, 2, "-0.02")]   // -0.015
    [InlineData(-1, 400, 2, "0.00")]    // -0.0025 -> asagi (mutlak deger)
    [InlineData(1, 2000000, 6, "0.000001")]   // 0.0000005
    [InlineData(-1, 2000000, 6, "-0.000001")]
    [InlineData(-3, 2000000, 6, "-0.000002")] // -0.0000015
    [InlineData(-1, 4000000, 6, "0.000000")]  // -0.00000025
    [InlineData(1, 3, 0, "0")]
    [InlineData(2, 3, 0, "1")]
    [InlineData(-2, 3, 0, "-1")]
    [InlineData(1, 200, 2, "0.01")]   // 0.005 -> yarim -> sifirdan uzaga
    [InlineData(1, 400, 2, "0.00")]   // 0.0025 -> asagi
    public void Fraction_ToDecimal_Yarim_Degerler_Sifirdan_Uzaga_Yuvarlanmali(int pay, int payda, int scale, string beklenen)
    {
        new Fraction(pay, payda).ToDecimal(scale).Should().Be(D(beklenen));
    }

    [Fact]
    public void Fraction_ToDecimal_Sifir_Sifir_Vermeli()
    {
        Fraction.Zero.ToDecimal().Should().Be(0m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(29)]
    public void Fraction_ToDecimal_Gecersiz_Scale_Reddedilmeli(int scale)
    {
        var act = () => Fraction.One.ToDecimal(scale);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Fraction_ToDecimal_Decimal_Araligini_Asan_Deger_Tasma_Hatasi_Vermeli()
    {
        var act = () => new Fraction(BigInteger.Pow(10, 30), 1).ToDecimal();

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Fraction_FromDecimal_Kayipsiz_Tam_Kesir_Uretmeli_Yuvarlama_Yapmamali()
    {
        // 0.1240195 = 1240195 / 10^7 = 248039 / 2000000
        var f = Fraction.FromDecimal(0.1240195m);

        f.Numerator.Should().Be(248039);
        f.Denominator.Should().Be(2000000);
    }

    [Fact]
    public void Fraction_FromDecimal_Tapu_Alani_3539_74_Icin_176987_50_Vermeli()
    {
        var f = Fraction.FromDecimal(3539.74m);

        f.Numerator.Should().Be(176987);
        f.Denominator.Should().Be(50);
    }

    [Fact]
    public void Fraction_FromDecimal_Sondaki_Sifirlar_Degeri_Degistirmemeli()
    {
        Fraction.FromDecimal(1.10m).Should().Be(new Fraction(11, 10));
        Fraction.FromDecimal(1.1000000m).Should().Be(new Fraction(11, 10));
    }

    [Fact]
    public void Fraction_FromDecimal_Negatif_Ve_Sifir_Degerleri_Dogru_Cevirmeli()
    {
        Fraction.FromDecimal(-0.25m).Should().Be(new Fraction(-1, 4));
        Fraction.FromDecimal(0m).Should().Be(Fraction.Zero);
        Fraction.FromDecimal(0.00m).Should().Be(Fraction.Zero);
    }

    [Theory]
    [InlineData("26629.48")]
    [InlineData("3539.74")]
    [InlineData("0.1240195")]
    [InlineData("0.2015451")]
    [InlineData("-21.02")]
    [InlineData("0")]
    [InlineData("0.0000001")]
    public void Fraction_Decimal_Gidis_Donus_Degeri_Aynen_Korumali(string metin)
    {
        var d = D(metin);

        Fraction.FromDecimal(d).ToDecimal(10).Should().Be(d);
    }

    [Fact]
    public void Fraction_Decimal_Gidis_Donus_MaxValue_Ve_MinValue_Aynen_Korumali()
    {
        Fraction.FromDecimal(decimal.MaxValue).ToDecimal(0).Should().Be(decimal.MaxValue);
        Fraction.FromDecimal(decimal.MinValue).ToDecimal(0).Should().Be(decimal.MinValue);
    }

    [Fact]
    public void Fraction_FromDecimal_Ondalik_Toplam_Kayipsiz_Olmali()
    {
        // 0.1 + 0.2 = 0.3 kayipsiz (double'da 0.30000000000000004)
        (Fraction.FromDecimal(0.1m) + Fraction.FromDecimal(0.2m)).Should().Be(Fraction.FromDecimal(0.3m));
    }

    [Fact]
    public void Fraction_ToString_Pay_Bolu_Payda_Bicimi_Vermeli()
    {
        new Fraction(2, 4).ToString().Should().Be("1/2");
        new Fraction(-3, 9).ToString().Should().Be("-1/3");
        Fraction.Zero.ToString().Should().Be("0/1");
    }
}

/// <summary>
/// D31 Kural 2 — Tescil gosterimi: musatkil parsel "TAM", hisseli parselde payda = Alan_m2 x 100 (dm2).
/// </summary>
public sealed class TescilHissesiTests
{
    private static decimal D(string metin) => decimal.Parse(metin, CultureInfo.InvariantCulture);

    [Fact]
    public void TescilHissesi_Tek_Paydasli_Parsel_TAM_Gostermeli()
    {
        var tescil = TescilHissesi.Olustur(Fraction.One, 450.00m, paydasSayisi: 1);

        tescil.Tam.Should().BeTrue();
        tescil.ToString().Should().Be("TAM");
    }

    [Fact]
    public void TescilHissesi_Tek_Paydasta_Hisse_Bir_Degilse_Reddedilmeli()
    {
        var act = () => TescilHissesi.Olustur(new Fraction(1, 2), 450.00m, paydasSayisi: 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TescilHissesi_Hisseli_Parselde_Payda_Alan_Carpi_100_Dm2_Olmali()
    {
        // 3539.74 m2 -> payda 353974 (spec 25.2)
        var tescil = TescilHissesi.Olustur(new Fraction(66387, 353974), 3539.74m, paydasSayisi: 11);

        tescil.Tam.Should().BeFalse();
        tescil.Pay.Should().Be(66387);
        tescil.Payda.Should().Be(353974);
        tescil.ToString().Should().Be("66387/353974");
    }

    [Fact]
    public void TescilHissesi_Sadelesmis_Hisse_Tescil_Ortak_Paydasi_Korunarak_Yazilmali()
    {
        // 1/2 hisse, 450.00 m2 -> 22500/45000 (tescilde ortak payda sadelestirilmez)
        var tescil = TescilHissesi.Olustur(new Fraction(1, 2), 450.00m, paydasSayisi: 2);

        tescil.Pay.Should().Be(22500);
        tescil.Payda.Should().Be(45000);
    }

    [Fact]
    public void TescilHissesi_Hisse_Dm2_Tam_Sayisi_Degilse_Reddedilmeli()
    {
        // 1/3 * 353974 tam sayi degil -> once LargestRemainderAllocator ile dagitilmali
        var act = () => TescilHissesi.Olustur(new Fraction(1, 3), 3539.74m, paydasSayisi: 3);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TescilHissesi_Alan_Ikiden_Fazla_Ondalik_Icerirse_Reddedilmeli()
    {
        var act = () => TescilHissesi.Olustur(new Fraction(1, 2), 3539.745m, paydasSayisi: 2);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TescilHissesi_Paydas_Sayisi_Sifir_Veya_Negatifse_Reddedilmeli(int paydasSayisi)
    {
        var act = () => TescilHissesi.Olustur(Fraction.One, 100m, paydasSayisi);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void TescilHissesi_Alan_Sifir_Veya_Negatifse_Reddedilmeli(int alan)
    {
        var act = () => TescilHissesi.Olustur(new Fraction(1, 2), alan, paydasSayisi: 2);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("3539.74", 353974)]
    [InlineData("26629.48", 2662948)]
    [InlineData("450.00", 45000)]
    [InlineData("0.01", 1)]
    public void TescilHissesi_PaydaDm2_Alandan_Dogru_Tam_Sayi_Uretmeli(string alan, int beklenen)
    {
        TescilHissesi.PaydaDm2(D(alan)).Should().Be(beklenen);
    }

    [Fact]
    public void TescilHissesi_Tek_Paydasta_Gecersiz_Alan_Reddedilmeli()
    {
        var act = () => TescilHissesi.Olustur(Fraction.One, -5m, paydasSayisi: 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TescilHissesi_Hisse_Bir_Den_Buyuk_Veya_Negatifse_Reddedilmeli()
    {
        var act1 = () => TescilHissesi.Olustur(new Fraction(3, 2), 100m, paydasSayisi: 2);
        var act2 = () => TescilHissesi.Olustur(new Fraction(-1, 2), 100m, paydasSayisi: 2);

        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TescilHissesi_Gecersiz_Tam_Pay_Payda_Kombinasyonlari_Olustur_Ile_Uretilememeli()
    {
        // BULGU-2: Tam/Pay/Payda gecersiz kombinasyonlari (payda 0, negatif pay, pay > payda,
        // Tam=true iken pay/payda != 1) hicbir giris yolundan elde edilemez; Olustur hepsini reddeder.
        var gecersizler = new List<Action>
        {
            () => TescilHissesi.Olustur(new Fraction(1, 2), 0m, paydasSayisi: 2),            // payda 0
            () => TescilHissesi.Olustur(new Fraction(-1, 2), 100m, paydasSayisi: 2),          // negatif pay
            () => TescilHissesi.Olustur(new Fraction(3, 2), 100m, paydasSayisi: 2),           // pay > payda
            () => TescilHissesi.Olustur(new Fraction(1, 2), 100m, paydasSayisi: 1),           // Tam ama hisse != 1
            () => TescilHissesi.Olustur(new Fraction(1, 3), 100m, paydasSayisi: 2),           // tam sayi dm2 degil
            () => TescilHissesi.Olustur(new Fraction(1, 2), 100m, paydasSayisi: 0),           // paydas yok
        };

        foreach (var act in gecersizler)
        {
            act.Should().Throw<ArgumentException>(); // ArgumentOutOfRangeException da ArgumentException'dir
        }
    }

    [Fact]
    public void TescilHissesi_Null_Veya_Default_Ornek_Olmamali_Yapici_Private_Olmali()
    {
        var tip = typeof(TescilHissesi);

        tip.IsClass.Should().BeTrue("default(struct) geçersiz 0/0 örnek üretmesin; referans tipi default'u null'dur");
        tip.IsSealed.Should().BeTrue();
        tip.GetConstructors().Should().BeEmpty("public yapici yok");
        tip.GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Where(c => c.GetParameters().Length == 3).Should().OnlyContain(c => c.IsPrivate);
        tip.GetProperties().Should().OnlyContain(p => p.SetMethod == null, "'with' ile alan degistirilemez (init/set yok)");

        TescilHissesi.Olustur(Fraction.One, 100m, 1).Should().NotBeNull();
        TescilHissesi.Olustur(new Fraction(1, 2), 100m, 2).Should().NotBeNull();
    }
}

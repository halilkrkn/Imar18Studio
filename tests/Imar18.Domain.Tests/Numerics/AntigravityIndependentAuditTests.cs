using System.Numerics;
using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Numerics;
using Xunit;

namespace Imar18.Domain.Tests.Numerics;

/// <summary>
/// Google Antigravity Bağımsız Test ve Denetim Paketi (P1 Sayısal Çekirdek Denetimi).
/// Bu sınıftaki testler bağımsız referans değerleriyle (koddaki ara fonksiyonlar kullanılmadan)
/// elle hesaplanmış matematiksel sınırlara dayanır.
/// </summary>
public sealed class AntigravityIndependentAuditTests
{
    private static AllocationWeight W(string id, int pay, int payda) => new(id, new Fraction(pay, payda));

    private static Dictionary<string, BigInteger> Sozluk(Result<IReadOnlyList<AllocationShare>> sonuc)
    {
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.Code : "başarılı olmalı");
        return sonuc.Value.ToDictionary(p => p.Id, p => p.Units, StringComparer.Ordinal);
    }

    private static BigInteger Topla(Result<IReadOnlyList<AllocationShare>> sonuc)
    {
        sonuc.IsSuccess.Should().BeTrue();
        return sonuc.Value.Select(p => p.Units).Aggregate(BigInteger.Zero, (acc, u) => acc + u);
    }

    // =========================================================================
    // 3.b: Fraction default struct derinlemesine denetim testleri
    // =========================================================================

    [Fact]
    public void Fraction_Default_Deger_SifirBoluBir_Ile_Tam_Esit_Ve_Uyumlu_Olmali()
    {
        var varsayilan = default(Fraction);
        var acikSifir = new Fraction(0, 1);
        var sifirFarkliPayda = new Fraction(0, 99);

        // Numerator & Denominator
        varsayilan.Numerator.Should().Be(BigInteger.Zero);
        varsayilan.Denominator.Should().Be(BigInteger.One);
        varsayilan.IsZero.Should().BeTrue();
        varsayilan.Sign.Should().Be(0);

        // Equals & GetHashCode
        varsayilan.Equals(acikSifir).Should().BeTrue();
        acikSifir.Equals(varsayilan).Should().BeTrue();
        varsayilan.Equals(sifirFarkliPayda).Should().BeTrue();
        (varsayilan == acikSifir).Should().BeTrue();
        (varsayilan != acikSifir).Should().BeFalse();
        varsayilan.GetHashCode().Should().Be(acikSifir.GetHashCode());

        // CompareTo
        varsayilan.CompareTo(acikSifir).Should().Be(0);
        acikSifir.CompareTo(varsayilan).Should().Be(0);
        varsayilan.CompareTo(new Fraction(1, 10)).Should().BeNegative();
        varsayilan.CompareTo(new Fraction(-1, 10)).Should().BePositive();

        // Aritmetik islemler
        (varsayilan + new Fraction(2, 5)).Should().Be(new Fraction(2, 5));
        (new Fraction(2, 5) + varsayilan).Should().Be(new Fraction(2, 5));
        (varsayilan - new Fraction(2, 5)).Should().Be(new Fraction(-2, 5));
        (varsayilan * new Fraction(2, 5)).Should().Be(Fraction.Zero);
        (varsayilan / new Fraction(2, 5)).Should().Be(Fraction.Zero);

        // Bolen default(Fraction) oldugunda DivideByZeroException
        var bolmeHata = () => new Fraction(1, 2) / varsayilan;
        bolmeHata.Should().Throw<DivideByZeroException>();

        // ToString & ToDecimal & Floor
        varsayilan.ToString().Should().Be("0/1");
        varsayilan.ToDecimal().Should().Be(0m);
        varsayilan.Floor().Should().Be(BigInteger.Zero);
    }

    // =========================================================================
    // 3.h: Bağımsız referansla (elle hesaplanmış) 5 yeni sınır testi
    // =========================================================================

    /// <summary>
    /// Test 1: Tek kalem dağıtımı.
    /// Bağımsız Referans: 1 malik, 250 birim. Dağıtım kotası tam 250, kalan 0.
    /// Sonuç: Doğrudan 250 birim tek kaleme tahsis edilmelidir.
    /// </summary>
    [Fact]
    public void BagimsizReferans_1_Tek_Kalem_Tum_Toplami_Almali()
    {
        const int toplamBirim = 250;
        var girdi = new[] { W("TEK-MALIK", 1, 1) };

        var paylar = Sozluk(LargestRemainderAllocator.Allocate(toplamBirim, girdi));

        paylar.Should().ContainSingle();
        paylar["TEK-MALIK"].Should().Be(250);
    }

    /// <summary>
    /// Test 2: Eşit kalanlı 3 eşit paydaş (100 birim toplam).
    /// Bağımsız Referans:
    /// - Kalemler: P1: 1/3, P2: 1/3, P3: 1/3. Toplam: 100 birim.
    /// - Kota_i = 100 * (1/3) = 33 + 1/3 = 33.3333...
    /// - Tabanlar: P1=33, P2=33, P3=33. Toplam taban = 99. Eksik = 1.
    /// - Kalanlar eşit (1/3), ağırlıklar eşit (1/3).
    /// - StringComparer.Ordinal Id sırası: "P1" &lt; "P2" &lt; "P3".
    /// - Bonus (+1) yalnızca ilk sıradaki "P1"'e verilir.
    /// - Beklenen: P1 = 34, P2 = 33, P3 = 33. Toplam = 100.
    /// </summary>
    [Fact]
    public void BagimsizReferans_2_Uc_Esit_Paydas_Toplam_100_Birim_Kalan_TieBreak_Ordinal_Id_Almali()
    {
        const int toplamBirim = 100;
        // Girdi sırasını kasten karışık veriyoruz: P3, P1, P2
        var girdi = new[] { W("P3", 1, 3), W("P1", 1, 3), W("P2", 1, 3) };

        var paylar = Sozluk(LargestRemainderAllocator.Allocate(toplamBirim, girdi));

        paylar["P1"].Should().Be(34);
        paylar["P2"].Should().Be(33);
        paylar["P3"].Should().Be(33);
        (paylar["P1"] + paylar["P2"] + paylar["P3"]).Should().Be(100);
    }

    /// <summary>
    /// Test 3: Sıfır ağırlıklı kalem içeren dağıtım.
    /// Bağımsız Referans:
    /// - Kalemler: A: 1/2, B: 0/1, C: 1/4, D: 1/4. Toplam: 10 birim.
    /// - Toplam ağırlık = 1/2 + 0 + 1/4 + 1/4 = 1.
    /// - Kotalar:
    ///   A: 10 * 1/2 = 5.0  -&gt; Taban = 5, Kalan = 0
    ///   B: 10 * 0   = 0.0  -&gt; Taban = 0, Kalan = 0
    ///   C: 10 * 1/4 = 2.5  -&gt; Taban = 2, Kalan = 0.5
    ///   D: 10 * 1/4 = 2.5  -&gt; Taban = 2, Kalan = 0.5
    /// - Taban toplamı = 5 + 0 + 2 + 2 = 9. Eksik = 10 - 9 = 1.
    /// - Kalanlar: C ve D (0.5) en büyük kalanlardır.
    /// - Ağırlıklar eşit (1/4), Id Ordinal: "C" &lt; "D".
    /// - Bonus (+1) "C"'ye gider.
    /// - Beklenen: A = 5, B = 0, C = 3, D = 2. Toplam = 10.
    /// </summary>
    [Fact]
    public void BagimsizReferans_3_Sifir_Agirlikli_Kalem_Sifir_Birim_Almali_Ve_Kalanlar_Dogru_Dagitilmali()
    {
        const int toplamBirim = 10;
        var girdi = new[] { W("A", 1, 2), W("B", 0, 1), W("C", 1, 4), W("D", 1, 4) };

        var paylar = Sozluk(LargestRemainderAllocator.Allocate(toplamBirim, girdi));

        paylar["A"].Should().Be(5);
        paylar["B"].Should().Be(0);
        paylar["C"].Should().Be(3);
        paylar["D"].Should().Be(2);
        (paylar["A"] + paylar["B"] + paylar["C"] + paylar["D"]).Should().Be(10);
    }

    /// <summary>
    /// Test 4: Çok küçük pay (1 / 1_000_000) vs Çok büyük pay (999_999 / 1_000_000).
    /// Bağımsız Referans:
    /// - Kalemler: KUCUK: 1/1_000_000, BUYUK: 999_999/1_000_000. Toplam: 5 birim.
    /// - Kota BUYUK = 5 * 999_999 / 1_000_000 = 4.999995 -&gt; Taban = 4, Kalan = 0.999995.
    /// - Kota KUCUK = 5 * 1 / 1_000_000       = 0.000005 -&gt; Taban = 0, Kalan = 0.000005.
    /// - Taban toplamı = 4. Eksik = 1.
    /// - En büyük kalan BUYUK (0.999995 &gt; 0.000005) -&gt; BUYUK +1 alır.
    /// - Beklenen: BUYUK = 5, KUCUK = 0. Toplam = 5.
    /// </summary>
    [Fact]
    public void BagimsizReferans_4_Cok_Kucuk_Pay_Sifir_Alirken_Buyuk_Pay_Tam_Almali()
    {
        const int toplamBirim = 5;
        var girdi = new[]
        {
            W("KUCUK", 1, 1_000_000),
            W("BUYUK", 999_999, 1_000_000),
        };

        var paylar = Sozluk(LargestRemainderAllocator.Allocate(toplamBirim, girdi));

        paylar["BUYUK"].Should().Be(5);
        paylar["KUCUK"].Should().Be(0);
        (paylar["BUYUK"] + paylar["KUCUK"]).Should().Be(5);
    }

    /// <summary>
    /// Test 5: Çok büyük BigInteger (10^30 birim) ve 3 eşit paydaş.
    /// Bağımsız Referans:
    /// - 10^30 sayısı 3'e bölündüğünde kalan 1'dir (çünkü 10 == 1 mod 3 =&gt; 10^30 == 1 mod 3).
    /// - Kota = (10^30) / 3 = q + 1/3 (burada q = (10^30 - 1) / 3).
    /// - Tabanlar: X: q, Y: q, Z: q.
    /// - Dağıtılan = 3q = 10^30 - 1. Eksik = 1.
    /// - Kalanlar eşit (1/3), ağırlıklar eşit (1/3).
    /// - Ordinal Id sırası: "X" &lt; "Y" &lt; "Z" -&gt; "X" bonus (+1) alır.
    /// - Beklenen: X = q + 1, Y = q, Z = q.
    /// - Toplam: 3q + 1 = 10^30.
    /// </summary>
    [Fact]
    public void BagimsizReferans_5_Cok_Buyuk_BigInteger_Ve_Uc_Esit_Paydas_Sigma_Korunmali()
    {
        var toplamBirim = BigInteger.Pow(10, 30);
        var beklenenQ = (toplamBirim - BigInteger.One) / 3;

        var girdi = new[] { W("Z", 1, 3), W("X", 1, 3), W("Y", 1, 3) };

        var paylar = Sozluk(LargestRemainderAllocator.Allocate(toplamBirim, girdi));

        paylar["X"].Should().Be(beklenenQ + BigInteger.One);
        paylar["Y"].Should().Be(beklenenQ);
        paylar["Z"].Should().Be(beklenenQ);
        (paylar["X"] + paylar["Y"] + paylar["Z"]).Should().Be(toplamBirim);
    }
}

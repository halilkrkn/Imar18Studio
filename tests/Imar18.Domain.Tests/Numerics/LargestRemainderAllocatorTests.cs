using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Numerics;
using Xunit;

namespace Imar18.Domain.Tests.Numerics;

/// <summary>
/// P1.2 — LargestRemainderAllocator (AC-PILOT-03 / TC: LargestRemainderAllocatorTests).
/// Beklenen degerler elle hesaplanmistir (kota = toplam x agirlik; taban + en buyuk kalan);
/// SORU-011: resmi cetveldeki 353975 toplami REFERANS DEGILDIR, Sigma cikti = payda dogrulanir.
/// </summary>
public sealed class LargestRemainderAllocatorTests
{
    private static AllocationWeight W(string id, int pay, int payda) => new(id, new Fraction(pay, payda));

    private static BigInteger Topla(IEnumerable<AllocationShare> paylar) =>
        paylar.Select(p => p.Units).Aggregate(BigInteger.Zero, (a, b) => a + b);

    private static Dictionary<string, BigInteger> Sozluk(IEnumerable<AllocationShare> paylar) =>
        paylar.ToDictionary(p => p.Id, p => p.Units, StringComparer.Ordinal);

    private static IReadOnlyList<AllocationShare> Basarili(Result<IReadOnlyList<AllocationShare>> sonuc)
    {
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.Code : "basarili olmali");
        return sonuc.Value;
    }

    // ---------- Temel davranis ve sinirlar ----------

    [Fact]
    public void LargestRemainder_Tek_Kalem_Tum_Toplami_Almali()
    {
        var paylar = Basarili(LargestRemainderAllocator.Allocate(2662948, [W("M1", 1, 1)]));

        paylar.Should().ContainSingle().Which.Units.Should().Be(2662948);
    }

    [Fact]
    public void LargestRemainder_Tam_Bolunen_Kotalar_Kalan_Dagitmadan_Aynen_Verilmeli()
    {
        // 100 birim: 1/2, 1/4, 1/4 -> 50, 25, 25
        var paylar = Sozluk(Basarili(LargestRemainderAllocator.Allocate(100, [W("A", 1, 2), W("B", 1, 4), W("C", 1, 4)])));

        paylar["A"].Should().Be(50);
        paylar["B"].Should().Be(25);
        paylar["C"].Should().Be(25);
    }

    [Fact]
    public void LargestRemainder_Uc_Esit_Hissede_10_Birim_Sigma_10_Ve_Id_Sirasiyla_Dagitmali()
    {
        // 10/3 = 3 kalan 1/3 her biri; taban toplam 9, eksik 1 -> esit kalan, esit pay -> Id sozluk sirasinda ilk ("A")
        var paylar = Sozluk(Basarili(LargestRemainderAllocator.Allocate(10, [W("C", 1, 3), W("A", 1, 3), W("B", 1, 3)])));

        paylar["A"].Should().Be(4);
        paylar["B"].Should().Be(3);
        paylar["C"].Should().Be(3);
    }

    [Fact]
    public void LargestRemainder_Sifir_Agirlikli_Kalem_Sifir_Almali_Toplam_Korunmali()
    {
        var paylar = Basarili(LargestRemainderAllocator.Allocate(7, [W("A", 1, 2), W("B", 0, 1), W("C", 1, 2)]));
        var s = Sozluk(paylar);

        s["B"].Should().Be(0);
        Topla(paylar).Should().Be(7);
        s["A"].Should().Be(4); // 3.5 / 3.5 esit kalan, esit pay -> Id ilk olan (A) yukari
        s["C"].Should().Be(3);
    }

    [Fact]
    public void LargestRemainder_Toplam_Sifir_Tum_Paylar_Sifir_Olmali()
    {
        var paylar = Basarili(LargestRemainderAllocator.Allocate(0, [W("A", 1, 3), W("B", 2, 3)]));

        paylar.Should().OnlyContain(p => p.Units.IsZero);
    }

    [Fact]
    public void LargestRemainder_Cok_Kucuk_Pay_Sifir_Birim_Alabilmeli_Sigma_Korunmali()
    {
        // 1/1_000_000 payli kalem 100 birimden 0 alir
        var paylar = Basarili(LargestRemainderAllocator.Allocate(100, [W("A", 999_999, 1_000_000), W("B", 1, 1_000_000)]));
        var s = Sozluk(paylar);

        s["A"].Should().Be(100);
        s["B"].Should().Be(0);
    }

    [Fact]
    public void LargestRemainder_Buyuk_Toplamda_BigInteger_Tasmadan_Referans_Sonucu_Vermeli()
    {
        // 10^40 = 3*q + 1 ; q = 333...3 (40 hane). Uc esit kalan -> "A" +1.
        var toplam = BigInteger.Pow(10, 40);
        var q = (toplam - 1) / 3;

        var s = Sozluk(Basarili(LargestRemainderAllocator.Allocate(toplam, [W("B", 1, 3), W("C", 1, 3), W("A", 1, 3)])));

        s["A"].Should().Be(q + 1);
        s["B"].Should().Be(q);
        s["C"].Should().Be(q);
        (s["A"] + s["B"] + s["C"]).Should().Be(toplam);
    }

    [Fact]
    public void LargestRemainder_Cok_Buyuk_Paydali_Agirliklarla_Tam_Rasyonel_Hesap_Yapmali()
    {
        var on50 = BigInteger.Pow(10, 50);
        var a = new AllocationWeight("A", new Fraction(on50 - 1, on50));
        var b = new AllocationWeight("B", new Fraction(1, on50));

        var s = Sozluk(Basarili(LargestRemainderAllocator.Allocate(1000, [a, b])));

        // A kotasi 1000 - 1000/10^50 -> taban 999, kalan ~1 ; B kotasi ~0 -> A +1
        s["A"].Should().Be(1000);
        s["B"].Should().Be(0);
    }

    // ---------- Esit kalan: kararli sira (spec 3.2 madde 4) ----------

    [Fact]
    public void LargestRemainder_Esit_Kalanda_Buyuk_Pay_Oncelikli_Olmali_Id_Degil()
    {
        // 3 birim: A=1/6 (kota 0.5), B=1/2 (kota 1.5), C=1/3 (kota 1.0)
        // Kalanlar: A .5, B .5, C 0 ; taban toplam 2 -> 1 eksik; esit kalanda BUYUK PAY (B) kazanir (Id olsa A kazanirdi).
        var s = Sozluk(Basarili(LargestRemainderAllocator.Allocate(3, [W("A", 1, 6), W("B", 1, 2), W("C", 1, 3)])));

        s["A"].Should().Be(0);
        s["B"].Should().Be(2);
        s["C"].Should().Be(1);
    }

    [Fact]
    public void LargestRemainder_Esit_Kalan_Esit_Payda_Id_StringComparer_Ordinal_Sirasi_Olmali()
    {
        // Ordinal: "Z1" < "a1" (buyuk harf kod noktasi kucuktur). Kulturel siralamada "a1" once gelirdi.
        var s = Sozluk(Basarili(LargestRemainderAllocator.Allocate(1, [W("a1", 1, 2), W("Z1", 1, 2)])));

        s["Z1"].Should().Be(1);
        s["a1"].Should().Be(0);
    }

    [Fact]
    public void LargestRemainder_Girdi_Sirasi_Degisse_Ayni_Sonuc_Vermeli_Determinizm()
    {
        var kalemler = new[]
        {
            W("M3", 1, 7), W("M1", 2, 7), W("M2", 1, 7), W("M5", 1, 14), W("M4", 5, 14),
        };
        var referans = Sozluk(Basarili(LargestRemainderAllocator.Allocate(1001, kalemler)));

        var rnd = new Random(18);
        for (var deneme = 0; deneme < 25; deneme++)
        {
            var karisik = kalemler.OrderBy(_ => rnd.Next()).ToArray();
            Sozluk(Basarili(LargestRemainderAllocator.Allocate(1001, karisik))).Should().BeEquivalentTo(referans);
        }
    }

    [Fact]
    public void LargestRemainder_Cikti_Id_Ordinal_Sirasinda_Dondurulmeli()
    {
        var paylar = Basarili(LargestRemainderAllocator.Allocate(10, [W("b", 1, 3), W("Z", 1, 3), W("a", 1, 3)]));

        paylar.Select(p => p.Id).Should().Equal("Z", "a", "b");
    }

    [Fact]
    public void LargestRemainder_Ayni_Girdi_Tekrar_Calistirilinca_Ayni_Sonucu_Vermeli()
    {
        var kalemler = new[] { W("A", 1, 3), W("B", 1, 3), W("C", 1, 3) };

        var bir = Basarili(LargestRemainderAllocator.Allocate(10, kalemler));
        var iki = Basarili(LargestRemainderAllocator.Allocate(10, kalemler));

        bir.Should().Equal(iki);
    }

    // ---------- Sigma garantisi (ozellik testi) ----------

    [Fact]
    public void LargestRemainder_Rastgele_Girdilerde_Sigma_Toplama_Esit_Ve_Sapma_Birden_Kucuk_Olmali()
    {
        var rnd = new Random(2026);
        for (var deneme = 0; deneme < 200; deneme++)
        {
            var n = rnd.Next(1, 45);
            var tamPaylar = Enumerable.Range(0, n).Select(_ => rnd.Next(0, 500)).ToArray();
            if (tamPaylar.Sum() == 0)
            {
                tamPaylar[0] = 1;
            }

            var payda = tamPaylar.Sum();
            var kalemler = tamPaylar.Select((p, i) => W($"M{i:D3}", p, payda)).ToArray();
            var toplam = (BigInteger)rnd.Next(0, 5_000_000);

            var paylar = Basarili(LargestRemainderAllocator.Allocate(toplam, kalemler));

            Topla(paylar).Should().Be(toplam);
            for (var i = 0; i < n; i++)
            {
                var kota = new Fraction(toplam * tamPaylar[i], payda);
                var birim = paylar.Single(p => p.Id == $"M{i:D3}").Units;
                (birim == kota.Floor() || birim == kota.Floor() + 1).Should().BeTrue(
                    "her pay kotanin tabani veya tabani+1 olmali");
            }
        }
    }

    // ---------- Hata yollari (ErrorCatalog) ----------

    [Theory]
    [InlineData(1, 2)]
    [InlineData(1, 1)]
    public void LargestRemainder_Agirlik_Toplami_Bir_Degilse_ERR_TAPU_003_Donmeli(int pay, int payda)
    {
        // 1/2 + 1/3 = 5/6 ; ikinci durum: 1 + 1/3 > 1
        var sonuc = LargestRemainderAllocator.Allocate(100, [W("A", pay, payda), W("B", 1, 3)]);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.TapuHisseToplamiBirDegil);
    }

    [Fact]
    public void LargestRemainder_Negatif_Agirlik_ERR_TAPU_004_Donmeli()
    {
        var sonuc = LargestRemainderAllocator.Allocate(100, [W("A", 3, 2), W("B", -1, 2)]);

        sonuc.Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Fact]
    public void LargestRemainder_Yinelenen_Id_ERR_TAPU_004_Donmeli()
    {
        var sonuc = LargestRemainderAllocator.Allocate(100, [W("A", 1, 2), W("A", 1, 2)]);

        sonuc.Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void LargestRemainder_Bos_Id_ERR_TAPU_004_Donmeli(string id)
    {
        var sonuc = LargestRemainderAllocator.Allocate(100, [new AllocationWeight(id, Fraction.One)]);

        sonuc.Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Fact]
    public void LargestRemainder_Bos_Liste_ERR_TAPU_004_Donmeli()
    {
        LargestRemainderAllocator.Allocate(100, []).Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Fact]
    public void LargestRemainder_Negatif_Toplam_ERR_TAPU_004_Donmeli()
    {
        LargestRemainderAllocator.Allocate(-1, [W("A", 1, 1)]).Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Fact]
    public void LargestRemainder_Null_Liste_ArgumentNullException_Firlatmali()
    {
        var act = () => LargestRemainderAllocator.Allocate(1, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ---------- m2 / dm2 yardimcisi (0.01 m2 = 1 dm2) ----------

    [Fact]
    public void LargestRemainder_AllocateAlanM2_Toplami_Yuz_Katina_Cevirip_Dagitmali()
    {
        var paylar = Basarili(LargestRemainderAllocator.AllocateAlanM2(26629.48m, [W("A", 1, 2), W("B", 1, 2)]));

        Topla(paylar).Should().Be(2662948);
        paylar.Should().OnlyContain(p => p.Units == 1331474);
        paylar[0].ToAlanM2().Should().Be(13314.74m);
    }

    [Fact]
    public void LargestRemainder_AllocateAlanM2_Ikiden_Fazla_Ondalik_ERR_TAPU_004_Donmeli()
    {
        LargestRemainderAllocator.AllocateAlanM2(10.005m, [W("A", 1, 1)]).Error.Code
            .Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    [Fact]
    public void LargestRemainder_AllocateAlanM2_Negatif_Alan_ERR_TAPU_004_Donmeli()
    {
        LargestRemainderAllocator.AllocateAlanM2(-0.01m, [W("A", 1, 1)]).Error.Code
            .Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
    }

    // ---------- Reviewer bulgulari (KVKK, anlik kopya, null sirasi) ----------

    [Fact]
    public void LargestRemainder_Hata_Teknik_Mesajinda_Malik_Kimligi_Sizdirilmamali_KVKK()
    {
        var sonuc = LargestRemainderAllocator.Allocate(100, [W("12345678901", 1, 2), W("12345678901", 1, 2)]);

        sonuc.Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        sonuc.Error.TechnicalMessage.Should().NotContain("12345678901");
        sonuc.Error.TechnicalMessage.Should().NotContain("{");
    }

    [Fact]
    public void LargestRemainder_Negatif_Agirlik_Hatasi_Kimlik_Degil_Sira_Numarasi_Icermeli()
    {
        var sonuc = LargestRemainderAllocator.Allocate(100, [W("AD-SOYAD", 3, 2), W("B", -1, 2)]);

        sonuc.Error.TechnicalMessage.Should().NotContain("AD-SOYAD").And.NotContain("{");
    }

    [Fact]
    public void LargestRemainder_Agirlik_Toplami_Hatasinda_Yer_Tutucu_Kalmamali()
    {
        var sonuc = LargestRemainderAllocator.Allocate(100, [W("A", 1, 2)]);

        sonuc.Error.Code.Should().Be(ErrorCodes.TapuHisseToplamiBirDegil);
        sonuc.Error.TechnicalMessage.Should().Contain("1/2").And.NotContain("{");
    }

    [Fact]
    public void LargestRemainder_AllocateAlanM2_Null_Agirlik_ArgumentNullException_Firlatmali()
    {
        var act = () => LargestRemainderAllocator.AllocateAlanM2(-1m, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void LargestRemainder_Tembel_Koleksiyon_Tek_Kez_Numaralandirilmali()
    {
        var sayac = new SayacliKoleksiyon([W("A", 1, 3), W("B", 1, 3), W("C", 1, 3)]);

        Basarili(LargestRemainderAllocator.Allocate(10, sayac));

        sayac.NumaralandirmaSayisi.Should().Be(1);
    }

    [Fact]
    public void LargestRemainder_Id_Buyuk_Kucuk_Harf_Ve_Bosluk_Farkli_Kimlik_Sayilmali()
    {
        var paylar = Basarili(LargestRemainderAllocator.Allocate(4, [W("A", 1, 4), W("a", 1, 4), W(" A", 1, 4), W("A ", 1, 4)]));

        paylar.Should().HaveCount(4);
        Topla(paylar).Should().Be(4);
    }

    [Fact]
    public void LargestRemainder_AllocateAlanM2_Sifir_Alan_Tum_Paylar_Sifir_Olmali()
    {
        Basarili(LargestRemainderAllocator.AllocateAlanM2(0m, [W("A", 1, 2), W("B", 1, 2)]))
            .Should().OnlyContain(p => p.Units.IsZero);
    }

    [Fact]
    public void LargestRemainder_AllocateAlanM2_Sondaki_Sifirli_Alan_Kabul_Edilmeli()
    {
        Topla(Basarili(LargestRemainderAllocator.AllocateAlanM2(10.500m, [W("A", 1, 1)]))).Should().Be(1050);
    }

    private sealed class SayacliKoleksiyon(IReadOnlyList<AllocationWeight> kaynak) : IReadOnlyCollection<AllocationWeight>
    {
        public int NumaralandirmaSayisi { get; private set; }

        public int Count => kaynak.Count;

        public IEnumerator<AllocationWeight> GetEnumerator()
        {
            NumaralandirmaSayisi++;
            return kaynak.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // ---------- Proje-B altin veri (sentetik, pilot-3-shared-parcel) ----------

    private static JsonElement ProjeBAltinVeri() =>
        JsonDocument.Parse(File.ReadAllText(Imar18.TestDestek.TestDataLocator.Sentetik("pilot-3-shared-parcel", "saha_proje_2_hisse_golden.json"))).RootElement.Clone();

    private static decimal Dec(JsonElement e) => e.GetDecimal();

    [Fact]
    public void ProjeB_Dagitilan_Toplam_Tam_26629_48_m2_Olmali_Ada_Ve_Yol_Alanlariyla_Birebir()
    {
        var altin = ProjeBAltinVeri();
        var r1 = Dec(altin.GetProperty("metrics").GetProperty("toplamKadastroAlani_R1"));
        var yol = Dec(altin.GetProperty("metrics").GetProperty("kamuyaAyrilanYol"));
        var adalar = altin.GetProperty("blocks").EnumerateArray()
            .ToDictionary(b => $"imar-{b.GetProperty("imarAda").GetInt32()}", b => Dec(b.GetProperty("totalArea")));
        adalar["yol-Y1"] = yol;

        r1.Should().Be(26629.48m);
        var kalemler = adalar.Select(a => new AllocationWeight(a.Key, Fraction.FromDecimal(a.Value) / Fraction.FromDecimal(r1))).ToList();

        var paylar = Basarili(LargestRemainderAllocator.AllocateAlanM2(r1, kalemler));

        Topla(paylar).Should().Be(2662948, "dagitilan toplam tam 26629.48 m2 (x100)");
        foreach (var pay in paylar)
        {
            pay.ToAlanM2().Should().Be(adalar[pay.Id], $"{pay.Id} kotasi tam sayi birimdir, sapma olmamali");
        }
    }

    [Fact]
    public void ProjeB_Her_Imar_Parselinde_Pay_Toplami_ParselAlani_Carpi_100_Olmali_44_Anonim_Paydas()
    {
        var altin = ProjeBAltinVeri();
        const int paydasSayisi = 44; // 40+ paydas; kimlikler sentetik, agirliklar deterministik
        var agirliklar = Enumerable.Range(1, paydasSayisi).Select(k => ((k * 37) % 23) + 1).ToArray();
        var agirlikToplami = agirliklar.Sum();

        foreach (var blok in altin.GetProperty("blocks").EnumerateArray())
        {
            var alan = Dec(blok.GetProperty("totalArea"));
            var kalemler = agirliklar.Select((w, i) => W($"MALIK-{i + 1:D2}", w, agirlikToplami)).ToList();

            var paylar = Basarili(LargestRemainderAllocator.AllocateAlanM2(alan, kalemler));

            var beklenenToplam = (BigInteger)(alan * 100m);
            Topla(paylar).Should().Be(beklenenToplam, $"imar ada {blok.GetProperty("imarAda").GetInt32()}: Sigma pay = Alan x 100");
            paylar.Should().HaveCount(paydasSayisi);
        }
    }

    [Fact]
    public void ProjeB_101_1_Ornek_Paylar_Largest_Remainder_ile_353974_Toplamina_Dengelenmeli_SORU_011()
    {
        // Resmi cikti ornek paylari: 66387, 66396, 66397 x3, 7334 x2, 7333 -> toplam 353975 (payda 353974).
        // SORU-011 acik: 353975 REFERANS DEGILDIR; LR ciktisinin toplami payda (353974) olmali.
        var altin = ProjeBAltinVeri();
        var ornek = altin.GetProperty("blocks").EnumerateArray()
            .First(b => b.GetProperty("imarAda").GetInt32() == 101)
            .GetProperty("sampleShares_101_1").EnumerateArray().Select(e => e.GetString()!).ToList();

        var resmiPaylar = new List<int>();
        foreach (var satir in ornek)
        {
            var m = Regex.Match(satir, @"^(\d+)/(\d+)(?: x(\d+))?", RegexOptions.CultureInvariant);
            m.Success.Should().BeTrue(satir);
            m.Groups[2].Value.Should().Be("353974");
            var adet = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 1;
            for (var i = 0; i < adet; i++)
            {
                resmiPaylar.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            }
        }

        resmiPaylar.Sum().Should().Be(353975, "girdi ornegi resmi cikti toplamidir (referans degil)");
        resmiPaylar.Should().HaveCount(8);

        var resmiToplam = resmiPaylar.Sum();
        var kalemler = resmiPaylar.Select((p, i) => W($"M{i + 1}", p, resmiToplam)).ToList();
        const int parselDm2 = 353974; // 3539.74 m2 x 100

        var paylar = Basarili(LargestRemainderAllocator.Allocate(parselDm2, kalemler));
        var s = Sozluk(paylar);

        Topla(paylar).Should().Be(parselDm2, "Sigma cikti = payda (353974)");
        Topla(paylar).Should().NotBe(353975);

        // Elle referans: kota_i = s_i * 353974/353975 = s_i - s_i/353975 -> taban = s_i - 1, kalan = 1 - s_i/353975.
        // Toplam taban 353967 -> 7 birim dagilir; en kucuk s en buyuk kalan. 66397'lik uc paydas (M3,M4,M5) esit kalan:
        // esit pay -> Id Ordinal ilk ikisi (M3, M4) +1 alir, M5 almaz.
        s["M1"].Should().Be(66387);  // 66387
        s["M2"].Should().Be(66396);  // 66396
        s["M3"].Should().Be(66397);
        s["M4"].Should().Be(66397);
        s["M5"].Should().Be(66396);  // en dusuk kalan: +1 almaz
        s["M6"].Should().Be(7334);
        s["M7"].Should().Be(7334);
        s["M8"].Should().Be(7333);
    }

    [Fact]
    public void ProjeB_101_1_Dagitilan_Paylar_Tescil_Gosterimine_Cevrilebilmeli()
    {
        var kalemler = new[] { W("M1", 66387, 353974), W("M2", 287587, 353974) };
        var paylar = Basarili(LargestRemainderAllocator.Allocate(353974, kalemler));

        var tescil = paylar.Select(p => TescilHissesi.Olustur(new Fraction(p.Units, 353974), 3539.74m, paydasSayisi: 2)).ToList();

        tescil.Select(t => t.ToString()).Should().Equal("66387/353974", "287587/353974");
        tescil.Select(t => t.Pay).Aggregate(BigInteger.Zero, (a, b) => a + b).Should().Be(353974);
    }
}

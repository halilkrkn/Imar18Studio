using System.Text;
using FluentAssertions;
using Imar18.Application.KoordinatDonusumu;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Crs;
using Imar18.Infrastructure.Cad.Donusum;
using Imar18.Infrastructure.Tests.Cad.Ncz;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cad.Donusum;

/// <summary>
/// SAHA-014: Netcad <c>.DNS</c> dönüşüm dosyası okuyucusu. Sentetik dosya Netcad düzenini taklit eder (Windows-1254 düz metin,
/// <c>\r\n</c>, <c>$AMAT</c> tek satır 6 bilimsel sayı: <c>A, B, CY, CX, C, D</c>; <c>$NOK</c> virgüllü
/// <c>NoktaNo, Y1, X1, Y2, X2,</c>; boş no / tüm-sıfır dolgu satırları; <c>$SON</c>). Koordinatlar sentetiktir; beklenen artıklar
/// elle kurulmuştur (gözlenen = hesaplanan − v).
/// </summary>
public sealed class NetcadDnsOkuyucuTests
{
    private static readonly NetcadDnsOkuyucu Okuyucu = new();

    internal const string AmatSatiri =
        " 9.99990000000000E-0001 -1.00000000000000E-0005 -2.55000000000000E+0001  1.82500000000000E+0001  1.00000000000000E-0005  9.99990000000000E-0001";

    internal static readonly string[] NokSatirlari =
    [
        "        N1,    500000.000,   4000000.000,    500009.490,   3999973.270,",
        "        N2,    501000.000,   4000000.000,    501009.500,   3999973.240,",
        "      P.33,    501000.000,   4001000.000,    501009.500,   4000973.210,",
        "        51,    500000.000,   4001000.000,    500009.510,   4000973.240,",
        "          ,         0.000,         0.000,         0.000,         0.000,",
        "          ,         0.000,         0.000,         0.000,         0.000,",
    ];

    internal static string Dns(string? amat = null, IEnumerable<string>? nok = null, bool son = true) =>
        string.Join("\r\n", new[] { "$AMAT", amat ?? AmatSatiri, "$NOK" }.Concat(nok ?? NokSatirlari).Concat(son ? ["$SON"] : [])) + "\r\n";

    internal static byte[] Bayt(string metin) =>
        System.Text.CodePagesEncodingProvider.Instance.GetEncoding(1254)!.GetBytes(metin);

    private static Imar18.Domain.Diagnostics.Result<DnsDosyasi> Oku(string metin) => Okuyucu.Oku(new MemoryStream(Bayt(metin)));

    [Fact]
    public void DnsOkuyucu_AMAT_Alti_Katsayiyi_A_B_CY_CX_C_D_Sirasiyla_Birebir_Okumali()
    {
        var k = Oku(Dns()).Value.Katsayilar;

        k.Should().Be(new DonusumKatsayilari(A: 0.99999m, B: -0.00001m, C: 0.00001m, D: 0.99999m, Cy: -25.5m, Cx: 18.25m));
    }

    [Fact]
    public void DnsOkuyucu_NOK_Ortak_Noktalarini_Dosya_Sirasiyla_Okumali_Bos_Ve_Sifir_Dolgu_Satirlarini_Atlamali()
    {
        var n = Oku(Dns()).Value.OrtakNoktalar;

        n.Select(x => x.NoktaNo).Should().Equal("N1", "N2", "P.33", "51");
        n[0].Should().Be(new DonusumOrtakNoktasi("N1", 500_000.000m, 4_000_000.000m, 500_009.490m, 3_999_973.270m));
        n[3].Should().Be(new DonusumOrtakNoktasi("51", 500_000.000m, 4_001_000.000m, 500_009.510m, 4_000_973.240m));
    }

    [Fact]
    public void DnsOkuyucu_Numarali_Ama_Tum_Koordinatlari_Sifir_Satiri_Dolgu_Sayip_Atlamali()
    {
        var r = Oku(Dns(nok: NokSatirlari.Append("        99,         0.000,         0.000,         0.000,         0.000,")));

        r.Value.OrtakNoktalar.Should().HaveCount(4);
    }

    [Fact]
    public void DnsOkuyucu_Artiklari_Olcegi_Donuklugu_Ve_Ortalama_Hatayi_Uretmeli()
    {
        var p = Oku(Dns()).Value.Parametreler;

        p.Model.Should().Be(DonusumModeli.Helmert2B);
        p.Artiklar.Select(a => (a.NoktaNo, a.Vy, a.Vx)).Should().Equal(
            ("N1", 0.010m, -0.020m), ("N2", -0.010m, 0m), ("P.33", 0m, 0.020m), ("51", 0m, 0m));
        p.OlcekKatsayisi!.Value.Should().BeApproximately(0.99999000005000050000375m, 1e-20m);
        p.DonuklukGrad!.Value.Should().Be(0.000636626139m);
        p.OrtalamaHataM!.Value.Should().BeApproximately(0.0158113883008418966m, 1e-18m);
    }

    [Fact]
    public void DnsOkuyucu_SON_Sonrasindaki_Satirlari_Yok_Saymali_Ve_LF_Satir_Sonunu_Kabul_Etmeli()
    {
        var metin = Dns().Replace("\r\n", "\n", StringComparison.Ordinal) + "çöp satır, 1, 2\n";

        Oku(metin).Value.OrtakNoktalar.Should().HaveCount(4);
    }

    [Fact]
    public void DnsOkuyucu_Ayni_Dosyayi_Iki_Kez_Okuyunca_Ayni_Sonucu_Vermeli()
    {
        var a = Oku(Dns()).Value;
        var b = Oku(Dns()).Value;

        a.Katsayilar.Should().Be(b.Katsayilar);
        a.OrtakNoktalar.Should().Equal(b.OrtakNoktalar);
    }

    public static TheoryData<string, string> GecersizDosyalar() => new()
    {
        { "AMAT yok", "$NOK\r\n N1, 1, 2, 3, 4,\r\n$SON\r\n" },
        { "NOK yok", "$AMAT\r\n" + AmatSatiri + "\r\n$SON\r\n" },
        { "SON yok", Dns(son: false) },
        { "AMAT 5 sayı", Dns(amat: " 1.0E+0000 0.0E+0000 0.0E+0000 0.0E+0000 1.0E+0000") },
        { "AMAT 7 sayı", Dns(amat: AmatSatiri + " 1.0E+0000") },
        { "AMAT sayı değil", Dns(amat: AmatSatiri.Replace("9.99990000000000E-0001", "abc", StringComparison.Ordinal)) },
        { "NOK eksik sütun", Dns(nok: ["        N1,    500000.000,   4000000.000,    500009.490,"]) },
        { "NOK sayı değil", Dns(nok: ["        N1,    500000.000,   4000000.000,    500009.490,   x,"]) },
        { "NOK virgüllü ondalık", Dns(nok: ["        N1,    500000,000,   4000000.000,    500009.490,   3999973.270,"]) },
        { "NOK yinelenen no", Dns(nok: [NokSatirlari[0], NokSatirlari[1], NokSatirlari[0].Replace("500009.490", "500009.491", StringComparison.Ordinal)]) },
        { "NOK yarım sıfır", Dns(nok: [NokSatirlari[0], NokSatirlari[1], "        N9,    500000.000,   4000000.000,         0.000,         0.000,"]) },
        { "NOK çok uzun no", Dns(nok: [NokSatirlari[0], NokSatirlari[1], "  " + new string('N', 33) + ",    500100.000,   4000100.000,    500109.500,   4000073.250,"]) },
        { "nokta yok", Dns(nok: [NokSatirlari[4]]) },
        { "tekil matris", Dns(amat: " 1.0E+0000 2.0E+0000 0.0E+0000 0.0E+0000 2.0E+0000 4.0E+0000") },
        { "boş", string.Empty },
        // Reviewer M2: decimal taşması üretecek değerler istisna değil Türkçe hata vermeli.
        { "AMAT taşan matris", Dns(amat: " 7.0E+0028 0.0E+0000 0.0E+0000 0.0E+0000 0.0E+0000 7.0E+0028") },
        { "AMAT taşan öteleme", Dns(amat: " 1.0E+0000 0.0E+0000 7.9E+0028 0.0E+0000 0.0E+0000 1.0E+0000") },
        { "AMAT decimal dışı", Dns(amat: " 1.0E+0029 0.0E+0000 0.0E+0000 0.0E+0000 0.0E+0000 1.0E+0000") },
        { "NOK aralık dışı koordinat", Dns(nok: [NokSatirlari[0], NokSatirlari[1], "        N9, 50000000000.000,   4000000.000,    500009.490,   3999973.270,"]) },
        { "NOK 7 ondalık", Dns(nok: [NokSatirlari[0], NokSatirlari[1], "        N9,    500100.0000001,   4000100.000,    500109.500,   4000073.250,"]) },
    };

    [Theory]
    [MemberData(nameof(GecersizDosyalar))]
    public void DnsOkuyucu_Bozuk_Dosyada_Istisna_Atmadan_Turkce_Hata_Donmeli(string durum, string metin)
    {
        var r = Oku(metin);

        r.IsFailure.Should().BeTrue(durum);
        r.Error.Code.Should().BeOneOf(ErrorCodes.GirdProjeGirdisiGecersiz, ErrorCodes.CrsDonusumHesaplanamadi);
        r.Error.UserMessage.Should().Contain(".DNS", durum);
        r.Error.TechnicalMessage.Should().NotContain("500000").And.NotContain("4000000", "hata metnine koordinat yazılmaz (KVKK)");
    }

    [Fact]
    public void DnsOkuyucu_Boyut_Sinirini_Asan_Akisi_Reddetmeli()
    {
        var r = Okuyucu.Oku(new MemoryStream(new byte[NetcadDnsOkuyucu.MaxBoyut + 1]));

        r.IsFailure.Should().BeTrue();
        r.Error.UserMessage.Should().Contain(".DNS");
    }

    [Fact]
    public void DnsOkuyucu_Windows1254_Turkce_Nokta_Adini_Dogru_Okumali()
    {
        var r = Oku(Dns(nok: [NokSatirlari[0], NokSatirlari[1], NokSatirlari[2].Replace("P.33", "ÇİĞ.1", StringComparison.Ordinal)]));

        r.Value.OrtakNoktalar[2].NoktaNo.Should().Be("ÇİĞ.1");
    }

    [Fact]
    public void DnsOkuyucu_Iptal_Edilirse_OperationCanceledException_Atmali()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Okuyucu.Oku(new MemoryStream(Bayt(Dns())), cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void DnsKatsayilari_Sentetik_Helmert_Noktalarinin_EnKucukKareler_Cozumuyle_Ayni_Olmali_Netcad_B_C_Duzeni()
    {
        // Bilinen Helmert (Netcad düzeni) ile gürültüsüz üretilen noktalar → mevcut LSQ hesaplayıcısı aynı A, B, C, D'yi bulmalı.
        var k = new DonusumKatsayilari(A: 0.9999962m, B: -0.0000069m, C: 0.0000069m, D: 0.9999962m, Cy: -26.8m, Cx: 19.14m);
        var n = new[] { (500_000m, 4_000_000m), (512_000m, 4_001_000m), (505_000m, 4_012_000m), (498_000m, 4_008_000m) }
            .Select((p, i) => new DonusumOrtakNoktasi("S" + i, p.Item1, p.Item2,
                (k.A * p.Item1) + (k.C * p.Item2) + k.Cy, (k.B * p.Item1) + (k.D * p.Item2) + k.Cx))
            .ToArray();

        var lsq = new HelmertAfinDonusumHesaplayici().Hesapla(DonusumModeli.Helmert2B, n).Value;

        lsq.A.Should().BeApproximately(k.A, 1e-12m);
        lsq.B.Should().BeApproximately(k.B, 1e-12m);
        lsq.C.Should().BeApproximately(k.C, 1e-12m);
        lsq.D.Should().BeApproximately(k.D, 1e-12m);
        lsq.Cy.Should().BeApproximately(k.Cy, 1e-6m);
        lsq.Cx.Should().BeApproximately(k.Cx, 1e-6m);
    }

    [RealDataFact]
    [Trait("Category", "RealData")]
    public void DnsOkuyucu_Gercek_Netcad_DNS_Dosyasi_8_Ortak_Nokta_Ve_AMAT_Ile_Hatasiz_Okunmali_1_Noktada_Artik_10_cm_Alti()
    {
        var dosya = Directory.EnumerateFiles(Path.Combine(GercekVeri.Kok, "donusum"), "*.dns", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(GercekVeri.Kok, "donusum"), "*.DNS", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase).Should().ContainSingle().Subject;

        using var akis = File.OpenRead(dosya);
        var r = Okuyucu.Oku(akis);

        r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.TechnicalMessage : string.Empty);
        var d = r.Value;
        d.OrtakNoktalar.Should().HaveCount(8);
        d.Katsayilar.HelmertMi.Should().BeTrue("Netcad deprem DNS'i 4 parametreli Helmert'tir (A = D, C = −B)");
        d.Katsayilar.Determinant.Should().BeInRange(0.99m, 1.01m);
        var ilk = d.Parametreler.Artiklar[0];
        DecimalKok((ilk.Vy * ilk.Vy) + (ilk.Vx * ilk.Vx)).Should().BeLessThan(0.10m, "1. noktada artık hata 10 cm altında");
        d.Parametreler.OrtalamaHataM!.Value.Should().BeLessThan(0.10m);

        // Netcad düzeninin bağımsız kanıtı: $AMAT katsayıları $NOK noktalarının en küçük kareler Helmert çözümüdür.
        var lsq = new HelmertAfinDonusumHesaplayici().Hesapla(DonusumModeli.Helmert2B, d.OrtakNoktalar).Value;
        lsq.A.Should().BeApproximately(d.Katsayilar.A, 1e-11m);
        lsq.B.Should().BeApproximately(d.Katsayilar.B, 1e-11m);
        lsq.C.Should().BeApproximately(d.Katsayilar.C, 1e-11m);
        lsq.Cy.Should().BeApproximately(d.Katsayilar.Cy, 1e-6m);
        lsq.Cx.Should().BeApproximately(d.Katsayilar.Cx, 1e-6m);
    }

    private static decimal DecimalKok(decimal v) => Imar18.Domain.Numerics.DecimalMath.Sqrt(v);
}

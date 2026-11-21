using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Numerics;
using Imar18.GeometryEngine.Crs;
using Imar18.Infrastructure.Cad.Ncz;
using Imar18.Infrastructure.Tests.Cikti;
using Xunit;
using static Imar18.Infrastructure.Tests.Cad.Ncz.NczParselasyonYaziciTests;

namespace Imar18.Infrastructure.Tests.Cad.Ncz;

/// <summary>
/// SAHA-017 (SORU-042, mühendisin Netcad 8.5 açılış hatası): şablona katman eklenirken font bloğunun parçalanmaması.
/// Bağımsız referans (gerçek dosyaların bayt incelemesi): <c>h[3]</c>'te <c>[0x06][int32 uzunluk][13 bayt ön başlık][n × 29 bayt]</c>,
/// <c>h[3] + 5 + uzunluk = h[7]</c> = font bloğu <c>0x0B</c> (<c>ARIAL.NCF</c>), <c>h[8]</c> = sembol <c>0x0C</c>, <c>h[10]</c> = <c>0x0D</c>,
/// <c>h[11]</c> = <c>0x17</c>; kayıt <c>[FD][bayrak][12][00][ad uzunluğu][20 bayt ad][4 bayt renk]</c>.
/// </summary>
public sealed class Saha017NczKatmanBloguTests
{
    private static readonly Encoding Turkce = CodePagesEncodingProvider.Instance.GetEncoding(1254)!;

    private static int Uzunluk(byte[] d) => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(H(d, 3) + 1));

    private static int Sayac(byte[] d) => BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(H(d, 3) + 16));

    /// <summary>Tüm SAHA-017 bayt değişmezleri (şablon ve çıktı için aynı).</summary>
    internal static void BlokDegismezleriTutmali(byte[] d)
    {
        d[H(d, 3)].Should().Be(0x06, "h[3] katman bloğudur");
        Uzunluk(d).Should().Be(13 + (29 * Sayac(d)), "uzunluk = 13 bayt ön başlık + 29 × katman");
        (H(d, 3) + 5 + Uzunluk(d)).Should().Be(H(d, 7), "katman bloğu tam font bloğunun başında biter");
        d[H(d, 7)].Should().Be(0x0B, "h[7] font bloğu");
        Encoding.ASCII.GetString(d, H(d, 7) + 10, 9).Should().Be("ARIAL.NCF");
        d[H(d, 8)].Should().Be(0x0C, "h[8] sembol bloğu");
        d[H(d, 10)].Should().Be(0x0D);
        d[H(d, 11)].Should().Be(0x17);
        for (var i = 0; i < Sayac(d); i++)
        {
            var k = H(d, 3) + 18 + (29 * i);
            d[k + 4].Should().BeLessThanOrEqualTo(20, "ad uzunluğu 20 baytı aşmaz");
        }
    }

    [Fact]
    public async Task NczYazici_Sablona_Katman_Eklenince_Font_Ve_Sembol_Bloklari_Bozulmamali_SAHA_017()
    {
        var (sablon, _) = SentetikSablon();
        BlokDegismezleriTutmali(sablon);
        var p = await PaketAsync(sablon);

        var d = NczParselasyonYazici.Yaz(p).Value;

        BlokDegismezleriTutmali(d);
        Sayac(d).Should().Be(Sayac(sablon) + 2, "TK_PARSEL_YOLATERK + NOKTA_PARSEL_YENI eklenir");
        Uzunluk(d).Should().Be(Uzunluk(sablon) + (2 * 29), "katman bloğu uzunluğu eklenen × 29 kadar artar");
        (H(d, 7) - H(sablon, 7)).Should().Be(2 * 29);
        (H(d, 8) - H(d, 7)).Should().Be(H(sablon, 8) - H(sablon, 7), "font bloğu boyu değişmez (parçalanmaz)");
        d.AsSpan(H(d, 7), H(d, 11) - H(d, 7)).SequenceEqual(sablon.AsSpan(H(sablon, 7), H(sablon, 11) - H(sablon, 7)))
            .Should().BeTrue("font/sembol/XML/0x0D bölgesi bayt bayt aynı");
        d.AsSpan(0, H(sablon, 7)).ToArray()[(H(d, 3) + 18)..].Should().Equal(sablon.AsSpan(H(sablon, 3) + 18, H(sablon, 7) - H(sablon, 3) - 18).ToArray(),
            "şablonun katman kayıtları yerinde kalır");
    }

    [Fact]
    public async Task NczYazici_Yeni_Katman_Kaydi_Sablon_Kayitlariyla_Ayni_29_Bayt_Duzeninde_Olmali_SAHA_017()
    {
        var (sablon, _) = SentetikSablon();
        var d = NczParselasyonYazici.Yaz(await PaketAsync(sablon)).Value;

        var yeni = d.AsSpan(H(sablon, 7), 29).ToArray(); // ilk eklenen kayıt eski blok sonunda (font bloğunun eski yerinde)

        yeni[..4].Should().Equal(0xFD, 0x80, 0x12, 0x00);
        Turkce.GetString(yeni, 5, yeni[4]).Should().Be(NczParselasyonYazici.YolTerkKatmani);
        yeni[^4..].Should().Equal(0x79, 0x00, 0x01, 0x00);
        Oku(d).Layers.Select(l => l.Name).Should().EndWith([NczParselasyonYazici.YolTerkKatmani, NczParselasyonYazici.NoktaKatmani]);
    }

    [Fact]
    public async Task NczYazici_Eski_Duzenli_Sablonda_Font_Blogu_Yoksa_Bozuk_Dosya_Uretmeden_Reddetmeli_SAHA_017()
    {
        var eski = new NczSentetikDosya().Katman("0").Katman(NczParselasyonYazici.GirenKatmani)
            .Poligon(1, "K1", [((double)OrnekProje.X0, (double)OrnekProje.Y0, 0), ((double)OrnekProje.X0, (double)OrnekProje.Y0 + 20, 0),
                ((double)OrnekProje.X0 + 10, (double)OrnekProje.Y0 + 20, 0), ((double)OrnekProje.X0 + 10, (double)OrnekProje.Y0, 0),
                ((double)OrnekProje.X0, (double)OrnekProje.Y0, 0)], 200.0)
            .Olustur(); // 0x06 / uzunluk / 0x0B yok
        var p = await PaketAsync(eski);

        var r = NczParselasyonYazici.Yaz(p);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be(ErrorCodes.CiktiCadDisaAktarim);
        r.Error.TechnicalMessage.Should().Contain("layer block");
    }

    [Fact]
    public async Task NczYazici_Iskelet_Modu_Katman_Blogu_0x06_Ve_Uzunlukla_h7de_Bitmeli_SAHA_017()
    {
        var d = NczParselasyonYazici.Yaz(await PaketAsync()).Value;

        d[H(d, 3)].Should().Be(0x06);
        Uzunluk(d).Should().Be(13 + (29 * Sayac(d)));
        (H(d, 3) + 5 + Uzunluk(d)).Should().Be(H(d, 7));
        Sayac(d).Should().Be(5);
        d.AsSpan(H(d, 3) + 18, 4).ToArray().Should().Equal(0xFD, 0x80, 0x12, 0x00);
    }

    [Fact]
    public async Task CiktiPaketi_DNS_Ileri_Donusumde_Sablon_Korunmali_Kadastro_Ileri_Donusturulup_Yazilmali_SAHA_017()
    {
        var (sablon, _) = SentetikSablon();
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        (await yol.GonderAsync(new NczSablonuAyarlaKomutu(sablon), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await yol.GonderAsync(new IleriDonusumUygulaKomutu(FenKlasoruZipTests.Donusum(), FenKlasoruZipTests.DnsK), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        var p = CiktiPaketiOlusturucu.Olustur(yol.Belge, ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji), new HelmertAfinDonusumHesaplayici()).Value;

        p.NczSablonu.Should().NotBeNull("şablon iskelet moduna düşürülmez");
        p.NczSablonIleriDonusumu.Should().Be(FenKlasoruZipTests.DnsK);
        var d = NczParselasyonYazici.Yaz(p).Value;

        BlokDegismezleriTutmali(d);
        var belge = Oku(d);
        var k1 = belge.Polylines.Single(x => x.LayerName == NczParselasyonYazici.GirenKatmani && x.Label == "K1");
        var beklenen = p.Parseller.Single(s => s.GeciciKod == "K1").Koseler;
        k1.Vertices.Take(4).Should().BeEquivalentTo(beklenen, "şablon kadastrosu bağımsız ileri formülle Deprem Sonrası'na taşınır");
        var y0 = Math.Round((FenKlasoruZipTests.DnsK.A * OrnekProje.Y0) + (FenKlasoruZipTests.DnsK.C * OrnekProje.X0) + FenKlasoruZipTests.DnsK.Cy, 6, MidpointRounding.AwayFromZero);
        k1.Vertices.Should().Contain(v => v.Y == y0);
        belge.Polylines.Should().NotContain(x => x.Vertices.Any(v => v.Y == OrnekProje.Y0 && v.X == OrnekProje.X0), "Deprem Öncesi köşe kalmaz");
        belge.Polylines.Select(x => x.Label).Should().NotContain("ESKIA1", "Deprem Öncesi A1 kopyası dönüştürülünce yeni A1 ile aynıdır ve düşer");
    }

    // ---------------- reviewer (SAHA-017) test boşlukları ----------------

    private static async Task<CiktiPaketi> DnsPaketiAsync(byte[] sablon)
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        (await yol.GonderAsync(new NczSablonuAyarlaKomutu(sablon), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await yol.GonderAsync(new IleriDonusumUygulaKomutu(FenKlasoruZipTests.Donusum(), FenKlasoruZipTests.DnsK), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        return CiktiPaketiOlusturucu.Olustur(yol.Belge, ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji), new HelmertAfinDonusumHesaplayici()).Value;
    }

    /// <summary>Bağımsız ileri formül (Netcad .DNS): Y2 = A·Y1 + C·X1 + CY, X2 = B·Y1 + D·X1 + CX, 6 ondalık AwayFromZero.</summary>
    private static (double N, double E) Ileri(decimal n, decimal e)
    {
        var k = FenKlasoruZipTests.DnsK;
        return ((double)Math.Round((k.B * e) + (k.D * n) + k.Cx, 6, MidpointRounding.AwayFromZero),
            (double)Math.Round((k.A * e) + (k.C * n) + k.Cy, 6, MidpointRounding.AwayFromZero));
    }

    /// <summary>Tip 1 (104 bayt) / tip 5 (93 + metin) / tip 6 (çözülmemiş) ham kayıt: ref ve min = max kutu.</summary>
    private static byte[] HamVarlik(byte tip, int boyut, decimal n, decimal e)
    {
        var y = new byte[boyut];
        y[0] = tip;
        y[1] = tip;
        BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(3), (double)n);
        BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(11), (double)e);
        foreach (var o in new[] { 45, 61 })
        {
            BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(o), (double)n);
            BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(o + 8), (double)e);
        }

        if (tip == 5)
        {
            y[92] = 3;
            "ABC"u8.CopyTo(y.AsSpan(93));
        }

        return y;
    }

    /// <summary>Gerçek düzende tip 2 çizgi (101 bayt): başlangıç 3..18, bitiş 81..96 (gerçek 3 dosyada 293/293 doğrulandı).</summary>
    private static byte[] Cizgi(decimal n1, decimal e1, decimal n2, decimal e2)
    {
        var y = HamVarlik(2, 101, n1, e1);
        BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(81), (double)n2);
        BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(89), (double)e2);
        return y;
    }

    [Fact]
    public async Task NczYazici_DNS_Nokta_Yazi_Cizgi_Ileri_Donusmeli_Cozulmemis_Tip6_Haritaya_Alinmamali_SAHA_017()
    {
        var (n, e) = (OrnekProje.X0 + 5m, OrnekProje.Y0 + 5m);
        var (sablon, _) = SentetikSablon(1, true, true, f => f
            .HamKayit(0x15, HamVarlik(1, 104, n, e))
            .HamKayit(0x15, HamVarlik(5, 96, n, e))
            .HamKayit(0x15, HamVarlik(6, 120, n, e))
            .HamKayit(0x15, Cizgi(OrnekProje.X0 + 2m, OrnekProje.Y0 + 2m, OrnekProje.X0 + 3m, OrnekProje.Y0 + 4m)));

        var d = NczParselasyonYazici.Yaz(await DnsPaketiAsync(sablon)).Value;

        var kayitlar = Kayitlar(d).Where(Varlik).ToList();
        var beklenen = Ileri(n, e);
        var nokta = kayitlar.Single(k => k.Yuk[0] == 1 && k.Yuk.Length == 104 && NoktaAdi(k.Yuk).Length == 0);
        (D(nokta.Yuk, 3), D(nokta.Yuk, 11)).Should().Be(beklenen, "tip 1 nokta konumu ileri dönüşür");
        (D(nokta.Yuk, 45), D(nokta.Yuk, 69)).Should().Be(beklenen, "nokta kutusu min = max dönüşmüş konum");
        var yazi = kayitlar.Single(k => k.Yuk[0] == 5 && Yazi(k.Yuk) == "ABC");
        (D(yazi.Yuk, 3), D(yazi.Yuk, 11)).Should().Be(beklenen, "tip 5 ekleme noktası ileri dönüşür");
        var bas = Ileri(OrnekProje.X0 + 2m, OrnekProje.Y0 + 2m);
        var cizgi = kayitlar.Single(k => k.Yuk[0] == 2 && (D(k.Yuk, 3), D(k.Yuk, 11)) == bas);
        (D(cizgi.Yuk, 81), D(cizgi.Yuk, 89)).Should().Be(Ileri(OrnekProje.X0 + 3m, OrnekProje.Y0 + 4m), "çizgi bitişi (spike §2.3: 81..96)");
        (D(cizgi.Yuk, 45), D(cizgi.Yuk, 53)).Should().Be(bas, "çizgi kutusu iki ucun min'i");
        kayitlar.Should().NotContain(k => k.Yuk[0] == 6, "reviewer Y1: iç düzeni çözülmemiş tip yarım dönüştürülmez, haritaya alınmaz");

        // 49..80 çizim kapsamı Deprem Sonrası varlıklardan yeniden hesaplanır.
        var (mnN, mnE, mxN, mxE) = (D(d, 49), D(d, 57), D(d, 65), D(d, 73));
        foreach (var k in kayitlar.Where(k => k.Yuk.Length >= 77 && k.Yuk[0] != 14))
        {
            D(k.Yuk, 45).Should().BeGreaterThanOrEqualTo(mnN);
            D(k.Yuk, 53).Should().BeGreaterThanOrEqualTo(mnE);
            D(k.Yuk, 61).Should().BeLessThanOrEqualTo(mxN);
            D(k.Yuk, 69).Should().BeLessThanOrEqualTo(mxE);
        }

        var kutulu = kayitlar.Where(k => k.Yuk.Length >= 77 && k.Yuk[0] != 14).ToList();
        (mnN, mnE, mxN, mxE).Should().Be((kutulu.Min(k => D(k.Yuk, 45)), kutulu.Min(k => D(k.Yuk, 53)), kutulu.Max(k => D(k.Yuk, 61)), kutulu.Max(k => D(k.Yuk, 69))),
            "kapsam = Deprem Sonrası varlık kutularının birleşimi");
        mnE.Should().NotBe((double)OrnekProje.Y0, "Deprem Öncesi kapsam kalmaz");
    }

    [Fact]
    public async Task NczYazici_DNS_Cozulemeyen_Koordinatli_Kayitta_Sessiz_Kismi_Donusum_Yerine_Reddetmeli_SAHA_017()
    {
        var bozuk = HamVarlik(1, 104, OrnekProje.X0, OrnekProje.Y0);
        BinaryPrimitives.WriteDoubleLittleEndian(bozuk.AsSpan(3), double.NaN);
        var (sablon, _) = SentetikSablon(1, true, true, f => f.HamKayit(0x15, bozuk));

        var r = NczParselasyonYazici.Yaz(await DnsPaketiAsync(sablon));

        r.IsFailure.Should().BeTrue("reviewer Y2: fail-closed");
        r.Error.Code.Should().Be(ErrorCodes.CiktiCadDisaAktarim);
        r.Error.TechnicalMessage.Should().Contain("cannot be decoded");
    }

    [Fact]
    public async Task NczYazici_Sablonun_DD00_ve_FD7E_Onekli_Katman_Kayitlari_Bayt_Bayt_Korunmali_SAHA_017()
    {
        var (sablon, _) = SentetikSablon();
        var t = H(sablon, 3) + 18;
        new byte[] { 0xDD, 0x00, 0x02, 0x00 }.CopyTo(sablon, t); // "0" (gerçek dosyada CIZPEN düzeni)
        new byte[] { 0xFD, 0x7E, 0xFF, 0xFF }.CopyTo(sablon, t + 29); // GİREN (gerçek dosyada DEGER/CIZGI düzeni)

        var d = NczParselasyonYazici.Yaz(await PaketAsync(sablon)).Value;

        d.AsSpan(t, 29 * 3).ToArray().Should().Equal(sablon.AsSpan(t, 29 * 3).ToArray(), "şablon kayıt önekleri değiştirilmez");
        d.AsSpan(t + (29 * 3), 4).ToArray().Should().Equal(0xFD, 0x80, 0x12, 0x00);
        BlokDegismezleriTutmali(d);
    }

    [Fact]
    public async Task NczYazici_Sablonda_Sembol_Ofseti_h8_Sifirsa_Reddetmeli_SAHA_017()
    {
        var (sablon, _) = SentetikSablon();
        NczSentetikDosya.BaslikYaz(sablon, 8, 0);

        var r = NczParselasyonYazici.Yaz(await PaketAsync(sablon));

        r.IsFailure.Should().BeTrue();
        r.Error.TechnicalMessage.Should().Contain("font/symbol block layout");
    }

    /// <summary>Yaklaşık Yer Krokisi şablonu: TK_OLUSAN poligonları imar parseli olarak (yalnız bellekte) yeniden yazılır.</summary>
    [RealDataFact]
    [Trait("Category", "RealData")]
    public void NczYaziciGercek_Kroki_Sablonuna_Katman_Eklenince_Font_Blogu_ARIAL_NCF_Bozulmamali_SAHA_017()
    {
        var dosya = Directory.EnumerateFiles(Path.Combine(GercekVeri.Kok, "kroki"), "*.NCZ").Order(StringComparer.Ordinal).First();
        var sablon = File.ReadAllBytes(dosya);
        BlokDegismezleriTutmali(sablon);
        var kaynak = Oku(sablon);
        var parseller = new List<ParselCetvelSatiri>();
        var koordinatlar = new List<KoordinatSatiri>();
        var noktalar = new Dictionary<GeodeticPoint, int>();
        var i = 0;
        foreach (var pl in kaynak.Polylines.Where(x => x.IsClosed && x.Vertices.Count >= 4 && x.LayerName.Contains("OLUSAN", StringComparison.Ordinal)))
        {
            var koseler = pl.Vertices.Take(pl.Vertices.Count - 1).ToArray();
            var nolar = koseler.Select(k =>
            {
                if (!noktalar.TryGetValue(k, out var no))
                {
                    no = noktalar.Count + 1;
                    noktalar.Add(k, no);
                    koordinatlar.Add(new KoordinatSatiri(no, k.Y, k.X));
                }

                return no;
            }).ToArray();
            var alan = GaussAreaCalculator.Compute(PolygonRing.FromClosed(pl.Vertices).Value).AreaM2;
            parseller.Add(new ParselCetvelSatiri("G" + ++i, "900/" + i, ParselRolu.Imar, nolar, [], alan, alan, HeM2Dm2.Ayir(alan), koseler));
        }

        parseller.Should().NotBeEmpty();
        var paket = new CiktiPaketi("Yerel Gerçek Veri Kroki", new string('b', 64), "muhendis-01", new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero),
            PilotDamgasi.Metin, PilotDamgasi.FenKlasoruSablonSurumu, "ITRF96 TM3", 1000, koordinatlar, parseller, null,
            new DopOzeti(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m), [], [], [])
        {
            NczSablonu = NczSablonu.Olustur(sablon).Value,
        };

        var r = NczParselasyonYazici.Yaz(paket);

        r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.TechnicalMessage : string.Empty);
        var d = r.Value;
        BlokDegismezleriTutmali(d);
        var eklenen = Sayac(d) - Sayac(sablon);
        eklenen.Should().Be(3, "kroki şablonunda TK_PARSEL_OLUŞAN, TK_PARSEL_YOLATERK ve NOKTA_PARSEL_YENI yoktur");
        Uzunluk(d).Should().Be(Uzunluk(sablon) + (eklenen * 29));
        foreach (var k in new[] { 7, 8, 10, 11 })
        {
            H(d, k).Should().Be(H(sablon, k) + (eklenen * 29), "h[" + k + "] eklenen × 29 kayar");
        }

        d.AsSpan(H(d, 7), H(d, 11) - H(d, 7)).SequenceEqual(sablon.AsSpan(H(sablon, 7), H(sablon, 11) - H(sablon, 7))).Should().BeTrue();
        Oku(d).Polylines.Count(x => x.LayerName == NczParselasyonYazici.OlusanKatmani).Should().Be(parseller.Count);
    }
}

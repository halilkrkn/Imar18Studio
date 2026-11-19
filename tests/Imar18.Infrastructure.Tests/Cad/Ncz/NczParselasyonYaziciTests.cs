using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cad.Ncz;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cad.Ncz;

/// <summary>
/// P7.3-C3 (SAHA-007): <c>PARSELASYON HARİTASI.NCZ</c> yazıcısı — sentetik round-trip. Bağımsız referans: <see cref="OrnekProje"/>
/// dikdörtgenleri (A1 = 12 × 10 = 120.00 m², Y1 = 8 × 10 = 80.00 m², K1 = 20 × 10 = 200.00 m²; elle hesaplanır) ve
/// <c>docs/spikes/P7.3_NCZ_Yazici_Spike.md</c> kayıt düzeni. Üretilen dosya mevcut <see cref="NczFormatAdapter"/> ile hatasız
/// geri okunmalı; parsel sayısı, köşeler (≤ 1 mm; Decimal 6 ondalıkta birebir), Gauss alanı ve başlık ofsetleri tutmalıdır.
/// Gerçek veri yoktur (AC-PILOT-08).
/// </summary>
public sealed class NczParselasyonYaziciTests
{
    private static readonly Encoding Turkce = CodePagesEncodingProvider.Instance.GetEncoding(1254)!;
    private static readonly NczFormatAdapter Adaptor = new();

    internal static async Task<CiktiPaketi> PaketAsync(byte[]? sablon = null)
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        if (sablon is not null)
        {
            var r = await yol.GonderAsync(new NczSablonuAyarlaKomutu(sablon), OrnekProje.Muhendis);
            r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.TechnicalMessage : string.Empty);
        }

        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        var p = CiktiPaketiOlusturucu.Olustur(yol.Belge, ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji));
        p.IsSuccess.Should().BeTrue(p.IsFailure ? p.Error.TechnicalMessage : string.Empty);
        return p.Value;
    }

    private static byte[] Yaz(CiktiPaketi p)
    {
        var r = NczParselasyonYazici.Yaz(p);
        r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.TechnicalMessage : string.Empty);
        return r.Value;
    }

    private static CadLayerDocument Oku(byte[] ncz)
    {
        using var s = new MemoryStream(ncz, writable: false);
        var r = Adaptor.Read(s);
        r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.TechnicalMessage : string.Empty);
        return r.Value;
    }

    private static decimal Gauss(CadPolyline p) => GaussAreaCalculator.Compute(PolygonRing.FromClosed(p.Vertices).Value).AreaM2;

    /// <summary>Sentetik şablon: başlık bölgesi işaretli, katman sonrası XML benzetimi, kadastro + eski imar + A1 kopyası + tip 14.</summary>
    internal static (byte[] Bayt, byte[] KadastroKaydi) SentetikSablon() => SentetikSablon(kadastroKatmani: 1, kadastroVar: true, tip14: true);

    internal static (byte[] Bayt, byte[] KadastroKaydi) SentetikSablon(int kadastroKatmani, bool kadastroVar, bool tip14)
    {
        var onBolge = new byte[600];
        Encoding.ASCII.GetBytes("SENTETIK-BASLIK-BOLGESI").CopyTo(onBolge, 120);
        var xml = Encoding.ASCII.GetBytes("<NetmapManager><Il>SENTETIK</Il></NetmapManager>" + new string(' ', 16));
        var x0 = (double)OrnekProje.X0;
        var y0 = (double)OrnekProje.Y0;
        var dosya = new NczSentetikDosya { OnBolge = onBolge, KatmanSonrasiBolge = xml }
            .Katman("0").Katman(NczParselasyonYazici.GirenKatmani).Katman(NczParselasyonYazici.OlusanKatmani);
        if (kadastroVar)
        {
            dosya.Poligon(kadastroKatmani, "K1", [(x0, y0, 0), (x0, y0 + 20, 0), (x0 + 10, y0 + 20, 0), (x0 + 10, y0, 0), (x0, y0, 0)], 200.0);
        }

        dosya.Poligon(2, "ESKI", [(x0 + 50, y0, 0), (x0 + 50, y0 + 5, 0), (x0 + 55, y0 + 5, 0), (x0 + 50, y0, 0)])
            .Poligon(0, "ESKIA1", [(x0 + 10, y0, 0), (x0 + 10, y0 + 12, 0), (x0, y0 + 12, 0), (x0, y0, 0), (x0 + 10, y0, 0)])
            .Cizgi(0, (x0, y0), (x0 + 1, y0 + 1));
        if (tip14)
        {
            dosya.HamKayit(0x15, Tip14());
        }

        var bayt = dosya.Olustur();
        NczSentetikDosya.BaslikYaz(bayt, 8, H(bayt, 7) + 89); // spike: h[8] = h[7] + 89 (katman sonrası bölgeyi gösterir)
        var kadastro = kadastroVar ? KayitBul(bayt, k => k.Yuk[0] == 7 && k.Yuk.Length > 100 && Turkce.GetString(k.Yuk, 82, k.Yuk[81]) == "K1") : [];
        return (bayt, kadastro);
    }

    private static byte[] Tip14()
    {
        var y = new byte[81];
        y[0] = 14;
        y[1] = 14;
        return y;
    }

    // ---------------- ham kayıt yardımcıları (spike düzeni; bağımsız okuyucu) ----------------

    internal sealed record HamKayit(int Ofset, byte Bayrak, byte[] Yuk)
    {
        public byte[] TumBayt(byte[] dosya) => dosya.AsSpan(Ofset, 5 + Yuk.Length).ToArray();
    }

    internal static int H(byte[] d, int k) => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(1 + (4 * k)));

    internal static List<HamKayit> Kayitlar(byte[] d)
    {
        var liste = new List<HamKayit>();
        for (var p = H(d, 11); p < d.Length;)
        {
            var n = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 1));
            liste.Add(new HamKayit(p, d[p], d.AsSpan(p + 5, n).ToArray()));
            p += 5 + n;
        }

        return liste;
    }

    internal static List<string> Katmanlar(byte[] d)
    {
        var t = H(d, 3);
        int n = BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(t + 16));
        return Enumerable.Range(0, n).Select(i => Turkce.GetString(d, t + 22 + (29 * i) + 1, d[t + 22 + (29 * i)])).ToList();
    }

    internal static int Katman(byte[] yuk) => BinaryPrimitives.ReadInt16LittleEndian(yuk.AsSpan(30));

    private static byte[] KayitBul(byte[] d, Func<HamKayit, bool> kosul)
    {
        var k = Kayitlar(d).Single(x => x.Yuk.Length > 31 && kosul(x));
        return k.TumBayt(d);
    }

    internal static double D(byte[] b, int o) => BinaryPrimitives.ReadDoubleLittleEndian(b.AsSpan(o));

    internal static string Yazi(byte[] yuk) => Turkce.GetString(yuk, 93, yuk[92]);

    internal static string NoktaAdi(byte[] yuk) => Turkce.GetString(yuk, 82, yuk[81]);

    internal static bool Varlik(HamKayit k) => k.Bayrak is 0x15 or 0x16 && k.Yuk.Length > 0;

    internal static int Indeks(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle);

    // ---------------- şablonsuz (iskelet) mod ----------------

    [Fact]
    public async Task NczYazici_Sablonsuz_Imar_Yol_Ve_Kadastro_Parselleri_Adaptorle_Birebir_Geri_Okunmali()
    {
        var p = await PaketAsync();

        var belge = Oku(Yaz(p));

        belge.Layers.Select(l => l.Name).Should().Equal("0", NczParselasyonYazici.GirenKatmani, NczParselasyonYazici.OlusanKatmani,
            NczParselasyonYazici.YolTerkKatmani, NczParselasyonYazici.NoktaKatmani);
        var olusan = belge.Polylines.Where(x => x.LayerName == NczParselasyonYazici.OlusanKatmani).ToList();
        var yol = belge.Polylines.Where(x => x.LayerName == NczParselasyonYazici.YolTerkKatmani).ToList();
        var giren = belge.Polylines.Where(x => x.LayerName == NczParselasyonYazici.GirenKatmani).ToList();
        olusan.Should().ContainSingle().Which.Label.Should().Be("101/1");
        yol.Should().ContainSingle().Which.Label.Should().Be("YOL");
        giren.Should().ContainSingle().Which.Label.Should().Be("K1");
        olusan[0].FeatureClass.Should().Be("PARSEL", "spike: imar parseli 0x16 sınıf PARSEL");

        foreach (var (cad, kod) in new[] { (olusan[0], "A1"), (yol[0], "Y1"), (giren[0], "K1") })
        {
            var satir = p.Parseller.Single(s => s.GeciciKod == kod);
            cad.IsClosed.Should().BeTrue();
            cad.Vertices.Should().Equal(satir.Koseler.Append(satir.Koseler[0]), "köşeler kayıpsız (≤ 1 mm) ve kapanış tekrarıyla yazılır");
            Gauss(cad).Should().Be(satir.HesapAlaniM2);
            cad.DeclaredAreaM2.Should().Be(satir.DengelenmisAlanM2);
        }

        Gauss(olusan[0]).Should().Be(120.00m);
        Gauss(yol[0]).Should().Be(80.00m);
        Gauss(giren[0]).Should().Be(200.00m);
    }

    [Fact]
    public async Task NczYazici_Kose_Noktalari_Tip1_Nokta_Kaydi_Olarak_Koordine_Ozet_Numarasiyla_Yazilmali()
    {
        var p = await PaketAsync();
        var d = Yaz(p);
        var noktaKatmani = Katmanlar(d).IndexOf(NczParselasyonYazici.NoktaKatmani);

        var noktalar = Kayitlar(d).Where(k => Varlik(k) && k.Yuk[0] == 1).ToList();

        var beklenen = p.Parseller.Where(s => s.Rol != ParselRolu.Kadastro).SelectMany(s => s.NoktaNolari).Distinct().Order().ToList();
        noktalar.Should().HaveCount(beklenen.Count);
        foreach (var k in noktalar)
        {
            k.Yuk.Should().HaveCount(104, "spike §2.2: nokta kaydı 104 bayt");
            Katman(k.Yuk).Should().Be(noktaKatmani);
            var no = int.Parse(NoktaAdi(k.Yuk), CultureInfo.InvariantCulture);
            beklenen.Should().Contain(no);
            var satir = p.Koordinatlar.Single(s => s.NoktaNo == no);
            ((decimal)D(k.Yuk, 3)).Should().Be(satir.X, "ref noktası (Kuzey)");
            ((decimal)D(k.Yuk, 11)).Should().Be(satir.Y, "ref noktası (Doğu)");
            D(k.Yuk, 45).Should().Be(D(k.Yuk, 61), "nokta sınır kutusu min = max");
        }
    }

    [Fact]
    public async Task NczYazici_Ada_Parsel_Etiketi_Tip5_Yazi_Olarak_Parsel_Icinde_Ve_Pilot_Damgasi_Yazilmali()
    {
        var p = await PaketAsync();
        var d = Yaz(p);
        var katmanlar = Katmanlar(d);

        var yazilar = Kayitlar(d).Where(k => Varlik(k) && k.Yuk[0] == 5).ToList();

        foreach (var k in yazilar)
        {
            k.Yuk.Should().HaveCount(93 + k.Yuk[92], "spike §2.4: kayıt boyu = 93 + metin uzunluğu");
            BinaryPrimitives.ReadSingleLittleEndian(k.Yuk.AsSpan(41)).Should().Be(1f);
            BinaryPrimitives.ReadSingleLittleEndian(k.Yuk.AsSpan(81)).Should().BeGreaterThan(0f, "yazı yüksekliği");
        }

        var etiket = yazilar.Single(k => Yazi(k.Yuk) == "101/1");
        katmanlar[Katman(etiket.Yuk)].Should().Be(NczParselasyonYazici.OlusanKatmani);
        var a1 = p.Parseller.Single(s => s.GeciciKod == "A1").Koseler;
        var (ekN, ekE) = ((decimal)D(etiket.Yuk, 3), (decimal)D(etiket.Yuk, 11));
        var (maxN, maxE) = ((decimal)D(etiket.Yuk, 61), (decimal)D(etiket.Yuk, 69));
        ((ekN + maxN) / 2m).Should().BeApproximately(OrnekProje.X0 + 5m, 0.001m, "etiket A1 ağırlık merkezine (N = X0 + 5) ortalanır");
        ((ekE + maxE) / 2m).Should().BeApproximately(OrnekProje.Y0 + 6m, 0.001m, "etiket A1 ağırlık merkezine (E = Y0 + 6) ortalanır");
        a1.Should().NotBeEmpty();
        yazilar.Select(k => Yazi(k.Yuk)).Should().Contain("YOL").And.Contain("K1");
        yazilar.Select(k => Yazi(k.Yuk)).Should().Contain(PilotDamgasi.Metin).And.Contain("RevisionId " + p.RevizyonId);
    }

    [Fact]
    public async Task NczYazici_Sablonsuz_Baslik_Ofsetleri_Ve_Sayac_Spike_Duzeniyle_Tutarli_Olmali()
    {
        var d = Yaz(await PaketAsync());

        H(d, 0).Should().Be(120);
        H(d, 2).Should().Be(125);
        H(d, 1).Should().Be(d.Length);
        var katman = Katmanlar(d).Count;
        (H(d, 7) + 4).Should().Be(H(d, 3) + 22 + (29 * katman));
        H(d, 10).Should().Be(H(d, 11) - 5);
        d[H(d, 10)].Should().Be(0x0D, "spike: h[10]'da 0x0D boş bölüm işaretçisi");
        BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(H(d, 10) + 1)).Should().Be(0);
        d[H(d, 11)].Should().Be(0x17, "akış 0x17 işaretçisiyle başlar");
        var varliklar = Kayitlar(d).Where(Varlik).ToList();
        BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(81)).Should().Be(varliklar.Count - 1, "spike: ofset 81 = varlık sayısı − 1");
        varliklar[^1].Yuk[0].Should().NotBe(14);
        var (mnN, mnE, mxN, mxE) = (D(d, 49), D(d, 57), D(d, 65), D(d, 73));
        foreach (var k in varliklar)
        {
            D(k.Yuk, 45).Should().BeGreaterThanOrEqualTo(mnN);
            D(k.Yuk, 53).Should().BeGreaterThanOrEqualTo(mnE);
            D(k.Yuk, 61).Should().BeLessThanOrEqualTo(mxN);
            D(k.Yuk, 69).Should().BeLessThanOrEqualTo(mxE);
        }
    }

    [Fact]
    public async Task NczYazici_Ayni_Paket_Ayni_Bayti_Uretmeli()
    {
        var p = await PaketAsync();

        Yaz(p).Should().Equal(Yaz(p));
    }

    // ---------------- şablon modu ----------------

    [Fact]
    public async Task NczYazici_Sablonlu_Baslik_Katman_XML_Ve_Kadastro_Kayitlari_Bayt_Bayt_Korunmali()
    {
        var (sablon, kadastroKaydi) = SentetikSablon();
        var p = await PaketAsync(sablon);

        var d = Yaz(p);

        H(d, 3).Should().Be(H(sablon, 3), "katman tablosu ofseti değişmez");
        d.AsSpan(85, H(sablon, 3) - 85).ToArray().Should().Equal(sablon.AsSpan(85, H(sablon, 3) - 85).ToArray(), "başlık bölgesi (kapsam/sayaç dışında) aynen korunur");
        var sablonKatmanSonu = H(sablon, 7) + 4;
        var araBolge = sablon.AsSpan(sablonKatmanSonu, H(sablon, 11) - sablonKatmanSonu).ToArray();
        Indeks(d, araBolge).Should().Be(H(d, 7) + 4, "font/sembol/XML bölgesi katman tablosundan hemen sonra aynen korunur");
        Indeks(d, kadastroKaydi).Should().BeGreaterThan(H(d, 11), "kadastro kaydı bayt bayt kopyalanır");
        Katmanlar(d).Should().Equal("0", NczParselasyonYazici.GirenKatmani, NczParselasyonYazici.OlusanKatmani,
            NczParselasyonYazici.YolTerkKatmani, NczParselasyonYazici.NoktaKatmani);
        d.AsSpan(H(d, 3), 22 + (29 * 3)).ToArray()[..16].Should().Equal(sablon.AsSpan(H(sablon, 3), 16).ToArray());
    }

    [Fact]
    public async Task NczYazici_Sablonlu_Eski_Imar_Kayitlari_Dusmeli_Yeni_Parseller_Eklenmeli_Tip14_Sonda_Kalmali()
    {
        var (sablon, _) = SentetikSablon();
        var p = await PaketAsync(sablon);

        var d = Yaz(p);
        var belge = Oku(d);

        belge.Polylines.Select(x => x.Label).Should().NotContain("ESKI", "TK_PARSEL_OLUŞAN yeniden yazılır")
            .And.NotContain("ESKIA1", "köşe dizisi projedeki imar parseliyle aynı şablon poligonu yeniden yazılır");
        belge.Polylines.Where(x => x.LayerName == NczParselasyonYazici.OlusanKatmani).Select(x => x.Label).Should().Equal("101/1");
        belge.Polylines.Where(x => x.LayerName == NczParselasyonYazici.YolTerkKatmani).Select(x => x.Label).Should().Equal("YOL");
        belge.Polylines.Where(x => x.LayerName == NczParselasyonYazici.GirenKatmani).Select(x => x.Label).Should().Equal(["K1"], "kadastro şablondan kopyalanır, yinelenmez");
        belge.SkippedEntityCount.Should().BeGreaterThan(0, "çizgi ve tip 14 korunur");
        var kayitlar = Kayitlar(d);
        kayitlar[^1].Yuk[0].Should().Be(14, "spike: son kayıt tip 14 — yeni kayıtlar önüne eklenir");
        kayitlar.Count(k => Varlik(k) && k.Yuk[0] == 2).Should().Be(1, "şablon çizgisi korunur");
    }

    [Fact]
    public async Task NczYazici_Sablonlu_Eklenen_Katmanlar_Kadar_Ofsetler_Kaymali_Ve_Tutarli_Olmali()
    {
        var (sablon, _) = SentetikSablon();
        var p = await PaketAsync(sablon);

        var d = Yaz(p);

        const int Eklenen = 2 * 29; // TK_PARSEL_YOLATERK + NOKTA_PARSEL_YENI
        H(d, 1).Should().Be(d.Length);
        H(d, 7).Should().Be(H(sablon, 7) + Eklenen);
        H(d, 10).Should().Be(H(sablon, 10) + Eklenen);
        H(d, 11).Should().Be(H(sablon, 11) + Eklenen);
        H(d, 9).Should().Be(H(sablon, 9));
        H(d, 8).Should().Be(H(sablon, 8) + Eklenen, "h[8] katman tablosundan sonrasını gösterir");
        (H(d, 8) - H(d, 7)).Should().Be(89);
        BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(H(d, 3) + 16)).Should().Be(5);
        var varliklar = Kayitlar(d).Where(Varlik).ToList();
        BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(81)).Should().Be(varliklar.Count - 1);
        Oku(d).Polylines.Should().HaveCount(3);
    }

    [Fact]
    public async Task NczYazici_Sablon_Ayristirilamazsa_ERR_CIKTI_003_Donmeli()
    {
        var (sablon, _) = SentetikSablon();
        BinaryPrimitives.WriteInt16LittleEndian(sablon.AsSpan(H(sablon, 3) + 16), 200); // katman tablosu taşar
        var p = await PaketAsync(sablon);

        var r = NczParselasyonYazici.Yaz(p);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be(ErrorCodes.CiktiCadDisaAktarim);
        r.Error.TechnicalMessage.Should().Contain("NCZ");
    }

    [Fact]
    public async Task NczYazici_Sablonlu_Uretim_Tekrarlanabilir_Olmali()
    {
        var (sablon, _) = SentetikSablon();
        var p = await PaketAsync(sablon);

        Yaz(p).Should().Equal(Yaz(p));
    }

    // ---------------- reviewer bulguları (H1/H2, test boşlukları) ----------------

    private static CiktiPaketi ImarDegistir(CiktiPaketi p, Func<ParselCetvelSatiri, ParselCetvelSatiri> f) =>
        p with { Parseller = p.Parseller.Select(x => x.GeciciKod == "A1" ? f(x) : x).ToArray() };

    [Theory]
    [InlineData("101/1234567890123")]
    [InlineData("101/Ω")]
    public async Task NczYazici_Etiket_Alana_Sigmazsa_Veya_1254_Disiysa_Kirpmadan_ERR_CIKTI_003_Donmeli(string etiket)
    {
        var p = ImarDegistir(await PaketAsync(), x => x with { TescilNo = etiket });

        var r = NczParselasyonYazici.Yaz(p);

        r.IsFailure.Should().BeTrue("tescil no sessizce kırpılmaz veya '?' olmaz (architect C4)");
        r.Error.Code.Should().Be(ErrorCodes.CiktiCadDisaAktarim);
    }

    [Fact]
    public async Task NczYazici_Sablonda_Proje_Kadastrosu_Yoksa_Reddetmeli()
    {
        var (sablon, _) = SentetikSablon(kadastroKatmani: 1, kadastroVar: false, tip14: true);
        var p = await PaketAsync(sablon);

        var r = NczParselasyonYazici.Yaz(p);

        r.IsFailure.Should().BeTrue("reviewer H2: kadastrosu eksik harita sessizce üretilmez");
        r.Error.TechnicalMessage.Should().Contain("cadastral");
    }

    [Fact]
    public async Task NczYazici_Sablonlu_Yonetilen_Katmandaki_Kadastro_Halkasi_Dusmemeli()
    {
        var (sablon, kadastroKaydi) = SentetikSablon(kadastroKatmani: 2, kadastroVar: true, tip14: true); // K1 TK_PARSEL_OLUŞAN'da
        var p = await PaketAsync(sablon);

        var d = Yaz(p);

        Indeks(d, kadastroKaydi).Should().BeGreaterThan(H(d, 11), "projedeki kadastro halkası yönetilen katmanda da korunur");
    }

    [Fact]
    public async Task NczYazici_Sablonda_Tip14_Yoksa_Yeni_Kayitlar_Sona_Eklenmeli()
    {
        var (sablon, _) = SentetikSablon(kadastroKatmani: 1, kadastroVar: true, tip14: false);
        var p = await PaketAsync(sablon);

        var kayitlar = Kayitlar(Yaz(p));

        Yazi(kayitlar[^1].Yuk).Should().StartWith("RevisionId ", "son yeni kayıt (damga) dosyanın sonundadır");
    }

    [Fact]
    public async Task NczYazici_Konkav_Parselde_Etiket_Ic_Noktada_Olmali()
    {
        // U biçimli imar parseli: alan ağırlık merkezi iki kol arasındaki boşlukta kalır → yedek iç nokta kullanılmalı.
        static GeodeticPoint K(decimal x, decimal y) => new(OrnekProje.X0 + x, OrnekProje.Y0 + y);
        GeodeticPoint[] u = [K(0, 0), K(0, 12), K(10, 12), K(10, 9), K(2, 9), K(2, 3), K(10, 3), K(10, 0)];
        var p = ImarDegistir(await PaketAsync(), x => x with { Koseler = u, NoktaNolari = Enumerable.Range(100, u.Length).ToArray() });

        var etiket = Kayitlar(Yaz(p)).Single(k => Varlik(k) && k.Yuk[0] == 5 && Yazi(k.Yuk) == "101/1");

        var n = ((decimal)D(etiket.Yuk, 3) + (decimal)D(etiket.Yuk, 61)) / 2m;
        var e = ((decimal)D(etiket.Yuk, 11) + (decimal)D(etiket.Yuk, 69)) / 2m;
        var ic = false;
        for (int i = 0, j = u.Length - 1; i < u.Length; j = i++)
        {
            if ((u[i].X > n) != (u[j].X > n) && e < u[j].Y + ((n - u[j].X) * (u[i].Y - u[j].Y) / (u[i].X - u[j].X)))
            {
                ic = !ic;
            }
        }

        ic.Should().BeTrue("etiket konkav parselin içinde olmalı");
    }

    [Fact]
    public async Task NczYazici_Iptal_Edilirse_OperationCanceled_Firlatmali()
    {
        var p = await PaketAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => NczParselasyonYazici.Yaz(p, cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }
}

using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cad.Ncz;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cad.Ncz;

/// <summary>
/// P4.0 (D14) NczFormatAdapter bicim uyumluluk testleri. Tum girdiler <see cref="NczSentetikDosya"/> ile uretilen
/// sentetik dosyalardir; beklenen alanlar elle hesaplanan bagimsiz referanslardir (30 x 40 = 1200 m2 vb.).
/// </summary>
public sealed class NczFormatAdapterTests
{
    // Sentetik, gercek konum icermeyen TM benzeri koordinatlar (X = Kuzey ~ 4.0e6, Y = Dogu ~ 5.0e5).
    private const double X0 = 4_000_000.0;
    private const double Y0 = 500_000.0;

    private static readonly (double X, double Y, double Z)[] Dikdortgen30x40 =
    [
        (X0, Y0, 0), (X0 + 40, Y0, 0), (X0 + 40, Y0 + 30, 0), (X0, Y0 + 30, 0), (X0, Y0, 0),
    ];

    private static readonly NczFormatAdapter Adaptor = new();

    private static NczSentetikDosya TemelDosya() => new NczSentetikDosya()
        .Katman("YAZI")
        .Katman("TK_PARSEL_GİREN")
        .Katman("TK_PARSEL_OLUŞAN");

    private static CadLayerDocument Oku(NczSentetikDosya dosya)
    {
        using var akis = dosya.Akis();
        var sonuc = Adaptor.Read(akis);
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        return sonuc.Value;
    }

    private static AppError OkuHata(byte[] bayt)
    {
        using var akis = new MemoryStream(bayt, writable: false);
        var sonuc = Adaptor.Read(akis);
        sonuc.IsFailure.Should().BeTrue();
        return sonuc.Error;
    }

    [Fact]
    public void NczAdaptor_Bicim_Ve_Uzanti_Ncz_Olarak_Bildirilmeli()
    {
        Adaptor.Format.Should().Be(CadFormat.Ncz);
        Adaptor.SupportsExtension(".ncz").Should().BeTrue();
        Adaptor.SupportsExtension(".NCZ").Should().BeTrue();
        Adaptor.SupportsExtension("ncz").Should().BeTrue();
        Adaptor.SupportsExtension(".dxf").Should().BeFalse();
        Adaptor.SupportsExtension(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void NczAdaptor_Katman_Adlarini_Windows1254_Turkce_Karakterleriyle_Sirali_Okumali()
    {
        var belge = Oku(TemelDosya().Katman("DÜZENLEME_SINIRI").Katman("TESCİLLİ_NOKTA"));

        belge.Format.Should().Be(CadFormat.Ncz);
        belge.Layers.Select(l => l.Name).Should().Equal(
            "YAZI", "TK_PARSEL_GİREN", "TK_PARSEL_OLUŞAN", "DÜZENLEME_SINIRI", "TESCİLLİ_NOKTA");
        belge.Layers.Select(l => l.Index).Should().Equal(0, 1, 2, 3, 4);
    }

    [Fact]
    public void NczAdaptor_Kapali_Poligonu_Jeodezik_X_Kuzey_Y_Dogu_Eksenine_Normalize_Etmeli()
    {
        var belge = Oku(TemelDosya().Poligon(2, "101/1", Dikdortgen30x40));

        var p = belge.Polylines.Should().ContainSingle().Subject;
        p.LayerName.Should().Be("TK_PARSEL_OLUŞAN");
        p.Label.Should().Be("101/1");
        p.FeatureClass.Should().BeNull();
        p.IsClosed.Should().BeTrue();
        p.DeclaredAreaM2.Should().BeNull();
        p.Vertices.Should().HaveCount(5);
        p.Vertices[1].Should().Be(new GeodeticPoint(X: 4_000_040m, Y: 500_000m));
        p.Vertices[2].Should().Be(new GeodeticPoint(X: 4_000_040m, Y: 500_030m));
        belge.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void NczAdaptor_Okunan_Dikdortgenin_Decimal_Gauss_Alani_Bagimsiz_Referansa_Esit_Olmali()
    {
        var belge = Oku(TemelDosya().Poligon(2, "101/1", Dikdortgen30x40));

        var halka = PolygonRing.FromClosed(belge.Polylines[0].Vertices);
        halka.IsSuccess.Should().BeTrue();
        GaussAreaCalculator.Compute(halka.Value).AreaM2.Should().Be(1200.00m);
    }

    [Fact]
    public void NczAdaptor_Ham_Ondalikli_Koordinati_Kayipsiz_Cozup_6_Ondaliga_AwayFromZero_Yuvarlamali()
    {
        // 2^-20 = 0.00000095367431640625 -> 0.000001 ; 2^-21 = 0.000000476837158203125 -> 0.000000.
        var ikiUzeriEksi20 = Math.ScaleB(1d, -20);
        var ikiUzeriEksi21 = Math.ScaleB(1d, -21);
        (double, double, double)[] koseler =
        [
            (X0 + 0.125, Y0 + ikiUzeriEksi20, 0), (X0 + 10, Y0 + ikiUzeriEksi21, 0), (X0 + 10, Y0 + 10, 0), (X0 + 0.125, Y0 + ikiUzeriEksi20, 0),
        ];

        var v = Oku(TemelDosya().Poligon(2, "T", koseler)).Polylines[0].Vertices;

        v[0].X.Should().Be(4_000_000.125m);
        v[0].Y.Should().Be(500_000.000001m);
        v[1].Y.Should().Be(500_000.000000m);
    }

    [Fact]
    public void NczAdaptor_Sifirdan_Farkli_Z_Kotlarini_Atip_Tek_ERR_GIRD_004_Uyarisi_Uretmeli()
    {
        (double, double, double)[] kotlu =
        [
            (X0, Y0, 987.5), (X0 + 40, Y0, 988.25), (X0 + 40, Y0 + 30, 0), (X0, Y0 + 30, 0), (X0, Y0, 987.5),
        ];

        var belge = Oku(TemelDosya().Poligon(1, "A", kotlu).Poligon(2, "B", kotlu));

        belge.Polylines[0].Vertices[0].Should().Be(new GeodeticPoint(X: 4_000_000m, Y: 500_000m));
        var uyari = belge.Warnings.Should().ContainSingle().Subject;
        uyari.Code.Should().Be(ErrorCodes.GirdZKoduIndirgendi);
        uyari.Severity.Should().Be(ErrorSeverity.Warning);
        uyari.TechnicalMessage.Should().Contain("2 of 2 polylines");
        uyari.TechnicalMessage.Should().NotContain("987").And.NotContain("4000000");
    }

    [Fact]
    public void NczAdaptor_Alan_Bayragi_Varsa_Bildirilen_Alani_Kayipsiz_Okumali()
    {
        var belge = Oku(TemelDosya().Poligon(1, "A", Dikdortgen30x40, bildirilenAlan: 1200.25));

        belge.Polylines[0].DeclaredAreaM2.Should().Be(1200.25m);
    }

    [Fact]
    public void NczAdaptor_Genisletilmis_0x16_Kaydinda_Sinif_Ve_Etiket_Alanlarini_Okumali()
    {
        var belge = Oku(TemelDosya().Poligon(2, "103/5", Dikdortgen30x40, bildirilenAlan: 1200, sinif: "PARSEL"));

        var p = belge.Polylines.Should().ContainSingle().Subject;
        p.FeatureClass.Should().Be("PARSEL");
        p.Label.Should().Be("103/5");
        p.DeclaredAreaM2.Should().Be(1200m);
        GaussAreaCalculator.Compute(PolygonRing.FromClosed(p.Vertices).Value).AreaM2.Should().Be(1200.00m);
    }

    [Fact]
    public void NczAdaptor_Ilk_Ve_Son_Kosesi_Farkli_Cizgiyi_Acik_Olarak_Isaretlemeli()
    {
        (double, double, double)[] acik = [(X0, Y0, 0), (X0 + 10, Y0, 0)];

        var p = Oku(TemelDosya().Poligon(0, "yol", acik)).Polylines.Should().ContainSingle().Subject;

        p.IsClosed.Should().BeFalse();
        p.Vertices.Should().HaveCount(2);
    }

    [Fact]
    public void NczAdaptor_Poligon_Disi_Varliklari_Atlayip_Sayisini_Raporlamali()
    {
        var belge = Oku(TemelDosya()
            .Cizgi(0, (X0, Y0), (X0 + 1, Y0 + 1))
            .Poligon(2, "101/1", Dikdortgen30x40)
            .Cizgi(1, (X0, Y0), (X0 + 2, Y0 + 2)));

        belge.Polylines.Should().ContainSingle();
        belge.SkippedEntityCount.Should().Be(2);
        belge.Texts.Should().BeEmpty("NCZ etiketi poligon kaydindadir; NCZ yazi varliklari bu adimda cozulmez");
    }

    [Fact]
    public void NczAdaptor_Bos_Varlik_Akisinda_Bos_Belge_Dondurmeli()
    {
        var belge = Oku(TemelDosya());

        belge.Polylines.Should().BeEmpty();
        belge.SkippedEntityCount.Should().Be(0);
        belge.Layers.Should().HaveCount(3);
    }

    [Fact]
    public void NczAdaptor_Imza_Bayti_Hataliysa_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var hata = OkuHata(new NczSentetikDosya { Imza = 0x50 }.Katman("YAZI").Olustur());

        hata.Code.Should().Be(ErrorCodes.GirdNczOkunamadi);
        hata.TechnicalMessage.Should().Contain("signature");
    }

    [Fact]
    public void NczAdaptor_Bilinmeyen_Baslik_Surumunde_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        OkuHata(new NczSentetikDosya { Baslik0 = 121 }.Katman("YAZI").Olustur()).Code.Should().Be(ErrorCodes.GirdNczOkunamadi);
        OkuHata(new NczSentetikDosya { Baslik2 = 99 }.Katman("YAZI").Olustur()).Code.Should().Be(ErrorCodes.GirdNczOkunamadi);
    }

    [Fact]
    public void NczAdaptor_Bildirilen_Dosya_Uzunlugu_Tutmazsa_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var hata = OkuHata(new NczSentetikDosya { BildirilenUzunluk = 10 }.Katman("YAZI").Olustur());

        hata.Code.Should().Be(ErrorCodes.GirdNczOkunamadi);
        hata.TechnicalMessage.Should().Contain("length");
    }

    [Fact]
    public void NczAdaptor_Kirpilmis_Dosyada_Cokmeden_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var bayt = TemelDosya().Poligon(2, "101/1", Dikdortgen30x40).Olustur();

        OkuHata(bayt[..^7]).Code.Should().Be(ErrorCodes.GirdNczOkunamadi);
        for (var n = 0; n < bayt.Length; n += 37)
        {
            OkuHata(bayt[..n]).Code.Should().Be(ErrorCodes.GirdNczOkunamadi);
        }
    }

    [Fact]
    public void NczAdaptor_Bildirilen_Uzunluk_Dogru_Ama_Kayit_Akisi_Tasiyorsa_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        // Kayit boyutu dosya sonunu gosterecek kadar buyuk: uzunluk basligi tutarli, akis tutarsiz.
        var yuk = new byte[10];
        var bayt = TemelDosya().HamKayit(0x15, yuk).Olustur();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bayt.AsSpan(bayt.Length - 14), 1_000);

        OkuHata(bayt).TechnicalMessage.Should().Contain("record");
    }

    [Fact]
    public void NczAdaptor_Kose_Bloku_24_Baytin_Kati_Degilse_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var bayt = TemelDosya().Poligon(2, "101/1", Dikdortgen30x40, fazlaBayt: 5).Olustur();

        OkuHata(bayt).TechnicalMessage.Should().Contain("vertex block");
    }

    [Fact]
    public void NczAdaptor_Katman_Indeksi_Tablo_Disindaysa_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var bayt = TemelDosya().Poligon(2, "101/1", Dikdortgen30x40, katmanAlaniOverride: 3).Olustur();

        OkuHata(bayt).TechnicalMessage.Should().Contain("layer index");
    }

    [Fact]
    public void NczAdaptor_Etiket_Uzunlugu_Alan_Kapasitesini_Asarsa_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var bayt = TemelDosya().Poligon(2, "101/1", Dikdortgen30x40, etiketUzunlukOverride: 15).Olustur();

        OkuHata(bayt).TechnicalMessage.Should().Contain("string");
    }

    [Fact]
    public void NczAdaptor_Bilinmeyen_Bayrakli_Kaydi_Reddetmeden_Taninmayan_Kayit_Olarak_Saymali()
    {
        // Yuku tesadufen 7 ile baslayan bilinmeyen bayrakli kayit poligon sayilmaz; dosya reddedilmez, sayac gorunur.
        var yuk = new byte[120];
        yuk[0] = 7;
        var belge = Oku(TemelDosya().HamKayit(0x19, yuk).HamKayit(0x1B, yuk).Poligon(2, "101/1", Dikdortgen30x40));

        belge.Polylines.Should().ContainSingle();
        belge.UnrecognizedRecordCount.Should().Be(1);
        belge.SkippedEntityCount.Should().Be(0);
    }

    [Fact]
    public void NczAdaptor_1mm_Izgara_Disi_Koseleri_Kuantize_Etmeden_Sayip_ERR_GIRD_006_Uyarisi_Uretmeli()
    {
        (double, double, double)[] izgaraDisi =
        [
            (X0 + 0.0004, Y0, 0), (X0 + 40, Y0 + 0.1234567, 0), (X0 + 40, Y0 + 30, 0), (X0, Y0 + 30, 0), (X0 + 0.0004, Y0, 0),
        ];

        var belge = Oku(TemelDosya().Poligon(2, "T", izgaraDisi).Poligon(1, "U", Dikdortgen30x40));

        belge.OffGridVertexCount.Should().Be(2); // ilk ve ikinci kose (kapanis tekrari sayilmaz)
        var uyari = belge.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdIzgaraDisiKose).Subject;
        uyari.TechnicalMessage.Should().Contain("2 vertices").And.Contain("NCZ input").And.NotContain("4000000");
        uyari.RequiresEngineerReview.Should().BeTrue();
        belge.Polylines[0].Vertices[0].X.Should().Be(4_000_000.0004m);
        belge.Polylines[0].Vertices[1].Y.Should().Be(500_000.123457m);
    }

    [Fact]
    public void NczAdaptor_Negatif_Katman_Sayisinda_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var bayt = TemelDosya().Olustur();
        var tablo = NczSentetikDosya.BaslikOku(bayt, 3);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bayt.AsSpan(tablo + 16), -1);

        OkuHata(bayt).TechnicalMessage.Should().Contain("layer table");
    }

    [Fact]
    public void NczAdaptor_Katman_Tablosu_Ofseti_Disaridaysa_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var bayt = TemelDosya().Olustur();
        NczSentetikDosya.BaslikYaz(bayt, 3, bayt.Length - 3);

        OkuHata(bayt).TechnicalMessage.Should().Contain("layer table offset");
    }

    [Fact]
    public void NczAdaptor_Katman_Tablosu_Sonu_Tutmazsa_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var bayt = TemelDosya().Olustur();
        NczSentetikDosya.BaslikYaz(bayt, 7, NczSentetikDosya.BaslikOku(bayt, 7) + 1);

        OkuHata(bayt).TechnicalMessage.Should().Contain("layer table end");
    }

    [Fact]
    public void NczAdaptor_Varlik_Akisi_Ofseti_Gecersizse_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var bayt = TemelDosya().Olustur();
        NczSentetikDosya.BaslikYaz(bayt, 11, 10);
        OkuHata(bayt).TechnicalMessage.Should().Contain("entity stream offset");

        NczSentetikDosya.BaslikYaz(bayt, 11, bayt.Length + 1);
        OkuHata(bayt).TechnicalMessage.Should().Contain("entity stream offset");
    }

    [Fact]
    public void NczAdaptor_Kirpik_Kayit_Basliginda_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var tam = TemelDosya().Olustur();
        var bayt = new byte[tam.Length + 3];
        tam.CopyTo(bayt, 0);
        bayt[^3] = 0x15;
        NczSentetikDosya.BaslikYaz(bayt, 1, bayt.Length);

        OkuHata(bayt).TechnicalMessage.Should().Contain("truncated record header");
    }

    [Fact]
    public void NczAdaptor_Negatif_Kayit_Boyutunda_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var bayt = TemelDosya().HamKayit(0x15, new byte[10]).Olustur();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bayt.AsSpan(bayt.Length - 14), -1);

        OkuHata(bayt).TechnicalMessage.Should().Contain("record exceeds");
    }

    [Fact]
    public void NczAdaptor_Kisa_Poligon_Yukunde_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var yuk = new byte[50];
        yuk[0] = 7;

        OkuHata(TemelDosya().HamKayit(0x15, yuk).Olustur()).TechnicalMessage.Should().Contain("polyline record truncated");
    }

    [Fact]
    public void NczAdaptor_Sinif_Alani_Tasarsa_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var bayt = TemelDosya().Poligon(2, "101/1", Dikdortgen30x40, sinif: "PARSEL", sinifUzunlukOverride: 28).Olustur();

        OkuHata(bayt).TechnicalMessage.Should().Contain("class pascal string");
    }

    [Fact]
    public void NczAdaptor_Bildirilen_Alan_NaN_Ise_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var bayt = TemelDosya().Poligon(2, "101/1", Dikdortgen30x40, bildirilenAlan: double.NaN).Olustur();

        OkuHata(bayt).TechnicalMessage.Should().Contain("declared area");
    }

    [Fact]
    public void NczAdaptor_Sifir_Koseli_Poligonda_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        var bayt = TemelDosya().Poligon(2, "101/1", []).Olustur();

        OkuHata(bayt).TechnicalMessage.Should().Contain("vertex block");
    }

    [Fact]
    public void NczAdaptor_Bayt_Bozma_Fuzz_Hic_Istisna_Atmamali()
    {
        // Baslik uzunlugu tutarli kalir; ic yapilara rastgele bozulma uygulanir (sabit tohum: tekrar edilebilir).
        var temiz = TemelDosya()
            .Cizgi(0, (X0, Y0), (X0 + 1, Y0 + 1))
            .Poligon(2, "101/1", Dikdortgen30x40, bildirilenAlan: 1200, sinif: "PARSEL")
            .Poligon(1, "A", Dikdortgen30x40)
            .Olustur();
        var rastgele = new Random(20261117);
        for (var deneme = 0; deneme < 3000; deneme++)
        {
            var bayt = (byte[])temiz.Clone();
            var bozma = 1 + rastgele.Next(4);
            for (var i = 0; i < bozma; i++)
            {
                bayt[49 + rastgele.Next(bayt.Length - 49)] = (byte)rastgele.Next(256);
            }

            using var akis = new MemoryStream(bayt, writable: false);
            var eylem = () => Adaptor.Read(akis);
            eylem.Should().NotThrow();
        }
    }

    [Fact]
    public void NczAdaptor_Ayristirma_Sirasinda_Iptal_Edilirse_OperationCanceledException_Firlatmali()
    {
        var dosya = TemelDosya();
        for (var i = 0; i < 300; i++)
        {
            dosya.Cizgi(0, (X0, Y0), (X0 + 1, Y0 + 1));
        }

        using var cts = new CancellationTokenSource();
        using var akis = new SondaIptalEdenAkis(dosya.Olustur(), cts);

        var eylem = () => Adaptor.Read(akis, cts.Token);

        eylem.Should().Throw<OperationCanceledException>();
        akis.IptalEdildi.Should().BeTrue();
    }

    [Fact]
    public void NczAdaptor_NaN_Veya_Sonsuz_Koordinatta_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        (double, double, double)[] nan = [(X0, Y0, 0), (double.NaN, Y0, 0), (X0 + 1, double.PositiveInfinity, 0), (X0, Y0, 0)];

        OkuHata(TemelDosya().Poligon(2, "T", nan).Olustur()).TechnicalMessage.Should().Contain("non-finite");
    }

    [Fact]
    public void NczAdaptor_Hata_Mesajlarinda_Koordinat_Veya_Etiket_Sizdirmamali()
    {
        var bayt = TemelDosya().Poligon(2, "GIZLI/99", Dikdortgen30x40, fazlaBayt: 5).Olustur();

        var hata = OkuHata(bayt);
        hata.TechnicalMessage.Should().NotContain("GIZLI").And.NotContain("4000000").And.NotContain("500000");
        hata.UserMessage.Should().NotContain("GIZLI");
    }

    [Fact]
    public void NczAdaptor_Bos_Veya_Cok_Kisa_Akista_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        OkuHata([]).Code.Should().Be(ErrorCodes.GirdNczOkunamadi);
        OkuHata([0x01, 0x78]).Code.Should().Be(ErrorCodes.GirdNczOkunamadi);
    }

    [Fact]
    public void NczAdaptor_Boyut_Sinirini_Asan_Akista_Okumadan_ERR_GIRD_NCZ_001_Dondurmeli()
    {
        using var buyuk = new SahteUzunAkis(NczFormatAdapter.MaxFileSizeBytes + 1);

        var sonuc = Adaptor.Read(buyuk);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.TechnicalMessage.Should().Contain("too large");
        buyuk.OkunanBayt.Should().Be(0);
    }

    [Fact]
    public void NczAdaptor_Iptal_Edilmis_Belirtecle_OperationCanceledException_Firlatmali()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var akis = TemelDosya().Poligon(2, "101/1", Dikdortgen30x40).Akis();

        var eylem = () => Adaptor.Read(akis, cts.Token);

        eylem.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void NczAdaptor_Null_Akista_ArgumentNullException_Firlatmali()
    {
        var eylem = () => Adaptor.Read(null!);

        eylem.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NczAdaptor_Ayni_Girdide_Tekrar_Edilebilir_Ayni_Sonucu_Uretmeli()
    {
        var dosya = TemelDosya().Poligon(2, "101/1", Dikdortgen30x40, bildirilenAlan: 1200).Poligon(1, "A", Dikdortgen30x40);

        var a = Oku(dosya);
        var b = Oku(dosya);

        a.Polylines.Select(p => (p.Label, p.LayerName, p.DeclaredAreaM2))
            .Should().Equal(b.Polylines.Select(p => (p.Label, p.LayerName, p.DeclaredAreaM2)));
        a.Polylines.SelectMany(p => p.Vertices).Should().Equal(b.Polylines.SelectMany(p => p.Vertices));
    }

    /// <summary>Akis sonuna (Read == 0) ulasildiginda belirteci iptal eder: iptal ayristirma asamasinda gerceklesir.</summary>
    private sealed class SondaIptalEdenAkis(byte[] veri, CancellationTokenSource cts) : MemoryStream(veri, writable: false)
    {
        public bool IptalEdildi { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = base.Read(buffer, offset, count);
            if (n == 0)
            {
                cts.Cancel();
                IptalEdildi = true;
            }

            return n;
        }
    }

    /// <summary>Uzunlugu buyuk bildiren ama okunmamasi gereken akis (bellek tuketmeden boyut sinirini test eder).</summary>
    private sealed class SahteUzunAkis(long uzunluk) : Stream
    {
        public long OkunanBayt { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => uzunluk;

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            OkunanBayt += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

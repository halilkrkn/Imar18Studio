using System.Numerics;
using System.Text;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Girdi;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Crs;
using Imar18.Infrastructure.Cad.Acad;
using Imar18.Infrastructure.Cad.Cks;
using Imar18.Infrastructure.Cad.Ncz;
using Imar18.Infrastructure.Tests.Cad.Ncz;
using Xunit;

namespace Imar18.Infrastructure.Tests;

/// <summary>
/// Antigravity Bağımsız Test ve Denetim Paketi — Faz P4 (P4.0–P4.3).
/// Üretim koduna dokunulmadan, bağımsız matematiksel ve mevzuatsal referanslarla
/// .NCZ / .DXF / .DWG / .CKS eksen normalizasyonu (TC-CAD-001), Z=0 yatay izdüşüm indirgemesi,
/// 1 mm dışı köşe uyarısı (ERR-GIRD-006 / SORU-025), ProjNet farklı datum/epok/dilim dışı ret testleri,
/// 2B Helmert artık kontrolü ve TAKBİS KVKK kişisel veri maskeleme testlerini kapsar.
/// </summary>
public sealed class AntigravityIndependentCadAuditTests
{
    private const double RefDogu = 500_000.0;
    private const double RefKuzey = 4_000_000.0;

    private static readonly DxfFormatAdapter DxfAdapter = new();
    private static readonly DwgFormatAdapter DwgAdapter = new();
    private static readonly NczFormatAdapter NczAdapter = new();
    private static readonly CksFileReader CksReader = new();

    private static byte[] DxfBayt(CadDocument doc)
    {
        using var ms = new MemoryStream();
        DxfWriter.Write(ms, doc, false, null);
        return ms.ToArray();
    }

    private static byte[] DwgBayt(CadDocument doc)
    {
        using var ms = new MemoryStream();
        DwgWriter.Write(ms, doc, null);
        return ms.ToArray();
    }

    #region 1. .NCZ / .DXF / .DWG / .CKS Eksen Normalizasyonu (TC-CAD-001)

    [Fact]
    public void TC_CAD_001_DXF_DikUcgen_Kartezyen_XY_Koordinatlarini_Jeodezik_Kuzey_Dogu_Eksenine_Normalize_Etmeli()
    {
        // CAD Kartezyen: X = Doğu, Y = Kuzey
        // Dik üçgen: Köşe 1: (Doğu0, Kuzey0), Köşe 2: (Doğu0 + 60, Kuzey0), Köşe 3: (Doğu0, Kuzey0 + 40)
        // Beklenen Jeodezik: X = Kuzey, Y = Doğu
        // Taban = 60 m (Doğu yönünde), Yükseklik = 40 m (Kuzey yönünde), Alan = 60 * 40 / 2 = 1200.00 m²
        var doc = new CadDocument();
        var layer = new Layer("TK_PARSEL_OLUŞAN");
        doc.Layers.Add(layer);

        var lw = new LwPolyline { Layer = layer, IsClosed = true };
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu, RefKuzey)));
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu + 60.0, RefKuzey)));
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu, RefKuzey + 40.0)));
        doc.Entities.Add(lw);

        using var akis = new MemoryStream(DxfBayt(doc), writable: false);
        var result = DxfAdapter.Read(akis);
        result.IsSuccess.Should().BeTrue();

        var polyline = result.Value.Polylines.Should().ContainSingle().Subject;
        polyline.IsClosed.Should().BeTrue();
        polyline.Vertices.Should().HaveCount(4, "kapalı halkanın son noktası ilk noktayla eşlenir");

        // Jeodezik koordinat eşleşmesi (TC-CAD-001):
        // Köşe 0: X = 4_000_000, Y = 500_000
        // Köşe 1: X = 4_000_000, Y = 500_060
        // Köşe 2: X = 4_000_040, Y = 500_000
        polyline.Vertices[0].Should().Be(new GeodeticPoint(X: 4_000_000m, Y: 500_000m));
        polyline.Vertices[1].Should().Be(new GeodeticPoint(X: 4_000_000m, Y: 500_060m));
        polyline.Vertices[2].Should().Be(new GeodeticPoint(X: 4_000_040m, Y: 500_000m));
        polyline.Vertices[3].Should().Be(polyline.Vertices[0]);

        var ring = PolygonRing.FromClosed(polyline.Vertices).Value;
        GaussAreaCalculator.Compute(ring).AreaM2.Should().Be(1200.00m);
    }

    [Fact]
    public void TC_CAD_001_DWG_Trapez_Poligonu_Eksen_Takasiyla_Okuyup_Alani_Dogrulamali()
    {
        // CAD Kartezyen: Alt taban (Kuzey0'da) 50 m, Üst taban (Kuzey0+20'de) 30 m, Yükseklik 20 m
        // Alan = ((50 + 30) / 2) * 20 = 800.00 m²
        var doc = new CadDocument();
        var layer = new Layer("TK_PARSEL_OLUŞAN");
        doc.Layers.Add(layer);

        var lw = new LwPolyline { Layer = layer, IsClosed = true };
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu, RefKuzey)));
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu + 50.0, RefKuzey)));
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu + 40.0, RefKuzey + 20.0)));
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu + 10.0, RefKuzey + 20.0)));
        doc.Entities.Add(lw);

        using var akis = new MemoryStream(DwgBayt(doc), writable: false);
        var result = DwgAdapter.Read(akis);
        result.IsSuccess.Should().BeTrue();

        var polyline = result.Value.Polylines.Should().ContainSingle().Subject;
        polyline.Vertices[1].Should().Be(new GeodeticPoint(X: 4_000_000m, Y: 500_050m));
        polyline.Vertices[2].Should().Be(new GeodeticPoint(X: 4_000_020m, Y: 500_040m));

        var ring = PolygonRing.FromClosed(polyline.Vertices).Value;
        GaussAreaCalculator.Compute(ring).AreaM2.Should().Be(800.00m);
    }

    [Fact]
    public void TC_CAD_001_NCZ_Sentetik_Poligonda_Jeodezik_Eksenlerin_Birebir_Korundugunu_Dogrulamali()
    {
        // Netcad NCZ ham ikili verisinde koordinatlar jeodezik (X = Kuzey, Y = Doğu) saklanır.
        // Paralelkenar: X0..X0+30, Y0..Y0+50, Alan = 30 * 50 = 1500.00 m²
        (double X, double Y, double Z)[] paralelkenar =
        [
            (RefKuzey, RefDogu, 0),
            (RefKuzey, RefDogu + 50.0, 0),
            (RefKuzey + 30.0, RefDogu + 70.0, 0),
            (RefKuzey + 30.0, RefDogu + 20.0, 0),
            (RefKuzey, RefDogu, 0)
        ];

        var dosya = new NczSentetikDosya()
            .Katman("TK_PARSEL_OLUŞAN")
            .Poligon(0, "PARALEL-1", paralelkenar);

        using var akis = dosya.Akis();
        var result = NczAdapter.Read(akis);
        result.IsSuccess.Should().BeTrue();

        var polyline = result.Value.Polylines.Should().ContainSingle().Subject;
        polyline.Vertices[0].Should().Be(new GeodeticPoint(X: 4_000_000m, Y: 500_000m));
        polyline.Vertices[1].Should().Be(new GeodeticPoint(X: 4_000_000m, Y: 500_050m));
        polyline.Vertices[2].Should().Be(new GeodeticPoint(X: 4_000_030m, Y: 500_070m));
        polyline.Vertices[3].Should().Be(new GeodeticPoint(X: 4_000_030m, Y: 500_020m));

        var ring = PolygonRing.FromClosed(polyline.Vertices).Value;
        GaussAreaCalculator.Compute(ring).AreaM2.Should().Be(1500.00m);
    }

    [Fact]
    public void TC_CAD_001_CKS_Okuyucu_Netcad_Cetvel_Degerlerini_Ve_Kamuya_Ayrilan_Alani_Dogrulamali()
    {
        // Sentetik CKS belgesi
        var cksIcerik = new[]
        {
            "┌" + new string('─', 85) + "┐",
            "│ Toplam Tahsis                        =     5000.00                                   │",
            "│ Toplam İmar Ada Alanı                =     5000.00                                   │",
            "│ Toplam Ortak Katılım Alanı           =        0.00                                   │",
            "│ Toplam Düzenlemeye Giren (Dopal)     =     8000.00                                   │",
            "│ Toplam Düzenlemeye Giren (Dopalma)   =        0.00                                   │",
            "│ Toplam Kamulaştırılan Alan           =        0.00                                   │",
            "│ Toplam Bağışlanan Alan               =        0.00                                   │",
            "│ Toplam Kadastro Parsel Alanı         =     8000.00                                   │",
            "│ Toplam Pay                           =     8000.00                                   │",
            "├" + new string('─', 85) + "┤",
            "│ Ortak Katılım Oranı                  =   0.0000000                                   │",
            "│ Düzenleme Ortaklık Payı Oranı (DOPO) =   0.3750000                                   │",
            "│ DOPO (OKO ile birlikte)              =   0.3750000                                   │",
            "│ Maksimum Dop Oranı                   =        0.45                                   │",
            "├" + new string('─', 85) + "┤",
            "│ Kamuya Ayrılan Alan                  =     8000.00 -    5000.00 =    3000.00         │",
            "│ Düzenleme Ortaklık Payı              =     3000.00 /    8000.00 =   0.3750000        │",
            "└" + new string('─', 85) + "┘",
        };

        var baytlar = Encoding.UTF8.GetBytes(string.Join("\r\n", cksIcerik));
        using var ms = new MemoryStream(baytlar);

        var result = CksReader.Read(ms);
        result.IsSuccess.Should().BeTrue();

        var ozet = result.Value;
        ozet.ToplamDuzenlemeyeGirenDopAlinanM2.Should().Be(8000.00m);
        ozet.ToplamImarAdaAlaniM2.Should().Be(5000.00m);
        ozet.Dopo.Should().Be(0.3750000m);
        ozet.MaksimumDopOrani.Should().Be(0.45m);

        // Kamuya ayrılan alan R1 - R2 = 8000 - 5000 = 3000 m²
        (ozet.ToplamDuzenlemeyeGirenDopAlinanM2 - ozet.ToplamImarAdaAlaniM2).Should().Be(3000.00m);
    }

    #endregion

    #region 2. Z=0 Yatay İzdüşüm İndirgeme ve ERR-GIRD-004

    [Fact]
    public void Z0_Indirgeme_DXF_Farkli_Kotlu_Cizgileri_Z0a_Indirgemeli_Ve_Koordinat_Sizdirmadan_Uyarmali()
    {
        var doc = new CadDocument();
        var layer = new Layer("TK_PARSEL_GİREN");
        doc.Layers.Add(layer);

        // Z kotu 1234.56 m olan poligon
        var lw = new LwPolyline { Layer = layer, IsClosed = true, Elevation = 1234.56 };
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu, RefKuzey)));
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu + 20, RefKuzey)));
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu + 20, RefKuzey + 20)));
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu, RefKuzey + 20)));
        doc.Entities.Add(lw);

        using var akis = new MemoryStream(DxfBayt(doc), writable: false);
        var result = DxfAdapter.Read(akis);
        result.IsSuccess.Should().BeTrue();

        var polyline = result.Value.Polylines.Should().ContainSingle().Subject;
        polyline.Vertices.Should().AllSatisfy(p => p.X.Should().BeInRange(4_000_000m, 4_000_020m));

        var warning = result.Value.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdZKoduIndirgendi).Subject;
        warning.Severity.Should().Be(ErrorSeverity.Warning);
        warning.TechnicalMessage.Should().NotContain("1234.56", "kot değeri veya koordinat hata mesajına sızamaz");
    }

    [Fact]
    public void Z0_Indirgeme_NCZ_Yukseklik_Iceren_Poligonda_Z0_Ve_ERR_GIRD_004_Uretmeli()
    {
        (double X, double Y, double Z)[] kotluUcgen =
        [
            (RefKuzey, RefDogu, 150.25),
            (RefKuzey + 30, RefDogu, 152.80),
            (RefKuzey, RefDogu + 40, 149.10),
            (RefKuzey, RefDogu, 150.25)
        ];

        var dosya = new NczSentetikDosya()
            .Katman("TK_PARSEL_GİREN")
            .Poligon(0, "KOTLU-PARSEL", kotluUcgen);

        using var akis = dosya.Akis();
        var result = NczAdapter.Read(akis);
        result.IsSuccess.Should().BeTrue();

        result.Value.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdZKoduIndirgendi);
        result.Value.Warnings[0].TechnicalMessage.Should().NotContain("150.25");
    }

    #endregion

    #region 3. 1 mm Dışı Köşe Uyarısı (ERR-GIRD-006 / SORU-025)

    [Fact]
    public void IzgaraDisiKose_DXF_1mm_Disi_Koordinatlari_Kuantize_Etmeden_Saymali_Ve_ERR_GIRD_006_Vermeli()
    {
        // 1 mm ızgara: en fazla 3 ondalık (0.001 m).
        // Burada 4. ondalık basamakta değerler var: 0.0004 m, 0.0007 m
        var doc = new CadDocument();
        var layer = new Layer("TK_PARSEL_OLUŞAN");
        doc.Layers.Add(layer);

        var lw = new LwPolyline { Layer = layer, IsClosed = true };
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu + 0.0004, RefKuzey)));            // Izgara dışı
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu + 20.0, RefKuzey + 0.0007)));     // Izgara dışı
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu + 20.0, RefKuzey + 20.0)));       // Izgarada
        lw.Vertices.Add(new LwPolyline.Vertex(new XY(RefDogu, RefKuzey + 20.0)));              // Izgarada
        doc.Entities.Add(lw);

        using var akis = new MemoryStream(DxfBayt(doc), writable: false);
        var result = DxfAdapter.Read(akis);
        result.IsSuccess.Should().BeTrue();

        // 4 köşeden 2'si ızgara dışı (kapanış tekrarı sayılmaz)
        result.Value.OffGridVertexCount.Should().Be(2);

        var warning = result.Value.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdIzgaraDisiKose).Subject;
        warning.Severity.Should().Be(ErrorSeverity.Warning);
        warning.RequiresEngineerReview.Should().BeTrue("mühendis incelemesi gerektirir");
        warning.TechnicalMessage.Should().Contain("2");
        warning.TechnicalMessage.Should().NotContain("0.0004", "koordinat sızamaz");

        // Köşelerin sessizce kuantize EDİLMEDİĞİ, hassasiyetin korunduğu doğrulanır (SORU-025 / SORU-032 a):
        var polyline = result.Value.Polylines[0];
        polyline.Vertices[0].Y.Should().Be(500_000.0004m);
        polyline.Vertices[1].X.Should().Be(4_000_000.0007m);
    }

    [Fact]
    public void IzgaraDisiKose_NCZ_Tum_Koseleri_Izgarada_Olan_Poligonda_ERR_GIRD_006_Uretmemeli()
    {
        (double X, double Y, double Z)[] tamIzgara =
        [
            (RefKuzey, RefDogu, 0),
            (RefKuzey + 25.125, RefDogu, 0),       // 3 ondalık tam 1 mm
            (RefKuzey + 25.125, RefDogu + 40.500, 0),
            (RefKuzey, RefDogu + 40.500, 0),
            (RefKuzey, RefDogu, 0)
        ];

        var dosya = new NczSentetikDosya()
            .Katman("TK_PARSEL_GİREN")
            .Poligon(0, "TAM-IZGARA", tamIzgara);

        using var akis = dosya.Akis();
        var result = NczAdapter.Read(akis);
        result.IsSuccess.Should().BeTrue();

        result.Value.OffGridVertexCount.Should().Be(0);
        result.Value.Warnings.Should().NotContain(w => w.Code == ErrorCodes.GirdIzgaraDisiKose);
    }

    #endregion

    #region 4. ProjNet Farklı Datum / Epok / Dilim Dışı Ret Testleri

    [Fact]
    public void ProjNet_ITRF96_Ve_ED50_Farkli_Datumlar_Arasi_Sabit_Parametreyle_Donusumu_Reddetmeli()
    {
        // D3 Kuralı: Datumlar arası sabit kaydırma parametresi hardcode edilemez.
        var itrf30 = ProjeCrs.Olustur(GeodetikDatum.Itrf96, 30, 2005.00m, 1000).Value;
        var ed50_30 = ProjeCrs.Olustur(GeodetikDatum.Ed50, 30, null, 1000).Value;

        var nokta = new GeodeticPoint(X: 4_374_014m, Y: 500_000m);
        var result = TmProjeksiyonu.DilimDegistir([nokta], itrf30, ed50_30);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.CrsGirdisiGecersiz);
        result.Error.TechnicalMessage.Should().Contain("datum").And.Contain("Helmert");
    }

    [Fact]
    public void ProjNet_Farkli_Epoklarda_Dilim_Degisimini_Reddetmeli()
    {
        var crs1 = ProjeCrs.Olustur(GeodetikDatum.Itrf96, 30, 2005.00m, 1000).Value;
        var crs2 = ProjeCrs.Olustur(GeodetikDatum.Itrf96, 33, 2018.00m, 1000).Value;

        var nokta = new GeodeticPoint(X: 4_374_014m, Y: 500_000m);
        var result = TmProjeksiyonu.DilimDegistir([nokta], crs1, crs2);

        result.IsFailure.Should().BeTrue();
        result.Error.TechnicalMessage.Should().Contain("epoch");
    }

    [Theory]
    [InlineData(26)]
    [InlineData(28)]
    [InlineData(46)]
    [InlineData(31)]
    public void ProjeCrs_Gecersiz_Orta_Meridyende_Ret_Dondurmeli(int gecersizMeridyen)
    {
        // Türkiye TM 3° orta meridyenleri: 27, 30, 33, 36, 39, 42, 45 (3'ün katı, 27–45 aralığı)
        var result = ProjeCrs.Olustur(GeodetikDatum.Itrf96, gecersizMeridyen, 2005.00m, 1000);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.CrsGirdisiGecersiz);
    }

    [Theory]
    [InlineData(1899.99)]
    [InlineData(2100.01)]
    public void ProjeCrs_1900_2100_Araligi_Disindaki_Epokta_Ret_Dondurmeli(decimal gecersizEpok)
    {
        var result = ProjeCrs.Olustur(GeodetikDatum.Itrf96, 30, gecersizEpok, 1000);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.CrsGirdisiGecersiz);
    }

    [Fact]
    public void ProjeCrs_ED50_Datumu_Epok_Verilirse_Ret_Dondurmeli()
    {
        // ED50 statik datumdur, epok alamaz.
        var result = ProjeCrs.Olustur(GeodetikDatum.Ed50, 30, 2005.00m, 1000);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.CrsGirdisiGecersiz);
    }

    #endregion

    #region 5. 2B Helmert ve Afin Artık Kontrolü

    [Fact]
    public void Helmert_Tolerans_Asan_Noktada_ERR_CRS_005_Uyarisi_Uretmeli()
    {
        // 5 ortak nokta, 1. noktada kasıtlı 15 mm uyuşumsuzluk
        GeodeticPoint[] kaynak =
        [
            new(X: 4_000_000m, Y: 500_000m),
            new(X: 4_000_100m, Y: 500_000m),
            new(X: 4_000_100m, Y: 500_100m),
            new(X: 4_000_000m, Y: 500_100m),
            new(X: 4_000_050m, Y: 500_050m)
        ];

        // Tam öteleme tx = 10m, ty = 20m + 1. noktada 50 mm belirgin artık
        GeodeticPoint[] hedef =
        [
            new(X: 4_000_010.050m, Y: 500_020.000m),
            new(X: 4_000_110.000m, Y: 500_020.000m),
            new(X: 4_000_110.000m, Y: 500_120.000m),
            new(X: 4_000_010.000m, Y: 500_120.000m),
            new(X: 4_000_060.000m, Y: 500_070.000m)
        ];

        var noktalar = Enumerable.Range(0, 5)
            .Select(i => new OrtakNokta($"P{i + 1}", kaynak[i], hedef[i]))
            .ToList();

        // 10 mm tolerans verildiğinde P1 noktası artık sınırını aşmalıdır
        var result = HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, noktalar, toleransM: 0.010m);
        result.IsSuccess.Should().BeTrue();

        var rapor = result.Value;
        rapor.Artiklar.Count(a => a.ToleransAsimi).Should().BeGreaterThan(0);
        rapor.EnBuyukArtik.V.Should().BeGreaterThan(0.010m);

        rapor.Uyarilar.Should().ContainSingle(u => u.Code == ErrorCodes.CrsDonusumToleransAsimi);
        var uyari = rapor.Uyarilar[0];
        uyari.Severity.Should().Be(ErrorSeverity.Warning);
        uyari.RequiresEngineerReview.Should().BeTrue();
    }

    [Fact]
    public void Helmert_Yetersiz_Nokta_Sayisinda_ERR_CRS_004_Ile_Reddetmeli()
    {
        // 2B Helmert 4 parametre için en az 2 ortak nokta gerekir ancak serbestlik derecesi (f > 0) için en az 3 nokta gereklidir.
        // Tek nokta verildiğinde:
        var tekNokta = new List<OrtakNokta>
        {
            new("P1", new GeodeticPoint(4_000_000m, 500_000m), new GeodeticPoint(4_000_010m, 500_020m))
        };

        var result = HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, tekNokta, null);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.CrsDonusumHesaplanamadi);
    }

    #endregion

    #region 6. TAKBİS KVKK Kişisel Veri Maskeleme ve Red Testleri

    [Theory]
    [InlineData("12345678901")]           // 11 haneli standart TC
    [InlineData("98765432109")]           // Farklı bir 11 haneli TC
    [InlineData("123 456 789 01")]        // Boşluklu TC
    [InlineData("123-456-789-01")]        // Tireli TC
    [InlineData("123.456.789.01")]        // Noktalı TC
    public void TAKBIS_TC_Kimlik_No_Bicimindeki_Girdileri_KVKK_Geregi_Reddetmeli(string tcNo)
    {
        var hisseler = new[] { new TakbisHisseGirdisi(tcNo, BigInteger.One, BigInteger.One, true) };
        var parsel = new TakbisParselGirdisi("101/1", 1000.00m, hisseler);

        var result = TakbisGirdiDogrulayici.Dogrula(parsel);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        result.Error.TechnicalMessage.Should().Contain("KVKK");
        result.Error.TechnicalMessage.Should().NotContain(tcNo, "TC Kimlik No asla hata mesajına yazılamaz");
    }

    // SORU-055 (proje sahibi kararı, 20 Kasım 2026, SAHA-011): eski beklenti "ad-soyad / unvan içeren malik reddedilir" değişti.
    // Uygulama çevrimdışı çalıştığından yerel ekranlarda malik "AD SOYAD (BABA ADI)" olarak kabul edilir; KVKK sınırı 11 haneli
    // T.C. Kimlik No reddi (yukarıdaki test) ve ad içine gömülü kimlik numarası reddiyle korunur. Adlar uydurmadır.
    [Theory]
    [InlineData("Ahmet Yılmaz")]
    [InlineData("Fatma Demir (Hasan)")]
    [InlineData("MUSTAFA KEMAL / ALİ")]
    [InlineData("Özlem Şahin")]
    [InlineData("Anonim Şirketi Ltd")]
    public void TAKBIS_Ad_Soyad_Baba_Adi_Veya_Unvan_Iceren_Malik_Alanini_SORU_055_Geregi_Kabul_Etmeli(string adSoyad)
    {
        var hisseler = new[] { new TakbisHisseGirdisi(adSoyad, BigInteger.One, BigInteger.One, true) };
        var parsel = new TakbisParselGirdisi("101/1", 1000.00m, hisseler);

        var result = TakbisGirdiDogrulayici.Dogrula(parsel);
        result.IsSuccess.Should().BeTrue();
        result.Value.Malikler[0].MalikId.Should().Be(adSoyad);
    }

    [Theory]
    [InlineData("Ahmet Yılmaz 12345678901")]
    [InlineData("Fatma Demir (Hasan) 123 456 789 01")]
    public void TAKBIS_Ad_Icine_Gomulu_TC_Kimlik_No_KVKK_Geregi_Reddetmeli_Ve_Sizdirmamali(string deger)
    {
        var hisseler = new[] { new TakbisHisseGirdisi(deger, BigInteger.One, BigInteger.One, true) };
        var parsel = new TakbisParselGirdisi("101/1", 1000.00m, hisseler);

        var result = TakbisGirdiDogrulayici.Dogrula(parsel);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.TapuDagitimGirdisiGecersiz);
        result.Error.TechnicalMessage.Should().Contain("KVKK").And.NotContain(deger).And.NotContain("Yılmaz").And.NotContain("Demir");
    }

    [Theory]
    [InlineData("Malik-01")]
    [InlineData("Malik_02")]
    [InlineData("M.03")]
    [InlineData("MALIK100")]
    [InlineData("Paydas-A")]
    public void TAKBIS_Anonim_Bicimdeki_Malik_Kodlarini_Basariyla_Kabul_Etmeli(string anonimKod)
    {
        var hisseler = new[] { new TakbisHisseGirdisi(anonimKod, BigInteger.One, BigInteger.One, true) };
        var parsel = new TakbisParselGirdisi("101/1", 1000.00m, hisseler);

        var result = TakbisGirdiDogrulayici.Dogrula(parsel);
        result.IsSuccess.Should().BeTrue();
        result.Value.Malikler[0].MalikId.Should().Be(anonimKod);
    }

    #endregion
}

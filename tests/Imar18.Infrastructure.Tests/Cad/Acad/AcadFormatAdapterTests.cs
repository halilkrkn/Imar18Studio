using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cad.Acad;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cad.Acad;

/// <summary>
/// P4.1 yedek AutoCAD girdisi (D14): DXF/DWG adaptorleri. Girdiler testte ACadSharp ile uretilen sentetik
/// belgelerdir (gercek veri yok). Kartezyen CAD (X = Saga/Dogu, Y = Yukari/Kuzey) jeodezik X = Kuzey, Y = Dogu'ya
/// normalize edilmelidir (AC-CAD-02, TC-CAD-001).
/// </summary>
public sealed class AcadFormatAdapterTests
{
    // Sentetik TM benzeri degerler: Dogu ~ 5.0e5 (CAD X), Kuzey ~ 4.0e6 (CAD Y).
    private const double Dogu0 = 500_000.0;
    private const double Kuzey0 = 4_000_000.0;
    private const string Olusan = "TK_PARSEL_OLUŞAN";

    private static readonly DxfFormatAdapter Dxf = new();
    private static readonly DwgFormatAdapter Dwg = new();

    /// <summary>CAD X ekseni boyunca 30 m (Dogu), Y ekseni boyunca 40 m (Kuzey): 1200 m2.</summary>
    private static readonly (double X, double Y)[] Dikdortgen =
    [
        (Dogu0, Kuzey0), (Dogu0 + 30, Kuzey0), (Dogu0 + 30, Kuzey0 + 40), (Dogu0, Kuzey0 + 40),
    ];

    private static (CadDocument Belge, Layer Katman) YeniBelge(string katmanAdi = Olusan)
    {
        var belge = new CadDocument();
        var katman = new Layer(katmanAdi);
        belge.Layers.Add(katman);
        return (belge, katman);
    }

    private static LwPolyline Lw(Layer katman, IEnumerable<(double X, double Y)> koseler, bool kapali = true, double kot = 0)
    {
        var lw = new LwPolyline { Layer = katman, IsClosed = kapali, Elevation = kot };
        foreach (var (x, y) in koseler)
        {
            lw.Vertices.Add(new LwPolyline.Vertex(new XY(x, y)));
        }

        return lw;
    }

    private static byte[] DxfBayt(CadDocument belge)
    {
        using var ms = new MemoryStream();
        DxfWriter.Write(ms, belge, false, null);
        return ms.ToArray();
    }

    private static byte[] DwgBayt(CadDocument belge)
    {
        using var ms = new MemoryStream();
        DwgWriter.Write(ms, belge, null);
        return ms.ToArray();
    }

    private static CadLayerDocument Oku(ICadFormatAdapter adaptor, byte[] bayt)
    {
        using var akis = new MemoryStream(bayt, writable: false);
        var sonuc = adaptor.Read(akis);
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        return sonuc.Value;
    }

    private static AppError OkuHata(ICadFormatAdapter adaptor, byte[] bayt)
    {
        using var akis = new MemoryStream(bayt, writable: false);
        var sonuc = adaptor.Read(akis);
        sonuc.IsFailure.Should().BeTrue();
        return sonuc.Error;
    }

    private static Polyline2D Klasik2B(Layer katman, IEnumerable<(double X, double Y)> koseler, double kot = 0)
    {
        var p = new Polyline2D([.. koseler.Select(k => new Vertex2D(new XYZ(k.X, k.Y, 0)))], true) { Layer = katman, IsClosed = true };
        p.Elevation = kot;
        return p;
    }

    private static CadLayerDocument Donustur(CadDocument belge)
    {
        var sonuc = AcadDocumentConverter.Convert(belge, CadFormat.Dxf, null, CancellationToken.None);
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        return sonuc.Value;
    }

    private static decimal Alan(CadPolyline p) =>
        GaussAreaCalculator.Compute(PolygonRing.FromClosed(p.Vertices).Value).AreaM2;

    [Fact]
    public void AcadAdaptor_Bicim_Ve_Uzantilari_Dogru_Bildirmeli()
    {
        Dxf.Format.Should().Be(CadFormat.Dxf);
        Dwg.Format.Should().Be(CadFormat.Dwg);
        Dxf.SupportsExtension(".DXF").Should().BeTrue();
        Dxf.SupportsExtension("dwg").Should().BeFalse();
        Dwg.SupportsExtension(".dwg").Should().BeTrue();
        Dwg.SupportsExtension(".ncz").Should().BeFalse();
    }

    [Fact]
    public void DxfAdaptor_Kartezyen_XY_Koordinatini_Jeodezik_X_Kuzey_Y_Dogu_Eksenine_Takas_Etmeli()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Lw(katman, Dikdortgen));

        var p = Oku(Dxf, DxfBayt(belge)).Polylines.Should().ContainSingle().Subject;

        p.LayerName.Should().Be(Olusan);
        p.IsClosed.Should().BeTrue();
        p.Vertices.Should().HaveCount(5, "kapali bayrakli halkaya kapanis tekrari eklenir");
        p.Vertices[0].Should().Be(new GeodeticPoint(X: 4_000_000m, Y: 500_000m));
        p.Vertices[1].Should().Be(new GeodeticPoint(X: 4_000_000m, Y: 500_030m));
        p.Vertices[2].Should().Be(new GeodeticPoint(X: 4_000_040m, Y: 500_030m));
        p.Vertices[^1].Should().Be(p.Vertices[0]);
        Alan(p).Should().Be(1200.00m);
    }

    [Fact]
    public void DwgAdaptor_ACadSharp_Ile_Yazilan_Dwg_Dosyasini_Ayni_Geometriyle_Okumali()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Lw(katman, Dikdortgen));

        var d = Oku(Dwg, DwgBayt(belge));

        d.Format.Should().Be(CadFormat.Dwg);
        var p = d.Polylines.Should().ContainSingle().Subject;
        p.LayerName.Should().Be(Olusan);
        p.Vertices[2].Should().Be(new GeodeticPoint(X: 4_000_040m, Y: 500_030m));
        Alan(p).Should().Be(1200.00m);
    }

    [Fact]
    public void DxfAdaptor_Katman_Adlarini_Turkce_Karakterleriyle_Okumali()
    {
        var (belge, katman) = YeniBelge("TK_PARSEL_GİREN");
        belge.Layers.Add(new Layer("DÜZENLEME_SINIRI"));
        belge.Entities.Add(Lw(katman, Dikdortgen));

        var d = Oku(Dxf, DxfBayt(belge));

        d.Layers.Select(l => l.Name).Should().Contain(["TK_PARSEL_GİREN", "DÜZENLEME_SINIRI"]);
        d.Polylines[0].LayerName.Should().Be("TK_PARSEL_GİREN");
    }

    [Fact]
    public void DxfAdaptor_Sifirdan_Farkli_Kotu_Z0_Duzlemine_Indirgeyip_ERR_GIRD_004_Uretmeli()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Lw(katman, Dikdortgen, kot: 987.5));

        var d = Oku(Dxf, DxfBayt(belge));

        d.Polylines[0].Vertices[0].Should().Be(new GeodeticPoint(X: 4_000_000m, Y: 500_000m));
        d.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdZKoduIndirgendi)
            .Which.TechnicalMessage.Should().Contain("DXF input (1 of 1 polylines)").And.NotContain("987");
    }

    private static Polyline3D Kotlu3B(Layer katman) => new(
        [new XYZ(Dogu0, Kuzey0, 10), new XYZ(Dogu0 + 30, Kuzey0, 11), new XYZ(Dogu0 + 30, Kuzey0 + 40, 12), new XYZ(Dogu0, Kuzey0 + 40, 13)],
        true) { Layer = katman, IsClosed = true };

    [Fact]
    public void DwgAdaptor_3B_Cizgideki_Z_Kotlarini_Atip_Uyari_Uretmeli()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Kotlu3B(katman));

        var d = Oku(Dwg, DwgBayt(belge));

        var p = d.Polylines.Should().ContainSingle().Subject;
        p.IsClosed.Should().BeTrue();
        Alan(p).Should().Be(1200.00m);
        d.Warnings.Should().Contain(w => w.Code == ErrorCodes.GirdZKoduIndirgendi);
    }

    [Fact]
    public void DxfAdaptor_ACadSharp_3B_Cizgi_Okuma_Kisitinda_Cokmeden_ERR_GIRD_001_Dondurmeli()
    {
        // ACadSharp 3.0.0 bilinen kisit (P4.1): DXF POLYLINE(3B) okumasi "Vertex3D already has an owner" atar.
        // Kutuphane duzeltilirse bu test kirilir ve DXF 3B yolunun yeniden dogrulanmasi gerektigini haber verir.
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Kotlu3B(katman));

        var hata = OkuHata(Dxf, DxfBayt(belge));
        hata.Code.Should().Be(ErrorCodes.GirdCadOkunamadi);
        hata.TechnicalMessage.Should().Contain("3D polyline").And.Contain("DWG");
    }

    [Fact]
    public void DwgAdaptor_Klasik_2B_Polyline_Kapali_Halkayi_Eksen_Takasiyla_Okumali()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Klasik2B(katman, Dikdortgen, kot: 5));

        var d = Oku(Dwg, DwgBayt(belge));

        var p = d.Polylines.Should().ContainSingle().Subject;
        p.IsClosed.Should().BeTrue();
        p.Vertices[2].Should().Be(new GeodeticPoint(X: 4_000_040m, Y: 500_030m));
        Alan(p).Should().Be(1200.00m);
        d.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdZKoduIndirgendi);
    }

    [Fact]
    public void AcadDonusturucu_Spline_Veya_Egri_Uydurmali_2B_Polyline_Atlanmali()
    {
        var (belge, katman) = YeniBelge();
        var spline = Klasik2B(katman, Dikdortgen);
        spline.Flags |= PolylineFlags.SplineFit;
        var egri = Klasik2B(katman, Dikdortgen);
        egri.Flags |= PolylineFlags.CurveFit;
        belge.Entities.Add(spline);
        belge.Entities.Add(egri);
        belge.Entities.Add(Klasik2B(katman, Dikdortgen));

        var d = Donustur(belge);

        d.Polylines.Should().ContainSingle();
        d.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdDesteklenmeyenGeometriAtlandi)
            .Which.TechnicalMessage.Should().Contain("2 polylines").And.Contain("fit");
    }

    [Fact]
    public void AcadDonusturucu_Yayli_2B_Polyline_Atlanmali()
    {
        var (belge, katman) = YeniBelge();
        var yayli = Klasik2B(katman, Dikdortgen);
        yayli.Vertices.First().Bulge = 0.5;
        belge.Entities.Add(yayli);

        var d = Donustur(belge);

        d.Polylines.Should().BeEmpty();
        d.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdDesteklenmeyenGeometriAtlandi)
            .Which.TechnicalMessage.Should().Contain("arc");
    }

    [Fact]
    public void AcadDonusturucu_Kosesiz_Veya_Sonlu_Olmayan_Koordinatli_Cizgiyi_Belgeyi_Dusurmeden_Atlamali()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(new LwPolyline { Layer = katman });
        belge.Entities.Add(Lw(katman, [(double.NaN, Kuzey0), (Dogu0 + 1, Kuzey0), (Dogu0 + 1, Kuzey0 + 1)]));
        belge.Entities.Add(Lw(katman, Dikdortgen));

        var d = Donustur(belge);

        d.Polylines.Should().ContainSingle();
        d.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdDesteklenmeyenGeometriAtlandi)
            .Which.TechnicalMessage.Should().Contain("2 polylines").And.Contain("no vertices").And.Contain("non-finite");
    }

    [Fact]
    public void AcadDonusturucu_Blok_Referansini_Acmadan_ERR_GIRD_008_Ile_Bildirmeli()
    {
        var (belge, katman) = YeniBelge();
        var blok = new BlockRecord("PARSEL_BLOK");
        belge.BlockRecords.Add(blok);
        belge.Entities.Add(new Insert(blok) { Layer = katman });
        belge.Entities.Add(Lw(katman, Dikdortgen));

        var d = Donustur(belge);

        d.Polylines.Should().ContainSingle();
        d.SkippedEntityCount.Should().Be(1);
        d.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdDesteklenmeyenGeometriAtlandi)
            .Which.TechnicalMessage.Should().Contain("block reference");
    }

    [Fact]
    public void DxfAdaptor_UTF8_BOM_Ve_999_Yorum_Satiriyla_Baslayan_Dosyayi_Okumali()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Lw(katman, Dikdortgen));
        var govde = DxfBayt(belge);
        byte[] onek = [0xEF, 0xBB, 0xBF, .. "999\nsentetik yorum\n"u8];

        var d = Oku(Dxf, [.. onek, .. govde]);

        d.Polylines.Should().ContainSingle();
    }

    [Fact]
    public void DxfAdaptor_Mojibake_Onarimi_Bilinen_Yanlis_Pozitifi_Belgelenmeli()
    {
        // Dogru cozulmus ama yeniden kodlaninca gecerli UTF-8 olusturan ad (ornek "Ã©") onarilir.
        // Yalnizca AC1021+ DXF'te uygulanir; Netcad katman adlarinda bu dizilim beklenmez (rapor notu).
        AcadDocumentConverter.RepairUtf8Mojibake("\u00c3\u00a9", 1252).Should().Be("\u00e9");
    }

    [Theory]
    [InlineData("TK_PARSEL_OLU\u00c5\u017eAN", "TK_PARSEL_OLUŞAN")]
    [InlineData("TK_PARSEL_OLU\u00c5\u009eAN", "TK_PARSEL_OLUŞAN")]
    [InlineData("TK_PARSEL_G\u00c4\u00b0REN", "TK_PARSEL_GİREN")]
    [InlineData("D\u00c3\u0153ZENLEME_SINIRI", "DÜZENLEME_SINIRI")]
    [InlineData("TK_PARSEL_OLUŞAN", "TK_PARSEL_OLUŞAN")]
    [InlineData("CAF\u00c9", "CAF\u00c9")]
    [InlineData("YAZI", "YAZI")]
    public void DxfAdaptor_UTF8_Dxf_Kod_Sayfasi_Bozulmasini_Yalnizca_Kayipsiz_Ise_Onarmali(string girdi, string beklenen)
    {
        AcadDocumentConverter.RepairUtf8Mojibake(girdi, 1252).Should().Be(beklenen);
    }

    [Fact]
    public void DxfAdaptor_1mm_Izgara_Disi_Koselerde_SORU_025_ERR_GIRD_006_Uyarisi_Uretip_Kuantize_Etmemeli()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Lw(katman, [(Dogu0 + 0.0004, Kuzey0), (Dogu0 + 30, Kuzey0), (Dogu0 + 30, Kuzey0 + 40.1234567), (Dogu0, Kuzey0 + 40)]));

        var d = Oku(Dxf, DxfBayt(belge));

        d.OffGridVertexCount.Should().Be(2, "ilk ve ucuncu kose; kapanis tekrari sayilmaz");
        d.Polylines[0].Vertices[0].Y.Should().Be(500_000.0004m);
        d.Polylines[0].Vertices[2].X.Should().Be(4_000_040.123457m);
        var uyari = d.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdIzgaraDisiKose).Subject;
        uyari.RequiresEngineerReview.Should().BeTrue();
        uyari.TechnicalMessage.Should().Contain("2 vertices").And.NotContain("500000").And.NotContain("4000040");
    }

    [Fact]
    public void DxfAdaptor_Izgara_Uzerindeki_Kosede_Izgara_Uyarisi_Uretmemeli()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Lw(katman, [(Dogu0 + 0.125, Kuzey0), (Dogu0 + 30, Kuzey0), (Dogu0 + 30, Kuzey0 + 40), (Dogu0, Kuzey0 + 40)]));

        var d = Oku(Dxf, DxfBayt(belge));

        d.OffGridVertexCount.Should().Be(0);
        d.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void DxfAdaptor_Yay_Segmentli_Cizgiyi_Atlayip_ERR_GIRD_008_Uretmeli()
    {
        var (belge, katman) = YeniBelge();
        var yayli = Lw(katman, Dikdortgen);
        yayli.Vertices[1].Bulge = 0.25;
        belge.Entities.Add(yayli);
        belge.Entities.Add(Lw(katman, Dikdortgen));

        var d = Oku(Dxf, DxfBayt(belge));

        d.Polylines.Should().ContainSingle();
        var uyari = d.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdDesteklenmeyenGeometriAtlandi).Subject;
        uyari.TechnicalMessage.Should().Contain("1 polylines").And.Contain("arc");
        uyari.RequiresEngineerReview.Should().BeTrue();
    }

    [Fact]
    public void DxfAdaptor_Egik_Ocs_Normalli_Cizgiyi_Atlayip_ERR_GIRD_008_Uretmeli()
    {
        var (belge, katman) = YeniBelge();
        var ters = Lw(katman, Dikdortgen);
        ters.Normal = new XYZ(0, 0, -1);
        belge.Entities.Add(ters);

        var d = Oku(Dxf, DxfBayt(belge));

        d.Polylines.Should().BeEmpty();
        d.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdDesteklenmeyenGeometriAtlandi)
            .Which.TechnicalMessage.Should().Contain("ocs");
    }

    [Fact]
    public void DxfAdaptor_Bayraksiz_Ama_1mm_Icinde_Kapanan_Cizgiyi_Kapali_Saymali()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Lw(katman, [.. Dikdortgen, (Dogu0 + 0.0005, Kuzey0 + 0.0005)], kapali: false));

        var d = Oku(Dxf, DxfBayt(belge));
        var p = d.Polylines.Should().ContainSingle().Subject;

        p.IsClosed.Should().BeTrue();
        p.Vertices.Should().HaveCount(5);
        p.Vertices[^1].Should().Be(p.Vertices[0], "1 mm kapanis toleransinda son kose ilk koseye esitlenir");
        Alan(p).Should().Be(1200.00m);
        d.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdToleranslaKapatildi)
            .Which.TechnicalMessage.Should().Contain("1 polylines");
    }

    [Fact]
    public void DxfAdaptor_Kapali_Bayrakli_Ve_Son_Kosesi_1mm_Icinde_Cizgiye_Fazladan_Kose_Eklememeli()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Lw(katman, [.. Dikdortgen, (Dogu0 + 0.0005, Kuzey0 + 0.0005)], kapali: true));

        var d = Oku(Dxf, DxfBayt(belge));

        var p = d.Polylines.Should().ContainSingle().Subject;
        p.Vertices.Should().HaveCount(5, "mm alti serit olusmamali");
        p.Vertices[^1].Should().Be(p.Vertices[0]);
        Alan(p).Should().Be(1200.00m);
        d.Warnings.Should().Contain(w => w.Code == ErrorCodes.GirdToleranslaKapatildi);
    }

    [Fact]
    public void DxfAdaptor_Ucten_Az_Koseli_Kapali_Bayrakli_Cizgiyi_Kapali_Saymamali()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Lw(katman, [(Dogu0, Kuzey0), (Dogu0 + 10, Kuzey0)], kapali: true));

        var p = Oku(Dxf, DxfBayt(belge)).Polylines.Should().ContainSingle().Subject;

        p.IsClosed.Should().BeFalse();
        p.Vertices.Should().HaveCount(2);
    }

    [Fact]
    public void DxfAdaptor_Bayraksiz_Ve_1mm_Disinda_Biten_Cizgiyi_Acik_Saymali()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Lw(katman, [.. Dikdortgen, (Dogu0 + 0.002, Kuzey0)], kapali: false));

        var p = Oku(Dxf, DxfBayt(belge)).Polylines.Should().ContainSingle().Subject;

        p.IsClosed.Should().BeFalse();
        p.Vertices.Should().HaveCount(5);
    }

    [Fact]
    public void DxfAdaptor_Poligon_Disi_Varliklari_Sayip_Atlamali()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(new Line(new XYZ(Dogu0, Kuzey0, 0), new XYZ(Dogu0 + 1, Kuzey0 + 1, 0)) { Layer = katman });
        belge.Entities.Add(new TextEntity { Value = "101/1", Layer = katman });
        belge.Entities.Add(Lw(katman, Dikdortgen));

        var d = Oku(Dxf, DxfBayt(belge));

        d.Polylines.Should().ContainSingle();
        d.SkippedEntityCount.Should().Be(1, "cizgi atlanir; yazi P4.2'de etiket adayi olarak okunur");
        d.Texts.Should().ContainSingle().Which.Text.Should().Be("101/1");
        d.Warnings.Should().BeEmpty("cizgi ve yazi parsel geometrisi degildir");
        d.UnrecognizedRecordCount.Should().Be(0);
        d.Polylines[0].Label.Should().BeEmpty("DXF/DWG cizgisi parsel etiketi tasimaz; etiket eslemesi P4.2'dedir");
    }

    [Fact]
    public void AcadAdaptor_Bozuk_Veya_Bos_Girdide_Cokmeden_ERR_GIRD_001_Dondurmeli()
    {
        OkuHata(Dxf, []).Code.Should().Be(ErrorCodes.GirdCadOkunamadi);
        OkuHata(Dwg, []).Code.Should().Be(ErrorCodes.GirdCadOkunamadi);
        OkuHata(Dxf, "bozuk dxf icerigi\n0\nEOF"u8.ToArray()).Code.Should().Be(ErrorCodes.GirdCadOkunamadi);
        OkuHata(Dwg, "AC9999 bozuk"u8.ToArray()).Code.Should().BeOneOf(ErrorCodes.GirdCadOkunamadi, ErrorCodes.GirdCadSurumUyumsuz);
    }

    [Fact]
    public void AcadAdaptor_Kirpilmis_Dosyalarda_Hic_Istisna_Atmamali()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Lw(katman, Dikdortgen));
        var dxf = DxfBayt(belge);
        var dwg = DwgBayt(belge);

        foreach (var (adaptor, bayt) in new (ICadFormatAdapter, byte[])[] { (Dxf, dxf), (Dwg, dwg) })
        {
            for (var n = 0; n < bayt.Length; n += Math.Max(1, bayt.Length / 40))
            {
                using var akis = new MemoryStream(bayt[..n], writable: false);
                var eylem = () => adaptor.Read(akis);
                eylem.Should().NotThrow();
            }
        }
    }

    [Fact]
    public void AcadAdaptor_Hata_Mesaji_Koordinat_Sizdirmamali()
    {
        var hata = OkuHata(Dxf, "0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n10\n500000.123\n"u8.ToArray());

        hata.TechnicalMessage.Should().NotContain("500000");
        hata.UserMessage.Should().NotContain("500000");
    }

    [Fact]
    public void AcadAdaptor_Boyut_Sinirini_Asan_Akista_Okumadan_Hata_Dondurmeli()
    {
        using var buyuk = new SahteUzunAkis(DxfFormatAdapter.MaxFileSizeBytes + 1);

        var sonuc = Dxf.Read(buyuk);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.GirdCadOkunamadi);
        sonuc.Error.TechnicalMessage.Should().Contain("too large");
        buyuk.OkunanBayt.Should().Be(0);
    }

    [Fact]
    public void AcadAdaptor_Iptal_Edilmis_Belirtecle_OperationCanceledException_Firlatmali()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Lw(katman, Dikdortgen));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var akis = new MemoryStream(DxfBayt(belge));

        var eylem = () => Dxf.Read(akis, cts.Token);

        eylem.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void AcadAdaptor_Null_Akista_ArgumentNullException_Firlatmali()
    {
        var eylem = () => Dwg.Read(null!);

        eylem.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AcadAdaptor_Ayni_Dosyada_Tekrar_Edilebilir_Sonuc_Uretmeli()
    {
        var (belge, katman) = YeniBelge();
        belge.Entities.Add(Lw(katman, Dikdortgen, kot: 3));
        var bayt = DxfBayt(belge);

        var a = Oku(Dxf, bayt);
        var b = Oku(Dxf, bayt);

        a.Polylines.SelectMany(p => p.Vertices).Should().Equal(b.Polylines.SelectMany(p => p.Vertices));
        a.Warnings.Select(w => w.TechnicalMessage).Should().Equal(b.Warnings.Select(w => w.TechnicalMessage));
    }

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

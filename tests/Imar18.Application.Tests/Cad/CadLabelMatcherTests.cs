using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Xunit;

namespace Imar18.Application.Tests.Cad;

/// <summary>P4.2 DXF/DWG parsel etiketi (TEXT/MTEXT) – poligon eslestirmesi (STATUS SORU-033 c). Sentetik koordinatlar.</summary>
public sealed class CadLabelMatcherTests
{
    private const string Olusan = "TK_PARSEL_OLUŞAN";
    private const string Giren = "TK_PARSEL_GİREN";
    private static readonly CadLayerRole[] ImarRolu = [CadLayerRole.ZoningParcel];

    private static GeodeticPoint P(decimal x, decimal y) => new(X: 4_000_000m + x, Y: 500_000m + y);

    private static CadPolyline Kare(string katman, decimal x0, decimal y0, decimal kenar, string etiket = "") =>
        new(katman, etiket, null, [P(x0, y0), P(x0 + kenar, y0), P(x0 + kenar, y0 + kenar), P(x0, y0 + kenar), P(x0, y0)], true, null);

    private static CadText Yazi(string metin, decimal x, decimal y, string katman = "YAZI") => new(katman, metin, P(x, y));

    private static CadLayerDocument Belge(IReadOnlyList<CadPolyline> poligonlar, IReadOnlyList<CadText> yazilar) =>
        new(CadFormat.Dxf, [new CadLayer(0, Olusan), new CadLayer(1, Giren), new CadLayer(2, "YAZI")], poligonlar, yazilar, 0, 0, 0, []);

    private static CadLabelMatchResult Eslestir(CadLayerDocument belge) =>
        CadLabelMatcher.Match(belge, CadLayerRoleMap.Standard, ImarRolu);

    [Fact]
    public void Etiket_Poligon_Icindeki_Tek_Yaziyi_Etikete_Atamali()
    {
        var r = Eslestir(Belge([Kare(Olusan, 0, 0, 20), Kare(Olusan, 20, 0, 20)], [Yazi("101/1", 10, 10), Yazi(" 101/2 ", 30, 5)]));

        r.Polylines.Select(p => p.Label).Should().Equal("101/1", "101/2");
        r.MatchedCount.Should().Be(2);
        r.UnlabeledCount.Should().Be(0);
        r.AmbiguousCount.Should().Be(0);
        r.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Etiket_Yazisiz_Poligonu_Etiketsiz_Birakip_ERR_GIRD_010_Uyarmali()
    {
        var r = Eslestir(Belge([Kare(Olusan, 0, 0, 20)], [Yazi("101/1", 50, 50)]));

        r.Polylines[0].Label.Should().BeEmpty();
        r.UnlabeledCount.Should().Be(1);
        var uyari = r.Warnings.Should().ContainSingle(w => w.Code == ErrorCodes.GirdEtiketPoligonEslesmedi).Subject;
        uyari.TechnicalMessage.Should().Contain("1 unlabeled").And.Contain("0 ambiguous").And.NotContain("4000");
        uyari.RequiresEngineerReview.Should().BeTrue();
    }

    [Fact]
    public void Etiket_Iki_Farkli_Yazi_Iceren_Poligonu_Belirsiz_Saymali()
    {
        var r = Eslestir(Belge([Kare(Olusan, 0, 0, 20)], [Yazi("101/1", 5, 5), Yazi("101/9", 15, 15)]));

        r.Polylines[0].Label.Should().BeEmpty();
        r.AmbiguousCount.Should().Be(1);
    }

    [Fact]
    public void Etiket_Ayni_Metin_Iki_Kez_Yazilmissa_Tek_Etiket_Saymali()
    {
        var r = Eslestir(Belge([Kare(Olusan, 0, 0, 20)], [Yazi("101/1", 5, 5), Yazi("101/1", 15, 15)]));

        r.Polylines[0].Label.Should().Be("101/1");
    }

    [Fact]
    public void Etiket_Sinir_Uzerindeki_Yaziyi_Icerde_Saymamali()
    {
        var r = Eslestir(Belge([Kare(Olusan, 0, 0, 20)], [Yazi("101/1", 20, 10), Yazi("101/2", 0, 0)]));

        r.Polylines[0].Label.Should().BeEmpty();
        r.UnlabeledCount.Should().Be(1);
    }

    [Fact]
    public void Etiket_Konkav_Poligonun_Girintisindeki_Yaziyi_Icerde_Saymamali()
    {
        // L bicimi: 20x20 kareden sag ust 10x10 cikarilmis; (15,15) girintidedir.
        CadPolyline l = new(Olusan, "", null, [P(0, 0), P(20, 0), P(20, 10), P(10, 10), P(10, 20), P(0, 20), P(0, 0)], true, null);

        var r = Eslestir(Belge([l], [Yazi("girintide", 15, 15), Yazi("101/7", 5, 15)]));

        r.Polylines[0].Label.Should().Be("101/7");
    }

    [Fact]
    public void Etiket_Ust_Uste_Binen_Hedef_Poligonlarin_Paylastigi_Yaziyi_Belirsiz_Saymali()
    {
        var r = Eslestir(Belge([Kare(Olusan, 0, 0, 20), Kare(Olusan, 10, 10, 20)], [Yazi("ortak", 15, 15)]));

        r.Polylines.Should().OnlyContain(p => p.Label.Length == 0);
        r.AmbiguousCount.Should().Be(2);
    }

    [Fact]
    public void Etiket_Hedef_Rol_Disindaki_Acik_Veya_Zaten_Etiketli_Cizgilere_Dokunmamali()
    {
        var etiketli = Kare(Olusan, 0, 0, 20, "NCZ-101/1");
        var giren = Kare(Giren, 0, 0, 20);
        CadPolyline acik = new(Olusan, "", null, [P(0, 0), P(10, 0)], false, null);

        var r = Eslestir(Belge([etiketli, giren, acik], [Yazi("101/1", 5, 5)]));

        r.Polylines[0].Label.Should().Be("NCZ-101/1");
        r.Polylines[1].Label.Should().BeEmpty("giren katmani hedef rol degil");
        r.Polylines[2].Label.Should().BeEmpty();
        r.MatchedCount.Should().Be(0);
        r.UnlabeledCount.Should().Be(0);
    }

    [Fact]
    public void Etiket_Muhendis_Rol_Atamasiyla_Standart_Disi_Katmanda_Calismali()
    {
        var roller = CadLayerRoleMap.Standard.WithAssignment("@SM", CadLayerRole.ZoningParcel);
        CadLayerDocument belge = new(CadFormat.Dxf, [new CadLayer(0, "@SM")], [Kare("@SM", 0, 0, 20)], [Yazi("103/C1", 5, 5)], 0, 0, 0, []);

        CadLabelMatcher.Match(belge, roller, ImarRolu).Polylines[0].Label.Should().Be("103/C1");
    }

    [Fact]
    public void Etiket_Bos_Metinli_Yazilari_Yok_Saymali()
    {
        var r = Eslestir(Belge([Kare(Olusan, 0, 0, 20)], [Yazi("   ", 5, 5), Yazi("101/1", 6, 6)]));

        r.Polylines[0].Label.Should().Be("101/1");
    }

    [Fact]
    public void Etiket_Belge_Diger_Alanlarini_Korumali_Ve_Tekrar_Edilebilir_Olmali()
    {
        var belge = Belge([Kare(Olusan, 0, 0, 20)], [Yazi("101/1", 10, 10)]);

        var a = Eslestir(belge);
        var b = Eslestir(belge);

        a.Polylines[0].Vertices.Should().Equal(belge.Polylines[0].Vertices);
        a.Polylines.Select(p => p.Label).Should().Equal(b.Polylines.Select(p => p.Label));
    }

    [Theory]
    [InlineData("523.45")]
    [InlineData("523,45")]
    [InlineData("1050.0")]
    public void Etiket_Alan_Veya_Kot_Yazisini_Parsel_Kodu_Olarak_Atamamali(string sayisal)
    {
        var r = Eslestir(Belge([Kare(Olusan, 0, 0, 20)], [Yazi(sayisal, 5, 5), Yazi("101/1", 15, 15)]));

        r.Polylines[0].Label.Should().Be("101/1");
    }

    [Fact]
    public void Etiket_Yazi_Katmani_Suzgeci_Verilirse_Yalnizca_O_Katmanlari_Kullanmali()
    {
        var belge = Belge([Kare(Olusan, 0, 0, 20)], [Yazi("YAPI-A", 5, 5, "BINA"), Yazi("101/1", 15, 15, "PARSEL_NO")]);

        CadLabelMatcher.Match(belge, CadLayerRoleMap.Standard, ImarRolu, textLayers: ["PARSEL_NO"]).Polylines[0].Label
            .Should().Be("101/1");
        Eslestir(belge).Polylines[0].Label.Should().BeEmpty("suzgecsiz iki farkli metin belirsizdir");
    }

    [Fact]
    public void Etiket_Ust_Uste_Kadastro_Ve_Imar_Katmanlarinda_Rol_Bazli_Eslestirmeli()
    {
        var belge = Belge([Kare(Giren, 0, 0, 40), Kare(Olusan, 0, 0, 20)], [Yazi("101/1", 5, 5)]);

        var imar = CadLabelMatcher.Match(belge, CadLayerRoleMap.Standard, [CadLayerRole.ZoningParcel]);

        imar.Polylines[1].Label.Should().Be("101/1", "rol bazli eslestirmede farkli roldeki poligon yaziyi paylasmaz");
        imar.Polylines[0].Label.Should().BeEmpty();
    }

    [Fact]
    public void Etiket_Null_Girdiyi_Reddetmeli()
    {
        ((Action)(() => CadLabelMatcher.Match(null!, CadLayerRoleMap.Standard, ImarRolu))).Should().Throw<ArgumentNullException>();
    }
}

using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Geometry;
using Xunit;

namespace Imar18.Application.Tests.Cad;

/// <summary>
/// P5.3 (P4'ten kalan sihirbaz bağlantısı): içe aktarılan CAD belgesinin mühendis katman–rol eşlemesiyle proje geometrisine
/// dönüştürülmesi. Yalnızca kapalı parsel poligonları alınır; kapanış tekrarı atılır; etiketsiz/yinelenen kimlikler
/// deterministik adlandırılır; geçersiz halkalar komutu düşürmez, sayılır.
/// </summary>
public sealed class ProjeGeometriEslemesiTests
{
    private static CadPolyline Kare(string katman, string etiket, decimal x0, bool kapali = true, bool kapanisTekrari = true)
    {
        var v = new List<GeodeticPoint> { new(x0, 0m), new(x0, 10m), new(x0 + 10m, 10m), new(x0 + 10m, 0m) };
        if (kapanisTekrari)
        {
            v.Add(v[0]);
        }

        return new CadPolyline(katman, etiket, null, v, kapali, null);
    }

    private static CadLayerDocument Belge(params CadPolyline[] cizgiler) => new(
        CadFormat.Ncz,
        cizgiler.Select(c => c.LayerName).Distinct().Select((n, i) => new CadLayer(i, n)).ToArray(),
        cizgiler,
        [],
        0,
        0,
        0,
        []);

    [Fact]
    public void ProjeGeometriEslemesi_Standart_Rollerle_Parsel_Geometrisi_Uretmeli()
    {
        var belge = Belge(
            Kare("TK_PARSEL_GİREN", "901/1", 0m),
            Kare("TK_PARSEL_OLUŞAN", "901/A1", 100m),
            Kare("TK_PARSEL_YOLATERK", "901/Y1", 200m),
            Kare("PL_PARK", "PARK", 300m),
            Kare("DÜZENLEME_SINIRI", "x", 400m));

        var sonuc = ProjeGeometriEslemesi.Olustur(belge, CadLayerRoleMap.Standard);

        sonuc.Parseller.Select(p => (p.ParselId, p.Rol)).Should().Equal(
            ("901/1", ParselRolu.Kadastro), ("901/A1", ParselRolu.Imar), ("901/Y1", ParselRolu.YolTerk), ("PARK", ParselRolu.KamuAlani));
        sonuc.Parseller.Should().OnlyContain(p => p.Koseler.Count == 4, "kapanış tekrarı atılır");
        sonuc.AtlananSayisi.Should().Be(0, "düzenleme sınırı parsel rolü değildir, atlanmış sayılmaz");
    }

    [Fact]
    public void ProjeGeometriEslemesi_Etiketsiz_Ve_Yinelenen_Kimlikleri_Deterministik_Adlandirmali()
    {
        var belge = Belge(Kare("TK_PARSEL_OLUŞAN", "", 0m), Kare("TK_PARSEL_OLUŞAN", "A1", 100m), Kare("TK_PARSEL_OLUŞAN", "A1", 200m));

        var sonuc = ProjeGeometriEslemesi.Olustur(belge, CadLayerRoleMap.Standard);

        sonuc.Parseller.Select(p => p.ParselId).Should().Equal("TK_PARSEL_OLUŞAN#1", "A1", "A1~2");
    }

    [Fact]
    public void ProjeGeometriEslemesi_Acik_Veya_Gecersiz_Poligonlari_Atlamali_Ve_Saymali()
    {
        var dejenere = new CadPolyline("TK_PARSEL_OLUŞAN", "D", null, [new(0m, 0m), new(0m, 1m), new(0m, 2m), new(0m, 0m)], true, null);
        var belge = Belge(Kare("TK_PARSEL_OLUŞAN", "acik", 0m, kapali: false), dejenere, Kare("TK_PARSEL_OLUŞAN", "iyi", 100m, kapanisTekrari: false));

        var sonuc = ProjeGeometriEslemesi.Olustur(belge, CadLayerRoleMap.Standard);

        sonuc.Parseller.Should().ContainSingle(p => p.ParselId == "iyi");
        sonuc.AtlananSayisi.Should().Be(2);
    }

    [Fact]
    public void ProjeGeometriEslemesi_Muhendis_Rol_Atamasini_Kullanmali()
    {
        var belge = Belge(Kare("@SM", "A1", 0m));

        ProjeGeometriEslemesi.Olustur(belge, CadLayerRoleMap.Standard).Parseller.Should().BeEmpty();
        ProjeGeometriEslemesi.Olustur(belge, CadLayerRoleMap.Standard.WithAssignment("@SM", CadLayerRole.ZoningParcel))
            .Parseller.Should().ContainSingle(p => p.Rol == ParselRolu.Imar);
    }
}

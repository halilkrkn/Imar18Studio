using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tecviz;
using Xunit;

namespace Imar18.Application.Tests.Cad;

/// <summary>
/// SAHA-013: mühendisin <c>.NCZ</c> dosyasındaki <c>02_PARSEL_ALN_YENI</c>, <c>YOL</c>, <c>TK_OLUSAN</c> ve <c>İMAR</c> katmanları
/// içe aktarılırken rolleri otomatik önerilmeli: adında <c>OLUSAN</c>/<c>OLUŞAN</c> geçen her katman kesinlikle "TK_Oluşan"
/// (<see cref="CadLayerRole.ZoningParcel"/>), adı tam <c>İMAR</c>/<c>IMAR</c> olan katman "Yoksay" (<see cref="CadLayerRole.Ignored"/>,
/// DOP R2'ye girmez), <c>PARSEL_ALN</c> içeren katman "Kadastro Parseli", <c>YOL</c> "Yola Terk".
/// Bağımsız referans (elle hesap, tamamen sentetik; Kuzey 0..50 m şeritleri): kadastro K1 Doğu 0–25 m = 1250.00 m²;
/// TK_OLUSAN A1 0–12 m = 600.00 m² + A2 12–20 m = 400.00 m² = 1000.00 m²; yol Y1 20–25 m = 250.00 m²; İMAR poligonu 10–30 m =
/// 1000.00 m² (yalnız A2'yi tümüyle kapsar: 400 &lt; 0.5 × 1000, kabuk/yinelenen sayılmaz — imar rolünde olsaydı R2 = 2000.00 olurdu).
/// Beklenen: R1 = 1250.00, R2 = 1000.00, DOPO = (1250 − 1000) / 1250 = 0.2000000, kamuya ayrılan 250.00 m².
/// </summary>
public sealed class Saha013TkOlusanEslemeTests
{
    private const string Kadastro = "02_PARSEL_ALN_YENI";
    private const string Yol = "YOL";
    private const string TkOlusan = "TK_OLUSAN";
    private const string Imar = "İMAR";

    /// <summary>Kuzey (X) 0..50 m, Doğu (Y) <paramref name="y1"/>..<paramref name="y2"/> m dikdörtgeni (kapanış tekrarlı).</summary>
    private static CadPolyline Serit(string katman, string etiket, decimal y1, decimal y2)
    {
        GeodeticPoint P(decimal x, decimal y) => new(OrnekProje.X0 + x, OrnekProje.Y0 + y);
        return new CadPolyline(katman, etiket, null, [P(0m, y1), P(0m, y2), P(50m, y2), P(50m, y1), P(0m, y1)], true, null);
    }

    /// <summary>Saha ekran görüntüsündeki katman düzeni (sentetik geometri).</summary>
    private static CadLayerDocument SahaBelgesi() => new(
        CadFormat.Ncz,
        [new CadLayer(0, Kadastro), new CadLayer(1, Yol), new CadLayer(2, TkOlusan), new CadLayer(3, Imar)],
        [
            Serit(Kadastro, "K1", 0m, 25m),
            Serit(Yol, "Y1", 20m, 25m),
            Serit(TkOlusan, "A1", 0m, 12m),
            Serit(TkOlusan, "A2", 12m, 20m),
            Serit(Imar, "IMAR-1", 10m, 30m),
        ],
        [],
        0,
        0,
        0,
        []);

    /// <summary>1. adımın yaptığı gibi: her katman otomatik önerilen rolüyle eşlemeye yazılır.</summary>
    private static CadLayerRoleMap OtomatikRoller(CadLayerDocument belge) =>
        belge.Layers.Aggregate(CadLayerRoleMap.Standard, (m, l) => m.WithAssignment(l.Name, CadLayerRoleMap.OtomatikRol(l.Name)));

    private static decimal Toplam(ProjeGeometriSonucu s, ParselRolu rol) => s.Parseller
        .Where(p => p.Rol == rol)
        .Sum(p => GaussAreaCalculator.Compute(PolygonRing.FromOpen(p.Koseler).Value).AreaM2);

    // ============================ Birim: otomatik rol önerisi ============================

    [Theory]
    [InlineData(Kadastro, CadLayerRole.CadastralParcel)]
    [InlineData(Yol, CadLayerRole.RoadDedication)]
    [InlineData(TkOlusan, CadLayerRole.ZoningParcel)]
    [InlineData(Imar, CadLayerRole.Ignored)]
    public void OtomatikRol_Saha_Katmanlarini_Beklenen_Rollere_Eslemeli(string katman, CadLayerRole beklenen)
    {
        CadLayerRoleMap.OtomatikRol(katman).Should().Be(beklenen);
    }

    [Theory]
    [InlineData("TK_OLUSAN")]
    [InlineData("TK_OLUŞAN")]
    [InlineData("TK_PARSEL_OLUŞAN")]
    [InlineData("TK_PARSEL_OLUSAN")]
    [InlineData("PARSEL_OLUSAN")]
    [InlineData("OLUŞAN")]
    [InlineData("02_PARSEL_ALN_OLUSAN")]
    [InlineData("  TK_OLUSAN  ")]
    public void OtomatikRol_Adinda_OLUSAN_Gecen_Her_Katman_Kadastro_Degil_TK_Olusan_Olmali(string katman)
    {
        CadLayerRoleMap.OtomatikRol(katman).Should().Be(CadLayerRole.ZoningParcel, "OLUSAN/OLUŞAN kuralı diğer tüm kurallardan önce uygulanır");
    }

    [Theory]
    [InlineData("İMAR")]
    [InlineData("IMAR")]
    [InlineData("  İMAR ")]
    public void OtomatikRol_Adi_Tam_IMAR_Olan_Katman_Yoksay_Olmali(string katman)
    {
        CadLayerRoleMap.OtomatikRol(katman).Should().Be(CadLayerRole.Ignored);
    }

    [Theory]
    [InlineData("02_PARSEL_ALN_YENI")]
    [InlineData("PARSEL_ALN")]
    [InlineData("01_PARSEL_ALN_ESKI")]
    public void OtomatikRol_PARSEL_ALN_Iceren_Katman_Kadastro_Parseli_Olmali(string katman)
    {
        CadLayerRoleMap.OtomatikRol(katman).Should().Be(CadLayerRole.CadastralParcel);
    }

    [Theory]
    [InlineData("IMAR_ADASI")]
    [InlineData("İMAR_PLANI")]
    [InlineData("TK_İMAR")]
    [InlineData("imar")]
    [InlineData("YOL_AKS")]
    [InlineData("@SM")]
    [InlineData("YAZI")]
    [InlineData("")]
    public void OtomatikRol_Kurala_Uymayan_Katman_Atanmamis_Kalmali(string katman)
    {
        CadLayerRoleMap.OtomatikRol(katman).Should().Be(CadLayerRole.Unassigned, "tam ad / içerik kuralı dışında tahmin yapılmaz");
    }

    [Fact]
    public void OtomatikRol_Standart_Netcad_Adlarinda_Standart_Esleme_Ile_Ayni_Olmali()
    {
        foreach (var (katman, rol) in CadLayerRoleMap.Standard.Assignments)
        {
            CadLayerRoleMap.OtomatikRol(katman).Should().Be(rol, katman);
        }
    }

    [Fact]
    public void OtomatikRol_Standart_Eslemeyi_Degistirmemeli_Resolve_Tahmin_Yapmamali()
    {
        CadLayerRoleMap.OtomatikRol(TkOlusan);

        CadLayerRoleMap.Standard.Resolve(TkOlusan).Should().Be(CadLayerRole.Unassigned, "öneri yalnız 1. adım satırına yazılır; standart eşleme birebir ad sözleşmesidir");
        CadLayerRoleMap.Standard.Assignments.Should().HaveCount(10);
    }

    [Fact]
    public void OtomatikRol_Null_Katman_Adi_ArgumentNullException_Firlatmali()
    {
        var eylem = () => CadLayerRoleMap.OtomatikRol(null!);

        eylem.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OtomatikRol_Tekrar_Edilebilir_Olmali()
    {
        var ilk = SahaBelgesi().Layers.Select(l => CadLayerRoleMap.OtomatikRol(l.Name)).ToArray();
        var ikinci = SahaBelgesi().Layers.Select(l => CadLayerRoleMap.OtomatikRol(l.Name)).ToArray();

        ikinci.Should().Equal(ilk);
    }

    // ============================ Proje geometrisi: İMAR R2'ye girmez ============================

    [Fact]
    public void ProjeGeometriEslemesi_Otomatik_Rollerle_IMAR_Katmani_Projeye_Ve_R2ye_Girmemeli()
    {
        var sonuc = ProjeGeometriEslemesi.Olustur(SahaBelgesi(), OtomatikRoller(SahaBelgesi()));

        sonuc.Parseller.Select(p => (p.ParselId, p.Rol)).Should().Equal(
            ("K1", ParselRolu.Kadastro), ("Y1", ParselRolu.YolTerk), ("A1", ParselRolu.Imar), ("A2", ParselRolu.Imar));
        sonuc.Parseller.Should().NotContain(p => p.ParselId == "IMAR-1");
        Toplam(sonuc, ParselRolu.Imar).Should().Be(1000.00m, "R2 yalnız TK_OLUSAN (600 + 400)");
        Toplam(sonuc, ParselRolu.Kadastro).Should().Be(1250.00m);
        sonuc.Uyarilar.Should().BeEmpty("İMAR Yoksay olduğundan çift sayım önlemi devreye girmez");
        sonuc.CiftSayimHaricSayisi.Should().Be(0);
        sonuc.AtlananSayisi.Should().Be(0);
    }

    [Fact]
    public void ProjeGeometriEslemesi_IMAR_Imar_Rolunde_Olsaydi_R2_Sisecekti_Referans_Ayirt_Edici_Olmali()
    {
        var yanlis = OtomatikRoller(SahaBelgesi()).WithAssignment(Imar, CadLayerRole.ZoningParcel);

        var sonuc = ProjeGeometriEslemesi.Olustur(SahaBelgesi(), yanlis);

        Toplam(sonuc, ParselRolu.Imar).Should().Be(2000.00m, "İMAR poligonu kabuk/yinelenen sayılmaz; yanlış rol R2'yi 1000 m² şişirirdi");
    }

    // ============================ Entegrasyon: CAD → proje → DOP ============================

    [Fact]
    public async Task Entegrasyon_Otomatik_Rollerle_DOP_R2_1000_Ve_DOPO_Dogru_Olmali()
    {
        var geometri = ProjeGeometriEslemesi.Olustur(SahaBelgesi(), OtomatikRoller(SahaBelgesi()));
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik SAHA-013", zaman)).Value;
        IProjeKomutu[] komutlar =
        [
            new ProjeCrsAyarlaKomutu(OrnekProje.Crs()),
            new ParselGeometrileriAyarlaKomutu(geometri.Parseller),
            new TecvizGirdileriAyarlaKomutu([new TecvizParselGirdisi("K1", 1250.00m, 1250.00m, OrnekProje.Tecviz())], null),
        ];
        foreach (var k in komutlar)
        {
            (await yol.GonderAsync(k, OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        }

        var oneri = ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji);
        (await yol.GonderAsync(new DopGirdisiAyarlaKomutu(DopGirdisi.Olustur(oneri.R1TapuToplamiM2!.Value, oneri.OnerilenR2M2!.Value).Value), OrnekProje.Muhendis))
            .IsSuccess.Should().BeTrue();
        var d = ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji);

        d.R1TapuToplamiM2.Should().Be(1250.00m);
        d.OnerilenR2M2.Should().Be(1000.00m, "İMAR (1000.00 m²) R2'ye eklenmemeli");
        d.Dop!.Girdi.R2.Should().Be(1000.00m);
        d.Dop.Dopo.Should().Be(0.2000000m, "(1250 − 1000) / 1250");
        d.Dop.KamuyaAyrilanAlanM2.Should().Be(250.00m);
        d.Bulgular.Should().NotContain(b => b.Severity == ErrorSeverity.Critical);
    }
}

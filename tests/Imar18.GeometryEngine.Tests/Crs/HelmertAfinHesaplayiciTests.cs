using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Crs;
using Xunit;

namespace Imar18.GeometryEngine.Tests.Crs;

/// <summary>
/// P4.2 2B Helmert (4 parametre) ve Afin (6 parametre) donusum raporu. Beklenen degerler C#'taki agirlik merkezi
/// formulunden bagimsiz olarak Python'da tam rasyonel (Fraction) normal denklemlerle cozulmustur; noktalar sentetiktir.
/// Model: X' = a X - b Y + tx, Y' = b X + a Y + ty (X = Kuzey, Y = Dogu).
/// </summary>
public sealed class HelmertAfinHesaplayiciTests
{
    private static readonly GeodeticPoint[] Kaynak =
    [
        new(X: 4_000_000m, Y: 500_000m), new(X: 4_000_100m, Y: 500_000m), new(X: 4_000_100m, Y: 500_100m),
        new(X: 4_000_000m, Y: 500_100m), new(X: 4_000_050m, Y: 500_020m),
    ];

    // a = 1.00001, b = 0.00002, tx = -30.25, ty = -79.5 ile tam hesaplanmis hedefler.
    private static readonly GeodeticPoint[] TamHedef =
    [
        new(X: 3_999_999.75m, Y: 500_005.5m), new(X: 4_000_099.751m, Y: 500_005.502m), new(X: 4_000_099.749m, Y: 500_105.503m),
        new(X: 3_999_999.748m, Y: 500_105.501m), new(X: 4_000_049.7501m, Y: 500_025.5012m),
    ];

    // TamHedef + sentetik olcu gurultusu (+12/-8, -5/+10, +7/+4, -10/-6, +3/0 mm).
    private static readonly GeodeticPoint[] GurultuluHedef =
    [
        new(X: 3_999_999.762m, Y: 500_005.492m), new(X: 4_000_099.746m, Y: 500_005.512m), new(X: 4_000_099.756m, Y: 500_105.507m),
        new(X: 3_999_999.738m, Y: 500_105.495m), new(X: 4_000_049.7531m, Y: 500_025.5012m),
    ];

    private static IReadOnlyList<OrtakNokta> Noktalar(GeodeticPoint[] hedef, int adet = 5) =>
        Enumerable.Range(0, adet).Select(i => new OrtakNokta($"N{i + 1}", Kaynak[i], hedef[i])).ToList();

    private static void Yakin(decimal gercek, decimal beklenen, decimal tolerans) =>
        Math.Abs(gercek - beklenen).Should().BeLessThanOrEqualTo(tolerans, $"beklenen {beklenen}, bulunan {gercek}");

    [Fact]
    public void Helmert_Tam_Veride_Parametreleri_Kesin_Bulup_Sifir_Artik_Vermeli()
    {
        var r = HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, Noktalar(TamHedef), toleransM: null).Value;

        Yakin(r.Parametreler["a"], 1.00001m, 1e-15m);
        Yakin(r.Parametreler["b"], 0.00002m, 1e-15m);
        Yakin(r.Parametreler["tx"], -30.25m, 1e-6m);
        Yakin(r.Parametreler["ty"], -79.5m, 1e-6m);
        Yakin(r.OlcekFaktoru!.Value, 1.000010000199998m, 1e-12m);
        Yakin(r.DonmeAcisiGrad!.Value, 0.001273226812m, 1e-10m);
        r.Artiklar.Should().OnlyContain(a => Math.Abs(a.Vx) < 1e-9m && Math.Abs(a.Vy) < 1e-9m);
        Yakin(r.KareselOrtalamaHataM!.Value, 0m, 1e-9m);
        r.SerbestlikDerecesi.Should().Be(6);
    }

    [Fact]
    public void Helmert_Gurultulu_Veride_Bagimsiz_En_Kucuk_Kareler_Referansiyla_Esit_Olmali()
    {
        var r = HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, Noktalar(GurultuluHedef), toleransM: null).Value;

        Yakin(r.Parametreler["a"], 1.000000347490347m, 1e-14m);
        Yakin(r.Parametreler["b"], 0.000114015444015m, 1e-14m);
        Yakin(r.Parametreler["tx"], 55.373779923m, 1e-6m);
        Yakin(r.Parametreler["ty"], -450.739797297m, 1e-6m);
        Yakin(r.KareselOrtalamaHataM!.Value, 0.007617167m, 1e-9m);
        Yakin(r.OlcekFaktoru!.Value, 1.000000353990m, 1e-11m);
        Yakin(r.DonmeAcisiGrad!.Value, 0.0072584460m, 1e-9m);
        decimal[] vx = [-0.005981m, 0.010054m, -0.011347m, 0.006618m, 0.000656m];
        decimal[] vy = [0.003724m, -0.004875m, 0.000160m, 0.000759m, 0.000232m];
        for (var i = 0; i < 5; i++)
        {
            Yakin(r.Artiklar[i].Vx, vx[i], 1e-6m);
            Yakin(r.Artiklar[i].Vy, vy[i], 1e-6m);
        }
    }

    [Fact]
    public void Afin_Gurultulu_Veride_Bagimsiz_En_Kucuk_Kareler_Referansiyla_Esit_Olmali()
    {
        var r = HelmertAfinHesaplayici.Hesapla(DonusumModeli.Afin2B, Noktalar(GurultuluHedef), toleransM: null).Value;

        Yakin(r.Parametreler["a0"], -4.686649253731m, 1e-6m);
        Yakin(r.Parametreler["a1"], 1.000010000000m, 1e-12m);
        Yakin(r.Parametreler["a2"], -0.000071119403m, 1e-12m);
        Yakin(r.Parametreler["b0"], -630.177820895522m, 1e-6m);
        Yakin(r.Parametreler["b1"], 0.000160000000m, 1e-12m);
        Yakin(r.Parametreler["b2"], 0.999991343284m, 1e-12m);
        Yakin(r.KareselOrtalamaHataM!.Value, 0.008738639m, 1e-9m);
        r.SerbestlikDerecesi.Should().Be(4);
        r.OlcekFaktoru.Should().BeNull("afin donusumde tek olcek/donme yoktur");
        Yakin(r.Artiklar[0].Vx, -0.008351m, 1e-6m);
        Yakin(r.Artiklar[4].Vy, 0.000448m, 1e-6m);
    }

    [Fact]
    public void Helmert_Tolerans_Asan_Noktalari_Isaretlemeli_Ve_En_Buyuk_Artigi_Raporlamali()
    {
        var r = HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, Noktalar(GurultuluHedef), toleransM: 0.011m).Value;

        r.Artiklar.Where(a => a.ToleransAsimi).Select(a => a.NoktaId).Should().Equal("N2", "N3");
        r.EnBuyukArtik.NoktaId.Should().Be("N3");
        Yakin(r.EnBuyukArtik.V, 0.011348m, 1e-6m);
        r.Uyarilar.Should().ContainSingle(u => u.Code == ErrorCodes.CrsDonusumToleransAsimi)
            .Which.TechnicalMessage.Should().Contain("2 of 5");
    }

    [Fact]
    public void Helmert_Iki_Noktada_Kesin_Cozum_Verip_Karesel_Ortalama_Hatayi_Tanimsiz_Birakmali()
    {
        var r = HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, Noktalar(TamHedef, 2), toleransM: null).Value;

        r.SerbestlikDerecesi.Should().Be(0);
        r.KareselOrtalamaHataM.Should().BeNull();
        Yakin(r.Parametreler["a"], 1.00001m, 1e-15m);
    }

    [Theory]
    [InlineData(DonusumModeli.Helmert2B, 1)]
    [InlineData(DonusumModeli.Afin2B, 2)]
    public void Donusum_Yetersiz_Ortak_Noktada_ERR_CRS_004_Dondurmeli(DonusumModeli model, int adet)
    {
        var hata = HelmertAfinHesaplayici.Hesapla(model, Noktalar(TamHedef, adet), null).Error;

        hata.Code.Should().Be(ErrorCodes.CrsDonusumHesaplanamadi);
        hata.TechnicalMessage.Should().Contain("at least");
    }

    [Fact]
    public void Afin_Dogrusal_Dizilmis_Noktalarda_ERR_CRS_004_Dondurmeli()
    {
        OrtakNokta[] dogrusal =
        [
            new("A", new(X: 4_000_000m, Y: 500_000m), new(X: 4_000_000m, Y: 500_000m)),
            new("B", new(X: 4_000_010m, Y: 500_010m), new(X: 4_000_010m, Y: 500_010m)),
            new("C", new(X: 4_000_020m, Y: 500_020m), new(X: 4_000_020m, Y: 500_020m)),
        ];

        HelmertAfinHesaplayici.Hesapla(DonusumModeli.Afin2B, dogrusal, null).Error.TechnicalMessage.Should().Contain("collinear");
    }

    [Fact]
    public void Afin_Neredeyse_Dogrusal_Noktalarda_ERR_CRS_004_Dondurmeli()
    {
        // 1 km boyunca dizilmis, ortadaki 1 cm sapan uc nokta: kotu kosullu afin geometri.
        OrtakNokta[] neredeyse =
        [
            new("A", new(X: 4_000_000m, Y: 500_000m), new(X: 4_000_000m, Y: 500_000m)),
            new("B", new(X: 4_000_500m, Y: 500_000.01m), new(X: 4_000_500m, Y: 500_000.01m)),
            new("C", new(X: 4_001_000m, Y: 500_000m), new(X: 4_001_000m, Y: 500_000m)),
        ];

        HelmertAfinHesaplayici.Hesapla(DonusumModeli.Afin2B, neredeyse, null).Error.TechnicalMessage.Should().Contain("collinear");
    }

    [Fact]
    public void Helmert_Cakisik_Noktalarda_ERR_CRS_004_Dondurmeli()
    {
        OrtakNokta[] cakisik =
        [
            new("A", new(X: 4_000_000m, Y: 500_000m), new(X: 4_000_000m, Y: 500_000m)),
            new("B", new(X: 4_000_000m, Y: 500_000m), new(X: 4_000_001m, Y: 500_000m)),
        ];

        HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, cakisik, null).Error.Code.Should().Be(ErrorCodes.CrsDonusumHesaplanamadi);
    }

    [Fact]
    public void Donusum_Yinelenen_Veya_Bos_Nokta_Kimliginde_ERR_CRS_004_Dondurmeli()
    {
        var yinelenen = Noktalar(TamHedef).Select((n, i) => i == 1 ? n with { NoktaId = "N1" } : n).ToList();
        var bos = Noktalar(TamHedef).Select((n, i) => i == 0 ? n with { NoktaId = " " } : n).ToList();

        HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, yinelenen, null).Error.TechnicalMessage.Should().Contain("point id");
        HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, bos, null).Error.TechnicalMessage.Should().Contain("point id");
    }

    [Fact]
    public void Donusum_Cok_Genis_Yayilimda_Tasmadan_ERR_CRS_004_Dondurmeli()
    {
        OrtakNokta[] genis =
        [
            new("A", new(X: 4_000_000m, Y: 300_000m), new(X: 4_000_000m, Y: 300_000m)),
            new("B", new(X: 4_400_000m, Y: 700_000m), new(X: 4_400_000m, Y: 700_000m)),
            new("C", new(X: 4_000_000m, Y: 700_000m), new(X: 4_000_000m, Y: 700_000m)),
        ];

        HelmertAfinHesaplayici.Hesapla(DonusumModeli.Afin2B, genis, null).Error.TechnicalMessage.Should().Contain("spread");
    }

    [Fact]
    public void Donusum_Negatif_Tolerans_Ve_Null_Girdi_Reddedilmeli()
    {
        HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, Noktalar(TamHedef), -0.01m).Error.Code
            .Should().Be(ErrorCodes.CrsDonusumHesaplanamadi);
        ((Action)(() => HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, null!, null))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Donusum_Hata_Mesaji_Koordinat_Sizdirmamali()
    {
        var hata = HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, Noktalar(TamHedef, 1), null).Error;

        hata.TechnicalMessage.Should().NotContain("4000000").And.NotContain("500000");
    }

    [Fact]
    public void Donusum_Tekrar_Edilebilir_Ve_Nokta_Sirasindan_Bagimsiz_Parametre_Uretmeli()
    {
        var ileri = HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, Noktalar(GurultuluHedef), null).Value;
        var ters = HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, Noktalar(GurultuluHedef).Reverse().ToList(), null).Value;

        Yakin(ters.Parametreler["a"], ileri.Parametreler["a"], 1e-20m);
        Yakin(ters.Parametreler["tx"], ileri.Parametreler["tx"], 1e-12m);
    }

    [Fact]
    public void Donusum_Rapordaki_Parametrelerle_Nokta_Donusturebilmeli()
    {
        var r = HelmertAfinHesaplayici.Hesapla(DonusumModeli.Helmert2B, Noktalar(TamHedef), null).Value;

        var p = r.Uygula(new GeodeticPoint(X: 4_000_000m, Y: 500_000m));

        Yakin(p.X, 3_999_999.75m, 1e-6m);
        Yakin(p.Y, 500_005.5m, 1e-6m);
    }
}

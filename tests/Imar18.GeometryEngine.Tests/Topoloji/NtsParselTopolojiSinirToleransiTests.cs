using FluentAssertions;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Topoloji;
using Xunit;

namespace Imar18.GeometryEngine.Tests.Topoloji;

/// <summary>
/// SAHA-004 (P7 saha düzeltmesi, SORU-037): gerçek Netcad çizimlerinde (1) milimetrik sınır dokunması / taşması / çakışması onay
/// engeli değil, sınır toleransı bulgusudur; (2) farklı imar adaları arasındaki karşılanmayan alan imar yoludur (parseller arası
/// boşluk sayılmaz); (3) taşma ve boşluk bileşen / parsel bazında, ilgili parsel kimlikleri ve alanıyla raporlanır.
/// Geometriler eksenlere paralel dikdörtgenlerdir; beklenen alanlar elle (en × boy) hesaplanmıştır. Yerel X = Kuzey, Y = Doğu.
/// </summary>
public sealed class NtsParselTopolojiSinirToleransiTests
{
    private const decimal X0 = 4_400_000m;
    private const decimal Y0 = 500_000m;
    private static readonly NtsParselTopolojiDenetcisi Denetci = new();

    /// <summary>X ∈ [x1, x2] (Kuzey), Y ∈ [y1, y2] (Doğu) dikdörtgeni.</summary>
    private static ParselGeometrisi D(string id, ParselRolu rol, decimal y1, decimal y2, decimal x1 = 0m, decimal x2 = 10m) => new(
        id, rol,
        [new GeodeticPoint(X0 + x1, Y0 + y1), new GeodeticPoint(X0 + x1, Y0 + y2), new GeodeticPoint(X0 + x2, Y0 + y2), new GeodeticPoint(X0 + x2, Y0 + y1)]);

    [Fact]
    public void SinirToleransi_Sabitleri_Saha_Kararina_Esit_Olmali()
    {
        ParselTopolojisi.SinirToleransiAlanM2.Should().Be(0.05m);
        ParselTopolojisi.SinirSeridiGenislikM.Should().Be(0.01m);
        ParselTopolojisi.SinirSeridiAzamiAlanM2.Should().Be(0.50m);
        ParselTopolojisi.ToplamSinirToleransiAzamiM2.Should().Be(1.00m);
    }

    [Fact]
    public void SinirToleransi_Dar_Ama_Buyuk_Alanli_Serit_Ust_Siniri_Asinca_Kritik_Kalmali()
    {
        // Reviewer H2: 9 mm x 100 m = 0.90 m2; 1 cm den dar ama serit alan tavanini (0.50 m2) asar -> onay engeli.
        var b = Denetci.Denetle([
            D("K1", ParselRolu.Kadastro, 0m, 20m, 0m, 100m),
            D("101/1", ParselRolu.Imar, 0m, 12m, 0m, 100m), D("101/2", ParselRolu.Imar, 12m, 20.009m, 0m, 100m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["101/2", "K1"], 0.90m, SinirToleransiIcinde: false));
    }

    [Theory]
    [InlineData(11.95, 0.05, true)]
    [InlineData(11.94, 0.06, false)]
    public void SinirToleransi_Alan_Siniri_0_05_Dahil_0_06_Haric_Olmali(double a2Baslangic, double beklenenAlan, bool tolerans)
    {
        // Serit olmayan (1 m eninde, en az 5 cm genislikte) cakisma: yalniz alan kurali uygulanir. Kadastro yok: kapsama analizi yapilmaz.
        var b = Denetci.Denetle([D("101/1", ParselRolu.Imar, 0m, 12m), D("101/2", ParselRolu.Imar, (decimal)a2Baslangic, 20m, 0m, 1m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanCakismasi, ["101/1", "101/2"], (decimal)beklenenAlan, tolerans));
    }

    [Fact]
    public void SinirToleransi_Cok_Sayida_Esik_Alti_Kiymik_Toplami_Tek_Bulgu_Olarak_Raporlanmali()
    {
        // Reviewer M1: 5 cakismanin her biri 0.0004 x 10 = 0.004 m2 (tek tek 0.00 -> raporlanmaz); toplam 0.02 m2 kaybolmamali.
        var b = Denetci.Denetle([.. Enumerable.Range(0, 6).Select(i => D($"101/{i + 1}", ParselRolu.Imar, 2m * i, (2m * i) + 2.0004m))]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanCakismasi, ["101/1", "101/2", "101/3", "101/4", "101/5", "101/6"], 0.02m, SinirToleransiIcinde: true));
    }

    [Fact]
    public void ImarYolu_Cizilmis_Yol_Terk_Varken_Adalar_Arasi_Kalan_Bosluk_Kritik_Kalmali()
    {
        // Reviewer H1: yol terk (Y1) cizilmis ama adalar arasindaki alanin yarisini kapliyor; kalan 6 x 5 = 30 m2 imar yolu sayilmaz.
        var b = Denetci.Denetle([
            D("K1", ParselRolu.Kadastro, 0m, 40m), D("101/1", ParselRolu.Imar, 0m, 12m),
            D("Y1", ParselRolu.YolTerk, 12m, 18m, 0m, 5m), D("102/1", ParselRolu.Imar, 18m, 40m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanKapsamaBoslugu, ["101/1", "102/1", "K1", "Y1"], 30.00m));
    }

    [Fact]
    public void SinirToleransi_Milimetrik_Imar_Cakismasi_Tolerans_Icinde_Isaretlenmeli()
    {
        // 101/1 [0, 12.003] ∩ 101/2 [12, 20] = 0.003 m × 10 m = 0.03 m² ≤ 0.05 m² (ve 3 mm < 1 cm şerit).
        var b = Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 20m), D("101/1", ParselRolu.Imar, 0m, 12.003m), D("101/2", ParselRolu.Imar, 12m, 20m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanCakismasi, ["101/1", "101/2"], 0.03m, SinirToleransiIcinde: true));
    }

    [Fact]
    public void SinirToleransi_Milimetrik_Kadastro_Cakismasi_Tolerans_Icinde_Isaretlenmeli()
    {
        // K1 [0, 10.004] ∩ K2 [10, 20] = 0.004 × 10 = 0.04 m².
        var b = Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 10.004m), D("K2", ParselRolu.Kadastro, 10m, 20m), D("A1", ParselRolu.Imar, 0m, 20m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroCakismasi, ["K1", "K2"], 0.04m, SinirToleransiIcinde: true));
    }

    [Fact]
    public void SinirToleransi_Uzun_Ince_Serit_Tasma_Alan_Esigini_Assa_Da_Sinir_Seridi_Sayilmali()
    {
        // 100 m uzunluğundaki ortak kenar boyunca 2 mm taşma: 0.002 × 100 = 0.20 m² > 0.05 m², ama şerit 1 cm'den dar.
        var b = Denetci.Denetle([
            D("K1", ParselRolu.Kadastro, 0m, 20m, 0m, 100m),
            D("101/1", ParselRolu.Imar, 0m, 12m, 0m, 100m), D("101/2", ParselRolu.Imar, 12m, 20.002m, 0m, 100m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["101/2", "K1"], 0.20m, SinirToleransiIcinde: true));
    }

    [Fact]
    public void SinirToleransi_Genis_Tasma_Tolerans_Disinda_Kalmali()
    {
        // 2 cm × 10 m = 0.20 m²: alan eşiği ve 1 cm şerit genişliği aşılır → onay engeli (Critical) korunur.
        var b = Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 20m), D("101/1", ParselRolu.Imar, 0m, 12m), D("101/2", ParselRolu.Imar, 12m, 20.02m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["101/2", "K1"], 0.20m, SinirToleransiIcinde: false));
    }

    [Fact]
    public void SinirToleransi_Tasma_Parsel_Bazinda_Ayri_Ayri_Raporlanmali()
    {
        // K1 X ∈ [0, 10]; 101/1 kuzeye 0.002 m (12 × 0.002 = 0.024 → 0.02 m²), 101/2 kuzeye 1 m (8 × 1 = 8 m²) taşar.
        var b = Denetci.Denetle([
            D("K1", ParselRolu.Kadastro, 0m, 20m),
            D("101/1", ParselRolu.Imar, 0m, 12m, 0m, 10.002m), D("101/2", ParselRolu.Imar, 12m, 20m, 0m, 11m)]);

        b.Should().BeEquivalentTo(
            [
                new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["101/1", "K1"], 0.02m, SinirToleransiIcinde: true),
                new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["101/2", "K1"], 8.00m, SinirToleransiIcinde: false),
            ],
            o => o.WithStrictOrdering());
    }

    [Fact]
    public void ImarYolu_Farkli_Imar_Adalari_Arasindaki_Karsilanmayan_Alan_Bosluk_Degil_Imar_Yolu_Sayilmali()
    {
        // 101 ve 102 adaları arasında 6 m × 10 m = 60.00 m² çizilmemiş imar yolu.
        var b = Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 30m), D("101/1", ParselRolu.Imar, 0m, 12m), D("102/1", ParselRolu.Imar, 18m, 30m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.ImarYoluAlani, ["101/1", "102/1", "K1"], 60.00m));
    }

    [Fact]
    public void ImarYolu_Ayni_Ada_Parselleri_Arasindaki_Genis_Bosluk_Kritik_Bosluk_Kalmali()
    {
        var b = Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 20m), D("101/1", ParselRolu.Imar, 0m, 12m), D("101/2", ParselRolu.Imar, 13m, 20m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanKapsamaBoslugu, ["101/1", "101/2", "K1"], 10.00m));
    }

    [Fact]
    public void ImarYolu_Ada_Kodu_Cozulemeyen_Kimliklerde_Bosluk_Kritik_Kalmali()
    {
        // "A1" / "B1" ada/parsel biçiminde değildir; imar adası tahmin edilmez → boşluk engel olarak kalır (fail-safe).
        var b = Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 30m), D("A1", ParselRolu.Imar, 0m, 12m), D("B1", ParselRolu.Imar, 18m, 30m)]);

        b.Should().ContainSingle().Which.Tur.Should().Be(TopolojiBulguTuru.OlusanKapsamaBoslugu);
    }

    [Fact]
    public void ImarYolu_Bosluk_Bilesenleri_Ayri_Raporlanmali_Milimetrik_Kiymik_Tolerans_Icinde_Kalmali()
    {
        // 101/102 arasında 6 × 10 = 60 m² yol + 102/1 ile 102/2 arasında 3 mm × 10 m = 0.03 m² kıymık (aynı ada).
        var b = Denetci.Denetle([
            D("K1", ParselRolu.Kadastro, 0m, 40m), D("101/1", ParselRolu.Imar, 0m, 12m),
            D("102/1", ParselRolu.Imar, 18m, 29.997m), D("102/2", ParselRolu.Imar, 30m, 40m)]);

        b.Should().BeEquivalentTo(
            [
                new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanKapsamaBoslugu, ["102/1", "102/2", "K1"], 0.03m, SinirToleransiIcinde: true),
                new ParselTopolojiBulgusu(TopolojiBulguTuru.ImarYoluAlani, ["101/1", "102/1", "K1"], 60.00m),
            ],
            o => o.WithStrictOrdering());
    }

    [Fact]
    public void ImarYolu_Cizilmis_Yol_Terk_Parseli_Ada_Kodu_Sayilmamali()
    {
        // Yol terk (102/Y1) ada kodu sayılmaz; 101/1 ile yol arasındaki boşluk tek imar adasına komşudur → kritik boşluk.
        var b = Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 20m), D("101/1", ParselRolu.Imar, 0m, 12m), D("102/Y1", ParselRolu.YolTerk, 13m, 20m)]);

        b.Should().ContainSingle().Which.Tur.Should().Be(TopolojiBulguTuru.OlusanKapsamaBoslugu);
    }

    [Theory]
    [InlineData("101/1", "101")]
    [InlineData("101/1~2", "101")]
    [InlineData("901/A1", "901")]
    [InlineData(" 102 / 5 ", "102")]
    [InlineData("1/2/3", null)]
    [InlineData("A1", null)]
    [InlineData("/5", null)]
    [InlineData("", null)]
    public void ImarYolu_Ada_Kodu_Kimlikten_Cozulmeli(string kimlik, string? beklenen)
    {
        ParselTopolojisi.ImarAdasi(kimlik).Should().Be(beklenen);
    }
}

using FluentAssertions;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Topoloji;
using Xunit;

namespace Imar18.GeometryEngine.Tests.Topoloji;

/// <summary>
/// Antigravity Bağımsız Denetim ve Sınır Testleri (Faz P7.0b — SAHA-004 Geometri / Topoloji).
/// src/ üretim kodlarına dokunulmadan bağımsız sınır, negatif ve fail-safe senaryolarını doğrular:
/// 1. Dar şerit alan tavanı tam 0.50 m² (kabul) ve 0.51 m² (kritik) geçişi.
/// 2. 1 cm şerit genişliği aşımında (12 mm × 10 m = 0.12 m²) kritik engel korunumu.
/// 3. Çok sayıda eşik altı (< 0.01 m²) kıymığın toplamı 0.05 m²'yi aşarsa kritik yükseltme.
/// 4. KamuAlani (PARK) parseline değen adalar arası boşluğun imar yolu sayılmaması (Reviewer H1).
/// 5. Tek imar adası dışındaki çizilmemiş yol alanının kritik boşluk kalması.
/// 6. Üç farklı imar adası arasındaki T-kavşaklı imar yolunun tek bileşen olarak raporlanması.
/// 7. ParselTopolojisi.ImarAdasi kenar / geçersiz kimlik koruması.
/// </summary>
public sealed class AntigravityIndependentSaha004GeometryAuditTests
{
    private const decimal X0 = 4_420_000m;
    private const decimal Y0 = 510_000m;
    private static readonly NtsParselTopolojiDenetcisi Denetci = new();

    private static ParselGeometrisi D(string id, ParselRolu rol, decimal y1, decimal y2, decimal x1 = 0m, decimal x2 = 10m) => new(
        id,
        rol,
        [
            new GeodeticPoint(X0 + x1, Y0 + y1),
            new GeodeticPoint(X0 + x1, Y0 + y2),
            new GeodeticPoint(X0 + x2, Y0 + y2),
            new GeodeticPoint(X0 + x2, Y0 + y1),
        ]);

    [Theory]
    [InlineData(20.005, 0.50, true)]
    [InlineData(20.0051, 0.51, false)]
    public void SinirToleransi_Dar_Serit_Alan_Ust_Siniri_Tam_0_50_Kabul_0_51_Kritik_Olmali(
        double tasanY,
        double beklenenAlan,
        bool beklenenTolerans)
    {
        // 100 m uzunluğunda kenar boyunca 5.0 mm (0.50 m²) vs 5.1 mm (0.51 m²) taşma; her ikisi de < 1 cm dar şerit.
        var bulgular = Denetci.Denetle([
            D("K1", ParselRolu.Kadastro, 0m, 20m, 0m, 100m),
            D("101/1", ParselRolu.Imar, 0m, 12m, 0m, 100m),
            D("101/2", ParselRolu.Imar, 12m, (decimal)tasanY, 0m, 100m),
        ]);

        bulgular.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(
                TopolojiBulguTuru.KadastroDisinaTasma,
                ["101/2", "K1"],
                (decimal)beklenenAlan,
                SinirToleransiIcinde: beklenenTolerans));
    }

    [Fact]
    public void SinirToleransi_Serit_Genisligi_12mm_Ve_Alan_0_12m2_Kritik_Kalmali()
    {
        // 1.2 cm × 10 m = 0.12 m²: hem alan eşiği (0.05 m²) hem dar şerit genişliği (1.0 cm) aşılır -> onay engeli.
        var bulgular = Denetci.Denetle([
            D("K1", ParselRolu.Kadastro, 0m, 20m, 0m, 10m),
            D("101/1", ParselRolu.Imar, 0m, 12.012m, 0m, 10m),
            D("101/2", ParselRolu.Imar, 12m, 20m, 0m, 10m),
        ]);

        bulgular.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(
                TopolojiBulguTuru.OlusanCakismasi,
                ["101/1", "101/2"],
                0.12m,
                SinirToleransiIcinde: false));
    }

    [Fact]
    public void SinirToleransi_Cok_Sayida_Esik_Alti_Kiymik_Toplami_0_05_Asarsa_Kritik_Olmali()
    {
        // 12 ardışık çakışma; her biri 0.00049 m × 10 m = 0.0049 m² (< 0.01 m² tekil eşik).
        // Toplam = 12 × 0.0049 = 0.0588 -> 0.06 m² (> 0.05 m² sınır toleransı -> SinirToleransiIcinde = false).
        var parseller = Enumerable.Range(0, 13)
            .Select(i => D($"201/{i + 1:00}", ParselRolu.Imar, 2m * i, (2m * i) + 2.00049m))
            .ToArray();

        var bulgular = Denetci.Denetle(parseller);

        var bulgu = bulgular.Should().ContainSingle().Subject;
        bulgu.Tur.Should().Be(TopolojiBulguTuru.OlusanCakismasi);
        bulgu.AlanM2.Should().Be(0.06m);
        bulgu.SinirToleransiIcinde.Should().BeFalse("eşik altı kıymıkların toplamı 0.05 m²'yi aşınca kritik engel olmalıdır");
        bulgu.ParselIdleri.Should().HaveCount(13);
    }

    [Fact]
    public void ImarYolu_KamuAlani_Parseline_Degen_Adalar_Arasi_Bosluk_Kritik_Kalmali()
    {
        // Reviewer H1 (KamuAlani dalı): 101/1 ve 102/1 imar parselleri arasında boşluk var, ancak PARK (KamuAlani) bu boşluğa değiyor.
        // Çizili kamu/yol parseline değen boşluk çizilmemiş imar yolu sayılamaz -> OlusanKapsamaBoslugu (Critical).
        var bulgular = Denetci.Denetle([
            D("K1", ParselRolu.Kadastro, 0m, 40m, 0m, 10m),
            D("101/1", ParselRolu.Imar, 0m, 12m, 0m, 10m),
            D("903/PARK", ParselRolu.KamuAlani, 12m, 18m, 0m, 5m),
            D("102/1", ParselRolu.Imar, 18m, 40m, 0m, 10m),
        ]);

        bulgular.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(
                TopolojiBulguTuru.OlusanKapsamaBoslugu,
                ["101/1", "102/1", "903/PARK", "K1"],
                30.00m,
                SinirToleransiIcinde: false));
    }

    [Fact]
    public void ImarYolu_Tek_Imar_Adasi_Disindaki_Bosluk_Kritik_Kalmali()
    {
        // Yalnızca 101 adası var; doğu tarafındaki 8 m × 10 m = 80 m² çizilmemiş alan tek adaya komşu -> kritik boşluk.
        var bulgular = Denetci.Denetle([
            D("K1", ParselRolu.Kadastro, 0m, 20m, 0m, 10m),
            D("101/1", ParselRolu.Imar, 0m, 6m, 0m, 10m),
            D("101/2", ParselRolu.Imar, 6m, 12m, 0m, 10m),
        ]);

        bulgular.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(
                TopolojiBulguTuru.OlusanKapsamaBoslugu,
                ["101/2", "K1"],
                80.00m,
                SinirToleransiIcinde: false));
    }

    [Fact]
    public void ImarYolu_Uc_Farkli_Imar_Adasi_Arasindaki_Yol_Tek_Bilesen_Ve_Dogru_Alanla_Raporlanmali()
    {
        // Kadastro K1: Y ∈ [0, 30], X ∈ [0, 30] = 900 m².
        // 101/1: Y ∈ [0, 10], X ∈ [0, 30] = 300 m²
        // 102/1: Y ∈ [16, 30], X ∈ [0, 12] = 168 m²
        // 103/1: Y ∈ [16, 30], X ∈ [18, 30] = 168 m²
        // T-şekilli çizilmemiş imar yolu alanı = 900 - (300 + 168 + 168) = 264.00 m².
        var bulgular = Denetci.Denetle([
            D("K1", ParselRolu.Kadastro, 0m, 30m, 0m, 30m),
            D("101/1", ParselRolu.Imar, 0m, 10m, 0m, 30m),
            D("102/1", ParselRolu.Imar, 16m, 30m, 0m, 12m),
            D("103/1", ParselRolu.Imar, 16m, 30m, 18m, 30m),
        ]);

        bulgular.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(
                TopolojiBulguTuru.ImarYoluAlani,
                ["101/1", "102/1", "103/1", "K1"],
                264.00m,
                SinirToleransiIcinde: false));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("/", null)]
    [InlineData("101/", null)]
    [InlineData("/1", null)]
    [InlineData("  /1", null)]
    [InlineData("901/1/C1", null)]
    [InlineData("101/1/2", null)]
    [InlineData("101/1", "101")]
    public void ParselTopolojisi_ImarAdasi_Kenar_Durumlarinda_Tahmin_Yapmamali(string? kimlik, string? beklenen)
    {
        ParselTopolojisi.ImarAdasi(kimlik).Should().Be(beklenen);
    }
}

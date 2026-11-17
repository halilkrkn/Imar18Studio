using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Subdivision;
using Imar18.GeometryEngine.Topoloji;
using Xunit;

namespace Imar18.GeometryEngine.Tests.Topoloji;

/// <summary>
/// Antigravity Bağımsız Denetim Testleri (Faz P6 — Topoloji ve Bisection SORU-023).
/// BisectionEngine konkav ada taraması (SORU-023) ile NtsParselTopolojiDenetcisi çakışma,
/// boşluk, taşma (ERR-TOPO-005..008) ve fail-safe analiz mekanizmalarını bağımsız olarak doğrular.
/// </summary>
public sealed class AntigravityIndependentTopologyAuditTests
{
    private const decimal X0 = 4_400_000m;
    private const decimal Y0 = 500_000m;
    private static readonly NtsParselTopolojiDenetcisi Denetci = new();

    private static GeodeticPoint P(decimal x, decimal y) => new(X0 + x, Y0 + y);

    private static PolygonRing Halka(params (decimal X, decimal Y)[] koseler) =>
        PolygonRing.FromOpen(koseler.Select(k => P(k.X, k.Y)).ToArray()).Value;

    private static ParselGeometrisi Dikdortgen(string id, ParselRolu rol, decimal y1, decimal y2, decimal x1 = 0m, decimal x2 = 10m) => new(
        id, rol,
        [new GeodeticPoint(X0 + x1, Y0 + y1), new GeodeticPoint(X0 + x1, Y0 + y2),
         new GeodeticPoint(X0 + x2, Y0 + y2), new GeodeticPoint(X0 + x2, Y0 + y1)]);

    /// <summary>
    /// U-Ada geometrisi: 100m x 40m dış zarf, ortasında 80m x 20m iç oyuk.
    /// Alt kol: X in [0, 100], Y in [0, 10]
    /// Arka gövde: X in [0, 20], Y in [10, 30]
    /// Üst kol: X in [0, 100], Y in [30, 40]
    /// Cephe 7: V7 (100,0) -> V0 (0,0) kenarı.
    /// </summary>
    private static PolygonRing UAda() => Halka(
        (0m, 0m), (0m, 40m), (100m, 40m), (100m, 30m),
        (20m, 30m), (20m, 10m), (100m, 10m), (100m, 0m));

    [Fact]
    public void Bisection_SORU_023_Ilk_Acida_MultiPolygon_Olsa_Bile_Gecerli_Aciyi_Kabul_Etmeli()
    {
        // Cephe 7: (100,0)->(0,0). Hedef: 20 m2.
        // 90 derecede X >= 98.5 iki kolu ayırıp MultiPolygon üretir.
        // 85 derecede hedef alanına ulaşılamaz.
        // 95 derecede tek parçalı kesim kabul edilir (SORU-023).
        var secenek = new BisectionOptions(
            PreferredAngleDeg: 90m,
            MinAngleDeg: 75m,
            MaxAngleDeg: 105m,
            AngleStepDeg: 5m,
            ConvergenceToleranceM2: 0.0001m,
            PostQuantizationAreaToleranceM2: 0.01m,
            MaxInnerIterations: 60);

        var istek = new BisectionRequest(UAda(), 7, 20m, secenek);
        var sonuc = BisectionEngine.Cut(istek);

        sonuc.Result.IsSuccess.Should().BeTrue();
        sonuc.Result.Value.AngleDeg.Should().Be(95m, "90 derecede MultiPolygon oluşsa da 95 derecedeki tek parçalı çözüm kabul edilmeli");
        sonuc.Attempts.Should().Contain(a => a.AngleDeg == 90m && a.Outcome == AttemptOutcome.MultiPolygon);
        sonuc.Attempts.Should().Contain(a => a.AngleDeg == 95m && a.Outcome == AttemptOutcome.Accepted);
    }

    [Fact]
    public void Bisection_Hicbir_Acida_Cozum_Yoksa_Ve_MultiPolygon_Varsa_ERR_TOPO_004_Donmeli()
    {
        // 900 m2 hedef: 75-105 aralığında her açıda en az bir kolda MultiPolygon veya erişilemezlik oluşur.
        // Motor durmayıp tüm aralığı taramalı ve en üst öncelikli hata olarak ERR-TOPO-004 döndürmelidir.
        var secenek = new BisectionOptions(
            PreferredAngleDeg: 90m,
            MinAngleDeg: 75m,
            MaxAngleDeg: 105m,
            AngleStepDeg: 5m,
            ConvergenceToleranceM2: 0.0001m,
            PostQuantizationAreaToleranceM2: 0.01m,
            MaxInnerIterations: 60);

        var istek = new BisectionRequest(UAda(), 7, 900m, secenek);
        var sonuc = BisectionEngine.Cut(istek);

        sonuc.Result.IsFailure.Should().BeTrue();
        sonuc.Result.Error.Code.Should().Be(ErrorCodes.TopoKonkavKesimCokParcali);
        sonuc.Result.Error.Severity.Should().Be(ErrorSeverity.Critical);
        sonuc.Result.Error.RequiresEngineerReview.Should().BeTrue();
        sonuc.Attempts.Count.Should().Be(7, "75, 80, 85, 90, 95, 100, 105 olmak üzere 7 açının tümü taranmalı");
    }

    [Fact]
    public void Topoloji_Cakisma_Bosluk_Ve_Tasma_Tam_Bagimsiz_Dogrulamasi()
    {
        // K1 [0, 20] (200 m2) kadastro parseli
        // A1 [0, 11] (110 m2) imar parseli
        // A2 [10, 22] (120 m2) imar parseli:
        // A1 ve A2 arasında [10, 11] çakışması = 1m x 10m = 10.00 m2 -> ERR-TOPO-005 (OlusanCakismasi)
        // A2 kadastro dışına [20, 22] taşması = 2m x 10m = 20.00 m2 -> ERR-TOPO-007 (KadastroDisinaTasma)
        var parseller = new[]
        {
            Dikdortgen("K1", ParselRolu.Kadastro, 0m, 20m),
            Dikdortgen("A1", ParselRolu.Imar, 0m, 11m),
            Dikdortgen("A2", ParselRolu.Imar, 10m, 22m),
        };

        var bulgular = Denetci.Denetle(parseller);

        bulgular.Should().Contain(b => b.Tur == TopolojiBulguTuru.OlusanCakismasi && b.AlanM2 == 10.00m);
        bulgular.Should().Contain(b => b.Tur == TopolojiBulguTuru.KadastroDisinaTasma && b.AlanM2 == 20.00m);
    }

    [Fact]
    public void Topoloji_Kadastro_Serit_Boslugu_10cm_Alti_Mitre_Kapanisiyla_Yakalanmali()
    {
        // İki kadastro parseli arasında 0.08 m (8 cm < 10 cm) aralık
        // K1 [0, 10], K2 [10.08, 20] -> 0.08m x 10m = 0.80 m2 boşluk
        var parseller = new[]
        {
            Dikdortgen("K1", ParselRolu.Kadastro, 0m, 10m),
            Dikdortgen("K2", ParselRolu.Kadastro, 10.08m, 20m),
        };

        var bulgular = Denetci.Denetle(parseller);

        bulgular.Should().ContainSingle(b => b.Tur == TopolojiBulguTuru.KadastroBoslugu && b.AlanM2 == 0.80m);
    }

    [Fact]
    public void Topoloji_Gecersiz_Halka_Veya_Hata_Durumunda_AnalizYapilamadi_Donmeli()
    {
        // 2 köşeli geçersiz geometri verildiğinde çökmeyip AnalizYapilamadi bulgusu dönmeli
        var gecersiz = new ParselGeometrisi("G1", ParselRolu.Kadastro, [P(0m, 0m), P(10m, 0m)]);
        var gecerli = Dikdortgen("K1", ParselRolu.Kadastro, 0m, 20m);

        var bulgular = Denetci.Denetle([gecerli, gecersiz]);

        bulgular.Should().ContainSingle(b => b.Tur == TopolojiBulguTuru.AnalizYapilamadi && b.ParselIdleri.Contains("G1"));
    }
}

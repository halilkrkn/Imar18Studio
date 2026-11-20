using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Subdivision;
using Xunit;

namespace Imar18.GeometryEngine.Tests;

/// <summary>
/// SAHA-015: kroki blok bölücü (taslak bölge + kesin alanlı paralel kesim). Tüm geometri sentetiktir (X = Kuzey, Y = Doğu; yerel
/// orijin X0/Y0). Beklenen alanlar elle hesaplanır: 22 m × 100 m dikdörtgen blok (2200 m²); eğik çizgi (Y 30 → 36) ile kesilen
/// yamuk = 22 × (30 + 36) / 2 = 726 m².
/// </summary>
public sealed class Saha015KrokiBlokBolucuTests
{
    private const decimal X0 = 4_400_000m;
    private const decimal Y0 = 500_000m;

    private static readonly KrokiBlokBolucu Bolucu = new();

    private static GeodeticPoint P(decimal x, decimal y) => new(X: X0 + x, Y: Y0 + y);

    private static BolmeDogrusu D(decimal x1, decimal y1, decimal x2, decimal y2) => new(P(x1, y1), P(x2, y2));

    private static IReadOnlyList<GeodeticPoint> Dikdortgen(decimal kuzey = 22m, decimal dogu = 100m) => [P(0, 0), P(0, dogu), P(kuzey, dogu), P(kuzey, 0)];

    private static decimal Alan(IReadOnlyList<GeodeticPoint> k) => GaussAreaCalculator.Compute(PolygonRing.FromOpen(k).Value).AreaM2Exact;

    private static readonly BolmeDogrusu EgikA = D(0, 30, 22, 36);
    private static readonly BolmeDogrusu EgikB = D(0, 65, 22, 71);

    [Fact]
    public void KrokiBlokBolucu_TaslakBol_Egik_Iki_Cizgi_Sirasiz_Verilse_De_Batidan_Doguya_Siralanip_Uc_Bolge_Uretmeli()
    {
        var s = Bolucu.TaslakBol(Dikdortgen(), [EgikB, EgikA]);

        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.TechnicalMessage : string.Empty);
        s.Value.SiraliCizgiler.Should().Equal(EgikA, EgikB);
        s.Value.Bolgeler.Select(Alan).Should().Equal(726m, 770m, 704m);
    }

    [Fact]
    public void KrokiBlokBolucu_TaslakBol_Kisa_Kalan_Ve_Tasan_Cizgileri_Sonsuz_Dogru_Boyunca_Uzatmali()
    {
        var s = Bolucu.TaslakBol(Dikdortgen(), [D(3, 50, 19, 50), D(-5, 40, 30, 40)]).Value;

        // Y = 40 (taşan) ve Y = 50 (kısa) tam blok genişliğinde keser.
        s.Bolgeler.Select(Alan).Should().Equal(880m, 220m, 1100m);
    }

    [Fact]
    public void KrokiBlokBolucu_TaslakBol_Blok_Kosesinden_Gecen_Cizgide_Kose_Iki_Bolgede_De_Kalmali()
    {
        var s = Bolucu.TaslakBol(Dikdortgen(22m, 22m), [D(0, 0, 22, 22)]).Value;

        s.Bolgeler.Select(Alan).Should().Equal(242m, 242m);
        s.Bolgeler[0].Should().Contain(P(0, 0)).And.Contain(P(22, 22));
        s.Bolgeler[1].Should().Contain(P(0, 0)).And.Contain(P(22, 22));
    }

    [Fact]
    public void KrokiBlokBolucu_KesinBol_Egik_Cizgilere_Paralel_Hedef_Alanlari_0_001_m2_Icinde_Vermeli_Koseler_Paylasilmali()
    {
        var s = Bolucu.KesinBol(Dikdortgen(), [EgikA, EgikB], [880.00m, 660.00m, 660.00m]);

        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.TechnicalMessage : string.Empty);
        var p = s.Value;
        p.Should().HaveCount(3);
        Math.Abs(Alan(p[0]) - 880m).Should().BeLessThanOrEqualTo(0.001m);
        Math.Abs(Alan(p[1]) - 660m).Should().BeLessThanOrEqualTo(0.001m);
        Math.Abs(p.Sum(Alan) - 2200m).Should().BeLessThanOrEqualTo(0.0001m, "parçalar bloğu boşluksuz ve çakışmasız döşer");
        p[0].Intersect(p[1]).Should().HaveCount(2, "kesim doğrusunun iki ucu iki parçada aynı köşedir");
        p.SelectMany(k => k).Should().OnlyContain(k => decimal.Round(k.X, 6) == k.X && decimal.Round(k.Y, 6) == k.Y);

        // Kesim kenarı kroki çizgisine paraleldir: (ΔY, ΔX) ∥ (6, 22).
        var ortak = p[0].Intersect(p[1]).OrderBy(k => k.X).ToArray();
        var capraz = ((ortak[1].Y - ortak[0].Y) * 22m) - ((ortak[1].X - ortak[0].X) * 6m);
        Math.Abs(capraz).Should().BeLessThan(0.0001m);
    }

    [Fact]
    public void KrokiBlokBolucu_KesinBol_Dort_Cizgi_Bes_Esit_Hedef_Kesin_Alanlar_Uretmeli()
    {
        BolmeDogrusu[] cizgiler = [D(0, 18, 22, 22), D(1, 39, 21, 41), D(0, 58, 22, 62), D(0, 79, 23, 81)];

        var p = Bolucu.KesinBol(Dikdortgen(), cizgiler, [440m, 440m, 440m, 440m, 440m]).Value;

        p.Should().HaveCount(5);
        p.Select(Alan).Should().OnlyContain(a => Math.Abs(a - 440m) <= 0.001m);
    }

    [Fact]
    public void KrokiBlokBolucu_KesinBol_Konkav_Blokta_Kesim_Cok_Parcaliysa_ERR_TOPO_004_Donmeli()
    {
        // C biçimli blok (doğuya açık): Y = 20'deki Kuzey–Güney kesimi iki kolu ayrı ayrı keser.
        IReadOnlyList<GeodeticPoint> c = [P(0, 0), P(0, 30), P(10, 30), P(10, 10), P(20, 10), P(20, 30), P(30, 30), P(30, 0)];

        var s = Bolucu.KesinBol(c, [D(0, 20, 30, 20)], [300m, Alan(c) - 300m]);

        s.IsFailure.Should().BeTrue();
        s.Error.Code.Should().Be(ErrorCodes.TopoKonkavKesimCokParcali);
    }

    [Fact]
    public void KrokiBlokBolucu_TaslakBol_Birbirine_Paralel_Olmayan_Cizgileri_Reddetmeli()
    {
        var s = Bolucu.TaslakBol(Dikdortgen(), [D(0, 30, 22, 30), D(11, 0, 11, 100)]);

        s.Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz);
    }

    [Fact]
    public void KrokiBlokBolucu_TaslakBol_Bloku_Kesmeyen_Cizgiyi_Reddetmeli()
    {
        var s = Bolucu.TaslakBol(Dikdortgen(), [D(0, 150, 22, 150)]);

        s.Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz);
    }

    [Fact]
    public void KrokiBlokBolucu_Gecersiz_Girdiler_ERR_PLAN_003_Donmeli_Istisna_Atmamali()
    {
        Bolucu.TaslakBol(Dikdortgen(), []).Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz);
        Bolucu.TaslakBol(Dikdortgen(), [D(5, 5, 5, 5)]).Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz);
        Bolucu.TaslakBol([P(0, 0), P(0, 1)], [EgikA]).IsFailure.Should().BeTrue();
        Bolucu.KesinBol(Dikdortgen(), [EgikA], [880m]).Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz, "hedef sayısı çizgi + 1 olmalı");
        Bolucu.KesinBol(Dikdortgen(), [EgikA], [2200m, 0m]).Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz);
        Bolucu.KesinBol(Dikdortgen(), [EgikA], [-1m, 2201m]).Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz);
        Bolucu.KesinBol(Dikdortgen(), [EgikA], [880m, 1000m]).Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz,
            "Σ hedef blok alanına (0.01 m² içinde) eşit olmalı");
    }

    [Fact]
    public void KrokiBlokBolucu_Iptal_Edilince_OperationCanceledException_Atmali()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Bolucu.KesinBol(Dikdortgen(), [EgikA], [880m, 1320m], cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void KrokiBlokBolucu_TaslakBol_Blok_Icinde_Kesisen_Cizgileri_Reddetmeli()
    {
        // Doğrultular ±24° (paralellik eşiği içinde) ama kirişler X = 11'de kesişir (architect Y2).
        var s = Bolucu.TaslakBol(Dikdortgen(), [D(0, 30, 22, 40), D(0, 40, 22, 30)]);

        s.Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz);
        s.Error.TechnicalMessage.Should().Contain("cross each other");
    }

    [Fact]
    public void KrokiBlokBolucu_TaslakBol_Kisa_Cizim_Blok_Kirisine_Uzatilip_Komsu_Cizgiyi_Kesiyorsa_Reddetmeli()
    {
        // A: kısa çizim (X 0..2) blok kirişine uzatılınca Y 40 → 46 olur ve dik B (Y 42) ile blok içinde kesişir.
        // A' (Y 50 → 56) B ile kesişmez ve kiriş ortasına göre B'den sonra gelir.
        var a = D(0, 40, 2, 40.5454545m);
        var b = D(20, 42, 22, 42);

        var s = Bolucu.TaslakBol(Dikdortgen(), [a, b]);

        s.IsFailure.Should().BeTrue("A'nın kirişi (Y 40 → 46) B'yi (Y 42) blok içinde keser");
        Bolucu.TaslakBol(Dikdortgen(), [D(0, 50, 2, 50.5454545m), b]).Value.SiraliCizgiler.Should().Equal(b, D(0, 50, 2, 50.5454545m));
    }
}

using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Subdivision;
using Xunit;

namespace Imar18.GeometryEngine.Tests;

/// <summary>
/// P2.3 — Iki donguli BisectionEngine (tek kesim), 1 mm kuantizasyon, Decimal Gauss yeniden dogrulama ve
/// konkav adada MultiPolygon guvenli durusu (spec 4.2, TC-GEOM-001, AC-PILOT-01).
/// Koordinatlar yerel (X=Kuzey, Y=Dogu) + O = (4500000, 500000). Beklenen kesim konumlari ve alanlar elle
/// (dikdortgen/yamuk/ucgen alan formulleri) hesaplanmistir; B5 ve B8 ayrica bagimsiz Python kirpma hesabiyla dogrulandi.
/// Toleranslar ve aci araliklari TEST GIRDISIDIR, mevzuat degeri degildir.
/// </summary>
public sealed class BisectionEngineTests
{
    private const decimal Ox = 4500000.000m;
    private const decimal Oy = 500000.000m;

    private static GeodeticPoint P(decimal x, decimal y) => new(X: Ox + x, Y: Oy + y);

    private static PolygonRing Halka(params (decimal X, decimal Y)[] yerel)
    {
        var sonuc = PolygonRing.FromOpen(yerel.Select(p => P(p.X, p.Y)).ToList());
        sonuc.IsSuccess.Should().BeTrue();
        return sonuc.Value;
    }

    private static PolygonRing Dikdortgen() => Halka((0, 0), (0, 40), (30, 40), (30, 0));      // G3, 1200 m2
    private static PolygonRing Yamuk() => Halka((0, 0), (0, 40), (30, 30), (30, 10));           // G5, 900 m2
    private static PolygonRing LAda() => Halka((0, 0), (0, 40), (10, 40), (10, 10), (30, 10), (30, 0)); // G6, 600 m2
    private static PolygonRing UAda() => Halka((0, 0), (0, 50), (100, 50), (100, 40), (10, 40), (10, 10), (100, 10), (100, 0)); // G7, 2300
    private static PolygonRing Paralelkenar() => Halka((0, 0), (0, 40), (30, 70), (30, 30));    // 1200 m2

    private static BisectionOptions Secenek(
        decimal tercih = 90m, decimal? min = null, decimal? max = null, decimal adim = 5m,
        decimal yakinsama = 0.0001m, decimal kuantTol = 0.01m, int iterasyon = 60) =>
        new(tercih, min ?? tercih, max ?? tercih, adim, yakinsama, kuantTol, iterasyon);

    private static BisectionCut Basarili(BisectionOutcome sonuc)
    {
        sonuc.Result.IsSuccess.Should().BeTrue(sonuc.Result.IsFailure ? sonuc.Result.Error.Code : "");
        return sonuc.Result.Value;
    }

    private static decimal Alan(PolygonRing r) => GaussAreaCalculator.Compute(r).AreaM2Exact;

    [Fact]
    public void Bisection_Dikdortgende_90_Derecede_Hedef_450_Icin_Kesim_Y15_Olmali()
    {
        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(Dikdortgen(), 0, 450m, Secenek())));

        kesim.CutStart.Should().Be(P(0, 15));
        kesim.CutEnd.Should().Be(P(30, 15));
        kesim.PieceArea.AreaM2Exact.Should().Be(450m);
        kesim.RemainderArea.AreaM2Exact.Should().Be(750m);
        kesim.AngleDeg.Should().Be(90m);
        kesim.InnerIterations.Should().Be(3);   // 20 -> 600, 10 -> 300, 15 -> 450
        kesim.FrontageLengthM.Should().Be(15m);
    }

    [Fact]
    public void Bisection_Dikdortgende_Hedef_100_Kuantizasyon_Sonrasi_Alan_Korunumu_Saglanmali()
    {
        // t = 10/3 -> 3.333 mm izgarasi: parca 99.990, kalan 1100.010, toplam 1200 tam.
        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(Dikdortgen(), 0, 100m, Secenek(kuantTol: 0.01m))));

        kesim.CutStart.Should().Be(P(0, 3.333m));
        kesim.PieceArea.AreaM2Exact.Should().Be(99.990m);
        kesim.RemainderArea.AreaM2Exact.Should().Be(1100.010m);
        (kesim.PieceArea.AreaM2Exact + kesim.RemainderArea.AreaM2Exact).Should().Be(1200m);
        kesim.AreaDeviationM2.Should().Be(-0.010m);
    }

    [Fact]
    public void Bisection_Kuantizasyon_Sapmasi_Toleransi_Asarsa_ERR_KUANT_001_Ve_Sonuc_Vermemeli()
    {
        var sonuc = BisectionEngine.Cut(new BisectionRequest(Dikdortgen(), 0, 100m, Secenek(kuantTol: 0.0099m)));

        sonuc.Result.IsFailure.Should().BeTrue();
        sonuc.Result.Error.Code.Should().Be(ErrorCodes.KuantAlanSapmasi);
        sonuc.Attempts.Should().ContainSingle().Which.Outcome.Should().Be(AttemptOutcome.QuantizationAreaDeviation);
    }

    [Theory]
    [InlineData("75.96375653207352", 11.25, 18.75)]   // atan(4): kesim dogu 1 / kuzey 4
    [InlineData("104.03624346792648", 18.75, 11.25)]
    public void Bisection_Egik_Aciyla_Kesimde_Elle_Hesaplanan_Konumu_Vermeli(string aci, double pY, double qY)
    {
        var alfa = decimal.Parse(aci, System.Globalization.CultureInfo.InvariantCulture);

        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(Dikdortgen(), 0, 450m, Secenek(tercih: alfa))));

        kesim.CutStart.Should().Be(P(0, (decimal)pY));
        kesim.CutEnd.Should().Be(P(30, (decimal)qY));
        kesim.PieceArea.AreaM2Exact.Should().Be(450m);
        kesim.AngleDeg.Should().Be(alfa);
    }

    [Fact]
    public void Bisection_Ters_Yonlu_Halkada_Cephe_Indeksiyle_Dogru_Tarafi_Kesmeli()
    {
        var ters = Halka((0, 0), (30, 0), (30, 40), (0, 40)); // saat yonu; cephe 3 = (0,40)->(0,0)

        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(ters, 3, 750m, Secenek())));

        kesim.CutStart.Should().Be(P(0, 15));
        kesim.PieceArea.AreaM2Exact.Should().Be(750m);
        kesim.RemainderArea.AreaM2Exact.Should().Be(450m);
        kesim.Piece.Vertices.Should().Contain(P(0, 40));
    }

    [Theory]
    [InlineData(450, 0, 20, 30, 20)]   // 150 (ucgen) + 300 -> Y = 20
    [InlineData(54, 0, 6, 18, 6)]      // 1.5 t^2 = 54 -> t = 6, Q X = 3t
    public void Bisection_Yamukta_Elle_Hesaplanan_Kesim_Konumunu_Vermeli(int hedef, double px, double py, double qx, double qy)
    {
        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(Yamuk(), 0, hedef, Secenek())));

        kesim.CutStart.Should().Be(P((decimal)px, (decimal)py));
        kesim.CutEnd.Should().Be(P((decimal)qx, (decimal)qy));
        kesim.PieceArea.AreaM2Exact.Should().Be(hedef);
    }

    [Fact]
    public void Bisection_Yamukta_Hedef_60_Kalan_Sapmasi_Toleransi_Asarsa_ERR_KUANT_001_Vermeli()
    {
        // t = sqrt(40): P (0, 6.325), Q (18.974, 6.325); parca 60.005275, kalan 839.989725 -> kalan sapmasi -0.010275
        var dar = BisectionEngine.Cut(new BisectionRequest(Yamuk(), 0, 60m, Secenek(kuantTol: 0.01m)));
        var genis = Basarili(BisectionEngine.Cut(new BisectionRequest(Yamuk(), 0, 60m, Secenek(kuantTol: 0.011m))));

        dar.Result.Error.Code.Should().Be(ErrorCodes.KuantAlanSapmasi);
        genis.CutStart.Should().Be(P(0, 6.325m));
        genis.CutEnd.Should().Be(P(18.974m, 6.325m));
        genis.PieceArea.AreaM2Exact.Should().Be(60.005275m);
        genis.RemainderArea.AreaM2Exact.Should().Be(839.989725m);
    }

    [Fact]
    public void Bisection_L_Adada_Konkav_Kosesi_Gecen_Tek_Parcali_Kesim_Yapmali()
    {
        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(LAda(), 0, 450m, Secenek())));

        kesim.CutStart.Should().Be(P(0, 25));
        kesim.CutEnd.Should().Be(P(10, 25));
        kesim.Piece.Vertices.Should().HaveCount(6);
        kesim.PieceArea.AreaM2Exact.Should().Be(450m);
        kesim.RemainderArea.AreaM2Exact.Should().Be(150m);
        kesim.InnerIterations.Should().Be(3);
    }

    [Fact]
    public void Bisection_L_Adada_Kucuk_Hedefte_Genis_Koldan_Kesmeli()
    {
        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(LAda(), 0, 150m, Secenek())));

        kesim.CutStart.Should().Be(P(0, 5));
        kesim.CutEnd.Should().Be(P(30, 5));
    }

    [Fact]
    public void Bisection_U_Adada_Cok_Parcali_Kesimde_ERR_TOPO_004_Ile_Guvenli_Durmali()
    {
        // Cephe 7: (100,0)->(0,0). X >= 55 yari duzlemi iki kolu ayri keser: MultiPolygon (2 x 450).
        var sonuc = BisectionEngine.Cut(new BisectionRequest(UAda(), 7, 900m, Secenek(min: 75m, max: 105m)));

        sonuc.Result.IsFailure.Should().BeTrue();
        sonuc.Result.Error.Code.Should().Be(ErrorCodes.TopoKonkavKesimCokParcali);
        sonuc.Result.Error.RequiresEngineerReview.Should().BeTrue();
        sonuc.Attempts.Should().HaveCount(1, "MultiPolygon ilk tespitte taramayi durdurur (SORU-023)");
        sonuc.Attempts[0].AngleDeg.Should().Be(90m);
        sonuc.Attempts[0].Outcome.Should().Be(AttemptOutcome.MultiPolygon);
    }

    [Fact]
    public void Bisection_Aci_Araliginda_Erisilemeyen_Hedefte_ERR_PLAN_005_Ve_Deterministik_Tarama_Sirasi_Vermeli()
    {
        // Paralelkenarda cepheden erisilebilen en buyuk alan 870.577 (75 derece) < 1000.
        var sonuc = BisectionEngine.Cut(new BisectionRequest(Paralelkenar(), 0, 1000m, Secenek(min: 75m, max: 105m, adim: 5m)));

        sonuc.Result.Error.Code.Should().Be(ErrorCodes.PlanHedefAciAraligindaErisilemez);
        sonuc.Attempts.Select(a => a.AngleDeg).Should().Equal(90m, 85m, 95m, 80m, 100m, 75m, 105m);
        sonuc.Attempts.Should().OnlyContain(a => a.Outcome == AttemptOutcome.TargetUnreachable);
    }

    [Fact]
    public void Bisection_Kesim_Mevcut_Koseden_Gecerse_Kose_Tekrari_Olmamali()
    {
        // Paralelkenar, hedef 450, 90 derece: kesim Y = 30 tam V3 (30,30) kosesinden gecer.
        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(Paralelkenar(), 0, 450m, Secenek())));

        kesim.CutEnd.Should().Be(P(30, 30));
        kesim.Piece.Vertices.Should().HaveCount(3);
        kesim.Remainder.Vertices.Should().HaveCount(4);
        kesim.Remainder.Vertices.Should().OnlyHaveUniqueItems();
        kesim.PieceArea.AreaM2Exact.Should().Be(450m);
    }

    [Fact]
    public void Bisection_Iterasyon_Siniri_Asilirsa_ERR_PLAN_004_Vermeli()
    {
        var sonuc = BisectionEngine.Cut(new BisectionRequest(Dikdortgen(), 0, 100m, Secenek(iterasyon: 2)));

        sonuc.Result.Error.Code.Should().Be(ErrorCodes.PlanHedefAlanaYakinsanamadi);
        sonuc.Attempts.Single().InnerIterations.Should().Be(2);
    }

    public static TheoryData<string, BisectionRequest> GecersizIstekler()
    {
        var r = Dikdortgen();
        return new()
        {
            { "hedef sifir", new(r, 0, 0m, Secenek()) },
            { "hedef ada alanina esit", new(r, 0, 1200m, Secenek()) },
            { "cephe indeksi negatif", new(r, -1, 450m, Secenek()) },
            { "cephe indeksi tasiyor", new(r, 4, 450m, Secenek()) },
            { "min aci 75 alti", new(r, 0, 450m, Secenek(min: 74m, max: 105m)) },
            { "max aci 105 ustu", new(r, 0, 450m, Secenek(min: 75m, max: 106m)) },
            { "tercih aralik disi", new(r, 0, 450m, new BisectionOptions(100m, 75m, 95m, 5m, 0.0001m, 0.01m, 60)) },
            { "adim sifir", new(r, 0, 450m, Secenek(adim: 0m)) },
            { "yakinsama toleransi sifir", new(r, 0, 450m, Secenek(yakinsama: 0m)) },
            { "yakinsama > kuantizasyon toleransi", new(r, 0, 450m, Secenek(yakinsama: 0.02m, kuantTol: 0.01m)) },
            { "iterasyon sifir", new(r, 0, 450m, Secenek(iterasyon: 0)) },
        };
    }

    [Theory]
    [MemberData(nameof(GecersizIstekler))]
    public void Bisection_Gecersiz_Girdide_ERR_PLAN_003_Vermeli(string ad, BisectionRequest istek)
    {
        var sonuc = BisectionEngine.Cut(istek);

        sonuc.Result.Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz, ad);
        sonuc.Attempts.Should().BeEmpty();
    }

    [Fact]
    public void Bisection_Izgara_Disi_Ada_Kosesinde_ERR_PLAN_003_Vermeli()
    {
        var izgaraDisi = Halka((0, 0), (0, 40.0005m), (30, 40), (30, 0));

        BisectionEngine.Cut(new BisectionRequest(izgaraDisi, 0, 450m, Secenek())).Result.Error.Code
            .Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz);
    }

    [Fact]
    public void Bisection_Kendi_Uzerinde_Kesisen_Adada_ERR_TOPO_001_Vermeli()
    {
        var papyon = Halka((0, 0), (0, 40), (30, 10), (30, 40));

        BisectionEngine.Cut(new BisectionRequest(papyon, 0, 100m, Secenek())).Result.Error.Code
            .Should().Be(ErrorCodes.TopoKendiUzerindeKesisim);
    }

    [Fact]
    public void Bisection_Null_Istek_Reddedilmeli()
    {
        var act = () => BisectionEngine.Cut(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Bisection_Iptal_Edilmis_Jetonda_OperationCanceledException_Atmali()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => BisectionEngine.Cut(new BisectionRequest(Dikdortgen(), 0, 450m, Secenek()), cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Bisection_Basarili_Kesimde_Degismezler_Saglanmali_Ve_Sonuc_Tekrarlanabilir_Olmali()
    {
        var istekler = new[]
        {
            new BisectionRequest(Dikdortgen(), 0, 450m, Secenek()),
            new BisectionRequest(Dikdortgen(), 0, 100m, Secenek()),
            new BisectionRequest(Yamuk(), 0, 450m, Secenek()),
            new BisectionRequest(LAda(), 0, 450m, Secenek()),
        };

        foreach (var istek in istekler)
        {
            var a = Basarili(BisectionEngine.Cut(istek));
            var b = Basarili(BisectionEngine.Cut(istek));

            // Ortak kesim koseleri her iki halkada birebir ayni (canonical vertex)
            a.Piece.Vertices.Should().Contain(a.CutStart).And.Contain(a.CutEnd);
            a.Remainder.Vertices.Should().Contain(a.CutStart).And.Contain(a.CutEnd);
            // Tum koseler 1 mm izgarasinda
            a.Piece.Vertices.Concat(a.Remainder.Vertices).Should().OnlyContain(v => CoordinateQuantizer.IsOnGrid(v));
            // Kesim noktalari ada kenari uzerindeyse alan korunumu decimal'de tam
            (Alan(a.Piece) + Alan(a.Remainder)).Should().Be(Alan(istek.Block));
            // Tekrarlanabilirlik
            a.Piece.Vertices.Should().Equal(b.Piece.Vertices);
            a.Remainder.Vertices.Should().Equal(b.Remainder.Vertices);
            a.AngleDeg.Should().Be(b.AngleDeg);
            a.BlockAreaDeviationM2.Should().Be(0m);
        }
    }

    // ---- Reviewer bulgulari (H1, H2, M1, M2, M3, L5, L7) ve eksik ada tipleri ----

    [Fact]
    public void Bisection_L_Adada_Kesim_Cizgisi_Kenarla_Cakisirsa_90_Derecede_Tek_Parcali_Kesim_Yapmali()
    {
        // H2: hedef 300 -> kesim Y = 10, (10,10)-(30,10) ada kenari ile cakisir; parca 30x10, kalan 10x30.
        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(LAda(), 0, 300m, Secenek())));

        kesim.AngleDeg.Should().Be(90m);
        kesim.CutStart.Should().Be(P(0, 10));
        kesim.CutEnd.Should().Be(P(30, 10));
        kesim.PieceArea.AreaM2Exact.Should().Be(300m);
        kesim.RemainderArea.AreaM2Exact.Should().Be(300m);
        kesim.Piece.Vertices.Should().HaveCount(4);
        kesim.Remainder.Vertices.Should().HaveCount(4, "kesim dogrusu uzerindeki geri donen (sifir genislikli) cikinti temizlenmeli");
    }

    [Fact]
    public void Bisection_Kesim_Cizgisi_Konkav_Koseye_Teget_Ise_MultiPolygon_Saymamali()
    {
        // M1: centik ucu (20,20) Y = 20 kesim cizgisine yalnizca teget; parca bagli (600 - 25 = 575).
        var disli = Halka((0, 0), (0, 40), (30, 40), (30, 15), (20, 20), (30, 10), (30, 0));

        var sonuc = BisectionEngine.Cut(new BisectionRequest(disli, 0, 575m, Secenek(min: 75m, max: 105m)));

        sonuc.Attempts[0].Outcome.Should().NotBe(AttemptOutcome.MultiPolygon);
        sonuc.Attempts.Should().HaveCountGreaterThan(1, "teget durum taramayi durdurmamali");
        if (sonuc.Result.IsFailure)
        {
            sonuc.Result.Error.Code.Should().NotBe(ErrorCodes.TopoKonkavKesimCokParcali);
        }
    }

    [Fact]
    public void Bisection_Kuantizasyon_Sonrasi_Ada_Toplam_Sapmasi_Ve_Yeni_Kose_Kenari_Raporlanmali()
    {
        // H1: Q (18.974, 6.325) egik V3->V0 kenarinin tam uzerinde degil; ada' = 899.995 -> toplam sapma -0.005.
        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(Yamuk(), 0, 60m, Secenek(kuantTol: 0.011m))));

        kesim.BlockAreaDeviationM2.Should().Be(-0.005m);
        kesim.RemainderAreaDeviationM2.Should().Be(-0.010275m);
        kesim.AreaDeviationM2.Should().Be(0.005275m);
        kesim.CutStartEdgeIndex.Should().Be(0);
        kesim.CutEndEdgeIndex.Should().Be(3, "Q, V3->V0 kenarina eklenmeli (komsu halka guncellemesi icin)");
        kesim.CutEndOnExistingVertex.Should().BeFalse();
    }

    [Fact]
    public void Bisection_Kesim_Mevcut_Koseye_Duserse_Kose_Indeksi_Raporlanmali()
    {
        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(Paralelkenar(), 0, 450m, Secenek())));

        kesim.CutEndOnExistingVertex.Should().BeTrue();
        kesim.CutEndEdgeIndex.Should().Be(3, "V3 (30,30) mevcut kosesi");
    }

    [Theory]
    [InlineData("0.0009")]
    [InlineData("0.0000001")]
    public void Bisection_Cok_Kucuk_Aci_Adiminda_ERR_PLAN_003_Vermeli(string adim)
    {
        var sonuc = BisectionEngine.Cut(new BisectionRequest(Dikdortgen(), 0, 450m,
            Secenek(min: 75m, max: 105m, adim: decimal.Parse(adim, System.Globalization.CultureInfo.InvariantCulture))));

        sonuc.Result.Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz);
    }

    [Fact]
    public void Bisection_Asiri_Iterasyon_Sinirinda_ERR_PLAN_003_Vermeli()
    {
        var sonuc = BisectionEngine.Cut(new BisectionRequest(Dikdortgen(), 0, 450m, Secenek(iterasyon: BisectionEngine.MaxInnerIterationsLimit + 1)));

        sonuc.Result.Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz);
    }

    [Fact]
    public void Bisection_Cifte_Kesinlige_Ulasilamayan_Toleransta_Sonsuz_Donguye_Girmemeli()
    {
        // M2: 1e-19 m2 tolerans double ile asla saglanamaz; aralik tukenince NotConverged.
        var sonuc = BisectionEngine.Cut(new BisectionRequest(Dikdortgen(), 0, 100m,
            Secenek(yakinsama: 0.0000000000000000001m, iterasyon: BisectionEngine.MaxInnerIterationsLimit)));

        sonuc.Result.Error.Code.Should().Be(ErrorCodes.PlanHedefAlanaYakinsanamadi);
        sonuc.Attempts.Single().InnerIterations.Should().BeLessThanOrEqualTo(BisectionEngine.MaxInnerIterationsLimit);
    }

    [Fact]
    public void Bisection_Hedef_Ada_Alanina_Cok_Yakinsa_Dejenere_Parca_Hatasi_Vermeli()
    {
        // M3: 30 t = 1199.995 -> P kuantize olunca V1'e cakisir; kalan sifir alanli -> ERR-TOPO-003 (KUANT-002 degil).
        var sonuc = BisectionEngine.Cut(new BisectionRequest(Dikdortgen(), 0, 1199.995m, Secenek()));

        sonuc.Result.Error.Code.Should().Be(ErrorCodes.TopoDejenerePoligon);
        sonuc.Attempts.Single().Outcome.Should().Be(AttemptOutcome.DegeneratePiece);
    }

    [Fact]
    public void Bisection_Saat_Yonundeki_Halkada_Egik_Aci_Ayna_Konumu_Vermeli()
    {
        // CW halka, cephe (0,40)->(0,0); atan(4): parca 30 t + 112.5 = 450 -> t = 11.25 -> P (0, 28.75), Q (30, 21.25)
        var ters = Halka((0, 0), (30, 0), (30, 40), (0, 40));
        var alfa = 75.96375653207352m;

        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(ters, 3, 450m, Secenek(tercih: alfa))));

        kesim.CutStart.Should().Be(P(0, 28.75m));
        kesim.CutEnd.Should().Be(P(30, 21.25m));
        kesim.PieceArea.AreaM2Exact.Should().Be(450m);
    }

    [Fact]
    public void Bisection_Ucgen_Kama_Adada_Kesim_Yapmali()
    {
        // Ucgen (0,0),(0,40),(30,0): A(t) = 30 t - 0.375 t^2 = 262.5 -> t = 10; Q hipotenusta X = 30 - 0.75*10 = 22.5
        var ucgen = Halka((0, 0), (0, 40), (30, 0));

        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(ucgen, 0, 262.5m, Secenek())));

        kesim.CutStart.Should().Be(P(0, 10));
        kesim.CutEnd.Should().Be(P(22.5m, 10));
        kesim.RemainderArea.AreaM2Exact.Should().Be(337.5m);
    }

    [Theory]
    [InlineData(900, 30, 30, 30)]   // pahin altinda: 30 t = 900
    [InlineData(1108, 37, 28, 37)]  // pah ustunde: 1050 + 30 d - d^2/2, d = 2 -> Q pah kenarinda X = 28
    public void Bisection_45_Derece_Pahli_Koseli_Adada_Kesim_Yapmali(int hedef, double py, double qx, double qy)
    {
        var pahli = Halka((0, 0), (0, 40), (25, 40), (30, 35), (30, 0)); // 1200 - 12.5 = 1187.5

        var kesim = Basarili(BisectionEngine.Cut(new BisectionRequest(pahli, 0, hedef, Secenek())));

        kesim.CutStart.Should().Be(P(0, (decimal)py));
        kesim.CutEnd.Should().Be(P((decimal)qx, (decimal)qy));
        (kesim.PieceArea.AreaM2Exact + kesim.RemainderArea.AreaM2Exact).Should().Be(1187.5m);
    }

    [Fact]
    public void Bisection_Hata_Mesajlari_Mutlak_Koordinat_Icermemeli()
    {
        var hatalar = new[]
        {
            BisectionEngine.Cut(new BisectionRequest(Dikdortgen(), 0, 100m, Secenek(kuantTol: 0.0099m))),    // KUANT-001
            BisectionEngine.Cut(new BisectionRequest(UAda(), 7, 900m, Secenek())),                          // TOPO-004
            BisectionEngine.Cut(new BisectionRequest(Dikdortgen(), 0, 100m, Secenek(iterasyon: 2))),          // PLAN-004
            BisectionEngine.Cut(new BisectionRequest(Dikdortgen(), 0, 1199.995m, Secenek())),                 // TOPO-003
            BisectionEngine.Cut(new BisectionRequest(Halka((0, 0), (0, 40), (30, 10), (30, 40)), 0, 100m, Secenek())), // TOPO-001
        }.Select(s => s.Result.Error).ToList();

        hatalar.Should().OnlyContain(h => !h.TechnicalMessage.Contains("4500") && !h.TechnicalMessage.Contains("50000")
            && !h.TechnicalMessage.Contains('{') && !h.TechnicalMessage.Contains('}'));
    }
}

using System.Globalization;
using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Subdivision;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using Xunit;

namespace Imar18.GeometryEngine.Tests;

/// <summary>
/// Google Antigravity Bağımsız Test ve Denetim Paketi (Faz P2 Geometri Motoru Denetimi).
/// 
/// Kapsam (Imar18Studio v2.19 §4.2, D28, nettopologysuite ve imar18-cad-topology):
/// 1. 6 Gerçek İmar Adası Geometrisi:
///    - Düz Dikdörtgen Ada
///    - Sırt Sırta Çift Cepheli Omurgalı Ada (103/104 Ada tipi)
///    - 45° Köşe Pahlı Ada (C1, C2, C6, C7 tipi)
///    - Yamuk / Trapez Ada
///    - Üçgen / Kama Ada
///    - Konkav L-Ada
/// 2. Alan Korunumu (Gauss Shoelace çift kontrol, parça + kalan = ada).
/// 3. 1 mm (0.001 m) Canonical Kuantizasyon ve IsValid Topoloji Geçerliliği.
/// 4. 75°–105° Diklik Aralığı Sınır Denetimleri (75°, 90°, 105° ve aralık dışı ret).
/// 5. Y/X Jeodezik Eksen Takası (x_NTS = Y_jeo, y_NTS = X_jeo) ve Yerel Orijin Hassasiyeti.
/// 6. Konkav Adada MultiPolygon Bölünme Tespiti (ERR-TOPO-004 güvenli duruşu).
/// </summary>
public sealed class AntigravityIndependentGeometryAuditTests
{
    // Türkiye ITRF96 TM 3° standartlarında gerçekçi mutlak koordinat orijini (metre)
    private const decimal OrijinX = 4540000.000m; // Kuzey (X_jeo)
    private const decimal OrijinY = 520000.000m;  // Doğu (Y_jeo)
    private static readonly GeodeticPoint Orijin = new(X: OrijinX, Y: OrijinY);

    private static GeodeticPoint P(decimal x, decimal y) => new(X: OrijinX + x, Y: OrijinY + y);

    private static PolygonRing Halka(params (decimal X, decimal Y)[] yerelNoktalar)
    {
        var sonuc = PolygonRing.FromOpen(yerelNoktalar.Select(p => P(p.X, p.Y)).ToList());
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.Code : "Halka geçerli olmalı");
        return sonuc.Value;
    }

    private static BisectionOptions StandartSecenek(
        decimal tercihAci = 90m,
        decimal? minAci = null,
        decimal? maxAci = null,
        decimal adim = 5m,
        decimal yakinsamaTol = 0.0001m,
        decimal kuantizasyonTol = 0.01m,
        int maxIterasyon = 60) =>
        new(tercihAci, minAci ?? tercihAci, maxAci ?? tercihAci, adim, yakinsamaTol, kuantizasyonTol, maxIterasyon);

    private static BisectionCut KesimiDogrula(BisectionOutcome sonuc)
    {
        sonuc.Result.IsSuccess.Should().BeTrue(sonuc.Result.IsFailure ? sonuc.Result.Error.TechnicalMessage : "Kesim başarılı olmalı");
        return sonuc.Result.Value;
    }

    // =========================================================================
    // BÖLÜM 1: 6 GERÇEK İMAR ADASI GEOMETRİSİ DENETİMLERİ
    // =========================================================================

    /// <summary>
    /// Ada 1: Düz Dikdörtgen Ada (30 x 60 m = 1800.00 m²).
    /// Cephe 0: (0,0) -> (0,60).
    /// Bağımsız Matematiksel Referans:
    /// - Hedef Alan = 600.00 m², Tercih Açısı = 90°.
    /// - Dik kesimde genişlik = 30 m olduğundan t = 600 / 30 = 20.000 m.
    /// - Kesim Başlangıç: P(0, 20.000), Kesim Bitiş: P(30, 20.000).
    /// - Parça Alanı = 30 * 20 = 600.00 m², Kalan Alanı = 30 * 40 = 1200.00 m².
    /// - Alan Korunumu: 600.00 + 1200.00 = 1800.00 m² (sıfır sapma).
    /// - 1 mm kuantizasyon ve NTS IsValid geçerliliği.
    /// </summary>
    [Fact]
    public void Ada1_Duz_Dikdortgen_Ada_90_Derece_Kesim_Alan_Korunumu_Ve_Izgara_Dogrulanmali()
    {
        var ada = Halka((0, 0), (0, 60), (30, 60), (30, 0));
        var istek = new BisectionRequest(ada, FrontageEdgeIndex: 0, TargetAreaM2: 600.00m, StandartSecenek());

        var kesim = KesimiDogrula(BisectionEngine.Cut(istek));

        kesim.CutStart.Should().Be(P(0, 20.000m));
        kesim.CutEnd.Should().Be(P(30, 20.000m));
        kesim.PieceArea.AreaM2Exact.Should().Be(600.00m);
        kesim.RemainderArea.AreaM2Exact.Should().Be(1200.00m);
        (kesim.PieceArea.AreaM2Exact + kesim.RemainderArea.AreaM2Exact).Should().Be(1800.00m);
        kesim.BlockAreaDeviationM2.Should().Be(0m);
        kesim.FrontageLengthM.Should().Be(20.000m);

        // Kuantizasyon ve topoloji geçerliliği
        kesim.Piece.Vertices.Concat(kesim.Remainder.Vertices).Should().OnlyContain(v => CoordinateQuantizer.IsOnGrid(v));
        NtsValidity.IsValid(kesim.Piece.Vertices, Orijin).Should().BeTrue();
        NtsValidity.IsValid(kesim.Remainder.Vertices, Orijin).Should().BeTrue();
    }

    /// <summary>
    /// Ada 2: Sırt Sırta Çift Cepheli Omurgalı Ada (103/104 Ada Tipi).
    /// Geometri: Alt cadde cephesi (0,0)->(0,80), üst cadde cephesi (40,80)->(40,0).
    /// Yan omurga uçları V2(20, 90) ve V5(20, -10). Simetrik 6 köşeli ada.
    /// Bağımsız Matematiksel Referans:
    /// - Dikdörtgen gövde (40 x 80) = 3200 m².
    /// - Sağ omurga üçgeni (taban 40, yükseklik 10) = 200 m².
    /// - Sol omurga üçgeni (taban 40, yükseklik 10) = 200 m².
    /// - Toplam Ada Alanı = 3200 + 200 + 200 = 3600.00 m².
    /// - Cephe 0 ((0,0)->(0,80)) üzerinden 90° dik kesim:
    ///   Sol omurga üçgeni (Y in [-10, 0]) = 200 m² parça tarafında kalır.
    ///   Y in [0, 80] arasında gövde genişliği 40 m'dir.
    ///   Hedef = 800.00 m² için: 200 + 40 * t = 800 => 40 * t = 600 => t = 15.000 m.
    /// - Beklenen Kesim: CutStart = P(0, 15.000), CutEnd = P(40, 15.000).
    /// - Parça Alanı = 800.00 m², Kalan Alanı = 2800.00 m² (Toplam = 3600.00 m²).
    /// </summary>
    [Fact]
    public void Ada2_Sirt_Sirta_Omurgali_Ada_Iki_Cadde_Arasi_Kesim_Dogrulanmali()
    {
        // 6 köşeli omurgalı ada: (0,0), (0,80), (20,90), (40,80), (40,0), (20,-10)
        var omurgaliAda = Halka((0, 0), (0, 80), (20, 90), (40, 80), (40, 0), (20, -10));
        var toplamAlan = GaussAreaCalculator.Compute(omurgaliAda).AreaM2Exact;
        toplamAlan.Should().Be(3600.00m);

        var istek = new BisectionRequest(omurgaliAda, FrontageEdgeIndex: 0, TargetAreaM2: 800.00m, StandartSecenek());
        var kesim = KesimiDogrula(BisectionEngine.Cut(istek));

        kesim.CutStart.Should().Be(P(0, 15.000m));
        kesim.CutEnd.Should().Be(P(40, 15.000m));
        kesim.CutStartEdgeIndex.Should().Be(0);
        kesim.CutEndEdgeIndex.Should().Be(3); // (40,80) -> (40,0) üst cadde cephesi
        kesim.CutEndOnExistingVertex.Should().BeFalse();

        kesim.PieceArea.AreaM2Exact.Should().Be(800.00m);
        kesim.RemainderArea.AreaM2Exact.Should().Be(2800.00m);
        (kesim.PieceArea.AreaM2Exact + kesim.RemainderArea.AreaM2Exact).Should().Be(3600.00m);

        // Kuantizasyon ve NTS IsValid
        kesim.Piece.Vertices.Concat(kesim.Remainder.Vertices).Should().OnlyContain(v => CoordinateQuantizer.IsOnGrid(v));
        NtsValidity.IsValid(kesim.Piece.Vertices, Orijin).Should().BeTrue();
        NtsValidity.IsValid(kesim.Remainder.Vertices, Orijin).Should().BeTrue();
    }

    /// <summary>
    /// Ada 3: 45° Köşe Pahlı Ada (C1, C2 Köşe Tipi).
    /// Geometri: (0, 0), (0, 50), (20, 50), (30, 40), (30, 0).
    /// Köşe pahı (20, 50) ile (30, 40) arasındaki 45° pah kenarıdır (V2->V3).
    /// Bağımsız Matematiksel Referans:
    /// - Dikdörtgen zarf 30 x 50 = 1500 m². Pah üçgeni = 0.5 * 10 * 10 = 50 m².
    /// - Ada Alanı = 1500 - 50 = 1450.00 m².
    /// - Cephe 0: (0,0)->(0,50). 90° dik kesim pah kenarını kestiğinde (t in [40, 50]):
    ///   Pah kenarı denklemi: X + Y = 70 => X = 70 - t.
    ///   Parça Alanı(t) = 30 * 40 + integral_{40}^t (70 - u) du.
    ///   t = 44.000 m için: A(44) = 1200 + [70(4) - (44^2 - 40^2)/2] = 1200 + 112 = 1312.00 m².
    /// - Kesim Bitiş Koordinatı: X = 70 - 44 = 26.000 m, Y = 44.000 m.
    ///   CutEnd = P(26.000, 44.000) tam 1 mm ızgarasındadır.
    /// - CutEndEdgeIndex = 2 (pah kenarı), CutEndOnExistingVertex = false.
    /// </summary>
    [Fact]
    public void Ada3_Kose_Pahli_Adada_Pah_Kenarini_Kesen_Kesim_Tam_Dogrulanmali()
    {
        var pahliAda = Halka((0, 0), (0, 50), (20, 50), (30, 40), (30, 0));
        var toplamAlan = GaussAreaCalculator.Compute(pahliAda).AreaM2Exact;
        toplamAlan.Should().Be(1450.00m);

        var istek = new BisectionRequest(pahliAda, FrontageEdgeIndex: 0, TargetAreaM2: 1312.00m, StandartSecenek());
        var kesim = KesimiDogrula(BisectionEngine.Cut(istek));

        kesim.CutStart.Should().Be(P(0, 44.000m));
        kesim.CutEnd.Should().Be(P(26.000m, 44.000m));
        kesim.CutStartEdgeIndex.Should().Be(0);
        kesim.CutEndEdgeIndex.Should().Be(2); // V2(20,50) -> V3(30,40) pah kenarı
        kesim.CutEndOnExistingVertex.Should().BeFalse();

        kesim.PieceArea.AreaM2Exact.Should().Be(1312.00m);
        kesim.RemainderArea.AreaM2Exact.Should().Be(138.00m);
        (kesim.PieceArea.AreaM2Exact + kesim.RemainderArea.AreaM2Exact).Should().Be(1450.00m);
        kesim.BlockAreaDeviationM2.Should().Be(0m);

        // Kuantizasyon ve IsValid
        kesim.Piece.Vertices.Concat(kesim.Remainder.Vertices).Should().OnlyContain(v => CoordinateQuantizer.IsOnGrid(v));
        NtsValidity.IsValid(kesim.Piece.Vertices, Orijin).Should().BeTrue();
        NtsValidity.IsValid(kesim.Remainder.Vertices, Orijin).Should().BeTrue();
    }

    /// <summary>
    /// Ada 4: Yamuk / Trapez Ada.
    /// Geometri: (0, 0), (0, 50), (20, 40), (20, 10).
    /// Yükseklik = 20 m, paralel olmayan kenarlar V1(0,50)->V2(20,40) ve V3(20,10)->V0(0,0).
    /// Bağımsız Matematiksel Referans:
    /// - Ada Alanı = 20 * (50 + 30) / 2 = 800.00 m².
    /// - Alt üçgen (Y in [0, 10]): V3->V0 kenarında X = 2 Y => Alan(Y=10) = 0.5 * 10 * 20 = 100.00 m².
    /// - Orta bölge (Y in [10, 40]): Gövde genişliği 20 m sabittir.
    /// - Hedef = 400.00 m² (tam orta yarı) için:
    ///   100 + 20 * (t - 10) = 400 => 20 * (t - 10) = 300 => t = 25.000 m.
    /// - Beklenen Kesim: CutStart = P(0, 25.000), CutEnd = P(20, 25.000).
    /// - Parça Alanı = 400.00 m², Kalan Alanı = 400.00 m² (sıfır sapma).
    /// </summary>
    [Fact]
    public void Ada4_Yamuk_Adada_90_Derece_Orta_Kesim_Dogrulanmali()
    {
        var yamukAda = Halka((0, 0), (0, 50), (20, 40), (20, 10));
        var toplamAlan = GaussAreaCalculator.Compute(yamukAda).AreaM2Exact;
        toplamAlan.Should().Be(800.00m);

        var istek = new BisectionRequest(yamukAda, FrontageEdgeIndex: 0, TargetAreaM2: 400.00m, StandartSecenek());
        var kesim = KesimiDogrula(BisectionEngine.Cut(istek));

        kesim.CutStart.Should().Be(P(0, 25.000m));
        kesim.CutEnd.Should().Be(P(20, 25.000m));
        kesim.PieceArea.AreaM2Exact.Should().Be(400.00m);
        kesim.RemainderArea.AreaM2Exact.Should().Be(400.00m);
        (kesim.PieceArea.AreaM2Exact + kesim.RemainderArea.AreaM2Exact).Should().Be(800.00m);

        kesim.Piece.Vertices.Concat(kesim.Remainder.Vertices).Should().OnlyContain(v => CoordinateQuantizer.IsOnGrid(v));
        NtsValidity.IsValid(kesim.Piece.Vertices, Orijin).Should().BeTrue();
        NtsValidity.IsValid(kesim.Remainder.Vertices, Orijin).Should().BeTrue();
    }

    /// <summary>
    /// Ada 5: Üçgen / Kama Ada.
    /// Geometri: (0, 0), (0, 60), (30, 0). Dik üçgen.
    /// Bağımsız Matematiksel Referans:
    /// - Ada Alanı = 0.5 * 60 * 30 = 900.00 m².
    /// - Cephe 0: (0,0)->(0,60). Hipotenüs denklemi: 2X + Y = 60 => X = 30 - Y/2.
    /// - t konumunda kesim genişliği W(t) = 30 - t/2.
    /// - Parça Alanı(t) = integral_0^t (30 - u/2) du = 30 t - t^2 / 4.
    /// - t = 20.000 m için: A(20) = 30(20) - 400/4 = 600 - 100 = 500.00 m².
    /// - Kesim Bitiş: X = 30 - 20/2 = 20.000 m, Y = 20.000 m.
    /// - CutStart = P(0, 20.000), CutEnd = P(20.000, 20.000).
    /// - Parça (yamuk) = 500.00 m², Kalan (küçük üçgen) = 400.00 m².
    /// - Alan Korunumu: 500.00 + 400.00 = 900.00 m² (sıfır sapma).
    /// </summary>
    [Fact]
    public void Ada5_Ucgen_Kama_Adada_Hipotenusu_Kesen_Bolme_Dogrulanmali()
    {
        var ucgenAda = Halka((0, 0), (0, 60), (30, 0));
        var toplamAlan = GaussAreaCalculator.Compute(ucgenAda).AreaM2Exact;
        toplamAlan.Should().Be(900.00m);

        var istek = new BisectionRequest(ucgenAda, FrontageEdgeIndex: 0, TargetAreaM2: 500.00m, StandartSecenek());
        var kesim = KesimiDogrula(BisectionEngine.Cut(istek));

        kesim.CutStart.Should().Be(P(0, 20.000m));
        kesim.CutEnd.Should().Be(P(20.000m, 20.000m));
        kesim.CutStartEdgeIndex.Should().Be(0);
        kesim.CutEndEdgeIndex.Should().Be(1); // hipotenüs kenarı: V1(0,60) -> V2(30,0)

        kesim.PieceArea.AreaM2Exact.Should().Be(500.00m);
        kesim.RemainderArea.AreaM2Exact.Should().Be(400.00m);
        (kesim.PieceArea.AreaM2Exact + kesim.RemainderArea.AreaM2Exact).Should().Be(900.00m);
        kesim.BlockAreaDeviationM2.Should().Be(0m);

        // Kalan küçük üçgen ve parça yamuk topolojik geçerlilik
        kesim.Piece.Vertices.Concat(kesim.Remainder.Vertices).Should().OnlyContain(v => CoordinateQuantizer.IsOnGrid(v));
        NtsValidity.IsValid(kesim.Piece.Vertices, Orijin).Should().BeTrue();
        NtsValidity.IsValid(kesim.Remainder.Vertices, Orijin).Should().BeTrue();
    }

    /// <summary>
    /// Ada 6: Konkav L-Ada.
    /// Geometri: (0, 0), (0, 50), (15, 50), (15, 20), (35, 20), (35, 0).
    /// Konkav iç köşe (15, 20).
    /// Bağımsız Matematiksel Referans:
    /// - Sol kol 15 x 50 = 750 m², Sağ taban kolu 20 x 20 = 400 m². Toplam Alan = 1150.00 m².
    /// - Senaryo A (Geniş taban bölgesi, t in [0, 20]):
    ///   Hedef = 350.00 m² => t = 350 / 35 = 10.000 m.
    ///   CutStart = P(0, 10.000), CutEnd = P(35, 10.000). Parça = 350 m², Kalan = 800 m².
    /// - Senaryo B (Konkav köşeyi geçen dar kol, t in [20, 50]):
    ///   Hedef = 850.00 m² => Taban 700 m² + 15 * (t - 20) = 850 => t = 30.000 m.
    ///   CutStart = P(0, 30.000), CutEnd = P(15, 30.000). Parça = 850 m², Kalan = 300 m².
    /// </summary>
    [Fact]
    public void Ada6_Konkav_L_Adada_Geniş_Kolda_Ve_Iç_Koseyi_Gecen_Dar_Kolda_Kesimler_Dogrulanmali()
    {
        var lAda = Halka((0, 0), (0, 50), (15, 50), (15, 20), (35, 20), (35, 0));
        var toplamAlan = GaussAreaCalculator.Compute(lAda).AreaM2Exact;
        toplamAlan.Should().Be(1150.00m);

        // Senaryo A: Geniş taban kolu (t = 10)
        var istekA = new BisectionRequest(lAda, FrontageEdgeIndex: 0, TargetAreaM2: 350.00m, StandartSecenek());
        var kesimA = KesimiDogrula(BisectionEngine.Cut(istekA));

        kesimA.CutStart.Should().Be(P(0, 10.000m));
        kesimA.CutEnd.Should().Be(P(35, 10.000m));
        kesimA.PieceArea.AreaM2Exact.Should().Be(350.00m);
        kesimA.RemainderArea.AreaM2Exact.Should().Be(800.00m);
        (kesimA.PieceArea.AreaM2Exact + kesimA.RemainderArea.AreaM2Exact).Should().Be(1150.00m);

        // Senaryo B: Konkav iç köşeyi geçen dar kol (t = 30)
        var istekB = new BisectionRequest(lAda, FrontageEdgeIndex: 0, TargetAreaM2: 850.00m, StandartSecenek());
        var kesimB = KesimiDogrula(BisectionEngine.Cut(istekB));

        kesimB.CutStart.Should().Be(P(0, 30.000m));
        kesimB.CutEnd.Should().Be(P(15, 30.000m));
        kesimB.PieceArea.AreaM2Exact.Should().Be(850.00m);
        kesimB.RemainderArea.AreaM2Exact.Should().Be(300.00m);
        (kesimB.PieceArea.AreaM2Exact + kesimB.RemainderArea.AreaM2Exact).Should().Be(1150.00m);

        // Topoloji ve kuantizasyon kontrolü
        kesimB.Piece.Vertices.Concat(kesimB.Remainder.Vertices).Should().OnlyContain(v => CoordinateQuantizer.IsOnGrid(v));
        NtsValidity.IsValid(kesimB.Piece.Vertices, Orijin).Should().BeTrue();
        NtsValidity.IsValid(kesimB.Remainder.Vertices, Orijin).Should().BeTrue();
    }

    // =========================================================================
    // BÖLÜM 2: KONKAV ADADA MULTIPOLYGON BÖLÜNME TESPİTİ SINIR TESTLERİ
    // =========================================================================

    /// <summary>
    /// Konkav U-Ada: İki paralel kol ve ortasında boşluk (iç koy) bulunan ada.
    /// Geometri: (0,0), (0,60), (50,60), (50,45), (15,45), (15,15), (50,15), (50,0).
    /// Cephe 7: V7(50,0) -> V0(0,0).
    /// Kesim çizgisi X in [15, 50] aralığında boşluğun üzerinden geçtiğinde iki kolu ayrı ayrı keser (4 sınır geçişi).
    /// Beklenen Davranış (spec §4.2, TC-GEOM-001, AC-PILOT-01):
    /// - MultiPolygon bölünmesi tespit edilir (transitions > 2).
    /// - ERR-TOPO-004 (TopoKonkavKesimCokParcali) hatası döner.
    /// - RequiresEngineerReview = true olmalıdır.
    /// - SORU-023 kararı (17 Kasım 2026, P6'da uygulandı): 75–105° aralığının tamamı taranır; hiçbir açıda tek parçalı
    ///   çözüm yoksa ERR-TOPO-004 döner (ilk deneme MultiPolygon'dur).
    /// </summary>
    [Fact]
    public void Konkav_U_Adada_Bosluktan_Gecen_Kesim_MultiPolygon_Tespiti_Ile_Guvenli_Durmali()
    {
        var uAda = Halka((0, 0), (0, 60), (50, 60), (50, 45), (15, 45), (15, 15), (50, 15), (50, 0));

        // Cephe 7: (50,0) -> (0,0). Hedef 900 m2 iki kolu ayrı ayrı kesmeyi gerektirir.
        var secenek = StandartSecenek(tercihAci: 90m, minAci: 75m, maxAci: 105m, adim: 5m);
        var sonuc = BisectionEngine.Cut(new BisectionRequest(uAda, FrontageEdgeIndex: 7, TargetAreaM2: 900.00m, secenek));

        sonuc.Result.IsFailure.Should().BeTrue();
        sonuc.Result.Error.Code.Should().Be(ErrorCodes.TopoKonkavKesimCokParcali);
        sonuc.Result.Error.RequiresEngineerReview.Should().BeTrue("Konkav çok parçalı bölünme harita mühendisi incelemesi gerektirir");
        sonuc.Attempts.Should().HaveCount(7, "SORU-023: tek parçalı çözüm için 75–105° aralığının tamamı taranır");
        sonuc.Attempts[0].Outcome.Should().Be(AttemptOutcome.MultiPolygon);
        sonuc.Attempts.Should().NotContain(a => a.Outcome == AttemptOutcome.Accepted);
    }

    // =========================================================================
    // BÖLÜM 3: 75°–105° DİKLİK ARALIĞI SINIR DENETİMLERİ
    // =========================================================================

    [Theory]
    [InlineData("75.000", true)]   // Min sınır tam 75°
    [InlineData("90.000", true)]   // Tercih edilen diklik 90°
    [InlineData("105.000", true)]  // Max sınır tam 105°
    [InlineData("74.999", false)]  // 75° altı geçersiz
    [InlineData("105.001", false)] // 105° üstü geçersiz
    public void Diklik_Araligi_Sinir_Degerleri_75_Ve_105_Derece_Kati_Denetlenmeli(string aciStr, bool gecerliMi)
    {
        var aci = decimal.Parse(aciStr, CultureInfo.InvariantCulture);
        var ada = Halka((0, 0), (0, 40), (30, 40), (30, 0));

        var secenek = new BisectionOptions(
            PreferredAngleDeg: aci,
            MinAngleDeg: aci < 75m ? aci : 75m,
            MaxAngleDeg: aci > 105m ? aci : 105m,
            AngleStepDeg: 1m,
            ConvergenceToleranceM2: 0.0001m,
            PostQuantizationAreaToleranceM2: 0.01m,
            MaxInnerIterations: 60);

        var sonuc = BisectionEngine.Cut(new BisectionRequest(ada, FrontageEdgeIndex: 0, TargetAreaM2: 300.00m, secenek));

        if (gecerliMi)
        {
            sonuc.Result.IsSuccess.Should().BeTrue($"Açı {aci}° izin verilen [75, 105] aralığındadır");
            sonuc.Result.Value.AngleDeg.Should().Be(aci);
        }
        else
        {
            sonuc.Result.IsFailure.Should().BeTrue($"Açı {aci}° [75, 105] aralığı dışındadır");
            sonuc.Result.Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz);
        }
    }

    /// <summary>
    /// Tercih açısı [min, max] aralığı dışında kaldığında motorun hata vermesi denetimi.
    /// </summary>
    [Fact]
    public void Tercih_Acisi_Min_Max_Araligi_Disinda_Ise_ERR_PLAN_003_Vermeli()
    {
        var ada = Halka((0, 0), (0, 40), (30, 40), (30, 0));
        var secenek = new BisectionOptions(
            PreferredAngleDeg: 80m, // tercih 80
            MinAngleDeg: 85m,       // min 85 (tercih < min)
            MaxAngleDeg: 100m,
            AngleStepDeg: 1m,
            ConvergenceToleranceM2: 0.0001m,
            PostQuantizationAreaToleranceM2: 0.01m,
            MaxInnerIterations: 60);

        var sonuc = BisectionEngine.Cut(new BisectionRequest(ada, 0, 300.00m, secenek));

        sonuc.Result.IsFailure.Should().BeTrue();
        sonuc.Result.Error.Code.Should().Be(ErrorCodes.PlanBolmeGirdisiGecersiz);
    }

    // =========================================================================
    // BÖLÜM 4: Y/X JEODEZİK EKSEN TAKASI VE YEREL ORİJİN HASSASİYETİ
    // =========================================================================

    /// <summary>
    /// Türkiye Jeodezik Sistemi (X=Kuzey, Y=Doğu) ile NTS Kartezyen (x=Doğu, y=Kuzey) eşlemesi denetimi:
    /// x_NTS = Y_jeo - Y0
    /// y_NTS = X_jeo - X0
    /// </summary>
    [Fact]
    public void EksenTakasi_Jeodezik_Ve_Nts_Koordinat_Eşlemesi_Ve_Geri_Donus_Birebir_Olmali()
    {
        var nokta = new GeodeticPoint(X: OrijinX + 123.456m, Y: OrijinY + 789.012m);

        var nts = CoordinateAxisSwapAdapter.ToNts(nokta, Orijin);

        nts.X.Should().Be(789.012d, "NTS X ekseni Jeodezik Y (Doğu) olmalıdır");
        nts.Y.Should().Be(123.456d, "NTS Y ekseni Jeodezik X (Kuzey) olmalıdır");

        var geriDonus = CoordinateAxisSwapAdapter.FromNts(nts, Orijin);
        geriDonus.X.Should().Be(nokta.X);
        geriDonus.Y.Should().Be(nokta.Y);
    }

    /// <summary>
    /// Mutlak koordinatta 1 mm kuantizasyon:
    /// CoordinateQuantizer.Quantize mutlak koordinatta AwayFromZero yuvarlar.
    /// Yerel koordinatta yuvarlama ile mutlak koordinatta yuvarlama farkı olmamalıdır.
    /// </summary>
    [Theory]
    [InlineData("4540000.0005", "4540000.001")] // 0.5 mm pozitif -> 1 mm
    [InlineData("4540000.0004", "4540000.000")] // 0.4 mm -> 0 mm
    [InlineData("4540000.0015", "4540000.002")] // 1.5 mm -> 2 mm
    [InlineData("4540000.0014", "4540000.001")] // 1.4 mm -> 1 mm
    public void Kuantizasyon_Mutlak_Koordinatta_1_mm_AwayFromZero_Yuvarlamali(string girdiStr, string beklenenStr)
    {
        var girdi = decimal.Parse(girdiStr, CultureInfo.InvariantCulture);
        var beklenen = decimal.Parse(beklenenStr, CultureInfo.InvariantCulture);

        CoordinateQuantizer.Quantize(girdi).Should().Be(beklenen);
    }

    /// <summary>
    /// Saat yönü (CW) ve saat yönü tersi (CCW) halkalarda Gauss Shoelace işaretli çift alan kontrolü.
    /// GaussAreaCalculator.SignedDoubleArea S1 == S2 çift kontrolü ile çalışır; aksi halde InvalidOperationException fırlatır.
    /// </summary>
    [Fact]
    public void Gauss_Shoelace_Saat_Yonu_Ve_Tersi_Halkalarda_Tam_Kesinlikle_Calismali()
    {
        // CCW (saat yönü tersi) halka: (0,0) -> (0,30) Doğu -> (40,30) Kuzey -> (40,0) Batı -> Güney
        var ccw = Halka((0, 0), (0, 30), (40, 30), (40, 0));
        var alanCcw = GaussAreaCalculator.Compute(ccw);
        alanCcw.AreaM2Exact.Should().Be(1200.00m);
        alanCcw.Orientation.Should().Be(RingOrientation.CounterClockwise);

        // CW (saat yönü) halka: (0,0) -> (40,0) Kuzey -> (40,30) Doğu -> (0,30) Güney -> Batı
        var cw = Halka((0, 0), (40, 0), (40, 30), (0, 30));
        var alanCw = GaussAreaCalculator.Compute(cw);
        alanCw.AreaM2Exact.Should().Be(1200.00m);
        alanCw.Orientation.Should().Be(RingOrientation.Clockwise);

        alanCcw.AreaM2Exact.Should().Be(alanCw.AreaM2Exact);
    }

    // =========================================================================
    // BÖLÜM 5: GÜVENLİK, KVKK VE GEÇERSİZ GİRDİ SINIR DENETİMLERİ
    // =========================================================================

    [Fact]
    public void Guvenlik_Hata_Mesajlarinda_Mutlak_Koordinat_Veya_Ham_Veri_Sizmamali()
    {
        var ada = Halka((0, 0), (0, 40), (30, 40), (30, 0));
        var sonuclar = new[]
        {
            BisectionEngine.Cut(new BisectionRequest(ada, 0, -10m, StandartSecenek())),
            BisectionEngine.Cut(new BisectionRequest(ada, 0, 2000m, StandartSecenek())),
            BisectionEngine.Cut(new BisectionRequest(ada, 5, 300m, StandartSecenek())),
        };

        foreach (var s in sonuclar)
        {
            s.Result.IsFailure.Should().BeTrue();
            var msg = s.Result.Error.TechnicalMessage;
            msg.Should().NotContain("4540000");
            msg.Should().NotContain("520000");
            msg.Should().NotContain("{");
            msg.Should().NotContain("}");
        }
    }

    [Fact]
    public void Kendi_Uzerinde_Kesisen_Ada_ERR_TOPO_001_Ile_Reddedilmeli()
    {
        // Papyon poligon (kendini keser)
        var papyon = Halka((0, 0), (0, 40), (30, 10), (30, 40));
        var istek = new BisectionRequest(papyon, 0, 100m, StandartSecenek());

        var sonuc = BisectionEngine.Cut(istek);

        sonuc.Result.IsFailure.Should().BeTrue();
        sonuc.Result.Error.Code.Should().Be(ErrorCodes.TopoKendiUzerindeKesisim);
    }
}

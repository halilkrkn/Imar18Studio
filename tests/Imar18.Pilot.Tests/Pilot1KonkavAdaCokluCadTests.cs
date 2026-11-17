using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Subdivision;
using Imar18.GeometryEngine.Topoloji;
using Imar18.Infrastructure.Cad.Acad;
using Imar18.Infrastructure.Cad.Ncz;
using Imar18.Infrastructure.Tests.Cad.Ncz;
using Xunit;

namespace Imar18.Pilot.Tests;

/// <summary>
/// P6.1 / AC-PILOT-01 — Konkav ada ve çoklu CAD: aynı sentetik L adası <c>.dxf</c>, <c>.dwg</c> ve <c>.ncz</c> olarak üretilip
/// <see cref="CadImportService"/> ile içe aktarılır; üç format aynı jeodezik halkayı ve Decimal Gauss alanını vermeli, iki döngülü
/// bisection 13 m cephe × 16 m derinlik (208 m²) parselini güvenle kesmeli, U adasında açıklanabilir biçimde durmalıdır (SORU-023).
/// L adası (yerel X = Kuzey, Y = Doğu): şerit X ∈ [0,16], Y ∈ [0,40] + kol X ∈ [16,40], Y ∈ [24,40]; alan 640 + 384 = 1024 m²
/// (elle). Cephe V0 (0,0) → V1 (0,40); 90° kesim Y = t: t ≤ 24 için alan 16·t.
/// </summary>
public sealed class Pilot1KonkavAdaCokluCadTests
{
    private const decimal X0 = 4_200_000m;
    private const decimal Y0 = 300_000m;
    private const string AdaKatmani = "ADA_KENARLT";

    private static readonly (decimal X, decimal Y)[] LAda = [(0, 0), (0, 40), (40, 40), (40, 24), (16, 24), (16, 0)];
    private static readonly (decimal X, decimal Y)[] UAda = [(0, 0), (0, 50), (100, 50), (100, 40), (10, 40), (10, 10), (100, 10), (100, 0)];

    private static readonly CadImportService Ice = new([new NczFormatAdapter(), new DxfFormatAdapter(), new DwgFormatAdapter()]);

    private static GeodeticPoint P(decimal x, decimal y) => new(X0 + x, Y0 + y);

    private static BisectionOptions Secenek() => new(90m, 75m, 105m, 5m, 0.0001m, 0.01m, 60);

    private static byte[] Ncz((decimal X, decimal Y)[] yerel)
    {
        var koseler = yerel.Append(yerel[0]).Select(k => ((double)(X0 + k.X), (double)(Y0 + k.Y), 0d)).ToList();
        return new NczSentetikDosya().Katman(AdaKatmani).Poligon(0, "ADA-1", koseler).Olustur();
    }

    private static CadDocument AcadBelgesi((decimal X, decimal Y)[] yerel)
    {
        var belge = new CadDocument();
        var katman = new Layer(AdaKatmani);
        belge.Layers.Add(katman);
        var lw = new LwPolyline { Layer = katman, IsClosed = true };
        foreach (var (x, y) in yerel)
        {
            lw.Vertices.Add(new LwPolyline.Vertex(new XY((double)(Y0 + y), (double)(X0 + x)))); // CAD x = Doğu, y = Kuzey
        }

        belge.Entities.Add(lw);
        return belge;
    }

    private static byte[] Dxf((decimal X, decimal Y)[] yerel)
    {
        using var ms = new MemoryStream();
        DxfWriter.Write(ms, AcadBelgesi(yerel), false, null);
        return ms.ToArray();
    }

    private static byte[] Dwg((decimal X, decimal Y)[] yerel)
    {
        using var ms = new MemoryStream();
        DwgWriter.Write(ms, AcadBelgesi(yerel), null);
        return ms.ToArray();
    }

    private static async Task<PolygonRing> IceAktarAsync(byte[] bayt, string uzanti)
    {
        var sonuc = await Ice.ImportAsync(new MemoryStream(bayt, writable: false), uzanti, null, CancellationToken.None);
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        var v = sonuc.Value.Polylines.Should().ContainSingle().Subject.Vertices;
        var halka = v.Count > 1 && v[0] == v[^1] ? PolygonRing.FromClosed(v) : PolygonRing.FromOpen(v);
        halka.IsSuccess.Should().BeTrue();
        return halka.Value;
    }

    public static TheoryData<string> Formatlar => [".ncz", ".dxf", ".dwg"];

    private static byte[] Uret(string uzanti, (decimal X, decimal Y)[] yerel) => uzanti switch
    {
        ".ncz" => Ncz(yerel),
        ".dxf" => Dxf(yerel),
        _ => Dwg(yerel),
    };

    private static int CepheIndeksi(PolygonRing r)
    {
        for (var i = 0; i < r.Vertices.Count; i++)
        {
            if (r.Vertices[i] == P(0, 0) && r.Vertices[(i + 1) % r.Vertices.Count] == P(0, 40))
            {
                return i;
            }
        }

        throw new InvalidOperationException("cephe kenarı bulunamadı");
    }

    [Theory]
    [MemberData(nameof(Formatlar))]
    public async Task Pilot1_Konkav_L_Ada_Uc_CAD_Formatinda_Ayni_Halka_Ve_1024_m2_Decimal_Gauss_Alani_Vermeli(string uzanti)
    {
        var halka = await IceAktarAsync(Uret(uzanti, LAda), uzanti);

        halka.Vertices.Should().BeEquivalentTo(LAda.Select(k => P(k.X, k.Y)));
        GaussAreaCalculator.Compute(halka).AreaM2Exact.Should().Be(1024m);
        PolygonTopolojisi.KendiniKesiyorMu(halka).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Formatlar))]
    public async Task Pilot1_Konkav_L_Adadan_13x16_Parsel_Guvenle_Kesilmeli_Ve_Alan_Korunmali(string uzanti)
    {
        var ada = await IceAktarAsync(Uret(uzanti, LAda), uzanti);

        var sonuc = BisectionEngine.Cut(new BisectionRequest(ada, CepheIndeksi(ada), 208m, Secenek()));

        sonuc.Result.IsSuccess.Should().BeTrue(sonuc.Result.IsFailure ? sonuc.Result.Error.Code : string.Empty);
        var k = sonuc.Result.Value;
        k.AngleDeg.Should().Be(90m);
        k.CutStart.Should().Be(P(0, 13));
        k.CutEnd.Should().Be(P(16, 13));
        k.FrontageLengthM.Should().Be(13.00m, "asgari cephe 13 m");
        k.PieceArea.AreaM2Exact.Should().Be(208m);
        k.RemainderArea.AreaM2Exact.Should().Be(816m);
        (k.PieceArea.AreaM2Exact + k.RemainderArea.AreaM2Exact).Should().Be(1024m, "kuantizasyon sonrası alan korunumu");

        // Kesim sonucu topoloji: parça + kalan adayı çakışmasız ve boşluksuz kaplar.
        new NtsParselTopolojiDenetcisi().Denetle([
            new ParselGeometrisi("ADA", ParselRolu.Kadastro, ada.Vertices),
            new ParselGeometrisi("A1", ParselRolu.Imar, k.Piece.Vertices),
            new ParselGeometrisi("A2", ParselRolu.Imar, k.Remainder.Vertices)]).Should().BeEmpty();
    }

    [Fact]
    public async Task Pilot1_Konkav_Kose_Uzerinden_Gecen_Kesim_Tek_Parcali_Olmali()
    {
        var ada = await IceAktarAsync(Dxf(LAda), ".dxf");

        // Hedef 624 = 16·30 + 24·(30 − 24): kesim Y = 30 konkav köşeyi (16,24) geçer, parça tek parçadır.
        var k = BisectionEngine.Cut(new BisectionRequest(ada, CepheIndeksi(ada), 624m, Secenek())).Result.Value;

        k.CutStart.Should().Be(P(0, 30));
        k.CutEnd.Should().Be(P(40, 30));
        k.PieceArea.AreaM2Exact.Should().Be(624m);
    }

    [Fact]
    public async Task Pilot1_U_Adada_Cok_Parcali_Kesim_Aciklanabilir_Bicimde_Durmali_ERR_TOPO_004()
    {
        var ada = await IceAktarAsync(Ncz(UAda), ".ncz");
        var cephe = ada.Vertices.ToList().IndexOf(P(100, 0));

        var sonuc = BisectionEngine.Cut(new BisectionRequest(ada, cephe, 900m, Secenek()));

        sonuc.Result.Error.Code.Should().Be(ErrorCodes.TopoKonkavKesimCokParcali);
        sonuc.Result.Error.RequiresEngineerReview.Should().BeTrue();
        sonuc.Attempts.Select(a => a.AngleDeg).Should().Equal(90m, 85m, 95m, 80m, 100m, 75m, 105m);
        sonuc.Attempts.Should().Contain(a => a.Outcome == AttemptOutcome.MultiPolygon);
    }

    [Fact]
    public async Task Pilot1_U_Adada_Baska_Acida_Tek_Parcali_Cozum_Varsa_Kabul_Edilmeli_SORU_023()
    {
        var ada = await IceAktarAsync(Dwg(UAda), ".dwg");
        var cephe = ada.Vertices.ToList().IndexOf(P(100, 0));

        var sonuc = BisectionEngine.Cut(new BisectionRequest(ada, cephe, 20m, Secenek()));

        sonuc.Result.IsSuccess.Should().BeTrue();
        sonuc.Result.Value.AngleDeg.Should().Be(95m);
        sonuc.Attempts[0].Outcome.Should().Be(AttemptOutcome.MultiPolygon);
    }
}

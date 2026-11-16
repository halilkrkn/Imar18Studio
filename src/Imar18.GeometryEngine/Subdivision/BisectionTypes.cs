using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.GeometryEngine.Subdivision;

/// <summary>
/// Tek kesim istegi: <paramref name="Block"/> adasini <paramref name="FrontageEdgeIndex"/> kenarindan
/// (Vk -&gt; Vk+1) baslayan ve <paramref name="TargetAreaM2"/> alanli parca + kalan olarak boler.
/// </summary>
public sealed record BisectionRequest(PolygonRing Block, int FrontageEdgeIndex, decimal TargetAreaM2, BisectionOptions Options);

/// <summary>
/// Bolme parametreleri; hicbirinin varsayilan degeri yoktur (mevzuat/muhendis girdisi — SORU-022, SORU-024).
/// Acilar cepheye gore derecedir ve spec 4.2 geregi 75–105 araligindadir.
/// </summary>
public sealed record BisectionOptions(
    decimal PreferredAngleDeg,
    decimal MinAngleDeg,
    decimal MaxAngleDeg,
    decimal AngleStepDeg,
    decimal ConvergenceToleranceM2,
    decimal PostQuantizationAreaToleranceM2,
    int MaxInnerIterations);

/// <summary>
/// Kabul edilen kesim: parca/kalan halkalari ortak, 1 mm izgarasindaki kesim koseleri <see cref="CutStart"/>/<see cref="CutEnd"/>'i paylasir.
/// <see cref="CutStartEdgeIndex"/>/<see cref="CutEndEdgeIndex"/> yeni koselerin eklendigi ada kenarlarini (Vi -&gt; Vi+1) verir;
/// <see cref="CutEndOnExistingVertex"/> true ise <see cref="CutEndEdgeIndex"/> mevcut kose indeksidir. Komsu parsel/yol halkalari
/// ayni kanonik koseyi bu bilgiyle ekler (SORU-030).
/// <see cref="BlockAreaDeviationM2"/> = (parca + kalan) − ada: kuantize kesim kosesinin ada kenarindan sapmasinin alan etkisi.
/// </summary>
public sealed record BisectionCut(
    PolygonRing Piece,
    PolygonRing Remainder,
    GeodeticPoint CutStart,
    GeodeticPoint CutEnd,
    int CutStartEdgeIndex,
    int CutEndEdgeIndex,
    bool CutEndOnExistingVertex,
    decimal AngleDeg,
    decimal FrontageLengthM,
    GaussArea PieceArea,
    GaussArea RemainderArea,
    decimal AreaDeviationM2,
    decimal RemainderAreaDeviationM2,
    decimal BlockAreaDeviationM2,
    int InnerIterations);

/// <summary>Tek aci denemesinin sonucu.</summary>
public enum AttemptOutcome
{
    Accepted,
    TargetUnreachable,
    NotConverged,
    MultiPolygon,
    QuantizationTopologyBroken,
    QuantizationAreaDeviation,
    DegeneratePiece,
}

/// <summary>Aci denemesi kaydi (AC-PILOT-01 "aciklanabilir durus").</summary>
public sealed record AngleAttempt(decimal AngleDeg, AttemptOutcome Outcome, int InnerIterations);

/// <summary>Bolme sonucu ve denenen acilar.</summary>
public sealed record BisectionOutcome(Result<BisectionCut> Result, IReadOnlyList<AngleAttempt> Attempts);

using Imar18.Domain.Geometry;

namespace Imar18.Presentation.Avalonia.Canvas;

/// <summary>SAHA-015: tuvalde çizilen kroki parselasyon parseli (aktif koordinat sisteminde açık halka + <c>Ada/Parsel • Malik • Alan m²</c> etiketi).</summary>
public sealed record ParselasyonCizimParseli(string Kod, IReadOnlyList<GeodeticPoint> Koseler, string Etiket);

/// <summary>
/// SAHA-015: CAD sahnesinin üstüne çizilen parselasyon katmanı. <see cref="Taslak"/> true ise onay bekleyen önizlemedir (kesikli parlak
/// çizgi + yarı saydam dolgu); false ise projeye uygulanmış kesin parselasyondur (düz çizgi). Görünüm hesap yapmaz.
/// </summary>
public sealed record ParselasyonKatmani(IReadOnlyList<ParselasyonCizimParseli> Parseller, bool Taslak);

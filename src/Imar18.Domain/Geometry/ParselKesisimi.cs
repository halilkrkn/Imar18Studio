namespace Imar18.Domain.Geometry;

/// <summary>
/// P7.3-B2 (SAHA-006): poligon kesişim alanı portu (Yaklaşık Yer Krokisi → Kroki satırları). Uygulaması GeometryEngine'dedir
/// (NTS overlay); Application NTS'ye bağımlı değildir. Alan yalnız dağıtım AĞIRLIĞI olarak kullanılır — tescil alanı her
/// imar parselinde Largest Remainder ile parsel alanına tam kapatılır; bu yüzden kayan nokta sonucu kapıda 6 ondalığa
/// (AwayFromZero) yuvarlanan <c>decimal</c> olarak döner.
/// </summary>
public interface IParselKesisimHesaplayici
{
    /// <summary>
    /// Verilen kapalı halkaların ortak kesişim alanı (m², 6 ondalık). Kesişim yoksa, halka sayısı 0 ise veya halkalardan biri
    /// alan oluşturmuyorsa 0. Kendini kesen halka sıfır tamponla onarılarak değerlendirilir.
    /// </summary>
    decimal KesisimAlaniM2(IReadOnlyList<IReadOnlyList<GeodeticPoint>> halkalar, CancellationToken cancellationToken = default);
}

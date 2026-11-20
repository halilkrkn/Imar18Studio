using Imar18.Domain.Geometry;

namespace Imar18.Application.Cad;

/// <summary>
/// SAHA-014: CAD katman belgesini (Deprem Öncesi) <c>.DNS</c> katsayılarıyla Deprem Sonrası'na ileri dönüştüren saf fonksiyon
/// (tam ondalık, <see cref="DonusumKatsayilari.Uygula"/>). Tuval sahnesi ve Yaklaşık Yer Krokisi kesişimi için kullanılır; katman,
/// etiket, kapalılık ve sayaçlar korunur. Kaynak dosyadaki alan özniteliği (<see cref="CadPolyline.DeclaredAreaM2"/>) Deprem
/// Öncesi geometrisine ait olduğundan düşürülür (resmî alan her zaman decimal Gauss ile yeniden hesaplanır).
/// </summary>
public static class CadBelgesiDonusumu
{
    /// <summary>Katsayı yoksa belgeyi aynen döner.</summary>
    public static CadLayerDocument Uygula(CadLayerDocument belge, DonusumKatsayilari? katsayilar)
    {
        ArgumentNullException.ThrowIfNull(belge);
        if (katsayilar is null)
        {
            return belge;
        }

        return belge with
        {
            Polylines = belge.Polylines
                .Select(p => p with { Vertices = p.Vertices.Select(katsayilar.Uygula).ToArray(), DeclaredAreaM2 = null })
                .ToArray(),
            // SAHA-015: kroki yazı konumu ve (varsa) görünen merkezi aynı katsayılarla dönüştürülür.
            Texts = belge.Texts.Select(t => t with
            {
                Position = katsayilar.Uygula(t.Position),
                Merkez = t.Merkez is { } m ? katsayilar.Uygula(m) : null,
            }).ToArray(),
        };
    }
}

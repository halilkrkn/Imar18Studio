using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;

namespace Imar18.GeometryEngine.Tecviz;

/// <summary>Hata çemberi zarfı (m², 2 ondalık AwayFromZero) ve nokta konum doğruluğu m_p (m, 6 ondalık).</summary>
public sealed record HataCemberiZarfi(decimal NoktaKonumDogruluguM, decimal AlanM2, decimal IcAlanM2, decimal DisAlanM2);

/// <summary>
/// Geçici koordinat profili YARDIMCI hesaplayıcısı (TPYS Md. 8(5), spec 4.3.3 / TC-TECVIZ-002; VARSAYILAN DEĞİL).
/// Her köşede r = m_p = √(m_x² + m_y²) yarıçaplı hata çemberi; dış zarf = parselin r kadar dışa ötelenmesi (yuvarlak köşe),
/// iç zarf = r kadar içe ötelenmesi. NTS tipleri bu sınıfın dışına çıkmaz; alanlar kapıda decimal'e çevrilir.
/// </summary>
public static class HataCemberiHesaplayici
{
    /// <summary>Çeyrek daire başına doğru parçası (yay yaklaşımı; 64 → π r² hatası ≈ 10⁻⁴ r²).</summary>
    public const int CeyrekDaireParcaSayisi = 64;

    private static readonly GeometryFactory Factory = new();

    public static Result<HataCemberiZarfi> Hesapla(PolygonRing ring, decimal mx, decimal my)
    {
        ArgumentNullException.ThrowIfNull(ring);

        if (mx < 0m || my < 0m)
        {
            return Hata(ErrorCodes.TecvizGirdisiGecersiz, "{reason}", "negative point accuracy");
        }

        // Taşma koruması: doğruluk, PolygonRing yerel kesinlik zarfını aşamaz (decimal mx*mx OverflowException yerine hata).
        if (mx > PolygonRing.MaxLocalCoordinateM || my > PolygonRing.MaxLocalCoordinateM)
        {
            return Hata(ErrorCodes.TecvizGirdisiGecersiz, "{reason}", "point accuracy exceeds supported range");
        }

        var r = Math.Sqrt((double)(mx * mx + my * my));
        var origin = ring.Vertices[0];
        var coords = ring.Vertices
            .Select(v => CoordinateAxisSwapAdapter.ToNts(v, origin))
            .Append(CoordinateAxisSwapAdapter.ToNts(origin, origin))
            .ToArray();
        var polygon = Factory.CreatePolygon(coords);

        // Domain halkası kendi üzerinde kesişimi denetlemez (P2 reviewer M4); Buffer öncesi NTS geçerliliği zorunlu.
        if (!polygon.IsValid)
        {
            return Hata(ErrorCodes.TopoKendiUzerindeKesisim, "{parcelId}", "error-circle input ring");
        }

        var parametre = new BufferParameters(CeyrekDaireParcaSayisi, EndCapStyle.Round, JoinStyle.Round, BufferParameters.DefaultMitreLimit);
        var dis = r > 0 ? polygon.Buffer(r, parametre).Area : polygon.Area;
        var ic = r > 0 ? polygon.Buffer(-r, parametre).Area : polygon.Area;

        return Result<HataCemberiZarfi>.Success(new HataCemberiZarfi(
            Math.Round((decimal)r, 6, MidpointRounding.AwayFromZero),
            GaussAreaCalculator.Compute(ring).AreaM2,
            Alan(ic),
            Alan(dis)));
    }

    private static Result<HataCemberiZarfi> Hata(string kod, string yerTutucu, string deger)
    {
        var hata = ErrorCatalog.Get(kod);
        return Result<HataCemberiZarfi>.Failure(hata with
        {
            TechnicalMessage = hata.TechnicalMessage.Replace(yerTutucu, deger, StringComparison.Ordinal),
        });
    }

    private static decimal Alan(double alan) => Math.Round((decimal)alan, 2, MidpointRounding.AwayFromZero);
}

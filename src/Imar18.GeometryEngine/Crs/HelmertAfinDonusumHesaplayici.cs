using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.GeometryEngine.Crs;

/// <summary>
/// P7.3-C2: <see cref="IDonusumHesaplayici"/> portunun en küçük kareler 2B Helmert / Afin uygulaması
/// (<see cref="HelmertAfinHesaplayici"/>). Kaynak = I. sistem (deprem öncesi), hedef = II. sistem (deprem sonrası).
/// Parametreler Netcad <c>.DNS</c> <c>A, B, C, D, CY, CX</c> düzenine çevrilir (<c>Y2 = A·Y1 + C·X1 + CY</c>,
/// <c>X2 = B·Y1 + D·X1 + CX</c>; SAHA-014'te gerçek DNS ile doğrulandı):
/// Helmert <c>X' = aX − bY + tx, Y' = bX + aY + ty</c> → <c>A = a, B = −b, C = b, D = a, CY = ty, CX = tx</c>;
/// Afin <c>X' = a0 + a1X + a2Y, Y' = b0 + b1X + b2Y</c> → <c>A = b2, B = a2, C = b1, D = a1, CY = b0, CX = a0</c>.
/// Dönüklük Helmert dönme açısıdır (grad, atan2(b, a)).
/// </summary>
public sealed class HelmertAfinDonusumHesaplayici : IDonusumHesaplayici
{
    public Result<DonusumParametreleri> Hesapla(DonusumModeli model, IReadOnlyList<DonusumOrtakNoktasi> ortakNoktalar)
    {
        ArgumentNullException.ThrowIfNull(ortakNoktalar);
        if (!Enum.IsDefined(model) || ortakNoktalar.Any(n => n?.NoktaNo is null))
        {
            var e = ErrorCatalog.Get(ErrorCodes.CrsDonusumHesaplanamadi);
            return Result<DonusumParametreleri>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", "undefined model or null common point", StringComparison.Ordinal) });
        }

        var noktalar = ortakNoktalar
            .Select(n => new OrtakNokta(n.NoktaNo, new GeodeticPoint(X: n.X1, Y: n.Y1), new GeodeticPoint(X: n.X2, Y: n.Y2)))
            .ToArray();
        var rapor = HelmertAfinHesaplayici.Hesapla(model, noktalar, null);
        if (rapor.IsFailure)
        {
            return Result<DonusumParametreleri>.Failure(rapor.Error);
        }

        var r = rapor.Value;
        var p = r.Parametreler;
        var (a, b, c, d, cy, cx) = model == DonusumModeli.Helmert2B
            ? (p["a"], -p["b"], p["b"], p["a"], p["ty"], p["tx"])
            : (p["b2"], p["a2"], p["b1"], p["a1"], p["b0"], p["a0"]);

        var hedef = ortakNoktalar.ToDictionary(n => n.NoktaNo.Trim(), StringComparer.Ordinal);
        var artiklar = r.Artiklar
            .Select(v => new DonusumArtigi(v.NoktaId, hedef[v.NoktaId].Y2 + v.Vy, hedef[v.NoktaId].X2 + v.Vx, v.Vy, v.Vx))
            .ToArray();
        return Result<DonusumParametreleri>.Success(new DonusumParametreleri(
            Model: model, A: a, B: b, C: c, D: d, Cy: cy, Cx: cx,
            OlcekKatsayisi: r.OlcekFaktoru, DonuklukGrad: r.DonmeAcisiGrad, OrtalamaHataM: r.KareselOrtalamaHataM, Artiklar: artiklar));
    }
}

using Imar18.Domain.Diagnostics;

namespace Imar18.Domain.Geometry;

/// <summary>
/// Proje CRS/dilim ve pafta olcegi girdisi (D3, SORU-009: sabit degil, proje/parsel girdisi).
/// Yalnizca dogrulama yapar; gecerli CRS listesi kodlanmaz ve donusum yapilmaz (SORU-028).
/// </summary>
public sealed class ProjectCoordinateReference
{
    private ProjectCoordinateReference(string crsId, int? zoneCentralMeridianDeg, decimal? epoch, int mapScaleDenominator)
    {
        CrsId = crsId;
        ZoneCentralMeridianDeg = zoneCentralMeridianDeg;
        Epoch = epoch;
        MapScaleDenominator = mapScaleDenominator;
    }

    /// <summary>Muhendisin girdigi CRS kimligi (bicimi SORU-028'e bagli).</summary>
    public string CrsId { get; }

    /// <summary>TM dilim orta meridyeni (derece), girildiyse.</summary>
    public int? ZoneCentralMeridianDeg { get; }

    /// <summary>Koordinat epoku, girildiyse.</summary>
    public decimal? Epoch { get; }

    /// <summary>Pafta olcegi paydasi (1/M icin M).</summary>
    public int MapScaleDenominator { get; }

    /// <returns>CRS kimligi bossa ERR-CRS-001; olcek paydasi pozitif degilse ERR-CRS-003.</returns>
    public static Result<ProjectCoordinateReference> Create(string? crsId, int? zoneCentralMeridianDeg, decimal? epoch, int mapScaleDenominator)
    {
        if (string.IsNullOrWhiteSpace(crsId))
        {
            var eksik = ErrorCatalog.Get(ErrorCodes.CrsEksik);
            return Result<ProjectCoordinateReference>.Failure(
                eksik with { TechnicalMessage = eksik.TechnicalMessage.Replace("{projectId}", "project", StringComparison.Ordinal) });
        }

        if (mapScaleDenominator <= 0)
        {
            var gecersiz = ErrorCatalog.Get(ErrorCodes.CrsGirdisiGecersiz);
            return Result<ProjectCoordinateReference>.Failure(
                gecersiz with { TechnicalMessage = gecersiz.TechnicalMessage.Replace("{reason}", "map scale denominator must be positive", StringComparison.Ordinal) });
        }

        return Result<ProjectCoordinateReference>.Success(
            new ProjectCoordinateReference(crsId.Trim(), zoneCentralMeridianDeg, epoch, mapScaleDenominator));
    }
}

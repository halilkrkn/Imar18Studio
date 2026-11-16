using Imar18.Domain.Diagnostics;

namespace Imar18.Domain.Geometry;

/// <summary>
/// Jeodezik yatay nokta (spec 3.2 madde 3): <see cref="X"/> = Yukari/Kuzey, <see cref="Y"/> = Saga/Dogu (metre, decimal).
/// Eksen karisikligini onlemek icin cagrilarda adlandirilmis argüman (<c>X:</c>, <c>Y:</c>) kullanilir.
/// </summary>
public readonly record struct GeodeticPoint(decimal X, decimal Y);

/// <summary>CAD girdisinden gelen 3B nokta; alan/topoloji oncesi <see cref="HorizontalProjection"/> ile Z=0 duzlemine indirgenir.</summary>
public readonly record struct GeodeticPoint3D(decimal X, decimal Y, decimal Z);

/// <summary>Yatay izdusum sonucu: 2B noktalar ve (Z != 0 varsa) tek ERR-GIRD-004 uyarisi.</summary>
public sealed record HorizontalProjectionResult(IReadOnlyList<GeodeticPoint> Points, IReadOnlyList<AppError> Warnings);

/// <summary>Z=0 yatay izdusum kapisi (spec 4.1 son paragraf, AC-CAD-02): Z atilir, X/Y aynen korunur.</summary>
public static class HorizontalProjection
{
    public static HorizontalProjectionResult ToHorizontal(IReadOnlyList<GeodeticPoint3D> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var sonuc = new GeodeticPoint[points.Count];
        var zSayisi = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.Z != 0m)
            {
                zSayisi++;
            }

            sonuc[i] = new GeodeticPoint(X: p.X, Y: p.Y);
        }

        IReadOnlyList<AppError> uyarilar = [];
        if (zSayisi > 0)
        {
            var hata = ErrorCatalog.Get(ErrorCodes.GirdZKoduIndirgendi);
            // Koordinat degeri mesaja yazilmaz (gizli koordinat kurali, security.md).
            uyarilar = [hata with { TechnicalMessage = hata.TechnicalMessage.Replace(
                "{path}", $"input ({zSayisi} of {points.Count} vertices)", StringComparison.Ordinal) }];
        }

        return new HorizontalProjectionResult(Array.AsReadOnly(sonuc), uyarilar);
    }
}

/// <summary>
/// 1 mm (0.001 m) koordinat izgarasi (spec 4.2, nettopologysuite skill). Yarim deger sifirdan uzaga yuvarlanir
/// (varsayim — STATUS SORU-026). Kuantizasyon daima MUTLAK koordinatta yapilir; yerel ofsette yapilmaz.
/// </summary>
public static class CoordinateQuantizer
{
    /// <summary>Ondalik basamak sayisi (1 mm).</summary>
    public const int Scale = 3;

    public static decimal Quantize(decimal value) => decimal.Round(value, Scale, MidpointRounding.AwayFromZero);

    public static GeodeticPoint Quantize(GeodeticPoint point) => new(X: Quantize(point.X), Y: Quantize(point.Y));

    public static bool IsOnGrid(GeodeticPoint point) => Quantize(point.X) == point.X && Quantize(point.Y) == point.Y;
}

using Imar18.Domain.Diagnostics;

namespace Imar18.Domain.Geometry;

/// <summary>
/// P7.3-C2 (SAHA-007): ortak nokta satırı <c>NoktaNo;Y1;X1;Y2;X2</c>. I. sistem (<c>Y1, X1</c>) deprem öncesi, II. sistem
/// (<c>Y2, X2</c>) deprem sonrası koordinatlarıdır (jeodezik: Y Sağa/Doğu, X Yukarı/Kuzey; metre).
/// </summary>
public sealed record DonusumOrtakNoktasi(string NoktaNo, decimal Y1, decimal X1, decimal Y2, decimal X2);

/// <summary>
/// P7.3-C2: proje dönüşüm girdisi (RevisionId'ye dahildir). Proje noktaları "deprem sonrası" (II. sistem) kabul edilir;
/// "deprem öncesi" (I. sistem) koordinatları I → II dönüşümünün tersiyle hesaplanır. <see cref="Mk"/> koordine özetteki
/// <c>MK</c> sütunudur (mühendis girdisi; arayüz varsayılanı 0.09 — SORU-041).
/// </summary>
public sealed record DonusumGirdisi(DonusumModeli Model, decimal Mk, IReadOnlyList<DonusumOrtakNoktasi> OrtakNoktalar)
{
    /// <summary>Arayüz varsayılan MK değeri (SORU-041 varsayımı).</summary>
    public const decimal VarsayilanMk = 0.09m;

    /// <summary>Kabul edilen en büyük MK (m) — uygulama güvenlik sınırı, mevzuat değeri değildir.</summary>
    public const decimal MaxMk = 10m;

    /// <summary>Modelin gerektirdiği en az ortak nokta sayısı (Helmert 2, Afin 3).</summary>
    public static int EnAzNokta(DonusumModeli model) => model == DonusumModeli.Helmert2B ? 2 : 3;
}

/// <summary>Ortak noktanın dönüşüm sonrası (II. sistemde hesaplanan) koordinatı ve hatası: V = hesaplanan − II. sistem.</summary>
public sealed record DonusumArtigi(string NoktaNo, decimal Y, decimal X, decimal Vy, decimal Vx);

/// <summary>
/// I → II sistemi 2B dönüşüm parametreleri (Netcad "A, B, C, D, CY, CX" düzeni):
/// <c>Y2 = A·Y1 + B·X1 + CY</c>, <c>X2 = C·Y1 + D·X1 + CX</c>. Helmert'te <c>C = −B</c>, <c>D = A</c>. Ölçek ve dönüklük yalnız
/// Helmert'te tanımlıdır; ortalama hata <c>m0 = √(Σv² / f)</c> (f = 0 ise null).
/// </summary>
public sealed record DonusumParametreleri(
    DonusumModeli Model,
    decimal A,
    decimal B,
    decimal C,
    decimal D,
    decimal Cy,
    decimal Cx,
    decimal? OlcekKatsayisi,
    decimal? DonuklukGrad,
    decimal? OrtalamaHataM,
    IReadOnlyList<DonusumArtigi> Artiklar)
{
    /// <summary>I. sistemden II. sisteme (deprem öncesi → sonrası).</summary>
    public GeodeticPoint Uygula(GeodeticPoint birinci) => new(
        X: (C * birinci.Y) + (D * birinci.X) + Cx,
        Y: (A * birinci.Y) + (B * birinci.X) + Cy);

    /// <summary>
    /// II. sistemden I. sisteme (deprem sonrası → öncesi): 2×2 matrisin tam ondalık tersi. Determinant sıfırsa
    /// <c>ERR-CRS-004</c>.
    /// </summary>
    public Result<GeodeticPoint> TersUygula(GeodeticPoint ikinci)
    {
        var det = (A * D) - (B * C);
        if (det == 0m)
        {
            var e = ErrorCatalog.Get(ErrorCodes.CrsDonusumHesaplanamadi);
            return Result<GeodeticPoint>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", "transformation matrix is singular", StringComparison.Ordinal) });
        }

        var dy = ikinci.Y - Cy;
        var dx = ikinci.X - Cx;
        return Result<GeodeticPoint>.Success(new GeodeticPoint(
            X: ((A * dx) - (C * dy)) / det,
            Y: ((D * dy) - (B * dx)) / det));
    }
}

/// <summary>
/// P7.3-C2: ortak noktalı 2B Helmert / Afin dönüşüm portu. Uygulaması GeometryEngine'dedir (<c>HelmertAfinHesaplayici</c>);
/// Application hesap motoruna doğrudan bağlanmaz. Hata: <c>ERR-CRS-004</c>.
/// </summary>
public interface IDonusumHesaplayici
{
    Result<DonusumParametreleri> Hesapla(DonusumModeli model, IReadOnlyList<DonusumOrtakNoktasi> ortakNoktalar);
}

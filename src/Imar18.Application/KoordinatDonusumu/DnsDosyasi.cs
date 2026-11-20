using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.Application.KoordinatDonusumu;

/// <summary>
/// SAHA-014: okunmuş Netcad <c>.DNS</c> dönüşüm dosyası. <see cref="Katsayilar"/> <c>$AMAT</c> satırındaki değerlerdir (birebir);
/// <see cref="OrtakNoktalar"/> <c>$NOK</c> bölümünün dosya sırasıyla, dolgu satırları atılmış hâlidir; <see cref="Parametreler"/>
/// bu katsayılarla ortak noktalardan hesaplanan artıklar (Vy, Vx), ölçek katsayısı, dönüklük ve ortalama hatayı taşır
/// (<see cref="DonusumParametreleri.Katsayilardan"/>).
/// </summary>
public sealed record DnsDosyasi(DonusumKatsayilari Katsayilar, IReadOnlyList<DonusumOrtakNoktasi> OrtakNoktalar, DonusumParametreleri Parametreler)
{
    /// <summary>
    /// 1. adımda projeye uygulanacak dönüşüm girdisi: model katsayıların biçiminden (Helmert / Afin), ortak noktalar DNS'ten,
    /// MK mühendis girdisidir.
    /// </summary>
    public DonusumGirdisi Girdi(decimal mk) => new(Parametreler.Model, mk, OrtakNoktalar);
}

/// <summary>
/// SAHA-014: Netcad <c>.DNS</c> okuyucu portu (Infrastructure: <c>NetcadDnsOkuyucu</c>). Bozuk dosyada istisna atmaz; Türkçe
/// kullanıcı mesajlı <c>ERR-GIRD-011</c> veya <c>ERR-CRS-004</c> döner; hata metinlerine koordinat yazılmaz. İptalde
/// <see cref="OperationCanceledException"/> atar. Akış kapatılmaz.
/// </summary>
public interface IDnsDosyaOkuyucu
{
    Result<DnsDosyasi> Oku(Stream kaynak, CancellationToken cancellationToken = default);
}

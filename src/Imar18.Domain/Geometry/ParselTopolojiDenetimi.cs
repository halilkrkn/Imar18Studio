namespace Imar18.Domain.Geometry;

/// <summary>Parseller arası topoloji bulgusunun türü (P6, SORU-034 k).</summary>
public enum TopolojiBulguTuru
{
    /// <summary>İki kadastro (giren) parselinin iç alanları çakışıyor.</summary>
    KadastroCakismasi,

    /// <summary>İki oluşan parselin (imar, yol terk, kamu alanı) iç alanları çakışıyor.</summary>
    OlusanCakismasi,

    /// <summary>Kadastro parselleri birleşiminde iç boşluk (delik) var.</summary>
    KadastroBoslugu,

    /// <summary>Düzenlemeye giren alanın oluşan parsellerle karşılanmayan kısmı (oluşan parseller arası boşluk).</summary>
    OlusanKapsamaBoslugu,

    /// <summary>Oluşan parsellerin kadastro (düzenleme) sınırı dışına taşan kısmı.</summary>
    KadastroDisinaTasma,

    /// <summary>Geometri motoru analizi tamamlayamadı (ör. topoloji istisnası); hesap eksik sayılır.</summary>
    AnalizYapilamadi,
}

/// <summary>
/// Tek topoloji bulgusu: tür, ilgili parsel kimlikleri (ordinal sıralı) ve alan (m², 2 ondalık AwayFromZero).
/// Boşluk/taşma bulgularında kimlik listesi analize giren parsellerdir.
/// </summary>
public sealed record ParselTopolojiBulgusu(TopolojiBulguTuru Tur, IReadOnlyList<string> ParselIdleri, decimal AlanM2);

/// <summary>
/// Parseller arası çakışma/boşluk/taşma analizi portu (Domain sözleşmesi; NTS overlay uygulaması GeometryEngine'dedir —
/// Application NetTopologySuite'e bağımlı olamaz). Çağıran kendini kesen halkaları vermez (onlar ayrıca <c>ERR-TOPO-001</c> ile
/// raporlanır); denetçinin geçersiz saydığı halka sessizce atlanmaz, <see cref="TopolojiBulguTuru.AnalizYapilamadi"/> olarak döner.
/// Bulgu sırası deterministiktir (tür, sonra kimlikler).
/// </summary>
public interface IParselTopolojiDenetcisi
{
    IReadOnlyList<ParselTopolojiBulgusu> Denetle(IReadOnlyList<ParselGeometrisi> parseller, CancellationToken cancellationToken = default);
}

/// <summary>Topoloji analizi sabitleri (uygulama eşiği; mevzuat değeri değildir — SORU-035).</summary>
public static class ParselTopolojisi
{
    /// <summary>
    /// Raporlama eşiği: alan 2 ondalığa (AwayFromZero) yuvarlandığında en az 0.01 m² olan çakışma/boşluk/taşma bulgudur
    /// (cetvellerdeki en küçük alan birimi; 1 mm altı çift kayan nokta kıymıkları raporlanmaz).
    /// </summary>
    public const decimal AlanEsigiM2 = 0.01m;

    /// <summary>Oluşan (yeni) parsel rolleri: imar, yol terk ve kamu alanı.</summary>
    public static bool OlusanMi(ParselRolu rol) => rol is ParselRolu.Imar or ParselRolu.YolTerk or ParselRolu.KamuAlani;
}

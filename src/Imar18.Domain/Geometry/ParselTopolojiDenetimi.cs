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

    /// <summary>
    /// SAHA-004: oluşan parsellerle karşılanmayan, en az iki farklı imar adasına komşu alan — çizilmemiş imar yolu / kamu alanı.
    /// Parseller arası boşluk (<see cref="OlusanKapsamaBoslugu"/>) sayılmaz; bilgi olarak raporlanır.
    /// </summary>
    ImarYoluAlani,
}

/// <summary>
/// Tek topoloji bulgusu: tür, ilgili parsel kimlikleri (ordinal sıralı) ve alan (m², 2 ondalık AwayFromZero).
/// Çakışmada kimlikler parsel çiftidir; taşmada taşan parsel + komşu kadastro parselleri; boşlukta boşluk bileşenine komşu
/// parsellerdir (SAHA-004: bileşen / parsel bazında). <paramref name="SinirToleransiIcinde"/>: milimetrik sınır dokunması
/// (alan ≤ <see cref="ParselTopolojisi.SinirToleransiAlanM2"/> veya her yerde <see cref="ParselTopolojisi.SinirSeridiGenislikM"/>'den
/// dar şerit) — onay engeli değil, mühendis incelemeli uyarıdır.
/// </summary>
public sealed record ParselTopolojiBulgusu(
    TopolojiBulguTuru Tur, IReadOnlyList<string> ParselIdleri, decimal AlanM2, bool SinirToleransiIcinde = false);

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

    /// <summary>
    /// SAHA-004 (SORU-037, uygulama eşiği): alanı bu değere eşit veya küçük çakışma/boşluk/taşma milimetrik sınır dokunmasıdır.
    /// </summary>
    public const decimal SinirToleransiAlanM2 = 0.05m;

    /// <summary>
    /// SAHA-004 (SORU-037, uygulama eşiği): her yerde bu genişlikten (1 cm) dar uzun şerit, alan eşiğini aşsa da kadastro–imar
    /// sınır toleransı sayılır (gerçek Netcad çizimlerinde ölçülen şeritler ≤ 2.5 mm).
    /// </summary>
    public const decimal SinirSeridiGenislikM = 0.01m;

    /// <summary>
    /// Reviewer H2 (SORU-037, uygulama eşiği): dar şerit kuralının alan üst sınırı. 1 cm'den dar olsa da bu değeri aşan şerit
    /// (ör. 9 mm × 100 m = 0.90 m²) sınır toleransı sayılmaz, onay engeli kalır.
    /// </summary>
    public const decimal SinirSeridiAzamiAlanM2 = 0.50m;

    /// <summary>
    /// Reviewer H2 (SORU-037, uygulama eşiği): projedeki tüm sınır toleransı bulgularının toplam alan tavanı. Aşılırsa
    /// milimetrik sayılmaz; tüm tolerans bulguları onay engeline (Critical) yükseltilir.
    /// </summary>
    public const decimal ToplamSinirToleransiAzamiM2 = 1.00m;

    /// <summary>Oluşan (yeni) parsel rolleri: imar, yol terk ve kamu alanı.</summary>
    public static bool OlusanMi(ParselRolu rol) => rol is ParselRolu.Imar or ParselRolu.YolTerk or ParselRolu.KamuAlani;

    /// <summary>"ada/parsel" kimliğinden imar adası kodu ('/' öncesi, kırpılmış); tam bir '/' yoksa null (ada tahmin edilmez).</summary>
    public static string? ImarAdasi(string? parselId)
    {
        if (string.IsNullOrWhiteSpace(parselId))
        {
            return null;
        }

        var i = parselId.IndexOf('/', StringComparison.Ordinal);
        if (i <= 0 || i == parselId.Length - 1 || parselId.IndexOf('/', i + 1) >= 0)
        {
            return null; // "ada/parsel" dışı (ör. "1/2/3") — ada tahmin edilmez (reviewer L2)
        }

        var ada = parselId[..i].Trim();
        return ada.Length == 0 ? null : ada;
    }
}

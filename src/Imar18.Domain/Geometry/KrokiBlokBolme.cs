using Imar18.Domain.Diagnostics;

namespace Imar18.Domain.Geometry;

/// <summary>
/// SAHA-015: Yaklaşık Yer Krokisi'nde mühendisin blok içine kabaca çizdiği açık bölme çizgisi (jeodezik X = Kuzey, Y = Doğu).
/// Yalnız doğrultusu ve konumu kullanılır; uçların blok sınırına tam değmesi gerekmez (kesim sonsuz doğru boyunca yapılır).
/// </summary>
public sealed record BolmeDogrusu(GeodeticPoint Baslangic, GeodeticPoint Bitis);

/// <summary>
/// SAHA-015: bloğun krokideki bölme çizgileriyle ayrılan taslak bölgeleri. <see cref="SiraliCizgiler"/> çizgilerin bölge
/// sırasındaki dizilişidir (ortalama çizgi doğrultusuna dik, Doğu'ya bakan normal boyunca batıdan doğuya; normal tam Kuzey–Güney
/// ise güneyden kuzeye); <see cref="Bolgeler"/> bu sırayla <c>çizgi sayısı + 1</c> açık köşe dizisidir (kapanış tekrarı yok, blokla
/// aynı yön, 6 ondalık).
/// </summary>
public sealed record KrokiTaslakBolmesi(IReadOnlyList<BolmeDogrusu> SiraliCizgiler, IReadOnlyList<IReadOnlyList<GeodeticPoint>> Bolgeler);

/// <summary>
/// SAHA-015 (SORU-044) kroki blok bölme portu. Uygulaması GeometryEngine'dedir (çift hassasiyetli yarı düzlem kırpma + ikili arama;
/// NTS geçerlilik denetimi); Application NTS'ye bağımlı değildir. Hiçbir uygulama istisna atmaz (iptal hariç): geçersiz girdi
/// <c>ERR-PLAN-003</c>, çok parçalı (konkav) kesim <c>ERR-TOPO-004</c>, yakınsamama <c>ERR-PLAN-004</c>, dejenere parça
/// <c>ERR-TOPO-003</c> döner. Yeni kesim köşeleri mutlak koordinatta 6 ondalığa (<see cref="PolygonRing.MaxScale"/>) yuvarlanır;
/// komşu parçalar aynı köşeyi paylaşır, mevcut blok köşeleri aynen korunur.
/// </summary>
public interface IKrokiBlokBolucu
{
    /// <summary>Bloğu krokideki çizgilerin kendi konumlarından keserek taslak bölgeleri (kabaca alanlar) çıkarır.</summary>
    Result<KrokiTaslakBolmesi> TaslakBol(IReadOnlyList<GeodeticPoint> blok, IReadOnlyList<BolmeDogrusu> cizgiler, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bloğu <paramref name="siraliCizgiler"/> sırasıyla, her kesimi kendi kroki çizgisine paralel kaydırarak (ikili arama) böler:
    /// i. parça alanı <paramref name="hedefAlanlarM2"/>[i] olur; son parça kalandır. Hedef sayısı çizgi sayısı + 1 olmalıdır.
    /// </summary>
    Result<IReadOnlyList<IReadOnlyList<GeodeticPoint>>> KesinBol(
        IReadOnlyList<GeodeticPoint> blok,
        IReadOnlyList<BolmeDogrusu> siraliCizgiler,
        IReadOnlyList<decimal> hedefAlanlarM2,
        CancellationToken cancellationToken = default);
}

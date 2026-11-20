using System.Collections.ObjectModel;

namespace Imar18.Application.Cad;

/// <summary>CAD katmaninin duzenleme icindeki rolu (roadmap §6 P4.1 standart katman eslemesi, spec §25.7).</summary>
public enum CadLayerRole
{
    /// <summary>Rol atanmamis (muhendis atamasi gerekir).</summary>
    Unassigned,

    /// <summary>Duzenlemeye giren kadastro parseli (<c>TK_PARSEL_GİREN</c>).</summary>
    CadastralParcel,

    /// <summary>Olusan imar parseli (<c>TK_PARSEL_OLUŞAN</c>).</summary>
    ZoningParcel,

    /// <summary>Yola terk alani (<c>TK_PARSEL_YOLATERK</c>).</summary>
    RoadDedication,

    /// <summary>Duzenleme siniri (<c>DÜZENLEME_SINIRI</c>).</summary>
    RegulationBoundary,

    /// <summary>Ada kenari (<c>ADA_KENARLT</c>, <c>PLAN_ADAKENARI</c>).</summary>
    BlockEdge,

    /// <summary>Park alani (<c>PL_PARK</c>).</summary>
    Park,

    /// <summary>Kadastro hatti (<c>KADASTRO_HAT</c>).</summary>
    CadastralLine,

    /// <summary>Yeni parsel noktasi (<c>NOKTA_PARSEL_YENI</c>).</summary>
    NewParcelPoint,

    /// <summary>Tescilli nokta (<c>TESCİLLİ_NOKTA</c>).</summary>
    RegisteredPoint,

    /// <summary>
    /// İmar adası (SAHA-010 / SORU-054): projede İmar Parseli varsa yalnız görsel ada sınırı referansıdır, R2'ye eklenmez;
    /// hiç İmar Parseli yoksa R2 ada alanlarından hesaplanır (ada, imar parseli rolüyle projeye girer).
    /// </summary>
    ZoningBlock,

    /// <summary>Yoksay (SAHA-010): katman tuvalde görünür kalır, hiçbir hesaba girmez.</summary>
    Ignored,
}

/// <summary>
/// Katman adi -> rol eslemesi. <see cref="Standard"/> roadmap'teki Netcad standart adlarini tasir; P4.0 bulgusu
/// (F7, STATUS SORU-032 b) geregi katman adlari projeler arasinda standart olmadigindan muhendis
/// <see cref="WithAssignment"/> ile proje bazli atama yapar. Karsilastirma bas/son bosluk kirpilmis ve
/// buyuk/kucuk harf duyarli (ordinal) yapilir; Turkce harf donusumu tahmin edilmez.
/// Ornekler degismezdir (immutable).
/// </summary>
public sealed class CadLayerRoleMap
{
    private readonly ReadOnlyDictionary<string, CadLayerRole> _assignments;

    private CadLayerRoleMap(SortedDictionary<string, CadLayerRole> assignments)
    {
        _assignments = new ReadOnlyDictionary<string, CadLayerRole>(assignments);
    }

    /// <summary>Roadmap §6 P4.1 / spec §25.7 standart Netcad katman adlari.</summary>
    public static CadLayerRoleMap Standard { get; } = new(new SortedDictionary<string, CadLayerRole>(StringComparer.Ordinal)
    {
        ["TK_PARSEL_GİREN"] = CadLayerRole.CadastralParcel,
        ["TK_PARSEL_OLUŞAN"] = CadLayerRole.ZoningParcel,
        ["TK_PARSEL_YOLATERK"] = CadLayerRole.RoadDedication,
        ["DÜZENLEME_SINIRI"] = CadLayerRole.RegulationBoundary,
        ["ADA_KENARLT"] = CadLayerRole.BlockEdge,
        ["PLAN_ADAKENARI"] = CadLayerRole.BlockEdge,
        ["PL_PARK"] = CadLayerRole.Park,
        ["KADASTRO_HAT"] = CadLayerRole.CadastralLine,
        ["NOKTA_PARSEL_YENI"] = CadLayerRole.NewParcelPoint,
        ["TESCİLLİ_NOKTA"] = CadLayerRole.RegisteredPoint,
    });

    /// <summary>
    /// SAHA-013: 1. adımda katmanlar yüklenirken önerilen rol (mühendis her satırı değiştirebilir). Kurallar bu sırayla uygulanır
    /// ve ilk eşleşen kazanır:
    /// <list type="number">
    /// <item>Adında <c>OLUSAN</c> veya <c>OLUŞAN</c> geçen her katman (<c>TK_OLUSAN</c>, <c>TK_PARSEL_OLUŞAN</c>, <c>PARSEL_OLUSAN</c> …)
    /// → <see cref="CadLayerRole.ZoningParcel"/>; diğer tüm kurallardan önce gelir, kadastro parseli sayılmaz.</item>
    /// <item>Adı tam <c>İMAR</c> veya <c>IMAR</c> → <see cref="CadLayerRole.Ignored"/> (tuvalde görünür, DOP R2'ye girmez).</item>
    /// <item><see cref="Standard"/> birebir Netcad adları.</item>
    /// <item>Adında <c>PARSEL_ALN</c> geçen (ör. <c>02_PARSEL_ALN_YENI</c>) → <see cref="CadLayerRole.CadastralParcel"/>.</item>
    /// <item>Adı tam <c>YOL</c> → <see cref="CadLayerRole.RoadDedication"/>.</item>
    /// </list>
    /// Diğer adlar <see cref="CadLayerRole.Unassigned"/> kalır. Karşılaştırma <see cref="Resolve"/> ile aynıdır (baş/son boşluk
    /// kırpılmış, ordinal / büyük-küçük harf duyarlı; Türkçe harf dönüşümü tahmin edilmez). <see cref="Standard"/> ve
    /// <see cref="Resolve"/> değişmez; öneri yalnız sihirbaz satırına yazılır ve <see cref="WithAssignment"/> ile kesinleşir.
    /// </summary>
    public static CadLayerRole OtomatikRol(string layerName)
    {
        ArgumentNullException.ThrowIfNull(layerName);
        var ad = layerName.Trim();
        if (ad.Contains("OLUSAN", StringComparison.Ordinal) || ad.Contains("OLUŞAN", StringComparison.Ordinal))
        {
            return CadLayerRole.ZoningParcel;
        }

        if (ad is "İMAR" or "IMAR")
        {
            return CadLayerRole.Ignored;
        }

        var standart = Standard.Resolve(ad);
        if (standart != CadLayerRole.Unassigned)
        {
            return standart;
        }

        if (ad.Contains("PARSEL_ALN", StringComparison.Ordinal))
        {
            return CadLayerRole.CadastralParcel;
        }

        return ad == "YOL" ? CadLayerRole.RoadDedication : CadLayerRole.Unassigned;
    }

    /// <summary>Tum atamalar (anahtarlar ordinal sirali).</summary>
    public IReadOnlyDictionary<string, CadLayerRole> Assignments => _assignments;

    /// <summary>Katmanin rolunu verir; atanmamissa <see cref="CadLayerRole.Unassigned"/>.</summary>
    public CadLayerRole Resolve(string layerName)
    {
        ArgumentNullException.ThrowIfNull(layerName);
        return _assignments.TryGetValue(layerName.Trim(), out var role) ? role : CadLayerRole.Unassigned;
    }

    /// <summary>Muhendis atamasiyla yeni esleme uretir (mevcut ornek degismez).</summary>
    public CadLayerRoleMap WithAssignment(string layerName, CadLayerRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        var copy = new SortedDictionary<string, CadLayerRole>(_assignments, StringComparer.Ordinal)
        {
            [layerName.Trim()] = role,
        };
        return new CadLayerRoleMap(copy);
    }
}

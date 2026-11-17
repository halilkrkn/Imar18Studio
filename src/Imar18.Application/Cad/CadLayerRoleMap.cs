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

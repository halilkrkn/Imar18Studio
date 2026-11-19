namespace Imar18.Application.Cikti;

/// <summary>
/// Pilot taslak damgası (security.md, AC-PILOT-04/07): Evre 1'de üretilen her PDF, Excel, CKS ve yerel proje kaydında bulunur.
/// Pilot checksum'u hukuki/kriptografik imza değildir (spec 14.1).
/// </summary>
public static class PilotDamgasi
{
    public const string Metin = "PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ";

    /// <summary>Pilot Fen Klasörü şablon sürümü (spec 14.3 <c>templateVersion</c>; resmî TKGM şablonu doğrulanmadı — taslak).</summary>
    public const string FenKlasoruSablonSurumu = "TKGM_2023_4_v2-PILOT-TASLAK";

    /// <summary>Doğrulanmış PARSİD şeması yok: yalnızca deneysel iç format (security.md, AC-PILOT-09).</summary>
    public const string ParsidSemaDurumu = "Deneysel iç format (doğrulanmış PARSİD şeması yok)";
}

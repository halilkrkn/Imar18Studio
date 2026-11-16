using System.Text.Json;

namespace Imar18.Domain.Tests.Mevzuat;

/// <summary>
/// test-data/synthetic altın veri setlerini salt okunur yükler (değerler testte değiştirilmez — SORU-010).
/// Depo kökü Imar18Studio.sln aranarak bulunur (STATUS BULGU-3 teknik borcu, P6'da TestDataLocator).
/// </summary>
internal static class AltinVeri
{
    public static JsonElement ProjeA() => Yukle("pilot-1-concave-parcel", "saha_proje_1_golden.json");

    public static JsonElement ProjeB() => Yukle("pilot-3-shared-parcel", "saha_proje_2_hisse_golden.json");

    public static JsonElement ProjeC() => Yukle("pilot-2-mixed-ownership", "saha_proje_3_park_golden.json");

    public static decimal Dec(this JsonElement e, string ad) => e.GetProperty(ad).GetDecimal();

    private static JsonElement Yukle(string klasor, string dosya)
    {
        var kok = AppContext.BaseDirectory;
        while (kok is not null && !File.Exists(Path.Combine(kok, "Imar18Studio.sln")))
        {
            kok = Path.GetDirectoryName(kok);
        }

        if (kok is null)
        {
            throw new InvalidOperationException("Depo kökü (Imar18Studio.sln) bulunamadı.");
        }

        var yol = Path.Combine(kok, "test-data", "synthetic", klasor, dosya);
        using var doc = JsonDocument.Parse(File.ReadAllText(yol));
        return doc.RootElement.Clone();
    }
}

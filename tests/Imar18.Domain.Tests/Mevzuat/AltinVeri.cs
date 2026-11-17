using System.Text.Json;
using Imar18.TestDestek;

namespace Imar18.Domain.Tests.Mevzuat;

/// <summary>
/// test-data/synthetic altın veri setlerini salt okunur yükler (değerler testte değiştirilmez — SORU-010).
/// Yol ortak <see cref="TestDataLocator"/> ile derleme çıktısındaki kopyadan çözülür (BULGU-3 kapandı, P6).
/// </summary>
internal static class AltinVeri
{
    public static JsonElement ProjeA() => Yukle("pilot-1-concave-parcel", "saha_proje_1_golden.json");

    public static JsonElement ProjeB() => Yukle("pilot-3-shared-parcel", "saha_proje_2_hisse_golden.json");

    public static JsonElement ProjeBDagitim() => Yukle("pilot-3-shared-parcel", "proje_b_dagitim_eslesmesi.json");

    public static JsonElement ProjeC() => Yukle("pilot-2-mixed-ownership", "saha_proje_3_park_golden.json");

    public static decimal Dec(this JsonElement e, string ad) => e.GetProperty(ad).GetDecimal();

    private static JsonElement Yukle(string klasor, string dosya)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TestDataLocator.Sentetik(klasor, dosya)));
        return doc.RootElement.Clone();
    }
}

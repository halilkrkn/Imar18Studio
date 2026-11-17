using System.Text.Json;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tahsis;

namespace Imar18.Pilot.Tests.Destek;

/// <summary>
/// Üç anonim saha projesinin (<c>test-data/synthetic/</c> altın verisi) pilot projesine çevrilmesi. Altın veride olan her değer
/// JSON'dan okunur (testte değiştirilmez — SORU-010); altın veride OLMAYAN ayrıntılar açıkça sentetik varsayımdır:
/// <list type="bullet">
/// <item>Proje-A: R1'in 901/1 ve 901/2 arasındaki bölünmesi (5000.06 / 3699.38 tapu, 5000.00 / 3699.32 hesap) ve malikler.</item>
/// <item>Proje-C: 23 imar parselinin tek tek alanları (22 × 524.31 + 524.28 = 12059.10) ve tek malik.</item>
/// <item>Proje-B: 37 imar parseli, 40 malik ve 94 eşleştirme satırı anonim dağıtım fixture'ından (SORU-015) gelir.</item>
/// </list>
/// </summary>
internal static class SahaProjeleri
{
    public const string ProjeAKlasoru = "pilot-1-concave-parcel";
    public const string ProjeBKlasoru = "pilot-3-shared-parcel";
    public const string ProjeCKlasoru = "pilot-2-mixed-ownership";

    public static JsonElement ProjeAJson() => PilotProjesi.Json(ProjeAKlasoru, "saha_proje_1_golden.json");

    public static JsonElement ProjeBJson() => PilotProjesi.Json(ProjeBKlasoru, "saha_proje_2_hisse_golden.json");

    public static JsonElement ProjeBDagitim() => PilotProjesi.Json(ProjeBKlasoru, "proje_b_dagitim_eslesmesi.json");

    public static JsonElement ProjeCJson() => PilotProjesi.Json(ProjeCKlasoru, "saha_proje_3_park_golden.json");

    public static PilotProjesi ProjeA()
    {
        var m = ProjeAJson().GetProperty("metrics");
        var r1 = PilotProjesi.Dec(m, "toplamKadastroAlani_R1");
        var hesap = PilotProjesi.Dec(m, "hesaplananAlan");
        var fark = PilotProjesi.Dec(m, "tecvizFarki");
        var parseller = ProjeAJson().GetProperty("blocks").EnumerateArray().SelectMany(b => b.GetProperty("parcels").EnumerateArray()).ToList();

        // Sentetik bölünme: K1 tapu 5000.06 / hesap 5000.00; K2 kalanı (Σ tapu = R1, Σ hesap = hesaplanan alan).
        return new PilotProjesi
        {
            Ad = "Pilot Proje-A (sentetik geometri)",
            Kadastro =
            [
                new PilotKadastro("901/1", 5000.06m, 5000.00m, [PilotProjesi.Tam("MLK-A01")]),
                new PilotKadastro("901/2", r1 - 5000.06m, hesap - 5000.00m, [PilotProjesi.Tam("MLK-A02")]),
            ],
            Olusan =
            [
                .. parseller.Select(p => new PilotOlusan(p.GetProperty("geciciKod").GetString()!, ParselRolu.Imar, PilotProjesi.Dec(p, "area"))),
                new PilotOlusan("901/Y1", ParselRolu.YolTerk, PilotProjesi.Dec(m, "kamuyaAyrilanYol") - fark),
            ],
            TecvizSiniriM2 = PilotProjesi.Dec(m, "tecvizSiniri_NetcadRaporu"),
            ToplamTecvizSiniriM2 = PilotProjesi.Dec(m, "tecvizSiniri_NetcadRaporu"),
            Kimlikler =
            [
                .. parseller.Select(p => new ParselKimlikCifti(p.GetProperty("geciciKod").GetString()!, p.GetProperty("tescilNo").GetString()!)),
                new ParselKimlikCifti("901/Y1", "YOL"),
            ],
        };
    }

    public static PilotProjesi ProjeB()
    {
        var m = ProjeBJson().GetProperty("metrics");
        var d = ProjeBDagitim();
        var satirlar = d.GetProperty("eslesmeler").EnumerateArray().ToList();
        var malikler = satirlar.GroupBy(r => r.GetProperty("malikKodu").GetString()!)
            .Select(g => new KadastroMaliki(g.Key, PilotProjesi.Kesir(g.First().GetProperty("girenHisse").GetString()!), Muvafakatli: true))
            .ToList();
        var imar = satirlar.GroupBy(r => r.GetProperty("imarParsel").GetString()!)
            .Select(g => new PilotOlusan(g.Key, ParselRolu.Imar, PilotProjesi.Dec(g.First(), "imarParselAlaniM2")))
            .ToList();
        var kadastroId = d.GetProperty("kadastroParsel").GetString()!;
        return new PilotProjesi
        {
            Ad = "Pilot Proje-B (sentetik geometri)",
            Kadastro = [new PilotKadastro(kadastroId, PilotProjesi.Dec(m, "toplamKadastroAlani_R1"), PilotProjesi.Dec(m, "hesaplananAlan"), malikler)],
            Olusan = [.. imar, new PilotOlusan(kadastroId + "/Y1", ParselRolu.YolTerk, PilotProjesi.Dec(m, "kamuyaAyrilanYol") - PilotProjesi.Dec(m, "tecvizFarki"))],
            TecvizSiniriM2 = PilotProjesi.Dec(m, "tecvizSiniri_NetcadRaporu"),
            HakSatirlari = satirlar.Select(r => new HakEslestirmeSatiri(
                r.GetProperty("malikKodu").GetString()!, kadastroId, r.GetProperty("imarParsel").GetString()!, PilotProjesi.Dec(r, "tahsisAlaniM2"))).ToList(),
        };
    }

    public static PilotProjesi ProjeC()
    {
        var j = ProjeCJson();
        var m = j.GetProperty("metrics");
        var dop = j.GetProperty("resmiDopSayfasi");
        var dengeleme = j.GetProperty("dengelemeCetveli");
        var imarToplami = PilotProjesi.Dec(dengeleme, "imarParselToplami");

        // Sentetik parsel alanları: 101/1..6, 102/1..9, 103/1..8 (23 parsel) — 22 × 524.31 + kalan.
        var kodlar = Enumerable.Range(1, 6).Select(i => $"101/{i}").Concat(Enumerable.Range(1, 9).Select(i => $"102/{i}"))
            .Concat(Enumerable.Range(1, 8).Select(i => $"103/{i}")).ToList();
        var imar = kodlar.Select((k, i) => new PilotOlusan(k, ParselRolu.Imar, i < kodlar.Count - 1 ? 524.31m : imarToplami - (524.31m * (kodlar.Count - 1)))).ToList();
        return new PilotProjesi
        {
            Ad = "Pilot Proje-C (sentetik geometri)",
            Kadastro = [new PilotKadastro("903/1", PilotProjesi.Dec(m, "toplamKadastroAlani_R1"), PilotProjesi.Dec(m, "hesaplananAlan"), [PilotProjesi.Tam("MLK-C01")])],
            Olusan =
            [
                .. imar,
                new PilotOlusan("903/1/PARK", ParselRolu.KamuAlani, PilotProjesi.Dec(m, "parkAlani")),
                new PilotOlusan("903/1/Y1", ParselRolu.YolTerk, PilotProjesi.Dec(dengeleme, "Y1") - PilotProjesi.Dec(m, "tecvizFarki")),
            ],
            TecvizSiniriM2 = PilotProjesi.Dec(m, "tecvizSiniri_NetcadRaporu"),
            // SORU-014: Netcad DOP sayfası R2'si referanstır (dengeleme imar toplamından 0.04 m² farklı → uyarı).
            Dop = Domain.Mevzuat.Dop.DopGirdisi.Olustur(PilotProjesi.Dec(dop, "R1"), PilotProjesi.Dec(dop, "R2_ToplamImarAdaAlani")).Value,
        };
    }
}

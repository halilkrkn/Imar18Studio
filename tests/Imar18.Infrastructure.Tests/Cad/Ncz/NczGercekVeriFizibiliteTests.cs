using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cad.Ncz;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cad.Ncz;

/// <summary>
/// P4.0 (D14) NCZ fizibilite kabul testleri: gercek saha <c>.NCZ</c> dosyalari (yalnizca yerel, gitignore'lu
/// <c>test-data/real/</c>) okunur; Decimal Gauss alanlari public sentetik altin veri / resmi cetvel degerleriyle
/// karsilastirilir. Kodda gercek dosya adi, ada/parsel kodu veya koordinat yoktur (AC-PILOT-08); beklenenler
/// <c>test-data/synthetic/</c> altin JSON dosyalarindan okunur. Klasor yoksa (CI) testler atlanir.
/// </summary>
[Trait("Category", "RealData")]
public sealed class NczGercekVeriFizibiliteTests
{
    private const string GirenKatmani = "TK_PARSEL_GİREN";
    private const string OlusanKatmani = "TK_PARSEL_OLUŞAN";

    private static readonly NczFormatAdapter Adaptor = new();

    [RealDataFact]
    public void NczFizibilite_Tum_Gercek_NCZ_Dosyalari_Hatasiz_Okunmali()
    {
        var dosyalar = Directory.EnumerateFiles(GercekVeri.Kok, "*.NCZ", SearchOption.AllDirectories).ToList();

        dosyalar.Should().HaveCountGreaterThanOrEqualTo(6);
        foreach (var dosya in dosyalar)
        {
            var belge = Oku(dosya);
            belge.Layers.Should().NotBeEmpty();
            belge.Polylines.Should().NotBeEmpty();
        }
    }

    [RealDataFact]
    public void NczFizibilite_ProjeA_Olusan_Parsellerin_Decimal_Gauss_Alani_Altin_Veriyle_Birebir_Esit_Olmali()
    {
        var belge = Oku(GercekVeri.ParselasyonDosyasi("proje-1-"));
        var altin = AltinJson("pilot-1-concave-parcel/saha_proje_1_golden.json");
        var beklenen = altin.RootElement.GetProperty("blocks").EnumerateArray()
            .SelectMany(b => b.GetProperty("parcels").EnumerateArray())
            .ToDictionary(p => p.GetProperty("tescilNo").GetString()!, p => p.GetProperty("area").GetDecimal());

        var olusan = belge.Polylines.Where(p => p.LayerName == OlusanKatmani).ToList();
        var hesaplanan = olusan.ToDictionary(p => p.Label, Alan);

        hesaplanan.Should().HaveCount(beklenen.Count);
        hesaplanan.Should().BeEquivalentTo(beklenen);
        hesaplanan.Values.Sum().Should().Be(Metrik(altin, "toplamImarTahsis_R2"));
    }

    [RealDataFact]
    public void NczFizibilite_ProjeA_Giren_Parsellerin_Bildirilen_Tapu_Alani_Toplami_R1_Olmali()
    {
        var belge = Oku(GercekVeri.ParselasyonDosyasi("proje-1-"));
        var altin = AltinJson("pilot-1-concave-parcel/saha_proje_1_golden.json");

        var giren = belge.Polylines.Where(p => p.LayerName == GirenKatmani).ToList();

        giren.Should().OnlyContain(p => p.DeclaredAreaM2.HasValue && p.IsClosed);
        giren.Sum(p => decimal.Round(p.DeclaredAreaM2!.Value, 2, MidpointRounding.AwayFromZero))
            .Should().Be(Metrik(altin, "toplamKadastroAlani_R1"));
    }

    [RealDataFact]
    public void NczFizibilite_ProjeB_37_Imar_Parselinin_Decimal_Gauss_Alani_Dagitim_Cetveliyle_Birebir_Esit_Olmali()
    {
        var belge = Oku(GercekVeri.ParselasyonDosyasi("proje-2-"));
        var fixture = AltinJson("pilot-3-shared-parcel/proje_b_dagitim_eslesmesi.json");
        var beklenen = fixture.RootElement.GetProperty("eslesmeler").EnumerateArray()
            .GroupBy(e => e.GetProperty("imarParsel").GetString()!)
            .ToDictionary(g => g.Key, g => g.First().GetProperty("imarParselAlaniM2").GetDecimal());

        // NCZ etiketi gecici kod tasir: "<imarAda>/<harf><no>" -> tescil "<imarAda>/<no>".
        var gecici = new Regex(@"^(?<ada>\d{3})/[A-Z](?<no>\d+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var hesaplanan = belge.Polylines
            .Select(p => (Eslesme: gecici.Match(p.Label), Poligon: p))
            .Where(x => x.Eslesme.Success)
            .ToDictionary(x => $"{x.Eslesme.Groups["ada"].Value}/{x.Eslesme.Groups["no"].Value}", x => Alan(x.Poligon));

        hesaplanan.Should().HaveCount(fixture.RootElement.GetProperty("imarParselSayisi").GetInt32());
        hesaplanan.Should().BeEquivalentTo(beklenen);
        hesaplanan.Values.Sum().Should().Be(fixture.RootElement.GetProperty("toplamImarTahsisAlaniM2").GetDecimal());
    }

    [RealDataFact]
    public void NczFizibilite_ProjeB_Kadastro_Poligonu_Gauss_Alani_Hesaplanan_Bildirilen_Alani_R1_Olmali()
    {
        var belge = Oku(GercekVeri.ParselasyonDosyasi("proje-2-"));
        var altin = AltinJson("pilot-3-shared-parcel/saha_proje_2_hisse_golden.json");

        var kadastro = belge.Polylines.Where(p => p.DeclaredAreaM2.HasValue).Should().ContainSingle().Subject;

        Alan(kadastro).Should().Be(Metrik(altin, "hesaplananAlan"));
        decimal.Round(kadastro.DeclaredAreaM2!.Value, 2, MidpointRounding.AwayFromZero).Should().Be(Metrik(altin, "toplamKadastroAlani_R1"));
    }

    [RealDataFact]
    public void NczFizibilite_ProjeC_Imar_Parsel_Toplami_Dengeleme_Cetveline_Park_Alani_Altin_Veriye_Esit_Olmali()
    {
        var belge = Oku(GercekVeri.ParselasyonDosyasi("proje-3-"));
        var altin = AltinJson("pilot-2-mixed-ownership/saha_proje_3_park_golden.json");
        var dengeleme = altin.RootElement.GetProperty("dengelemeCetveli");

        var parseller = belge.Polylines.Where(p => p.LayerName == OlusanKatmani && p.FeatureClass == "PARSEL").ToList();
        var park = belge.Polylines.Where(p => p.LayerName == OlusanKatmani && p.Label.EndsWith("/PARK", StringComparison.Ordinal))
            .Should().ContainSingle().Subject;

        parseller.Should().NotBeEmpty();
        parseller.Sum(Alan).Should().Be(dengeleme.GetProperty("imarParselToplami").GetDecimal());
        Alan(park).Should().Be(dengeleme.GetProperty("PARK").GetDecimal());
    }

    [RealDataFact]
    public void NczFizibilite_ProjeC_Duzenleme_Siniri_Gauss_Ve_Bildirilen_Alani_R1_Olmali()
    {
        var dosya = Directory.EnumerateFiles(GercekVeri.ProjeKlasoru("proje-3-"), "*.NCZ")
            .Single(f => !Path.GetFileName(f).StartsWith("PARSELASYON", StringComparison.OrdinalIgnoreCase));
        var belge = Oku(dosya);
        var r1 = Metrik(AltinJson("pilot-2-mixed-ownership/saha_proje_3_park_golden.json"), "toplamKadastroAlani_R1");

        var kadastro = belge.Polylines.Where(p => p.DeclaredAreaM2.HasValue).Should().ContainSingle().Subject;

        Alan(kadastro).Should().Be(r1);
        decimal.Round(kadastro.DeclaredAreaM2!.Value, 2, MidpointRounding.AwayFromZero).Should().Be(r1);
    }

    [RealDataFact]
    public void NczFizibilite_1mm_Kuantizasyon_Resmi_Parsel_Alanlarini_Degistirdigi_Icin_Adaptor_Kuantize_Etmemeli()
    {
        // SORU-032 kaniti: ayni Proje-A parselleri 1 mm izgaraya cekilirse en az bir resmi alan 0.01 m2+ sapar.
        var belge = Oku(GercekVeri.ParselasyonDosyasi("proje-1-"));
        var olusan = belge.Polylines.Where(p => p.LayerName == OlusanKatmani).ToList();

        var degisen = olusan.Count(p =>
            Alan(p) != GaussAreaCalculator.Compute(PolygonRing.FromClosed(p.Vertices.Select(CoordinateQuantizer.Quantize).ToList()).Value).AreaM2);

        degisen.Should().BeGreaterThan(0);
        belge.Polylines.SelectMany(p => p.Vertices).Should().Contain(v => !CoordinateQuantizer.IsOnGrid(v));
    }

    private static CadLayerDocument Oku(string dosya)
    {
        using var akis = File.OpenRead(dosya);
        var sonuc = Adaptor.Read(akis);
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        return sonuc.Value;
    }

    private static decimal Alan(CadPolyline p)
    {
        p.IsClosed.Should().BeTrue();
        var halka = PolygonRing.FromClosed(p.Vertices);
        halka.IsSuccess.Should().BeTrue(halka.IsFailure ? halka.Error.TechnicalMessage : string.Empty);
        return GaussAreaCalculator.Compute(halka.Value).AreaM2;
    }

    private static JsonDocument AltinJson(string goreli) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(GercekVeri.DepoKoku, "test-data", "synthetic", goreli)));

    private static decimal Metrik(JsonDocument altin, string ad) =>
        altin.RootElement.GetProperty("metrics").GetProperty(ad).GetDecimal();
}

/// <summary>Yerel <c>test-data/real/</c> klasoru yoksa testi atlayan <see cref="FactAttribute"/>.</summary>
public sealed class RealDataFactAttribute : FactAttribute
{
    public RealDataFactAttribute()
    {
        if (!GercekVeri.VarMi)
        {
            Skip = "test-data/real/ yerel gercek veri klasoru yok (CI veya baska makine).";
        }
    }
}

/// <summary>Depo koku ve yerel gercek veri klasoru bulucusu (Imar18Studio.sln aranir; STATUS BULGU-3 teknik borcu).</summary>
internal static class GercekVeri
{
    private static readonly string? KokYolu = KokuBul();

    public static string DepoKoku => KokYolu ?? throw new InvalidOperationException("Depo koku (Imar18Studio.sln) bulunamadi.");

    public static string Kok => Path.Combine(DepoKoku, "test-data", "real");

    /// <summary>Depo koku bulunamazsa da false doner (test kesfi sirasinda istisna atilmaz).</summary>
    public static bool VarMi => KokYolu is not null && Directory.Exists(Path.Combine(KokYolu, "test-data", "real"));

    /// <summary>Proje klasoru onekle bulunur (ornek: "proje-1-"); gercek klasor adi koda yazilmaz.</summary>
    public static string ProjeKlasoru(string onek) =>
        Directory.EnumerateDirectories(Kok, onek + "*").Should().ContainSingle().Subject;

    public static string ParselasyonDosyasi(string onek) =>
        Directory.EnumerateFiles(ProjeKlasoru(onek), "PARSELASYON*.NCZ").Should().ContainSingle().Subject;

    private static string? KokuBul()
    {
        var dizin = AppContext.BaseDirectory;
        while (dizin is not null && !File.Exists(Path.Combine(dizin, "Imar18Studio.sln")))
        {
            dizin = Path.GetDirectoryName(dizin);
        }

        return dizin;
    }
}

using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cad.Cks;
using Imar18.Infrastructure.Cad.Ncz;
using Imar18.Pilot.Tests.Destek;
using Imar18.TestDestek;
using Xunit;

namespace Imar18.Pilot.Tests;

/// <summary>
/// SAHA-004 (canlı Windows kullanımı): gerçek Netcad <c>.NCZ</c> çizimleri (yalnızca yerel, gitignore'lu <c>test-data/real/</c>)
/// topoloji denetiminden onay engeli olmadan geçmeli; milimetrik sınır dokunmaları uyarı, adalar arasındaki imar yolu bilgi olmalı
/// ve Proje 1 onaylanıp CKS / Excel / Fen Klasörü üretilebilmelidir. Kodda gerçek ada/parsel kodu, dosya adı veya koordinat yoktur
/// (AC-PILOT-08): kimlikler çalışma anında NCZ etiketlerinden okunur, beklenen metrikler sentetik altın veriden gelir.
/// Klasör yoksa (CI) testler atlanır.
/// </summary>
[Trait("Category", "RealData")]
public sealed class Saha004GercekNczTopolojiOnayTests
{
    private static readonly NczFormatAdapter Adaptor = new();

    [GercekVeriFact]
    public void Saha004_ProjeA_Gercek_NCZ_Topolojisinde_Kritik_Bulgu_Olmamali_Imar_Yolu_Bilgi_Sayilmali()
    {
        var geo = Geometri("proje-1-", CadLayerRoleMap.Standard);

        var bulgular = PilotCalismaAlani.Topoloji.Denetle(geo);

        bulgular.Should().NotContain(b => b.Tur == TopolojiBulguTuru.AnalizYapilamadi);
        bulgular.Where(b => b.Tur != TopolojiBulguTuru.ImarYoluAlani).Should().NotBeEmpty("gerçek çizimde milimetrik dokunmalar vardır")
            .And.OnlyContain(b => b.SinirToleransiIcinde && b.AlanM2 <= ParselTopolojisi.SinirToleransiAlanM2);
        bulgular.Should().NotContain(b => b.Tur == TopolojiBulguTuru.OlusanKapsamaBoslugu && !b.SinirToleransiIcinde, "adalar arası yol boşluk değildir");

        var yol = bulgular.Should().ContainSingle(b => b.Tur == TopolojiBulguTuru.ImarYoluAlani).Subject;
        var gaussFarki = Alan(geo, ParselRolu.Kadastro) - Alan(geo, ParselRolu.Imar);
        Math.Abs(yol.AlanM2 - gaussFarki).Should().BeLessThan(0.15m, "imar yolu ≈ Σ kadastro − Σ imar Gauss (milimetrik kıymıklar hariç)");
        yol.ParselIdleri.Select(ParselTopolojisi.ImarAdasi).OfType<string>().Distinct().Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [GercekVeriFact]
    public async Task Saha004_ProjeA_Gercek_NCZ_Ile_Onaylanip_CKS_Excel_Ve_Fen_Klasoru_Uretilmeli()
    {
        var altin = SahaProjeleri.ProjeAJson().GetProperty("metrics");
        var proje = GercekProjeA();
        using var alan = await PilotCalismaAlani.AcAsync(proje);

        var d = alan.Onay.Degerlendir();
        d.EksikGirdiler.Should().BeEmpty();
        d.KritikVar.Should().BeFalse(string.Join("; ", d.Bulgular.Where(b => b.Severity == ErrorSeverity.Critical).Select(b => b.TechnicalMessage)));
        d.Bulgular.Should().Contain(b => b.Code == ErrorCodes.TopoSinirToleransi && b.Severity == ErrorSeverity.Warning);
        d.Bulgular.Should().ContainSingle(b => b.Code == ErrorCodes.TopoImarYoluAlani).Which.Severity.Should().Be(ErrorSeverity.Info);
        d.TopolojiNotlari.Should().OnlyContain(n => n.Metin.Contains(" m²", StringComparison.Ordinal) && n.Seviye != ErrorSeverity.Critical);
        alan.Onay.OnayEkraniHazirla().OnayVerilebilir.Should().BeTrue();

        var onay = await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni);
        onay.IsSuccess.Should().BeTrue(onay.IsFailure ? onay.Error.TechnicalMessage : string.Empty);
        var cks = await alan.Cikti.UretAsync(CiktiTuru.CksDopOzeti, PilotCalismaAlani.Muhendis, alan.CiktiYolu("dop.cks"));
        var xlsx = await alan.Cikti.UretAsync(CiktiTuru.Cetveller, PilotCalismaAlani.Muhendis, alan.CiktiYolu("cetveller.xlsx"));
        var pdf = await alan.Cikti.UretAsync(CiktiTuru.FenKlasoru, PilotCalismaAlani.Muhendis, alan.CiktiYolu("fen.pdf"));

        foreach (var s in new[] { cks, xlsx, pdf })
        {
            s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.TechnicalMessage : string.Empty);
            File.Exists(s.Value.DosyaYolu).Should().BeTrue();
        }

        using var akis = File.OpenRead(cks.Value.DosyaYolu);
        var ozet = new CksFileReader().Read(akis).Value;
        ozet.ToplamDuzenlemeyeGirenDopAlinanM2.Should().Be(PilotProjesi.Dec(altin, "toplamKadastroAlani_R1"));
        ozet.ToplamImarAdaAlaniM2.Should().Be(PilotProjesi.Dec(altin, "toplamImarTahsis_R2"));
        ozet.Dopo.Should().Be(PilotProjesi.Dec(altin, "dopo"));
    }

    [GercekVeriFact]
    public void Saha004_ProjeB_Ve_ProjeC_Gercek_NCZ_Olusan_Parsellerinde_Kritik_Topoloji_Bulgusu_Olmamali()
    {
        // Proje-B parselasyon çiziminde parseller standart dışı katmandadır; mühendis ataması (SORU-032 b) benzetilir.
        var projeB = Belge("proje-2-");
        var rollerB = projeB.Polylines.Where(p => p.IsClosed).GroupBy(p => p.LayerName).OrderByDescending(g => g.Count()).First().Key;
        // Aynı katmanda ada dış sınırı ve yardımcı poligonlar da vardır; yalnız geçici parsel kodlu ("ada/harf+no") poligonlar
        // imar parselidir (NczGercekVeriFizibiliteTests ile aynı kural). Ada sınırı parsel sayılsaydı çakışma doğru biçimde Critical olurdu.
        var geciciKod = new System.Text.RegularExpressions.Regex(@"^\d{3}/[A-Z]\d+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var geoB = ProjeGeometriEslemesi.Olustur(projeB, CadLayerRoleMap.Standard.WithAssignment(rollerB, CadLayerRole.ZoningParcel)).Parseller
            .Where(p => geciciKod.IsMatch(p.ParselId)).ToList();
        var geoC = Geometri("proje-3-", CadLayerRoleMap.Standard);

        foreach (var geo in new IReadOnlyList<ParselGeometrisi>[] { geoB, geoC })
        {
            geo.Should().HaveCountGreaterThan(10);
            PilotCalismaAlani.Topoloji.Denetle(geo).Should().NotContain(b => !b.SinirToleransiIcinde && b.Tur != TopolojiBulguTuru.ImarYoluAlani);
        }
    }

    // ---------------- Yardımcılar ----------------

    /// <summary>
    /// Proje-A: geometri gerçek NCZ'den; tapu alanı NCZ'de poligona yazılmış alan (resmî cetvel), hesap alanı Decimal Gauss; tecviz
    /// sınırı altın veriden; malikler sentetik (her kadastro parseli tek malik). Yol terk ayrı çizilmemiştir (imar yolu bilgisi).
    /// </summary>
    private static PilotProjesi GercekProjeA()
    {
        var belge = Belge("proje-1-");
        var geo = ProjeGeometriEslemesi.Olustur(belge, CadLayerRoleMap.Standard).Parseller;
        var giren = belge.Polylines.Where(p => CadLayerRoleMap.Standard.Resolve(p.LayerName) == CadLayerRole.CadastralParcel).ToList();
        var gauss = geo.ToDictionary(p => p.ParselId, p => GaussAreaCalculator.Compute(PolygonRing.FromOpen(p.Koseler).Value).AreaM2, StringComparer.Ordinal);
        var m = SahaProjeleri.ProjeAJson().GetProperty("metrics");
        var sira = 0;
        return new PilotProjesi
        {
            Ad = "SAHA-004 Proje-A (gerçek NCZ geometrisi)",
            Kadastro = geo.Where(p => p.Rol == ParselRolu.Kadastro).Select(p => new PilotKadastro(
                p.ParselId,
                decimal.Round(giren.Single(g => g.Label.Trim() == p.ParselId).DeclaredAreaM2!.Value, 2, MidpointRounding.AwayFromZero),
                gauss[p.ParselId],
                [PilotProjesi.Tam($"MLK-R{++sira:00}")])).ToList(),
            Olusan = geo.Where(p => p.Rol == ParselRolu.Imar).Select(p => new PilotOlusan(p.ParselId, ParselRolu.Imar, gauss[p.ParselId])).ToList(),
            TecvizSiniriM2 = PilotProjesi.Dec(m, "tecvizSiniri_NetcadRaporu"),
            ToplamTecvizSiniriM2 = PilotProjesi.Dec(m, "tecvizSiniri_NetcadRaporu"),
            GercekGeometri = geo,
        };
    }

    private static decimal Alan(IEnumerable<ParselGeometrisi> geo, ParselRolu rol) =>
        geo.Where(p => p.Rol == rol).Sum(p => GaussAreaCalculator.Compute(PolygonRing.FromOpen(p.Koseler).Value).AreaM2);

    private static IReadOnlyList<ParselGeometrisi> Geometri(string onek, CadLayerRoleMap roller) =>
        ProjeGeometriEslemesi.Olustur(Belge(onek), roller).Parseller;

    private static CadLayerDocument Belge(string onek)
    {
        var klasor = Directory.EnumerateDirectories(TestDataLocator.GercekVeriKoku!, onek + "*").Single();
        var dosya = Directory.EnumerateFiles(klasor, "PARSELASYON*.NCZ").Single();
        using var akis = File.OpenRead(dosya);
        var sonuc = Adaptor.Read(akis);
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        return sonuc.Value;
    }
}

/// <summary>Yerel <c>test-data/real/</c> klasörü yoksa testi atlayan <see cref="FactAttribute"/> (CI'da klasör yoktur).</summary>
public sealed class GercekVeriFactAttribute : FactAttribute
{
    public GercekVeriFactAttribute()
    {
        if (!TestDataLocator.GercekVeriVarMi)
        {
            Skip = "test-data/real/ yerel gerçek veri klasörü yok (CI veya başka makine).";
        }
    }
}

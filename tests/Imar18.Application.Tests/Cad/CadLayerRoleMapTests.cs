using FluentAssertions;
using Imar18.Application.Cad;
using Xunit;

namespace Imar18.Application.Tests.Cad;

/// <summary>P4.1 standart katman eslemesi (roadmap §6 P4.1, spec §25.7) ve muhendis atamasi (STATUS SORU-032 b).</summary>
public sealed class CadLayerRoleMapTests
{
    [Theory]
    [InlineData("TK_PARSEL_GİREN", CadLayerRole.CadastralParcel)]
    [InlineData("TK_PARSEL_OLUŞAN", CadLayerRole.ZoningParcel)]
    [InlineData("TK_PARSEL_YOLATERK", CadLayerRole.RoadDedication)]
    [InlineData("DÜZENLEME_SINIRI", CadLayerRole.RegulationBoundary)]
    [InlineData("ADA_KENARLT", CadLayerRole.BlockEdge)]
    [InlineData("PLAN_ADAKENARI", CadLayerRole.BlockEdge)]
    [InlineData("PL_PARK", CadLayerRole.Park)]
    [InlineData("KADASTRO_HAT", CadLayerRole.CadastralLine)]
    [InlineData("NOKTA_PARSEL_YENI", CadLayerRole.NewParcelPoint)]
    [InlineData("TESCİLLİ_NOKTA", CadLayerRole.RegisteredPoint)]
    public void KatmanRolu_Standart_Netcad_Katman_Adlarini_Rolune_Eslemeli(string katman, CadLayerRole rol)
    {
        CadLayerRoleMap.Standard.Resolve(katman).Should().Be(rol);
    }

    [Theory]
    [InlineData("@SM")]
    [InlineData("YAZI")]
    [InlineData("tk_parsel_giren")]
    [InlineData("")]
    public void KatmanRolu_Standart_Olmayan_Katmani_Atanmamis_Saymali(string katman)
    {
        CadLayerRoleMap.Standard.Resolve(katman).Should().Be(CadLayerRole.Unassigned);
    }

    [Fact]
    public void KatmanRolu_Bas_Son_Bosluklari_Yok_Saymali()
    {
        CadLayerRoleMap.Standard.Resolve("  TK_PARSEL_OLUŞAN ").Should().Be(CadLayerRole.ZoningParcel);
    }

    [Fact]
    public void KatmanRolu_Muhendis_Atamasi_Standart_Haritayi_Degistirmeden_Yeni_Harita_Uretmeli()
    {
        var proje = CadLayerRoleMap.Standard
            .WithAssignment("@SM", CadLayerRole.ZoningParcel)
            .WithAssignment("ADAKENARI", CadLayerRole.CadastralParcel);

        proje.Resolve("@SM").Should().Be(CadLayerRole.ZoningParcel);
        proje.Resolve("ADAKENARI").Should().Be(CadLayerRole.CadastralParcel);
        proje.Resolve("TK_PARSEL_GİREN").Should().Be(CadLayerRole.CadastralParcel);
        CadLayerRoleMap.Standard.Resolve("@SM").Should().Be(CadLayerRole.Unassigned);
    }

    [Fact]
    public void KatmanRolu_Muhendis_Standart_Katmanin_Rolunu_Degistirebilmeli_Ya_Da_Kaldirabilmeli()
    {
        var proje = CadLayerRoleMap.Standard
            .WithAssignment("TK_PARSEL_YOLATERK", CadLayerRole.Park)
            .WithAssignment("PL_PARK", CadLayerRole.Unassigned);

        proje.Resolve("TK_PARSEL_YOLATERK").Should().Be(CadLayerRole.Park);
        proje.Resolve("PL_PARK").Should().Be(CadLayerRole.Unassigned);
    }

    [Fact]
    public void KatmanRolu_Bos_Katman_Adi_Atamasi_Reddedilmeli()
    {
        var eylem = () => CadLayerRoleMap.Standard.WithAssignment("  ", CadLayerRole.Park);

        eylem.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void KatmanRolu_Null_Katman_Adi_ArgumentNullException_Firlatmali()
    {
        var eylem = () => CadLayerRoleMap.Standard.Resolve(null!);

        eylem.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void KatmanRolu_Atamalar_Salt_Okunur_Ve_Deterministik_Sirali_Olmali()
    {
        var atamalar = CadLayerRoleMap.Standard.Assignments;

        atamalar.Should().HaveCount(10);
        atamalar.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
        atamalar.Should().BeAssignableTo<IReadOnlyDictionary<string, CadLayerRole>>();
    }
}

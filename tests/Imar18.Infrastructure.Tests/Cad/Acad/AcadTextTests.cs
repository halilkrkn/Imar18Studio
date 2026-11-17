using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cad.Acad;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cad.Acad;

/// <summary>P4.2: DXF/DWG TEXT/MTEXT varliklari etiket eslestirmesi icin eksen takasiyla <see cref="CadText"/>'e cevrilir.</summary>
public sealed class AcadTextTests
{
    private static CadLayerDocument Donustur(CadDocument belge) =>
        AcadDocumentConverter.Convert(belge, CadFormat.Dxf, null, CancellationToken.None).Value;

    [Fact]
    public void AcadYazi_TEXT_Ekleme_Noktasini_Jeodezik_Eksene_Takas_Etmeli()
    {
        var belge = new CadDocument();
        var katman = new Layer("YAZI");
        belge.Layers.Add(katman);
        belge.Entities.Add(new TextEntity { Value = " 101/1 ", InsertPoint = new XYZ(500_010.5, 4_000_020.25, 3), Layer = katman });

        var d = Donustur(belge);

        var y = d.Texts.Should().ContainSingle().Subject;
        y.Text.Should().Be("101/1");
        y.LayerName.Should().Be("YAZI");
        y.Position.Should().Be(new GeodeticPoint(X: 4_000_020.25m, Y: 500_010.5m));
        d.SkippedEntityCount.Should().Be(0);
    }

    [Theory]
    [InlineData("{\\fArial|b0|i0;101/2}", "101/2")]
    [InlineData("101/3\\P", "101/3")]
    [InlineData("\\A1;\\H2.5x;102/4", "102/4")]
    [InlineData("103\\P/5", "103 /5")]
    public void AcadYazi_MTEXT_Bicim_Kodlarini_Temizlemeli(string ham, string beklenen)
    {
        var belge = new CadDocument();
        belge.Entities.Add(new MText { Value = ham, InsertPoint = new XYZ(500_000, 4_000_000, 0) });

        Donustur(belge).Texts.Should().ContainSingle().Which.Text.Should().Be(beklenen);
    }

    [Fact]
    public void AcadYazi_Bos_Metinli_Yaziyi_Atlamali()
    {
        var belge = new CadDocument();
        belge.Entities.Add(new TextEntity { Value = "   " });

        Donustur(belge).Texts.Should().BeEmpty();
    }
}

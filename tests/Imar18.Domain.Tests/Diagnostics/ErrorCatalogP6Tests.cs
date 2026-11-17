using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Xunit;

namespace Imar18.Domain.Tests.Diagnostics;

/// <summary>P6 — Parseller arası topoloji kodları (katalog 1.7.0, STATUS SORU-035).</summary>
public sealed class ErrorCatalogP6Tests
{
    [Theory]
    [InlineData(ErrorCodes.TopoParselCakismasi, "ERR-TOPO-005", ErrorSeverity.Critical)]
    [InlineData(ErrorCodes.TopoParselBoslugu, "ERR-TOPO-006", ErrorSeverity.Critical)]
    [InlineData(ErrorCodes.TopoKadastroDisinaTasma, "ERR-TOPO-007", ErrorSeverity.Critical)]
    [InlineData(ErrorCodes.TopoAnalizYapilamadi, "ERR-TOPO-008", ErrorSeverity.Error)]
    public void HataKatalogu_P6_Topoloji_Kodlari_Tanimli_Ve_Muhendis_Incelemesi_Istemeli(string sabit, string kod, ErrorSeverity seviye)
    {
        sabit.Should().Be(kod);
        ErrorCatalog.TryGet(kod, out var hata).Should().BeTrue();
        hata.Severity.Should().Be(seviye);
        hata.Category.Should().Be(ErrorCategory.Topology);
        hata.RequiresEngineerReview.Should().BeTrue();
        hata.UserMessage.Should().NotBe(hata.TechnicalMessage);
    }

    [Fact]
    public void HataKatalogu_P6_Ile_Surum_En_Az_1_7_0_Olmali()
    {
        // SAHA-004 katalogu 1.8.0'a yükseltti; P6 kodları 1.7.0'dan beri vardır.
        Version.Parse(ErrorCatalog.Version).Should().BeGreaterThanOrEqualTo(new Version(1, 7, 0));
    }
}

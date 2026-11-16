using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Xunit;

namespace Imar18.Domain.Tests.Diagnostics;

/// <summary>
/// P3 — Tecviz, DOP ve hak eşleştirme hata kodları (katalog 1.3.0, STATUS SORU-031).
/// </summary>
public sealed class ErrorCatalogP3Tests
{
    [Theory]
    [InlineData("ERR-TECVIZ-004", ErrorSeverity.Error, ErrorCategory.Tecviz)]
    [InlineData("ERR-DOP-003", ErrorSeverity.Error, ErrorCategory.Dop)]
    [InlineData("ERR-DOP-004", ErrorSeverity.Warning, ErrorCategory.Dop)]
    [InlineData("ERR-DOP-005", ErrorSeverity.Critical, ErrorCategory.Dop)]
    [InlineData("ERR-TAPU-005", ErrorSeverity.Critical, ErrorCategory.Tapu)]
    [InlineData("ERR-TAPU-006", ErrorSeverity.Warning, ErrorCategory.Tapu)]
    [InlineData("ERR-TAPU-007", ErrorSeverity.Critical, ErrorCategory.Tapu)]
    [InlineData("ERR-TAPU-008", ErrorSeverity.Error, ErrorCategory.Tapu)]
    public void HataKatalogu_P3_Mevzuat_Kodlari_Tanimli_Olmali(string kod, ErrorSeverity seviye, ErrorCategory kategori)
    {
        ErrorCatalog.TryGet(kod, out var hata).Should().BeTrue($"{kod} katalogda tanimli olmali");
        hata.Severity.Should().Be(seviye);
        hata.Category.Should().Be(kategori);
        hata.UserMessage.Should().NotBe(hata.TechnicalMessage);
    }

    [Fact]
    public void HataKatalogu_P3_Ile_Surum_1_3_0_Olmali()
    {
        ErrorCatalog.Version.Should().Be("1.3.0");
    }

    [Theory]
    [InlineData("ERR-DOP-004")]
    [InlineData("ERR-TAPU-006")]
    public void HataKatalogu_P3_Uyari_Kodlari_Muhendis_Incelemesi_Istemeli(string kod)
    {
        ErrorCatalog.Get(kod).RequiresEngineerReview.Should().BeTrue();
    }
}

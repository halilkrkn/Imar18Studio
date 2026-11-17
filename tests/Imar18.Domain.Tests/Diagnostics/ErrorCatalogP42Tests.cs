using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Xunit;

namespace Imar18.Domain.Tests.Diagnostics;

/// <summary>P4.2 — CRS donusum ve etiket eslestirme kodlari (katalog 1.5.0).</summary>
public sealed class ErrorCatalogP42Tests
{
    [Theory]
    [InlineData("ERR-CRS-004", ErrorSeverity.Error, ErrorCategory.Crs, false)]
    [InlineData("ERR-CRS-005", ErrorSeverity.Warning, ErrorCategory.Crs, true)]
    [InlineData("ERR-GIRD-010", ErrorSeverity.Warning, ErrorCategory.Input, true)]
    public void HataKatalogu_P42_Kodlari_Tanimli_Olmali(string kod, ErrorSeverity seviye, ErrorCategory kategori, bool inceleme)
    {
        ErrorCatalog.TryGet(kod, out var hata).Should().BeTrue();
        hata.Severity.Should().Be(seviye);
        hata.Category.Should().Be(kategori);
        hata.RequiresEngineerReview.Should().Be(inceleme);
        hata.UserMessage.Should().NotBe(hata.TechnicalMessage);
    }

    [Fact]
    public void HataKatalogu_P42_Ile_Surum_1_5_0_Olmali()
    {
        ErrorCatalog.Version.Should().Be("1.5.0");
    }
}

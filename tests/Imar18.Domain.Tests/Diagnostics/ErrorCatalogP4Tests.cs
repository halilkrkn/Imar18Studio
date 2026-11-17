using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Xunit;

namespace Imar18.Domain.Tests.Diagnostics;

/// <summary>
/// P4.1 — Coklu CAD/CKS girdi kodlari (katalog 1.4.0, STATUS SORU-025 / SORU-032 / SORU-033).
/// </summary>
public sealed class ErrorCatalogP4Tests
{
    [Theory]
    [InlineData("ERR-GIRD-006", ErrorSeverity.Warning, true)]
    [InlineData("ERR-GIRD-007", ErrorSeverity.Error, false)]
    [InlineData("ERR-GIRD-008", ErrorSeverity.Warning, true)]
    [InlineData("ERR-GIRD-009", ErrorSeverity.Warning, true)]
    public void HataKatalogu_P4_Girdi_Kodlari_Tanimli_Olmali(string kod, ErrorSeverity seviye, bool inceleme)
    {
        ErrorCatalog.TryGet(kod, out var hata).Should().BeTrue($"{kod} katalogda tanimli olmali");
        hata.Severity.Should().Be(seviye);
        hata.Category.Should().Be(ErrorCategory.Input);
        hata.RequiresEngineerReview.Should().Be(inceleme);
        hata.AuditLogRequired.Should().BeFalse();
        hata.UserMessage.Should().NotBe(hata.TechnicalMessage);
    }

    [Fact]
    public void HataKatalogu_P4_Izgara_Disi_Uyarisi_SORU_025_Yer_Tutucularini_Tasimali()
    {
        var hata = ErrorCatalog.Get(ErrorCodes.GirdIzgaraDisiKose);

        hata.TechnicalMessage.Should().Contain("{count}").And.Contain("{path}");
        hata.UserMessage.Should().Contain("1 mm");
    }

    [Fact]
    public void HataKatalogu_P4_Kodlari_Surum_1_4_0_Ve_Sonrasinda_Bulunmali()
    {
        new Version(ErrorCatalog.Version).Should().BeGreaterThanOrEqualTo(new Version(1, 4, 0));
    }
}

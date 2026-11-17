using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Xunit;

namespace Imar18.Domain.Tests.Diagnostics;

/// <summary>P5 — Onay, çıktı ve proje kaydı kodları (katalog 1.6.0, STATUS SORU-034).</summary>
public sealed class ErrorCatalogP5Tests
{
    [Theory]
    [InlineData(ErrorCodes.CiktiOnayKritikHataEngeli, "ERR-CIKTI-004", ErrorCategory.Output, true)]
    [InlineData(ErrorCodes.CiktiOnayMetniKabulEdilmedi, "ERR-CIKTI-005", ErrorCategory.Output, false)]
    [InlineData(ErrorCodes.CiktiOnayIcinHesapEksik, "ERR-CIKTI-006", ErrorCategory.Output, true)]
    [InlineData(ErrorCodes.CiktiGecerliOnayYok, "ERR-CIKTI-007", ErrorCategory.Output, false)]
    [InlineData(ErrorCodes.CiktiOnayGeriAlinamaz, "ERR-CIKTI-008", ErrorCategory.Output, false)]
    [InlineData(ErrorCodes.CiktiProjeKaydiHatasi, "ERR-CIKTI-009", ErrorCategory.Output, false)]
    [InlineData(ErrorCodes.GirdProjeGirdisiGecersiz, "ERR-GIRD-011", ErrorCategory.Input, false)]
    public void HataKatalogu_P5_Kodlari_Error_Seviyesinde_Tanimli_Olmali(string sabit, string kod, ErrorCategory kategori, bool inceleme)
    {
        sabit.Should().Be(kod);
        ErrorCatalog.TryGet(kod, out var hata).Should().BeTrue();
        hata.Severity.Should().Be(ErrorSeverity.Error, "reddedilen girişim kendisi kritik veri hatası değildir; engeli Critical bulgu üretir");
        hata.Category.Should().Be(kategori);
        hata.RequiresEngineerReview.Should().Be(inceleme);
        hata.UserMessage.Should().NotBe(hata.TechnicalMessage);
    }

    [Fact]
    public void HataKatalogu_P5_Ile_Surum_En_Az_1_6_0_Olmali()
    {
        new Version(ErrorCatalog.Version).Should().BeGreaterThanOrEqualTo(new Version(1, 6, 0));
    }
}

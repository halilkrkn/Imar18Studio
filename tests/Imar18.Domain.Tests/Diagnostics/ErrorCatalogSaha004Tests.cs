using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Xunit;

namespace Imar18.Domain.Tests.Diagnostics;

/// <summary>SAHA-004 — sınır toleransı ve imar yolu topoloji kodları (katalog 1.8.0, STATUS SORU-037).</summary>
public sealed class ErrorCatalogSaha004Tests
{
    [Fact]
    public void HataKatalogu_Sinir_Toleransi_Kodu_Uyari_Ve_Muhendis_Incelemeli_Olmali_Onayi_Engellememeli()
    {
        ErrorCodes.TopoSinirToleransi.Should().Be("ERR-TOPO-009");
        var e = ErrorCatalog.Get(ErrorCodes.TopoSinirToleransi);

        e.Severity.Should().Be(ErrorSeverity.Warning, "milimetrik sınır dokunması Critical (onay engeli) değildir");
        e.Category.Should().Be(ErrorCategory.Topology);
        e.RequiresEngineerReview.Should().BeTrue();
        e.TechnicalMessage.Should().Contain("{parcelIds}").And.Contain("{area}").And.Contain("{kind}");
    }

    [Fact]
    public void HataKatalogu_Imar_Yolu_Kodu_Bilgi_Seviyesinde_Olmali()
    {
        ErrorCodes.TopoImarYoluAlani.Should().Be("ERR-TOPO-010");
        var e = ErrorCatalog.Get(ErrorCodes.TopoImarYoluAlani);

        e.Severity.Should().Be(ErrorSeverity.Info);
        e.Category.Should().Be(ErrorCategory.Topology);
        e.TechnicalMessage.Should().Contain("{area}").And.Contain("{blocks}");
    }

    [Fact]
    public void HataKatalogu_Saha004_Ile_Surum_1_8_0_Olmali()
    {
        ErrorCatalog.Version.Should().Be("1.8.0");
    }
}

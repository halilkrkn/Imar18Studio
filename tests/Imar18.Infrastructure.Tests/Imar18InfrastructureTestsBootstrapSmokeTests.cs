using FluentAssertions;
using Xunit;

namespace Imar18.Infrastructure.Tests;

public sealed class Imar18InfrastructureTestsBootstrapSmokeTests
{
    [Fact]
    [Trait("Category", "Smoke")]
    public void FazP0_Proje_Iskeleti_Ve_Test_Altyapisi_Calisir_Durumda_Olmali()
    {
        true.Should().BeTrue();
    }
}

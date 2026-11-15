using FluentAssertions;
using Xunit;

namespace Imar18.Application.Tests;

public sealed class Imar18ApplicationTestsBootstrapSmokeTests
{
    [Fact]
    [Trait("Category", "Smoke")]
    public void FazP0_Proje_Iskeleti_Ve_Test_Altyapisi_Calisir_Durumda_Olmali()
    {
        true.Should().BeTrue();
    }
}

namespace KuraStorage.IntegrationTests;

using Xunit;

public sealed class ArchitectureSmokeTests
{
    [Fact]
    public void InfrastructureAssemblyCanBeLoaded()
    {
        Assert.NotNull(typeof(Infrastructure.AssemblyMarker).Assembly);
    }
}

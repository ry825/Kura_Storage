namespace KuraStorage.Domain.Tests;

using Xunit;

public sealed class ArchitectureSmokeTests
{
    [Fact]
    public void DomainAssemblyCanBeLoaded()
    {
        Assert.NotNull(typeof(Domain.AssemblyMarker).Assembly);
    }
}

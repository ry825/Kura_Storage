namespace KuraStorage.Application.Tests;

using Xunit;

public sealed class ArchitectureSmokeTests
{
    [Fact]
    public void ApplicationAssemblyCanBeLoaded()
    {
        Assert.NotNull(typeof(Application.AssemblyMarker).Assembly);
    }
}

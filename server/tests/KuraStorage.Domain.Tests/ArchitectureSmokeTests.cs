namespace KuraStorage.Domain.Tests;

using Xunit;

public sealed class ArchitectureSmokeTests
{
    [Fact]
    public void DomainAssemblyCanBeLoaded()
    {
        Assert.NotNull(typeof(Domain.AssemblyMarker).Assembly);
    }

    [Fact]
    public void DomainAssembly_DoesNotReferenceFrameworkOrPersistencePackages()
    {
        var prohibitedPrefixes = new[]
        {
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
        };

        var references = typeof(Domain.AssemblyMarker).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            reference => prohibitedPrefixes.Any(prefix =>
                reference.Name?.StartsWith(prefix, StringComparison.Ordinal) == true));
    }
}

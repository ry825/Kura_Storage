using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class KuraStorageDbContextFactory : IDesignTimeDbContextFactory<KuraStorageDbContext>
{
    public KuraStorageDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("KURASTORAGE_DESIGN_CONNECTION") ??
            "Host=localhost;Database=kurastorage_design;Username=kurastorage";
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new KuraStorageDbContext(options);
    }
}

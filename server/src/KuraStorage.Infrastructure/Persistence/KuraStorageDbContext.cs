using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class KuraStorageDbContext(DbContextOptions<KuraStorageDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    public DbSet<AuthenticationAttempt> AuthenticationAttempts => Set<AuthenticationAttempt>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<FileEntry> FileEntries => Set<FileEntry>();

    public DbSet<FileOperation> FileOperations => Set<FileOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KuraStorageDbContext).Assembly);
    }
}

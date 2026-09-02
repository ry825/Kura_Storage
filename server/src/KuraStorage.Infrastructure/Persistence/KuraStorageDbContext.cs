using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Activity;
using KuraStorage.Domain.Backup;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Indexing;
using KuraStorage.Domain.Maintenance;
using KuraStorage.Domain.Media;
using KuraStorage.Domain.Organization;
using KuraStorage.Domain.Sharing;
using KuraStorage.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class KuraStorageDbContext(DbContextOptions<KuraStorageDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    public DbSet<AuthenticationAttempt> AuthenticationAttempts => Set<AuthenticationAttempt>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<UserActivity> UserActivities => Set<UserActivity>();

    public DbSet<BackupReceipt> BackupReceipts => Set<BackupReceipt>();

    public DbSet<FileEntry> FileEntries => Set<FileEntry>();

    public DbSet<FileVersionRecord> FileVersionRecords => Set<FileVersionRecord>();

    public DbSet<RecentFile> RecentFiles => Set<RecentFile>();

    public DbSet<FavoriteEntry> FavoriteEntries => Set<FavoriteEntry>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<EntryTag> EntryTags => Set<EntryTag>();

    public DbSet<FileOperation> FileOperations => Set<FileOperation>();

    public DbSet<TrashPurgeRun> TrashPurgeRuns => Set<TrashPurgeRun>();

    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();

    public DbSet<Share> Shares => Set<Share>();

    public DbSet<ShareMember> ShareMembers => Set<ShareMember>();

    public DbSet<IndexScanRun> IndexScanRuns => Set<IndexScanRun>();

    public DbSet<IndexScanItem> IndexScanItems => Set<IndexScanItem>();

    public DbSet<FileDerivative> FileDerivatives => Set<FileDerivative>();

    public DbSet<MediaJob> MediaJobs => Set<MediaJob>();

    public DbSet<DerivativeLease> DerivativeLeases => Set<DerivativeLease>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KuraStorageDbContext).Assembly);
    }
}

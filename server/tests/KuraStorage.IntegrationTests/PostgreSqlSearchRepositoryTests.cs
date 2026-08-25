using KuraStorage.Application.Search;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Sharing;
using KuraStorage.Infrastructure.Persistence;
using KuraStorage.Infrastructure.Persistence.Queries;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class PostgreSqlSearchRepositoryTests
{
    [Fact]
    public async Task Search_ResolvesOwnedDirectInheritedTieBreakFiltersAndCurrentStateInPostgreSql()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("search_repository")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync();

        var now = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
        var actorId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var actor = User(actorId, "searchactor", now);
        var owner = User(ownerId, "searchowner", now);
        var stranger = User(strangerId, "searchstranger", now);
        var admin = new User(adminId, "SEARCHADMIN", "searchadmin", "integration-hash", UserRole.Admin, now);
        var actorRoot = FileEntry.CreateRoot(actorId, now);
        var actorFile = File(actorId, actorRoot, "Actor report.txt", "text/plain", 7, now);
        var actorUnicode = File(actorId, actorRoot, "Résumé.txt", "text/plain", 8, now);
        var ownerRoot = FileEntry.CreateRoot(ownerId, now);
        var sharedFolder = Folder(ownerId, ownerRoot, "Reports", now);
        var sharedFile = File(ownerId, sharedFolder, "Quarterly REPORT.pdf", "application/pdf", 42, now);
        var image = File(ownerId, sharedFolder, "Photo.JPG", "image/jpeg", 99, now);
        var sharedUnicode = File(ownerId, sharedFolder, "Résumé.txt", "text/plain", 9, now);
        var privateFolder = Folder(ownerId, ownerRoot, "Private", now);
        var privateFile = File(ownerId, privateFolder, "Private report.txt", "text/plain", 5, now);
        var strangerRoot = FileEntry.CreateRoot(strangerId, now);
        var strangerFile = File(strangerId, strangerRoot, "Stranger report.txt", "text/plain", 5, now);
        var folderShare = Share(sharedFolder, ownerId, actorId, SharePermission.Editor, now);
        var directShare = Share(sharedFile, ownerId, actorId, SharePermission.Editor, now);
        database.AddRange(
            actor,
            owner,
            stranger,
            admin,
            actorRoot,
            actorFile,
            actorUnicode,
            ownerRoot,
            sharedFolder,
            sharedFile,
            image,
            sharedUnicode,
            privateFolder,
            privateFile,
            strangerRoot,
            strangerFile,
            folderShare,
            directShare);
        await database.SaveChangesAsync();

        var search = new SearchService(new PostgreSqlSearchRepository(database));
        var reports = await SearchAsync(search, actorId, new SearchQuery(Text: "report", PageSize: 100));

        Assert.Equal(3, reports.TotalCount);
        Assert.DoesNotContain(reports.Items, item => item.Id == privateFile.Id || item.Id == strangerFile.Id);
        Assert.Equal("OWNER", reports.Items.Single(item => item.Id == actorFile.Id).Permission);
        Assert.Equal("DIRECT", reports.Items.Single(item => item.Id == sharedFile.Id).PermissionSource);
        Assert.Equal(directShare.TargetEntryId, reports.Items.Single(item => item.Id == sharedFile.Id).ShareTargetId);
        Assert.Empty((await SearchAsync(search, adminId, new SearchQuery(Text: "report"))).Items);

        var unicodeFirstPage = await SearchAsync(
            search,
            actorId,
            new SearchQuery(Text: "RÉSUMÉ", Page: 1, PageSize: 1));
        var unicodeSecondPage = await SearchAsync(
            search,
            actorId,
            new SearchQuery(Text: "résumé", Page: 2, PageSize: 1));
        Assert.Equal(2, unicodeFirstPage.TotalCount);
        Assert.Equal(2, unicodeSecondPage.TotalCount);
        Assert.NotEqual(Assert.Single(unicodeFirstPage.Items).Id, Assert.Single(unicodeSecondPage.Items).Id);

        var ownerFiltered = await SearchAsync(search, actorId, new SearchQuery(OwnerUserId: ownerId));
        Assert.All(ownerFiltered.Items, item => Assert.Equal(ownerId, item.Owner.Id));
        var sourceFiltered = await SearchAsync(search, actorId, new SearchQuery(ShareTargetId: sharedFolder.Id));
        Assert.Contains(sourceFiltered.Items, item => item.Id == image.Id);
        Assert.DoesNotContain(sourceFiltered.Items, item => item.Id == actorFile.Id);

        var pendingOperation = new FileOperation(
            Guid.NewGuid(),
            actorId,
            FileOperationType.Rename,
            actorFile.Id,
            null,
            actorFile.RelativePath,
            actorFile.RelativePath,
            null,
            null,
            now);
        database.FileOperations.Add(pendingOperation);
        await database.SaveChangesAsync();
        Assert.Empty((await SearchAsync(search, actorId, new SearchQuery(Text: "actor report"))).Items);
        pendingOperation.Complete(now.AddSeconds(1));
        await database.SaveChangesAsync();
        Assert.Equal(
            actorFile.Id,
            Assert.Single((await SearchAsync(search, actorId, new SearchQuery(Text: "actor report"))).Items).Id);

        var images = await SearchAsync(
            search,
            actorId,
            new SearchQuery(FileCategory: "IMAGE", MinSize: 90, MaxSize: 100));
        var imageResult = Assert.Single(images.Items);
        Assert.Equal(image.Id, imageResult.Id);
        Assert.Equal("IMAGE", imageResult.FileCategory);
        Assert.Equal("INHERITED", imageResult.PermissionSource);
        Assert.Equal(sharedFolder.Id, imageResult.ShareTargetId);
        Assert.Equal(
            image.Id,
            Assert.Single((await SearchAsync(
                search,
                actorId,
                new SearchQuery(
                    Text: "photo",
                    EntryType: "FILE",
                    FileCategory: "IMAGE",
                    Status: "ACTIVE",
                    UpdatedFrom: now.AddMinutes(-1),
                    UpdatedTo: now.AddMinutes(1),
                    MinSize: 90,
                    MaxSize: 100,
                    OwnerUserId: ownerId,
                    ShareTargetId: sharedFolder.Id,
                    PageSize: 100))).Items).Id);

        folderShare.SetMemberPermission(actorId, SharePermission.Viewer, now.AddSeconds(2));
        await database.SaveChangesAsync();
        Assert.Equal(
            "VIEWER",
            Assert.Single((await SearchAsync(search, actorId, new SearchQuery(Text: "photo"))).Items).Permission);

        var shortPrefix = await SearchAsync(search, actorId, new SearchQuery(Text: "ph"));
        Assert.Equal(image.Id, Assert.Single(shortPrefix.Items).Id);
        Assert.Empty((await SearchAsync(search, actorId, new SearchQuery(Text: "to"))).Items);
        Assert.Empty((await SearchAsync(search, actorId, new SearchQuery(Text: "%' OR TRUE --"))).Items);

        sharedFile.MarkMissingCandidate(Guid.NewGuid(), now.AddMinutes(1));
        await database.SaveChangesAsync();
        var missing = await SearchAsync(search, actorId, new SearchQuery(Status: "MISSING_CANDIDATE"));
        Assert.Equal(sharedFile.Id, Assert.Single(missing.Items).Id);
        Assert.Equal("MISSING_CANDIDATE", missing.Items[0].Status);

        database.Shares.Remove(directShare);
        await database.SaveChangesAsync();
        var inheritedAfterDirectRemoval = await SearchAsync(search, actorId, new SearchQuery(Text: "quarterly"));
        Assert.Equal("INHERITED", Assert.Single(inheritedAfterDirectRemoval.Items).PermissionSource);

        sharedFile.ApplySourceObservation(
            sharedFile.Size,
            sharedFile.MimeType,
            now,
            null,
            now.AddMinutes(2),
            contentChanged: false);
        sharedFile.MoveTo(
            privateFolder.Id,
            RelativeStoragePath.Create(privateFolder.RelativePath).Append(FileName.Create(sharedFile.Name)),
            now.AddMinutes(3));
        await database.SaveChangesAsync();
        Assert.Empty((await SearchAsync(search, actorId, new SearchQuery(Text: "quarterly"))).Items);

        image.Trash(RelativeStoragePath.Create($"users/{ownerId:N}/trash/{image.Id:N}/Photo.JPG"), now.AddMinutes(4));
        await database.SaveChangesAsync();
        Assert.Empty((await SearchAsync(search, actorId, new SearchQuery(FileCategory: "IMAGE"))).Items);

        image.Restore(
            sharedFolder.Id,
            RelativeStoragePath.Create(sharedFolder.RelativePath).Append(FileName.Create(image.Name)),
            now.AddMinutes(5));
        await database.SaveChangesAsync();
        Assert.Equal(image.Id, Assert.Single((await SearchAsync(
            search,
            actorId,
            new SearchQuery(FileCategory: "IMAGE"))).Items).Id);

        database.FileEntries.Remove(image);
        await database.SaveChangesAsync();
        Assert.Empty((await SearchAsync(search, actorId, new SearchQuery(FileCategory: "IMAGE"))).Items);

        var firstPage = await SearchAsync(search, actorId, new SearchQuery(EntryType: "FOLDER", Page: 1, PageSize: 1));
        var secondPage = await SearchAsync(search, actorId, new SearchQuery(EntryType: "FOLDER", Page: 2, PageSize: 1));
        Assert.Equal(firstPage.TotalCount, secondPage.TotalCount);
        Assert.NotEqual(Assert.Single(firstPage.Items).Id, Assert.Single(secondPage.Items).Id);

        await AssertNameIndexesAreUsableAsync(postgres.GetConnectionString());
    }

    [Fact]
    public async Task Search_BoundsSharedDepthStopsCyclesAndFailsClosedForUnknownPermission()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("search_repository_boundaries")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync();

        var now = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);
        var actorId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var actor = User(actorId, "boundaryactor", now);
        var owner = User(ownerId, "boundaryowner", now);
        var actorRoot = FileEntry.CreateRoot(actorId, now);
        var ownerRoot = FileEntry.CreateRoot(ownerId, now);
        var depthFolders = new List<FileEntry>();
        var parent = ownerRoot;
        for (var depth = 0; depth <= 65; depth++)
        {
            parent = Folder(ownerId, parent, $"depth-{depth:D2}", now);
            depthFolders.Add(parent);
        }

        var depthShare = Share(depthFolders[0], ownerId, actorId, SharePermission.Viewer, now);
        var cycleA = Folder(ownerId, ownerRoot, "cycle-a", now);
        var cycleB = Folder(ownerId, cycleA, "cycle-b", now);
        var cycleShare = Share(cycleA, ownerId, actorId, SharePermission.Viewer, now);
        database.AddRange(actor, owner, actorRoot, ownerRoot);
        database.FileEntries.AddRange(depthFolders);
        database.AddRange(depthShare, cycleA, cycleB, cycleShare);
        await database.SaveChangesAsync();

        var search = new SearchService(new PostgreSqlSearchRepository(database));
        Assert.Equal(
            depthFolders[64].Id,
            Assert.Single((await SearchAsync(search, actorId, new SearchQuery(Text: "depth-64"))).Items).Id);
        Assert.Empty((await SearchAsync(search, actorId, new SearchQuery(Text: "depth-65"))).Items);

        await database.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE file_entries SET parent_id = {cycleB.Id} WHERE id = {cycleA.Id}");
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var cycleResult = await search.SearchAsync(
                actorId,
                new SearchQuery(Text: "cycle"),
                timeout.Token);
            Assert.True(cycleResult.IsSuccess);
            Assert.Equal(2, cycleResult.Value!.Items.Count);
        }

        await database.Database.ExecuteSqlRawAsync(
            "ALTER TABLE share_members DROP CONSTRAINT ck_share_members_permission");
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE share_members SET permission = 'UNKNOWN' WHERE share_id = {cycleShare.Id} AND user_id = {actorId}");
        Assert.Empty((await SearchAsync(search, actorId, new SearchQuery(Text: "cycle"))).Items);
    }

    private static async Task<SearchPage> SearchAsync(
        SearchService service,
        Guid actorId,
        SearchQuery query)
    {
        var result = await service.SearchAsync(actorId, query, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    private static async Task AssertNameIndexesAreUsableAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT indexname FROM pg_indexes
            WHERE tablename = 'file_entries'
              AND indexname = 'ix_file_entries_lower_name_trgm';
            SET enable_seqscan = off;
            EXPLAIN SELECT id FROM file_entries
            WHERE status IN ('ACTIVE', 'MISSING_CANDIDATE', 'MISSING')
              AND lower(name) LIKE '%report%';
            EXPLAIN SELECT id FROM file_entries
            WHERE status IN ('ACTIVE', 'MISSING_CANDIDATE', 'MISSING')
              AND starts_with(lower(name), 're');
            """,
            connection);
        var plan = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        do
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }
        while (await reader.NextResultAsync());

        Assert.Contains(plan, line => line.Contains("ix_file_entries_lower_name_trgm", StringComparison.Ordinal));
        Assert.Contains(plan, line => line.Contains("ix_file_entries_lower_name_prefix_id", StringComparison.Ordinal));
    }

    private static User User(Guid id, string username, DateTimeOffset now) =>
        new(id, username.ToUpperInvariant(), username, "integration-hash", UserRole.Member, now);

    private static FileEntry Folder(Guid ownerId, FileEntry parent, string name, DateTimeOffset now) =>
        FileEntry.CreateFolder(
            Guid.NewGuid(),
            ownerId,
            parent.Id,
            FileName.Create(name),
            RelativeStoragePath.Create(parent.RelativePath).Append(FileName.Create(name)),
            now);

    private static FileEntry File(
        Guid ownerId,
        FileEntry parent,
        string name,
        string mimeType,
        long size,
        DateTimeOffset now) =>
        FileEntry.CreateFile(
            Guid.NewGuid(),
            ownerId,
            parent.Id,
            FileName.Create(name),
            RelativeStoragePath.Create(parent.RelativePath).Append(FileName.Create(name)),
            mimeType,
            size,
            now);

    private static Share Share(
        FileEntry target,
        Guid ownerId,
        Guid memberId,
        SharePermission permission,
        DateTimeOffset now)
    {
        var share = new Share(Guid.NewGuid(), target.Id, ownerId, now);
        share.AddMember(memberId, permission, now);
        return share;
    }
}

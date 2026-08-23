using KuraStorage.Application.Sharing;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Sharing;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class AuthorizationRepositoryTests
{
    [Fact]
    public async Task PostgreSqlQuery_ResolvesBatchInheritanceIsolationAndInvalidTrees()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("authorization_repository")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync();

        var now = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);
        var ownerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        database.Users.AddRange(
            User(ownerId, "AUTHOWNER", UserRole.Member, now),
            User(actorId, "AUTHMEMBER", UserRole.Member, now),
            User(adminId, "AUTHADMIN", UserRole.Admin, now));
        var root = FileEntry.CreateRoot(ownerId, now);
        var sharedFolder = Folder(ownerId, root, "shared", now);
        var nearFolder = Folder(ownerId, sharedFolder, "near", now);
        var strongestFile = File(ownerId, nearFolder, "strongest.txt", now);
        var directTieFile = File(ownerId, sharedFolder, "direct-tie.txt", now);
        var directOnlyFile = File(ownerId, root, "direct-only.txt", now);
        var siblingFile = File(ownerId, root, "sibling.txt", now);
        var trashedFile = File(ownerId, root, "trashed.txt", now);
        trashedFile.Trash(
            RelativeStoragePath.Create($"users/{ownerId:N}/trash/{trashedFile.Id:N}/trashed.txt"),
            now.AddMinutes(1));
        var operationFolder = Folder(ownerId, root, "operation", now);
        var operationChild = File(ownerId, operationFolder, "blocked.txt", now);
        var pageFolder = Folder(ownerId, root, "page", now);
        var pageFiles = Enumerable.Range(0, 100)
            .Select(index => File(ownerId, pageFolder, $"page-{index:D3}.txt", now))
            .ToArray();

        var deepEntries = new List<FileEntry>();
        var deepParent = root;
        for (var depth = 1; depth <= 65; depth++)
        {
            deepParent = Folder(ownerId, deepParent, $"depth-{depth:D2}", now);
            deepEntries.Add(deepParent);
        }

        var cycleFirst = Folder(ownerId, root, "cycle-first", now);
        var cycleSecond = Folder(ownerId, cycleFirst, "cycle-second", now);
        database.FileEntries.AddRange(
            root,
            sharedFolder,
            nearFolder,
            strongestFile,
            directTieFile,
            directOnlyFile,
            siblingFile,
            trashedFile,
            operationFolder,
            operationChild,
            pageFolder,
            cycleFirst,
            cycleSecond);
        database.FileEntries.AddRange(pageFiles);
        database.FileEntries.AddRange(deepEntries);
        database.Shares.AddRange(
            Share(sharedFolder, ownerId, actorId, SharePermission.Editor, now),
            Share(nearFolder, ownerId, actorId, SharePermission.Manager, now),
            Share(strongestFile, ownerId, actorId, SharePermission.Viewer, now),
            Share(directTieFile, ownerId, actorId, SharePermission.Editor, now),
            Share(directOnlyFile, ownerId, actorId, SharePermission.Viewer, now),
            Share(trashedFile, ownerId, actorId, SharePermission.Manager, now),
            Share(operationFolder, ownerId, actorId, SharePermission.Viewer, now),
            Share(pageFolder, ownerId, actorId, SharePermission.Viewer, now));
        database.FileOperations.Add(new FileOperation(
            Guid.NewGuid(),
            ownerId,
            FileOperationType.Move,
            operationFolder.Id,
            Guid.NewGuid().ToString(),
            operationFolder.RelativePath,
            $"{root.RelativePath}/operation-moved",
            null,
            null,
            now));
        await database.SaveChangesAsync();
        await database.Database.ExecuteSqlRawAsync(
            "UPDATE file_entries SET parent_id = {0} WHERE id = {1}",
            cycleSecond.Id,
            cycleFirst.Id);

        var service = new AuthorizationService(new AuthorizationRepository(database));
        var resolved = await service.ResolveBatchAsync(
            actorId,
            [strongestFile.Id, directTieFile.Id, directOnlyFile.Id, siblingFile.Id,
                trashedFile.Id, operationChild.Id],
            CancellationToken.None);

        Assert.Equal(EffectivePermissionLevel.Manager, resolved[strongestFile.Id].Permission);
        Assert.Equal(PermissionSource.Inherited, resolved[strongestFile.Id].Source);
        Assert.Equal(nearFolder.Id, resolved[strongestFile.Id].ShareTargetId);
        Assert.Equal(EffectivePermissionLevel.Editor, resolved[directTieFile.Id].Permission);
        Assert.Equal(PermissionSource.Direct, resolved[directTieFile.Id].Source);
        Assert.Equal(EffectivePermissionLevel.Viewer, resolved[directOnlyFile.Id].Permission);
        Assert.Equal(EffectivePermissionLevel.None, resolved[siblingFile.Id].Permission);
        Assert.Equal(EffectivePermissionLevel.None, resolved[trashedFile.Id].Permission);
        Assert.Equal(EffectivePermissionLevel.None, resolved[operationChild.Id].Permission);

        var adminPermission = await service.ResolveAsync(adminId, directOnlyFile.Id, CancellationToken.None);
        Assert.Equal(EffectivePermissionLevel.None, adminPermission.Permission);

        await database.Database.ExecuteSqlRawAsync(
            "UPDATE users SET status = 'DISABLED' WHERE id = {0}",
            actorId);
        var disabledPermission = await service.ResolveAsync(actorId, directOnlyFile.Id, CancellationToken.None);
        Assert.Equal(EffectivePermissionLevel.None, disabledPermission.Permission);
        await database.Database.ExecuteSqlRawAsync(
            "UPDATE users SET status = 'ACTIVE' WHERE id = {0}",
            actorId);

        var page = await service.ResolveBatchAsync(actorId, pageFiles.Select(file => file.Id).ToArray(), CancellationToken.None);
        Assert.Equal(100, page.Count);
        Assert.All(page.Values, permission => Assert.Equal(EffectivePermissionLevel.Viewer, permission.Permission));

        var depthPermissions = await service.ResolveBatchAsync(
            ownerId,
            [deepEntries[63].Id, deepEntries[64].Id, cycleFirst.Id],
            CancellationToken.None);
        Assert.Equal(EffectivePermissionLevel.Owner, depthPermissions[deepEntries[63].Id].Permission);
        Assert.Equal(EffectivePermissionLevel.None, depthPermissions[deepEntries[64].Id].Permission);
        Assert.Equal(EffectivePermissionLevel.None, depthPermissions[cycleFirst.Id].Permission);

        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var explain = new NpgsqlCommand(
            "SET enable_seqscan = off; EXPLAIN SELECT share_id FROM share_members WHERE user_id = @actor",
            connection);
        explain.Parameters.AddWithValue("actor", actorId);
        var plan = new List<string>();
        await using var reader = await explain.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plan.Add(reader.GetString(0));
        }

        Assert.Contains(plan, line => line.Contains("ix_share_members_user_share", StringComparison.Ordinal));
    }

    private static User User(Guid id, string username, UserRole role, DateTimeOffset now) =>
        new(id, username, username, "integration-hash", role, now);

    private static FileEntry Folder(Guid ownerId, FileEntry parent, string name, DateTimeOffset now) =>
        FileEntry.CreateFolder(
            Guid.NewGuid(),
            ownerId,
            parent.Id,
            FileName.Create(name),
            RelativeStoragePath.Create(parent.RelativePath).Append(FileName.Create(name)),
            now);

    private static FileEntry File(Guid ownerId, FileEntry parent, string name, DateTimeOffset now) =>
        FileEntry.CreateFile(
            Guid.NewGuid(),
            ownerId,
            parent.Id,
            FileName.Create(name),
            RelativeStoragePath.Create(parent.RelativePath).Append(FileName.Create(name)),
            "text/plain",
            1,
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

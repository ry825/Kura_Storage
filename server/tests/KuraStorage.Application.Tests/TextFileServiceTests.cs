using System.Security.Cryptography;
using System.Text;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Sharing;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Sharing;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class TextFileServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T11:00:00Z");

    [Fact]
    public async Task GetAsync_ViewerReadsStrictUtf8WithoutBomAndCreatesBaseline()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("hello 🌏")).ToArray();
        var fixture = new Fixture(bytes, EffectivePermissionLevel.Viewer);

        var result = await fixture.Service.GetAsync(fixture.ActorId, fixture.Entry.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello 🌏", result.Value!.Content);
        Assert.Equal("UTF-8", result.Value.Encoding);
        Assert.Equal(1, result.Value.FileVersion);
        Assert.Equal(bytes.Length, result.Value.Size);
        Assert.Equal(Sha256(bytes), result.Value.Sha256);
        Assert.Single(fixture.Versions.Records);
        Assert.Equal(1, fixture.Files.SaveCalls);
        Assert.True(fixture.Files.LockAcquired);
    }

    [Theory]
    [InlineData(EffectivePermissionLevel.None)]
    public async Task GetAsync_UnauthorizedAccessIsExistenceHiding(EffectivePermissionLevel permission)
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("secret"), permission);

        var result = await fixture.Service.GetAsync(fixture.ActorId, fixture.Entry.Id, default);

        Assert.Equal(TextFileErrorCodes.FileNotFound, result.Failure!.Code);
        Assert.Equal(TextFileFailureKind.NotFound, result.Failure.Kind);
        Assert.Equal(0, fixture.Store.OpenReadCalls);
        Assert.Empty(fixture.Versions.Records);
    }

    [Fact]
    public async Task GetAsync_AdminRoleDoesNotGrantImplicitFileAccess()
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("private"), EffectivePermissionLevel.None);

        var result = await fixture.Service.GetAsync(fixture.ActorId, fixture.Entry.Id, default);

        Assert.Equal(TextFileErrorCodes.FileNotFound, result.Failure!.Code);
        Assert.Equal(fixture.ActorId, fixture.Authorization.LastActorId);
    }

    [Fact]
    public async Task GetAsync_UnsupportedMimeAndOversizeFailBeforeStorageRead()
    {
        var unsupported = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Owner, "image/jpeg");
        var oversized = new Fixture(
            Encoding.UTF8.GetBytes("x"),
            EffectivePermissionLevel.Owner,
            "text/plain",
            FileVersionRecord.MaximumContentBytes + 1);

        var unsupportedResult = await unsupported.Service.GetAsync(
            unsupported.ActorId, unsupported.Entry.Id, default);
        var oversizedResult = await oversized.Service.GetAsync(oversized.ActorId, oversized.Entry.Id, default);

        Assert.Equal(TextFileErrorCodes.UnsupportedTextType, unsupportedResult.Failure!.Code);
        Assert.Equal(TextFileFailureKind.UnsupportedMediaType, unsupportedResult.Failure.Kind);
        Assert.Equal(TextFileErrorCodes.TextSizeLimitExceeded, oversizedResult.Failure!.Code);
        Assert.Equal(TextFileFailureKind.PayloadTooLarge, oversizedResult.Failure.Kind);
        Assert.Equal(0, unsupported.Store.OpenReadCalls);
        Assert.Equal(0, oversized.Store.OpenReadCalls);
    }

    [Fact]
    public async Task GetAsync_InvalidUtf8AndIncompleteOperationFailClosed()
    {
        var invalid = new Fixture([0xc3, 0x28], EffectivePermissionLevel.Owner);
        var blocked = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Owner);
        blocked.Files.HasIncomplete = true;

        var invalidResult = await invalid.Service.GetAsync(invalid.ActorId, invalid.Entry.Id, default);
        var blockedResult = await blocked.Service.GetAsync(blocked.ActorId, blocked.Entry.Id, default);

        Assert.Equal(TextFileErrorCodes.TextEncodingInvalid, invalidResult.Failure!.Code);
        Assert.Equal(TextFileFailureKind.Unprocessable, invalidResult.Failure.Kind);
        Assert.Equal(TextFileErrorCodes.FileStateConflict, blockedResult.Failure!.Code);
        Assert.Equal(TextFileFailureKind.Conflict, blockedResult.Failure.Kind);
        Assert.Equal(0, blocked.Store.OpenReadCalls);
    }

    [Fact]
    public async Task SaveAsync_EditorPublishesBomlessVersionAndUpdatesEntryAtomically()
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        var operationId = Guid.NewGuid();

        var result = await fixture.Service.SaveAsync(
            new SaveTextFileCommand(
                fixture.ActorId,
                fixture.DeviceId,
                fixture.Entry.Id,
                "\uFEFFafter 🌏",
                1,
                operationId,
                "request-save"),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.FileVersion);
        Assert.Equal("TEXT_EDIT", result.Value.ChangeKind);
        Assert.Equal("after 🌏", Encoding.UTF8.GetString(fixture.Store.CurrentContent));
        Assert.Equal(2, fixture.Entry.FileVersion);
        Assert.Equal(fixture.Store.CurrentContent.LongLength, fixture.Entry.Size);
        Assert.Equal(2, fixture.Versions.Records.Count);
        Assert.Equal(FileVersionChangeKind.TextEdit, fixture.Versions.Records.Single(v => v.Version == 2).ChangeKind);
        Assert.Equal(fixture.ActorId, fixture.Versions.Records.Single(v => v.Version == 2).ActorUserId);
        Assert.Equal(fixture.DeviceId, fixture.Versions.Records.Single(v => v.Version == 2).ActorDeviceId);
        Assert.Equal(FileOperationStatus.Completed, Assert.Single(fixture.Files.Operations).Status);
        Assert.Equal("FILE_TEXT_EDIT", Assert.Single(fixture.Files.Audits).Action);
    }

    [Fact]
    public async Task SaveAsync_ViewerAndStaleVersionDoNotChangeCurrentOrHistory()
    {
        var viewer = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Viewer);
        var stale = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);

        var denied = await viewer.Service.SaveAsync(
            Command(viewer, "denied", expectedVersion: 1), default);
        var conflict = await stale.Service.SaveAsync(
            Command(stale, "stale", expectedVersion: 2), default);

        Assert.Equal(TextFileErrorCodes.FileNotFound, denied.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileVersionConflict, conflict.Failure!.Code);
        Assert.Equal("before", Encoding.UTF8.GetString(viewer.Store.CurrentContent));
        Assert.Equal("before", Encoding.UTF8.GetString(stale.Store.CurrentContent));
        Assert.Empty(viewer.Versions.Records);
        Assert.Empty(stale.Versions.Records);
        Assert.Empty(viewer.Files.Operations);
        Assert.Empty(stale.Files.Operations);
    }

    [Fact]
    public async Task SaveAsync_SameOperationIdReturnsOriginalResultWithoutDuplicateVersion()
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        var operationId = Guid.NewGuid();
        var command = Command(fixture, "after", expectedVersion: 1, operationId);

        var first = await fixture.Service.SaveAsync(command, default);
        var retry = await fixture.Service.SaveAsync(command, default);

        Assert.True(first.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value, retry.Value);
        Assert.Equal(2, fixture.Entry.FileVersion);
        Assert.Equal(2, fixture.Versions.Records.Count);
        Assert.Single(fixture.Files.Operations);
        Assert.Single(fixture.Files.Audits);
    }

    [Fact]
    public async Task ListVersionsAsync_ReturnsDescendingMetadataWithoutContentPaths()
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        Assert.True((await fixture.Service.SaveAsync(Command(fixture, "after", 1), default)).IsSuccess);

        var result = await fixture.Service.ListVersionsAsync(
            fixture.ActorId,
            fixture.Entry.Id,
            page: 1,
            pageSize: 50,
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal([2L, 1L], result.Value.Items.Select(item => item.Version));
        Assert.Equal("TEXT_EDIT", result.Value.Items[0].ChangeKind);
        Assert.Equal("Editor User", result.Value.Items[0].ActorDisplayName);
        Assert.Equal("Deleted user", result.Value.Items[1].ActorDisplayName);
        Assert.All(result.Value.Items, item => Assert.Equal(64, item.Sha256.Length));
        Assert.Equal(1, fixture.Versions.ListCalls);
    }

    [Fact]
    public async Task ListVersionsAsync_RejectsInvalidPageAndPermissionWithoutQuery()
    {
        var invalid = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Viewer);
        var denied = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.None);

        var invalidResult = await invalid.Service.ListVersionsAsync(
            invalid.ActorId, invalid.Entry.Id, 0, 50, default);
        var deniedResult = await denied.Service.ListVersionsAsync(
            denied.ActorId, denied.Entry.Id, 1, 50, default);

        Assert.Equal(TextFileErrorCodes.ValidationFailed, invalidResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileNotFound, deniedResult.Failure!.Code);
        Assert.Equal(0, invalid.Versions.ListCalls);
        Assert.Equal(0, denied.Versions.ListCalls);
    }

    [Fact]
    public async Task GetVersionTextAsync_ReturnsImmutablePastContentAndVerifiesChecksum()
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        Assert.True((await fixture.Service.SaveAsync(Command(fixture, "after", 1), default)).IsSuccess);

        var result = await fixture.Service.GetVersionTextAsync(
            fixture.ActorId,
            fixture.Entry.Id,
            version: 1,
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal("before", result.Value!.Content);
        Assert.Equal(1, result.Value.FileVersion);
        Assert.Equal(Sha256(Encoding.UTF8.GetBytes("before")), result.Value.Sha256);
        Assert.Equal("after", Encoding.UTF8.GetString(fixture.Store.CurrentContent));
    }

    [Fact]
    public async Task VersionReads_HideMissingCandidateMissingAndTrashedFiles()
    {
        var candidate = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Owner);
        var observation = Guid.NewGuid();
        candidate.Entry.MarkMissingCandidate(observation, Now);
        var candidateResult = await candidate.Service.ListVersionsAsync(
            candidate.ActorId, candidate.Entry.Id, 1, 50, default);

        var missing = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Owner);
        missing.Entry.MarkMissingCandidate(observation, Now);
        missing.Entry.ConfirmMissing(Guid.NewGuid(), Now.AddMinutes(6), TimeSpan.FromMinutes(5));
        var missingResult = await missing.Service.ListVersionsAsync(
            missing.ActorId, missing.Entry.Id, 1, 50, default);

        var trashed = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Owner);
        trashed.Entry.Trash(
            RelativeStoragePath.Create($"users/{trashed.Entry.OwnerUserId:N}/trash/{trashed.Entry.Id:N}/note.txt"),
            Now);
        var trashedResult = await trashed.Service.ListVersionsAsync(
            trashed.ActorId, trashed.Entry.Id, 1, 50, default);

        Assert.Equal(TextFileErrorCodes.FileNotFound, candidateResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileNotFound, missingResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileNotFound, trashedResult.Failure!.Code);
        Assert.Equal(0, candidate.Versions.ListCalls);
        Assert.Equal(0, missing.Versions.ListCalls);
        Assert.Equal(0, trashed.Versions.ListCalls);
    }

    [Fact]
    public async Task RestoreAsync_CopiesPastContentToNewVersionAndKeepsPreRestoreVersion()
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("version one"), EffectivePermissionLevel.Editor);
        Assert.True((await fixture.Service.SaveAsync(Command(fixture, "version two", 1), default)).IsSuccess);
        var operationId = Guid.NewGuid();

        var result = await fixture.Service.RestoreAsync(
            RestoreCommand(fixture, version: 1, expectedVersion: 2, operationId),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.FileVersion);
        Assert.Equal("RESTORE", result.Value.ChangeKind);
        Assert.Equal("version one", Encoding.UTF8.GetString(fixture.Store.CurrentContent));
        Assert.Equal([1L, 2L, 3L], fixture.Versions.Records.Select(record => record.Version).Order());
        Assert.Equal(FileVersionChangeKind.Restore, fixture.Versions.Records.Single(record => record.Version == 3).ChangeKind);
        Assert.Equal("version two", Encoding.UTF8.GetString(
            fixture.Store.GetVersionContent(fixture.Versions.Records.Single(record => record.Version == 2))));
        Assert.Equal("FILE_VERSION_RESTORE", fixture.Files.Audits.Last().Action);
    }

    [Fact]
    public async Task RestoreAsync_StaleExpectedVersionAndMissingTargetDoNotMutate()
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("current"), EffectivePermissionLevel.Editor);

        var stale = await fixture.Service.RestoreAsync(
            RestoreCommand(fixture, version: 1, expectedVersion: 2), default);
        var missing = await fixture.Service.RestoreAsync(
            RestoreCommand(fixture, version: 99, expectedVersion: 1), default);

        Assert.Equal(TextFileErrorCodes.FileVersionConflict, stale.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileVersionNotFound, missing.Failure!.Code);
        Assert.Equal(1, fixture.Entry.FileVersion);
        Assert.Equal("current", Encoding.UTF8.GetString(fixture.Store.CurrentContent));
        Assert.Single(fixture.Versions.Records);
        Assert.Empty(fixture.Files.Operations);
    }

    [Fact]
    public async Task RestoreAsync_SameOperationIdIsIdempotent()
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("one"), EffectivePermissionLevel.Editor);
        Assert.True((await fixture.Service.SaveAsync(Command(fixture, "two", 1), default)).IsSuccess);
        var operationId = Guid.NewGuid();
        var command = RestoreCommand(fixture, 1, 2, operationId);

        var first = await fixture.Service.RestoreAsync(command, default);
        var retry = await fixture.Service.RestoreAsync(command, default);

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value, retry.Value);
        Assert.Equal(3, fixture.Entry.FileVersion);
        Assert.Equal(3, fixture.Versions.Records.Count);
        Assert.Equal(2, fixture.Files.Operations.Count);
        Assert.Equal(2, fixture.Files.Audits.Count);
    }

    [Fact]
    public async Task SaveAndRestore_InsufficientCapacityDoNotCreateMutationVersion()
    {
        var save = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        save.Store.HasCapacity = false;
        var saveResult = await save.Service.SaveAsync(Command(save, "after", 1), default);

        var restore = new Fixture(Encoding.UTF8.GetBytes("one"), EffectivePermissionLevel.Editor);
        Assert.True((await restore.Service.SaveAsync(Command(restore, "two", 1), default)).IsSuccess);
        restore.Store.HasCapacity = false;
        var restoreResult = await restore.Service.RestoreAsync(
            RestoreCommand(restore, 1, 2), default);

        Assert.Equal(TextFileErrorCodes.StorageCapacityInsufficient, saveResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.StorageCapacityInsufficient, restoreResult.Failure!.Code);
        Assert.Empty(save.Versions.Records);
        Assert.Equal(2, restore.Versions.Records.Count);
        Assert.Equal("two", Encoding.UTF8.GetString(restore.Store.CurrentContent));
    }

    [Fact]
    public async Task Restore_CorruptHistoricalArtifactFailsWithoutChangingCurrent()
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("one"), EffectivePermissionLevel.Editor);
        Assert.True((await fixture.Service.SaveAsync(Command(fixture, "two", 1), default)).IsSuccess);
        fixture.Store.CorruptVersionReads = true;

        var result = await fixture.Service.RestoreAsync(RestoreCommand(fixture, 1, 2), default);

        Assert.Equal(TextFileErrorCodes.FileVersionCorrupt, result.Failure!.Code);
        Assert.Equal(2, fixture.Entry.FileVersion);
        Assert.Equal("two", Encoding.UTF8.GetString(fixture.Store.CurrentContent));
        Assert.Equal(2, fixture.Versions.Records.Count);
    }

    [Fact]
    public async Task GetAsync_InvalidIdentityAndStorageFailuresAreFailClosed()
    {
        var invalid = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Owner);
        var invalidResult = await invalid.Service.GetAsync(Guid.Empty, invalid.Entry.Id, default);

        var unavailable = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Owner);
        unavailable.Guard.Status = StorageStatus.Unavailable;
        var unavailableResult = await unavailable.Service.GetAsync(
            unavailable.ActorId, unavailable.Entry.Id, default);

        var publishFailure = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Owner);
        publishFailure.Store.ThrowPublishStorage = true;
        var publishResult = await publishFailure.Service.GetAsync(
            publishFailure.ActorId, publishFailure.Entry.Id, default);

        var consistencyFailure = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Owner);
        consistencyFailure.Store.ThrowPublishConsistency = true;
        var consistencyResult = await consistencyFailure.Service.GetAsync(
            consistencyFailure.ActorId, consistencyFailure.Entry.Id, default);

        Assert.Equal(TextFileErrorCodes.FileNotFound, invalidResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.StorageUnavailable, unavailableResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.StorageUnavailable, publishResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileVersionCorrupt, consistencyResult.Failure!.Code);
    }

    [Fact]
    public async Task GetAsync_CurrentReadMismatchSizeAndIoAreTypedFailures()
    {
        var mismatch = new Fixture(Encoding.UTF8.GetBytes("abc"), EffectivePermissionLevel.Owner);
        Assert.True((await mismatch.Service.GetAsync(mismatch.ActorId, mismatch.Entry.Id, default)).IsSuccess);
        mismatch.Store.ChangeCurrentContent(Encoding.UTF8.GetBytes("xyz"));
        var mismatchResult = await mismatch.Service.GetAsync(mismatch.ActorId, mismatch.Entry.Id, default);

        var size = new Fixture(Encoding.UTF8.GetBytes("abc"), EffectivePermissionLevel.Owner);
        Assert.True((await size.Service.GetAsync(size.ActorId, size.Entry.Id, default)).IsSuccess);
        size.Store.ChangeCurrentContent(Encoding.UTF8.GetBytes("abcd"));
        var sizeResult = await size.Service.GetAsync(size.ActorId, size.Entry.Id, default);

        var io = new Fixture(Encoding.UTF8.GetBytes("abc"), EffectivePermissionLevel.Owner);
        Assert.True((await io.Service.GetAsync(io.ActorId, io.Entry.Id, default)).IsSuccess);
        io.Store.ThrowCurrentRead = true;
        var ioResult = await io.Service.GetAsync(io.ActorId, io.Entry.Id, default);

        Assert.Equal(TextFileErrorCodes.FileVersionCorrupt, mismatchResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.TextSizeLimitExceeded, sizeResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.StorageUnavailable, ioResult.Failure!.Code);
    }

    [Fact]
    public async Task SaveAsync_ValidationEligibilityAndStorageFailuresDoNotCreateOperation()
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        var invalid = await fixture.Service.SaveAsync(
            new SaveTextFileCommand(
                Guid.Empty, fixture.DeviceId, fixture.Entry.Id, "after", 1, Guid.NewGuid(), "request"),
            default);
        var missingContent = await fixture.Service.SaveAsync(
            new SaveTextFileCommand(
                fixture.ActorId, fixture.DeviceId, fixture.Entry.Id, null, 1, Guid.NewGuid(), "request"),
            default);

        var unsupported = new Fixture(
            Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor, "image/png");
        var unsupportedResult = await unsupported.Service.SaveAsync(Command(unsupported, "after", 1), default);

        var blocked = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        blocked.Files.HasIncomplete = true;
        var blockedResult = await blocked.Service.SaveAsync(Command(blocked, "after", 1), default);

        var unavailable = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        unavailable.Guard.Status = StorageStatus.Unavailable;
        var unavailableResult = await unavailable.Service.SaveAsync(Command(unavailable, "after", 1), default);

        Assert.Equal(TextFileErrorCodes.ValidationFailed, invalid.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.ValidationFailed, missingContent.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.UnsupportedTextType, unsupportedResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileStateConflict, blockedResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.StorageUnavailable, unavailableResult.Failure!.Code);
        Assert.Empty(fixture.Files.Operations);
    }

    [Fact]
    public async Task SaveAsync_IdempotencyConflictsAndMissingCompletedRecordFailClosed()
    {
        var mismatch = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        var operationId = Guid.NewGuid();
        Assert.True((await mismatch.Service.SaveAsync(Command(mismatch, "after", 1, operationId), default)).IsSuccess);
        var mismatchResult = await mismatch.Service.SaveAsync(
            Command(mismatch, "different", 2, operationId), default);

        var missing = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        var missingId = Guid.NewGuid();
        Assert.True((await missing.Service.SaveAsync(Command(missing, "after", 1, missingId), default)).IsSuccess);
        missing.Versions.Records.RemoveAll(record => record.Version == 2);
        var missingResult = await missing.Service.SaveAsync(Command(missing, "after", 1, missingId), default);

        var recovery = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        recovery.Store.ThrowReplace = true;
        var recoveryCommand = Command(recovery, "after", 1);
        var firstFailure = await recovery.Service.SaveAsync(recoveryCommand, default);
        recovery.Store.ThrowReplace = false;
        var recoveryRetry = await recovery.Service.SaveAsync(recoveryCommand, default);

        Assert.Equal(TextFileErrorCodes.IdempotencyConflict, mismatchResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.RecoveryRequired, missingResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.RecoveryRequired, firstFailure.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.RecoveryRequired, recoveryRetry.Failure!.Code);
    }

    [Fact]
    public async Task SaveAsync_PublishAndPersistenceFailuresRequireRecovery()
    {
        var invalidPublish = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        Assert.True((await invalidPublish.Service.GetAsync(
            invalidPublish.ActorId, invalidPublish.Entry.Id, default)).IsSuccess);
        invalidPublish.Store.PublishInvalidEncoding = true;
        var invalidPublishResult = await invalidPublish.Service.SaveAsync(
            Command(invalidPublish, "after", 1), default);

        var mismatch = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        mismatch.Store.WriteUploadMismatch = true;
        var mismatchResult = await mismatch.Service.SaveAsync(Command(mismatch, "after", 1), default);

        var persistence = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        persistence.Files.ThrowPersistenceConflictOnSaveCall = 4;
        var persistenceResult = await persistence.Service.SaveAsync(Command(persistence, "after", 1), default);

        Assert.Equal(TextFileErrorCodes.RecoveryRequired, invalidPublishResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.RecoveryRequired, mismatchResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.RecoveryRequired, persistenceResult.Failure!.Code);
        Assert.All(
            new[] { invalidPublish, mismatch, persistence },
            item => Assert.Equal(FileOperationStatus.RecoveryRequired, Assert.Single(item.Files.Operations).Status));
    }

    [Fact]
    public async Task SaveAsync_ResumesPublishedVersionAndCompletesJournalMetadata()
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        Assert.True((await fixture.Service.GetAsync(fixture.ActorId, fixture.Entry.Id, default)).IsSuccess);
        var operationId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("after");
        var sha = Sha256(content);
        var record = new FileVersionRecord(
            Guid.NewGuid(), fixture.Entry.Id, 2, content.LongLength, sha,
            $"versions/{fixture.Entry.OwnerUserId:N}/{fixture.Entry.Id:N}/2/{sha}.bin",
            FileVersionChangeKind.TextEdit, fixture.ActorId, fixture.DeviceId, Now);
        fixture.Versions.Add(record);
        fixture.Store.SeedVersion(record, content);
        fixture.Files.Add(new FileOperation(
            operationId, fixture.Entry.OwnerUserId, FileOperationType.TextEdit, fixture.Entry.Id,
            operationId.ToString("D"), $"upload-temp/{fixture.Entry.OwnerUserId:N}/{operationId:N}.upload",
            fixture.Entry.RelativePath, content.LongLength, sha, Now, fixture.DeviceId, "request-save"));

        var result = await fixture.Service.SaveAsync(Command(fixture, "after", 1, operationId), default);

        Assert.True(result.IsSuccess);
        var operation = Assert.Single(fixture.Files.Operations);
        Assert.Equal(2, operation.ResultFileVersion);
        Assert.Equal(FileOperationStatus.Completed, operation.Status);
    }

    [Fact]
    public async Task VersionQueries_ValidationIncompleteStorageAndReadFailuresAreTyped()
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("one"), EffectivePermissionLevel.Viewer);
        var invalid = await fixture.Service.GetVersionTextAsync(fixture.ActorId, fixture.Entry.Id, 0, default);

        fixture.Files.HasIncomplete = true;
        var incompleteList = await fixture.Service.ListVersionsAsync(
            fixture.ActorId, fixture.Entry.Id, 1, 50, default);
        var incompleteText = await fixture.Service.GetVersionTextAsync(
            fixture.ActorId, fixture.Entry.Id, 1, default);

        fixture.Files.HasIncomplete = false;
        fixture.Guard.Status = StorageStatus.Unavailable;
        var unavailable = await fixture.Service.GetVersionTextAsync(
            fixture.ActorId, fixture.Entry.Id, 1, default);

        fixture.Guard.Status = StorageStatus.Available;
        Assert.True((await fixture.Service.ListVersionsAsync(
            fixture.ActorId, fixture.Entry.Id, 1, 50, default)).IsSuccess);
        var missing = await fixture.Service.GetVersionTextAsync(
            fixture.ActorId, fixture.Entry.Id, 99, default);
        fixture.Store.ThrowVersionRead = true;
        var io = await fixture.Service.GetVersionTextAsync(
            fixture.ActorId, fixture.Entry.Id, 1, default);

        Assert.Equal(TextFileErrorCodes.ValidationFailed, invalid.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileStateConflict, incompleteList.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileStateConflict, incompleteText.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.StorageUnavailable, unavailable.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileVersionNotFound, missing.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.StorageUnavailable, io.Failure!.Code);
    }

    [Fact]
    public async Task RestoreAsync_ValidationPermissionIncompleteStorageAndRetryConflictsAreTyped()
    {
        var invalid = new Fixture(Encoding.UTF8.GetBytes("one"), EffectivePermissionLevel.Editor);
        var invalidResult = await invalid.Service.RestoreAsync(
            new RestoreTextVersionCommand(
                Guid.Empty, invalid.DeviceId, invalid.Entry.Id, 1, 1, Guid.NewGuid(), "request"),
            default);

        var denied = new Fixture(Encoding.UTF8.GetBytes("one"), EffectivePermissionLevel.Viewer);
        var deniedResult = await denied.Service.RestoreAsync(RestoreCommand(denied, 1, 1), default);

        var blocked = new Fixture(Encoding.UTF8.GetBytes("one"), EffectivePermissionLevel.Editor);
        blocked.Files.HasIncomplete = true;
        var blockedResult = await blocked.Service.RestoreAsync(RestoreCommand(blocked, 1, 1), default);

        var unavailable = new Fixture(Encoding.UTF8.GetBytes("one"), EffectivePermissionLevel.Editor);
        unavailable.Guard.Status = StorageStatus.Unavailable;
        var unavailableResult = await unavailable.Service.RestoreAsync(RestoreCommand(unavailable, 1, 1), default);

        var conflict = new Fixture(Encoding.UTF8.GetBytes("one"), EffectivePermissionLevel.Editor);
        var operationId = Guid.NewGuid();
        Assert.True((await conflict.Service.RestoreAsync(
            RestoreCommand(conflict, 1, 1, operationId), default)).IsSuccess);
        var conflictResult = await conflict.Service.RestoreAsync(
            RestoreCommand(conflict, 2, 2, operationId), default);

        Assert.Equal(TextFileErrorCodes.ValidationFailed, invalidResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileNotFound, deniedResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileStateConflict, blockedResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.StorageUnavailable, unavailableResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.IdempotencyConflict, conflictResult.Failure!.Code);
    }

    [Fact]
    public async Task RestoreAsync_ReadPublishReplacementAndPersistenceFailuresAreTyped()
    {
        var read = await PreparedRestoreFixtureAsync();
        read.Store.ThrowVersionRead = true;
        var readResult = await read.Service.RestoreAsync(RestoreCommand(read, 1, 2), default);

        var publish = await PreparedRestoreFixtureAsync();
        publish.Store.PublishInvalidEncoding = true;
        var publishResult = await publish.Service.RestoreAsync(RestoreCommand(publish, 1, 2), default);

        var mismatch = await PreparedRestoreFixtureAsync();
        mismatch.Store.WriteUploadMismatch = true;
        var mismatchResult = await mismatch.Service.RestoreAsync(RestoreCommand(mismatch, 1, 2), default);

        var persistence = await PreparedRestoreFixtureAsync();
        persistence.Files.ThrowPersistenceConflictOnSaveCall = persistence.Files.SaveCalls + 4;
        var persistenceResult = await persistence.Service.RestoreAsync(
            RestoreCommand(persistence, 1, 2), default);

        Assert.Equal(TextFileErrorCodes.StorageUnavailable, readResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.RecoveryRequired, publishResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.RecoveryRequired, mismatchResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.RecoveryRequired, persistenceResult.Failure!.Code);
    }

    [Fact]
    public async Task RestoreAsync_ResumesPublishedVersionAndCompletesJournalMetadata()
    {
        var fixture = await PreparedRestoreFixtureAsync();
        var target = fixture.Versions.Records.Single(record => record.Version == 1);
        var content = fixture.Store.GetVersionContent(target);
        var operationId = Guid.NewGuid();
        var record = new FileVersionRecord(
            Guid.NewGuid(), fixture.Entry.Id, 3, target.Size, target.Sha256,
            $"versions/{fixture.Entry.OwnerUserId:N}/{fixture.Entry.Id:N}/3/{target.Sha256}.bin",
            FileVersionChangeKind.Restore, fixture.ActorId, fixture.DeviceId, Now);
        fixture.Versions.Add(record);
        fixture.Store.SeedVersion(record, content);
        var operation = new FileOperation(
            operationId, fixture.Entry.OwnerUserId, FileOperationType.VersionRestore, fixture.Entry.Id,
            operationId.ToString("D"), $"upload-temp/{fixture.Entry.OwnerUserId:N}/{operationId:N}.upload",
            fixture.Entry.RelativePath, target.Size, target.Sha256, Now,
            fixture.DeviceId, "request-restore", "1");
        operation.RecordPublishedVersion(
            2,
            3,
            $"version-temp/{fixture.Entry.OwnerUserId:N}/{fixture.Entry.Id:N}/3/{operationId:N}.part",
            record.ContentRelativePath,
            record.Sha256,
            Now);
        operation.MarkFilesystemDone(Now);
        fixture.Files.Add(operation);

        var result = await fixture.Service.RestoreAsync(
            RestoreCommand(fixture, 1, 2, operationId), default);

        Assert.True(result.IsSuccess);
        var completedOperation = fixture.Files.Operations.Last();
        Assert.Equal(3, completedOperation.ResultFileVersion);
        Assert.Equal(FileOperationStatus.Completed, completedOperation.Status);
    }

    [Fact]
    public async Task SaveAsync_EncodingAndBaselineFailuresAreTyped()
    {
        var oversized = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Editor);
        var oversizedResult = await oversized.Service.SaveAsync(
            Command(oversized, new string('a', checked((int)FileVersionRecord.MaximumContentBytes + 1)), 1),
            default);

        var invalidText = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Editor);
        var invalidTextResult = await invalidText.Service.SaveAsync(Command(invalidText, "\ud800", 1), default);

        var invalidBaseline = new Fixture([0xc3, 0x28], EffectivePermissionLevel.Editor);
        var invalidBaselineResult = await invalidBaseline.Service.SaveAsync(
            Command(invalidBaseline, "after", 1), default);

        var storage = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Editor);
        storage.Store.ThrowPublishStorage = true;
        var storageResult = await storage.Service.SaveAsync(Command(storage, "after", 1), default);

        Assert.Equal(TextFileErrorCodes.TextSizeLimitExceeded, oversizedResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.TextEncodingInvalid, invalidTextResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.TextEncodingInvalid, invalidBaselineResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.StorageUnavailable, storageResult.Failure!.Code);
    }

    [Fact]
    public async Task VersionQueries_BaselineFailuresAreTyped()
    {
        var invalid = new Fixture([0xc3, 0x28], EffectivePermissionLevel.Viewer);
        var invalidResult = await invalid.Service.ListVersionsAsync(
            invalid.ActorId, invalid.Entry.Id, 1, 50, default);

        var storage = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Viewer);
        storage.Store.ThrowPublishStorage = true;
        var storageResult = await storage.Service.ListVersionsAsync(
            storage.ActorId, storage.Entry.Id, 1, 50, default);

        var corrupt = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Viewer);
        corrupt.Store.ThrowPublishConsistency = true;
        var corruptResult = await corrupt.Service.GetVersionTextAsync(
            corrupt.ActorId, corrupt.Entry.Id, 1, default);

        Assert.Equal(TextFileErrorCodes.TextEncodingInvalid, invalidResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.StorageUnavailable, storageResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileVersionCorrupt, corruptResult.Failure!.Code);
    }

    [Fact]
    public async Task GetVersionTextAsync_DeniedUnsupportedBomInvalidUtf8AndChecksumAreHandled()
    {
        var denied = new Fixture(Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.None);
        var deniedResult = await denied.Service.GetVersionTextAsync(
            denied.ActorId, denied.Entry.Id, 1, default);

        var unsupported = new Fixture(
            Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Viewer, "image/png");
        var unsupportedResult = await unsupported.Service.GetVersionTextAsync(
            unsupported.ActorId, unsupported.Entry.Id, 1, default);

        var bomBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("x")).ToArray();
        var bom = new Fixture(bomBytes, EffectivePermissionLevel.Viewer);
        var bomResult = await bom.Service.GetVersionTextAsync(bom.ActorId, bom.Entry.Id, 1, default);

        var invalidUtf8 = VersionFixtureWithContent([0xc3, 0x28]);
        var invalidUtf8Result = await invalidUtf8.Service.GetVersionTextAsync(
            invalidUtf8.ActorId, invalidUtf8.Entry.Id, 1, default);

        var checksum = new Fixture(Encoding.UTF8.GetBytes("one"), EffectivePermissionLevel.Viewer);
        Assert.True((await checksum.Service.ListVersionsAsync(
            checksum.ActorId, checksum.Entry.Id, 1, 50, default)).IsSuccess);
        var checksumRecord = Assert.Single(checksum.Versions.Records);
        checksum.Store.SeedVersion(checksumRecord, Encoding.UTF8.GetBytes("two"));
        checksum.Store.SkipVersionVerification = true;
        var checksumResult = await checksum.Service.GetVersionTextAsync(
            checksum.ActorId, checksum.Entry.Id, 1, default);

        Assert.Equal(TextFileErrorCodes.FileNotFound, deniedResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.UnsupportedTextType, unsupportedResult.Failure!.Code);
        Assert.Equal("x", bomResult.Value!.Content);
        Assert.Equal(TextFileErrorCodes.FileVersionCorrupt, invalidUtf8Result.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileVersionCorrupt, checksumResult.Failure!.Code);
    }

    [Fact]
    public async Task RestoreAsync_EligibilityBaselineChecksumAndRecoveryRetryAreHandled()
    {
        var unsupported = new Fixture(
            Encoding.UTF8.GetBytes("x"), EffectivePermissionLevel.Editor, "image/png");
        var unsupportedResult = await unsupported.Service.RestoreAsync(
            RestoreCommand(unsupported, 1, 1), default);

        var invalidBaseline = new Fixture([0xc3, 0x28], EffectivePermissionLevel.Editor);
        var invalidBaselineResult = await invalidBaseline.Service.RestoreAsync(
            RestoreCommand(invalidBaseline, 1, 1), default);

        var checksum = await PreparedRestoreFixtureAsync();
        var target = checksum.Versions.Records.Single(record => record.Version == 1);
        checksum.Store.SeedVersion(target, Encoding.UTF8.GetBytes("two"));
        checksum.Store.SkipVersionVerification = true;
        var checksumResult = await checksum.Service.RestoreAsync(RestoreCommand(checksum, 1, 2), default);

        var recovery = await PreparedRestoreFixtureAsync();
        recovery.Store.ThrowReplace = true;
        var command = RestoreCommand(recovery, 1, 2);
        var first = await recovery.Service.RestoreAsync(command, default);
        recovery.Store.ThrowReplace = false;
        var retry = await recovery.Service.RestoreAsync(command, default);

        Assert.Equal(TextFileErrorCodes.UnsupportedTextType, unsupportedResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.TextEncodingInvalid, invalidBaselineResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.FileVersionCorrupt, checksumResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.RecoveryRequired, first.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.RecoveryRequired, retry.Failure!.Code);
    }

    [Fact]
    public async Task ExistingMutationVersionMismatchRequiresRecovery()
    {
        var save = new Fixture(Encoding.UTF8.GetBytes("before"), EffectivePermissionLevel.Editor);
        Assert.True((await save.Service.GetAsync(save.ActorId, save.Entry.Id, default)).IsSuccess);
        var saveId = Guid.NewGuid();
        var after = Encoding.UTF8.GetBytes("after");
        var afterSha = Sha256(after);
        save.Versions.Add(new FileVersionRecord(
            Guid.NewGuid(), save.Entry.Id, 2, after.LongLength, afterSha,
            $"versions/{save.Entry.OwnerUserId:N}/{save.Entry.Id:N}/2/{afterSha}.bin",
            FileVersionChangeKind.Restore, save.ActorId, save.DeviceId, Now));
        save.Files.Add(new FileOperation(
            saveId, save.Entry.OwnerUserId, FileOperationType.TextEdit, save.Entry.Id,
            saveId.ToString("D"), "temp", save.Entry.RelativePath,
            after.LongLength, afterSha, Now, save.DeviceId, "request-save"));
        var saveResult = await save.Service.SaveAsync(Command(save, "after", 1, saveId), default);

        var restore = await PreparedRestoreFixtureAsync();
        var restoreId = Guid.NewGuid();
        var restoreTarget = restore.Versions.Records.Single(record => record.Version == 1);
        restore.Versions.Add(new FileVersionRecord(
            Guid.NewGuid(), restore.Entry.Id, 3, restoreTarget.Size, restoreTarget.Sha256,
            $"versions/{restore.Entry.OwnerUserId:N}/{restore.Entry.Id:N}/3/{restoreTarget.Sha256}.bin",
            FileVersionChangeKind.TextEdit, restore.ActorId, restore.DeviceId, Now));
        restore.Files.Add(new FileOperation(
            restoreId, restore.Entry.OwnerUserId, FileOperationType.VersionRestore, restore.Entry.Id,
            restoreId.ToString("D"), "temp", restore.Entry.RelativePath,
            restoreTarget.Size, restoreTarget.Sha256, Now, restore.DeviceId, "request-restore", "1"));
        var restoreResult = await restore.Service.RestoreAsync(
            RestoreCommand(restore, 1, 2, restoreId), default);

        Assert.Equal(TextFileErrorCodes.RecoveryRequired, saveResult.Failure!.Code);
        Assert.Equal(TextFileErrorCodes.RecoveryRequired, restoreResult.Failure!.Code);
    }

    private static Fixture VersionFixtureWithContent(byte[] bytes)
    {
        var fixture = new Fixture(bytes, EffectivePermissionLevel.Viewer);
        var sha = Sha256(bytes);
        var record = new FileVersionRecord(
            Guid.NewGuid(), fixture.Entry.Id, 1, bytes.LongLength, sha,
            $"versions/{fixture.Entry.OwnerUserId:N}/{fixture.Entry.Id:N}/1/{sha}.bin",
            FileVersionChangeKind.Upload, null, null, Now);
        fixture.Versions.Add(record);
        fixture.Store.SeedVersion(record, bytes);
        return fixture;
    }

    private static async Task<Fixture> PreparedRestoreFixtureAsync()
    {
        var fixture = new Fixture(Encoding.UTF8.GetBytes("one"), EffectivePermissionLevel.Editor);
        Assert.True((await fixture.Service.SaveAsync(Command(fixture, "two", 1), default)).IsSuccess);
        return fixture;
    }

    private static SaveTextFileCommand Command(
        Fixture fixture,
        string content,
        long expectedVersion,
        Guid? operationId = null) =>
        new(
            fixture.ActorId,
            fixture.DeviceId,
            fixture.Entry.Id,
            content,
            expectedVersion,
            operationId ?? Guid.NewGuid(),
            "request-save");

    private static RestoreTextVersionCommand RestoreCommand(
        Fixture fixture,
        long version,
        long expectedVersion,
        Guid? operationId = null) =>
        new(
            fixture.ActorId,
            fixture.DeviceId,
            fixture.Entry.Id,
            version,
            expectedVersion,
            operationId ?? Guid.NewGuid(),
            "request-restore");

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed class Fixture
    {
        public Fixture(
            byte[] content,
            EffectivePermissionLevel permission,
            string mimeType = "text/plain",
            long? catalogSize = null)
        {
            ActorId = Guid.NewGuid();
            DeviceId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            Entry = FileEntry.CreateFile(
                Guid.NewGuid(), ownerId, Guid.NewGuid(), FileName.Create("note.txt"),
                RelativeStoragePath.Create($"users/{ownerId:N}/files/note.txt"),
                mimeType, catalogSize ?? content.LongLength, Now);
            Files = new FileRepository(Entry);
            Versions = new VersionRepository();
            Store = new ContentStore(content);
            Authorization = new Authorization(permission);
            Guard = new Guard();
            var versionService = new FileVersionService(
                Versions, Store, Store, Guard, new Clock(), Files);
            Service = new TextFileService(
                Files, Versions, Store, Store, Authorization, versionService, Guard, new Clock());
        }

        public Guid ActorId { get; }
        public Guid DeviceId { get; }
        public FileEntry Entry { get; }
        public FileRepository Files { get; }
        public VersionRepository Versions { get; }
        public ContentStore Store { get; }
        public Authorization Authorization { get; }
        public Guard Guard { get; }
        public TextFileService Service { get; }
    }

    private sealed class Authorization(EffectivePermissionLevel permission) : IAuthorizationService
    {
        public Guid? LastActorId { get; private set; }

        public Task<EffectivePermission> ResolveAsync(Guid actorUserId, Guid entryId, CancellationToken cancellationToken)
        {
            LastActorId = actorUserId;
            return Task.FromResult(new EffectivePermission(
                entryId,
                permission,
                permission == EffectivePermissionLevel.None ? null : PermissionSource.Owner,
                null,
                null));
        }

        public Task<IReadOnlyDictionary<Guid, EffectivePermission>> ResolveBatchAsync(
            Guid actorUserId, IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<bool> AllowsAsync(
            Guid actorUserId, Guid entryId, ShareOperation operation, CancellationToken cancellationToken) =>
            (await ResolveAsync(actorUserId, entryId, cancellationToken)).Allows(operation);
    }

    private sealed class VersionRepository : IFileVersionRepository
    {
        public List<FileVersionRecord> Records { get; } = [];
        public int ListCalls { get; private set; }

        public Task<FileVersionRecord?> FindAsync(Guid fileEntryId, long version, CancellationToken cancellationToken) =>
            Task.FromResult(Records.SingleOrDefault(record =>
                record.FileEntryId == fileEntryId && record.Version == version));

        public void Add(FileVersionRecord record) => Records.Add(record);

        public Task<IReadOnlyList<FileVersionHistoryRow>> ListAsync(
            Guid fileEntryId,
            long maximumVersion,
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            IReadOnlyList<FileVersionHistoryRow> result = Records
                .Where(record => record.FileEntryId == fileEntryId && record.Version <= maximumVersion)
                .OrderByDescending(record => record.Version)
                .Skip(skip)
                .Take(take)
                .Select(record => new FileVersionHistoryRow(
                    record.Version,
                    record.Size,
                    record.Sha256,
                    record.ChangeKind,
                    record.ActorUserId is null ? null : "Editor User",
                    record.CreatedAt))
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<int> CountAsync(
            Guid fileEntryId,
            long maximumVersion,
            CancellationToken cancellationToken) =>
            Task.FromResult(Records.Count(record =>
                record.FileEntryId == fileEntryId && record.Version <= maximumVersion));
    }

    private sealed class ContentStore(byte[] content) : IFileStore, IFileVersionStore
    {
        private readonly Dictionary<string, byte[]> temporary = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> versionContent = new(StringComparer.Ordinal);

        public byte[] CurrentContent { get; private set; } = content;

        public bool HasCapacity { get; set; } = true;

        public bool CorruptVersionReads { get; set; }

        public bool ThrowCurrentRead { get; set; }

        public bool ThrowVersionRead { get; set; }

        public bool SkipVersionVerification { get; set; }

        public bool ThrowPublishStorage { get; set; }

        public bool ThrowPublishConsistency { get; set; }

        public bool PublishInvalidEncoding { get; set; }

        public bool WriteUploadMismatch { get; set; }

        public bool ThrowReplace { get; set; }

        public int OpenReadCalls { get; private set; }

        public byte[] GetVersionContent(FileVersionRecord record) => versionContent[record.ContentRelativePath];

        public Task<Stream> OpenReadAsync(RelativeStoragePath path, CancellationToken cancellationToken)
        {
            OpenReadCalls++;
            if (ThrowCurrentRead)
            {
                throw new IOException();
            }

            return Task.FromResult<Stream>(new MemoryStream(CurrentContent, writable: false));
        }

        public void ChangeCurrentContent(byte[] value) => CurrentContent = value;

        public void SeedVersion(FileVersionRecord record, byte[] value) =>
            versionContent[record.ContentRelativePath] = value;

        public async Task<PublishedFileVersion?> TryPublishAsync(
            Guid ownerUserId,
            Guid fileEntryId,
            long version,
            Guid operationId,
            Stream source,
            long expectedSize,
            CancellationToken cancellationToken)
        {
            if (ThrowPublishStorage)
            {
                throw new FileVersionStorageUnavailableException();
            }

            if (ThrowPublishConsistency)
            {
                throw new FileVersionConsistencyException();
            }

            using var memory = new MemoryStream();
            await source.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            if (PublishInvalidEncoding)
            {
                return null;
            }

            try
            {
                _ = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }

            var sha = Sha256(bytes);
            var path = RelativeStoragePath.Create(
                $"versions/{ownerUserId:N}/{fileEntryId:N}/{version}/{sha}.bin");
            versionContent[path.Value] = bytes;
            return new PublishedFileVersion(
                RelativeStoragePath.Create($"version-temp/{ownerUserId:N}/{fileEntryId:N}/{version}/{operationId:N}.part"),
                path,
                bytes.LongLength,
                sha);
        }

        public Task<Stream> OpenReadAsync(
            RelativeStoragePath contentPath,
            long expectedSize,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            if (CorruptVersionReads)
            {
                throw new FileVersionConsistencyException();
            }

            if (ThrowVersionRead)
            {
                throw new IOException();
            }

            var bytes = versionContent[contentPath.Value];
            if (!SkipVersionVerification &&
                (bytes.LongLength != expectedSize || Sha256(bytes) != expectedSha256))
            {
                throw new FileVersionConsistencyException();
            }

            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        public Task<bool> HasCapacityAsync(long requiredBytes, CancellationToken cancellationToken) =>
            Task.FromResult(HasCapacity);
        public Task EnsureUserAreaAsync(Guid ownerUserId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateDirectoryAsync(RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task<StoredUpload> WriteUploadTempAsync(
            Guid ownerUserId,
            Guid operationId,
            Stream source,
            long expectedSize,
            CancellationToken cancellationToken)
        {
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            var path = RelativeStoragePath.Create($"upload-temp/{ownerUserId:N}/{operationId:N}.upload");
            temporary[path.Value] = bytes;
            return WriteUploadMismatch
                ? new StoredUpload(path, bytes.LongLength + 1, Sha256(bytes))
                : new StoredUpload(path, bytes.LongLength, Sha256(bytes));
        }
        public Task MoveAsync(RelativeStoragePath source, RelativeStoragePath target, bool sourceIsDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceAsync(
            RelativeStoragePath source,
            RelativeStoragePath target,
            CancellationToken cancellationToken)
        {
            if (ThrowReplace)
            {
                throw new IOException();
            }

            CurrentContent = temporary[source.Value];
            temporary.Remove(source.Value);
            return Task.CompletedTask;
        }
        public Task DeleteIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteTreeIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(RelativeStoragePath path, bool directory, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FileRepository(FileEntry entry) : IFileRepository
    {
        public bool HasIncomplete { get; set; }
        public int? ThrowPersistenceConflictOnSaveCall { get; set; }
        public bool LockAcquired { get; private set; }
        public int SaveCalls { get; private set; }
        public List<FileOperation> Operations { get; } = [];
        public List<KuraStorage.Domain.Audit.AuditLog> Audits { get; } = [];

        public Task<FileEntry?> FindByIdAsync(Guid entryId, CancellationToken cancellationToken) =>
            Task.FromResult(entry.Id == entryId ? entry : null);
        public Task<bool> ReloadAsync(FileEntry candidate, CancellationToken cancellationToken) =>
            Task.FromResult(candidate.Id == entry.Id);
        public Task<bool> HasIncompleteOperationAsync(
            Guid ownerUserId, Guid entryId, string relativePath, CancellationToken cancellationToken) =>
            Task.FromResult(HasIncomplete);
        public Task<IFileMutationLock> AcquireMutationLocksAsync(
            IEnumerable<Guid> entryIds, CancellationToken cancellationToken)
        {
            LockAcquired = true;
            return Task.FromResult<IFileMutationLock>(new MutationLock());
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCalls++;
            if (ThrowPersistenceConflictOnSaveCall == SaveCalls)
            {
                ThrowPersistenceConflictOnSaveCall = null;
                throw new FilePersistenceConflictException(new InvalidOperationException());
            }

            return Task.CompletedTask;
        }

        public Task<IFileTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IFileTransaction>(new Transaction());
        public Task<FileEntry?> FindOwnedAsync(Guid ownerUserId, Guid entryId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileEntry?> FindRootAsync(Guid ownerUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileEntry?> FindActiveChildAsync(Guid ownerUserId, Guid parentId, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileEntry?> FindActiveFolderByPathAsync(Guid ownerUserId, string relativePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsRelocationBlockedAsync(Guid ownerUserId, Guid entryId, string relativePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<FileEntry>> ListActiveChildrenAsync(Guid ownerUserId, Guid parentId, int skip, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountActiveChildrenAsync(Guid ownerUserId, Guid parentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<FileEntry>> ListTrashedAsync(Guid ownerUserId, int skip, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountTrashedAsync(Guid ownerUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<FileEntry>> ListDescendantsAsync(Guid ownerUserId, string relativePathPrefix, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileOperation?> FindOperationAsync(
            Guid ownerUserId,
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(Operations.SingleOrDefault(operation =>
                operation.OwnerUserId == ownerUserId && operation.IdempotencyKey == idempotencyKey));
        public Task<IReadOnlyList<FileOperation>> ListIncompleteOperationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Remove(FileEntry candidate) => throw new NotSupportedException();
        public void RemoveRange(IEnumerable<FileEntry> entries) => throw new NotSupportedException();
        public void Add(FileEntry candidate) => throw new NotSupportedException();
        public void Add(FileOperation operation) => Operations.Add(operation);
        public void Add(KuraStorage.Domain.Audit.AuditLog auditLog) => Audits.Add(auditLog);
    }

    private sealed class MutationLock : IFileMutationLock
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Transaction : IFileTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Guard : IStorageGuard
    {
        public StorageStatus Status { get; set; } = StorageStatus.Available;

        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(Status);
    }

    private sealed class Clock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}

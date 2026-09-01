using KuraStorage.Domain.Files;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class FileOperationVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecordPublishedVersion_PersistsRecoveryMetadataAndCompletesStage()
    {
        var operation = CreateOperation();
        var temporary = $"version-temp/{operation.OwnerUserId:N}/{operation.FileEntryId:N}/1/{operation.Id:N}.part";
        var final = $"versions/{operation.OwnerUserId:N}/{operation.FileEntryId:N}/1/{new string('a', 64)}.bin";

        operation.RecordPublishedVersion(null, 1, temporary, final, new string('a', 64), Now.AddMinutes(1));
        operation.RecordPublishedVersion(null, 1, temporary, final, new string('a', 64), Now.AddMinutes(2));

        Assert.Null(operation.PreviousFileVersion);
        Assert.Equal(1, operation.ResultFileVersion);
        Assert.Equal(temporary, operation.VersionTemporaryRelativePath);
        Assert.Equal(final, operation.VersionContentRelativePath);
        Assert.Equal(new string('a', 64), operation.VersionSha256);
        Assert.Equal(FileVersionPublishStage.Published, operation.VersionPublishStage);

        operation.Complete(Now.AddMinutes(3));

        Assert.Equal(FileVersionPublishStage.Completed, operation.VersionPublishStage);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void RecordPublishedVersion_InvalidVersionTransitionIsRejected(long previous, long result)
    {
        var operation = CreateOperation();

        Assert.Throws<ArgumentException>(() => operation.RecordPublishedVersion(
            previous,
            result,
            "version-temp/a.part",
            "versions/a.bin",
            new string('a', 64),
            Now));
    }

    [Fact]
    public void RecordPublishedVersion_RetryForAnotherArtifactIsRejected()
    {
        var operation = CreateOperation();
        operation.RecordPublishedVersion(
            null, 1, "version-temp/a.part", "versions/a.bin", new string('a', 64), Now);

        Assert.Throws<InvalidOperationException>(() => operation.RecordPublishedVersion(
            null, 1, "version-temp/a.part", "versions/b.bin", new string('b', 64), Now));
    }

    private static FileOperation CreateOperation()
    {
        var owner = Guid.NewGuid();
        return new FileOperation(
            Guid.NewGuid(),
            owner,
            FileOperationType.Upload,
            Guid.NewGuid(),
            Guid.NewGuid().ToString(),
            $"upload-temp/{owner:N}/source.upload",
            $"users/{owner:N}/files/note.txt",
            1,
            null,
            Now);
    }
}

namespace KuraStorage.IntegrationTests;

public sealed class OpenApiContractTests
{
    [Fact]
    public async Task MvpContract_DeclaresEveryImplementedFileEndpointAndSecurityBoundary()
    {
        var contractPath = Path.Combine(
            AppContext.BaseDirectory,
            "ContractFixtures",
            "kurastorage-api.yaml");
        var contract = await File.ReadAllTextAsync(contractPath);

        foreach (var path in new[]
        {
            "  /files:",
            "  /files/upload:",
            "  /upload-sessions:",
            "  /upload-sessions/{sessionId}:",
            "  /upload-sessions/{sessionId}/chunks:",
            "  /upload-sessions/{sessionId}/complete:",
            "  /folders:",
            "  /files/{fileId}:",
            "  /files/{fileId}/content:",
            "  /media-jobs/{jobId}:",
            "  /media-jobs/{jobId}/retry:",
            "  /trash:",
            "  /trash/{fileId}:",
            "  /files/{fileId}/restore:",
            "  /admin/storage:",
            "  /shares/candidates:",
            "  /shares:",
            "  /shares/{shareId}:",
            "  /shares/{shareId}/members/{userId}:",
            "  /search:",
            "  /recent-files:",
            "  /recent-files/{fileId}:",
            "  /favorites:",
            "  /favorites/{entryId}:",
            "  /tags:",
            "  /tags/{tagId}:",
            "  /files/{entryId}/organization:",
            "  /files/{entryId}/tags/{tagId}:",
        })
        {
            Assert.Contains(path, contract, StringComparison.Ordinal);
        }

        Assert.Contains("bearerAuth:", contract, StringComparison.Ordinal);
        Assert.Contains("    patch:", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: updateFile", contract, StringComparison.Ordinal);
        Assert.Contains("UpdateFileRequest:", contract, StringComparison.Ordinal);
        Assert.Contains("FILE_MOVE_CYCLE", contract, StringComparison.Ordinal);
        Assert.Contains("FILE_OPERATION_NOT_ALLOWED", contract, StringComparison.Ordinal);
        Assert.Contains("RECOVERY_REQUIRED", contract, StringComparison.Ordinal);
        Assert.Contains("RANGE_NOT_SATISFIABLE", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: getMediaJob", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: retryMediaJob", contract, StringComparison.Ordinal);
        Assert.Contains("MediaAcceptedResponse:", contract, StringComparison.Ordinal);
        Assert.Contains("MediaJob:", contract, StringComparison.Ordinal);
        Assert.Contains("        retryable:", contract, StringComparison.Ordinal);
        Assert.Contains(
            "enum: [original, thumbnail, image-low, image-medium, video-low, video-medium]",
            contract,
            StringComparison.Ordinal);
        Assert.Contains("MEDIA_VARIANT_UNSUPPORTED", contract, StringComparison.Ordinal);
        Assert.Contains("IDEMPOTENCY_CONFLICT", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: createUploadSession", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: uploadSessionChunk", contract, StringComparison.Ordinal);
        Assert.Contains("Upload-Offset", contract, StringComparison.Ordinal);
        Assert.Contains("X-Chunk-Sha256", contract, StringComparison.Ordinal);
        Assert.Contains("CHUNK_CHECKSUM_MISMATCH", contract, StringComparison.Ordinal);
        Assert.Contains("UPLOAD_LIMIT_REACHED", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: permanentlyDeleteTrashEntry", contract, StringComparison.Ordinal);
        Assert.Contains("purgeEligibleAt:", contract, StringComparison.Ordinal);
        Assert.Contains("AdminStorageStatus:", contract, StringComparison.Ordinal);
        Assert.Contains("TrashPurgeRunSummary:", contract, StringComparison.Ordinal);
        Assert.Contains("ShareCandidate:", contract, StringComparison.Ordinal);
        Assert.Contains("ShareMemberItem:", contract, StringComparison.Ordinal);
        Assert.Contains("ShareItem:", contract, StringComparison.Ordinal);
        Assert.Contains("SharePage:", contract, StringComparison.Ordinal);
        Assert.Contains("INVALID_SHARE_PERMISSION", contract, StringComparison.Ordinal);
        Assert.Contains("SHARE_NOT_FOUND", contract, StringComparison.Ordinal);
        Assert.Contains("SHARE_MEMBER_NOT_FOUND", contract, StringComparison.Ordinal);
        Assert.Contains("SHARE_CONFLICT", contract, StringComparison.Ordinal);
        Assert.Contains("SHARE_OPERATION_NOT_ALLOWED", contract, StringComparison.Ordinal);
        Assert.Contains("permissionSource:", contract, StringComparison.Ordinal);
        Assert.Contains("shareTargetId:", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: searchFiles", contract, StringComparison.Ordinal);
        Assert.Contains("SearchResultItem:", contract, StringComparison.Ordinal);
        Assert.Contains("SearchPage:", contract, StringComparison.Ordinal);
        Assert.Contains("enum: [IMAGE, VIDEO, AUDIO, DOCUMENT, ARCHIVE, OTHER]", contract, StringComparison.Ordinal);
        Assert.Contains("enum: [ACTIVE, MISSING_CANDIDATE, MISSING]", contract, StringComparison.Ordinal);
        Assert.Contains("INVALID_SEARCH_QUERY", contract, StringComparison.Ordinal);
        Assert.Contains("INVALID_SEARCH_FILTER", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: listRecentFiles", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: recordRecentFile", contract, StringComparison.Ordinal);
        Assert.Contains("RecentFileItem:", contract, StringComparison.Ordinal);
        Assert.Contains("RecentFilePage:", contract, StringComparison.Ordinal);
        Assert.Contains("INVALID_RECENT_FILES_REQUEST", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: listFavorites", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: createTag", contract, StringComparison.Ordinal);
        Assert.Contains("FavoriteItem:", contract, StringComparison.Ordinal);
        Assert.Contains("FavoritePage:", contract, StringComparison.Ordinal);
        Assert.Contains("TagItem:", contract, StringComparison.Ordinal);
        Assert.Contains("EntryOrganizationState:", contract, StringComparison.Ordinal);
        Assert.Contains("style: form", contract, StringComparison.Ordinal);
        Assert.Contains("uniqueItems: true", contract, StringComparison.Ordinal);
        Assert.Contains("TAG_NAME_CONFLICT", contract, StringComparison.Ordinal);
        Assert.Contains(
            "required: [deviceId, accessToken, refreshToken, accessTokenExpiresAt, refreshTokenExpiresAt, role]",
            contract,
            StringComparison.Ordinal);
        Assert.Contains("enum: [ADMIN, MEMBER]", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("ownerUserId:", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("relativePath:", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("temporaryRelativePath:", contract, StringComparison.Ordinal);
    }
}

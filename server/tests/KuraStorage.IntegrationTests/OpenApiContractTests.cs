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
            "  /folders:",
            "  /files/{fileId}:",
            "  /files/{fileId}/content:",
            "  /trash:",
            "  /trash/{fileId}:",
            "  /files/{fileId}/restore:",
            "  /admin/storage:",
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
        Assert.Contains("IDEMPOTENCY_CONFLICT", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: permanentlyDeleteTrashEntry", contract, StringComparison.Ordinal);
        Assert.Contains("purgeEligibleAt:", contract, StringComparison.Ordinal);
        Assert.Contains("AdminStorageStatus:", contract, StringComparison.Ordinal);
        Assert.Contains("TrashPurgeRunSummary:", contract, StringComparison.Ordinal);
        Assert.Contains(
            "required: [deviceId, accessToken, refreshToken, accessTokenExpiresAt, refreshTokenExpiresAt, role]",
            contract,
            StringComparison.Ordinal);
        Assert.Contains("enum: [ADMIN, MEMBER]", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("ownerUserId:", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("relativePath:", contract, StringComparison.Ordinal);
    }
}

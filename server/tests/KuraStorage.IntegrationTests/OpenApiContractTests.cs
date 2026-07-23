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
            "  /files/{fileId}/restore:",
        })
        {
            Assert.Contains(path, contract, StringComparison.Ordinal);
        }

        Assert.Contains("bearerAuth:", contract, StringComparison.Ordinal);
        Assert.Contains("RANGE_NOT_SATISFIABLE", contract, StringComparison.Ordinal);
        Assert.Contains("IDEMPOTENCY_CONFLICT", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("ownerUserId:", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("relativePath:", contract, StringComparison.Ordinal);
    }
}

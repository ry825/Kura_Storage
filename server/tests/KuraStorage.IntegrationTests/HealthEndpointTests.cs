using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace KuraStorage.IntegrationTests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task GetHealth_WhenStorageIsUnavailable_ReturnsOnlyPublicStatusFields()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/system/health", CancellationToken.None);
        var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(CancellationToken.None),
            cancellationToken: CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("AVAILABLE", json.RootElement.GetProperty("api").GetString());
        Assert.Equal("UNAVAILABLE", json.RootElement.GetProperty("storage").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(3, json.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task Refresh_WhenTokenIsNull_ReturnsValidationErrorWithoutAccessingDatabase()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { deviceId = Guid.NewGuid(), refreshToken = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            $"kurastorage-api-test-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(directory);
            var keyPath = Path.Combine(directory, "jwt-signing-key.pem");
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                File.WriteAllText(keyPath, key.ExportECPrivateKeyPem());
            }

            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Database:ConnectionString"] = "Host=localhost;Database=kurastorage_test;Username=kurastorage",
                            ["Storage:RootPath"] = Path.Combine(directory, "not-mounted"),
                            ["Storage:StorageId"] = "test-storage",
                            ["Storage:MinimumFreeBytes"] = "1",
                            ["Authentication:JwtIssuer"] = "kurastorage-test",
                            ["Authentication:JwtAudience"] = "kurastorage-test-client",
                            ["Authentication:JwtSigningKeyFile"] = keyPath,
                        });
                });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

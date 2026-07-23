using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;
using KuraStorage.Application.Identity;
using KuraStorage.Domain.Identity;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class PostgreSqlAuthFlowTests(PostgreSqlAuthFlowFixture fixture)
    : IClassFixture<PostgreSqlAuthFlowFixture>
{
    [Fact]
    public async Task InitialMigration_WhenApplied_CreatesIdentitySchemaWithoutPendingMigrations()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();

        var pending = await database.Database.GetPendingMigrationsAsync(CancellationToken.None);
        var applied = await database.Database.GetAppliedMigrationsAsync(CancellationToken.None);

        Assert.Empty(pending);
        Assert.Contains(applied, migration => migration.EndsWith("_InitialIdentity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthenticationFlow_WhenRunAgainstPostgreSql_EnforcesRouteRotationLogoutAndRevocation()
    {
        var userId = await fixture.CreateUserAsync();
        using var client = fixture.Factory.CreateClient();

        using var rejectedRegistration = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/register-device",
            new { username = "alice", password = fixture.Password, deviceName = "remote-phone" },
            route: "REMOTE_SECURE");
        Assert.Equal(HttpStatusCode.Forbidden, rejectedRegistration.StatusCode);
        await AssertErrorAsync(
            rejectedRegistration,
            "DEVICE_REGISTRATION_REQUIRES_LOCAL_DIRECT");

        using var registration = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/register-device",
            new { username = "alice", password = fixture.Password, deviceName = "local-phone" },
            route: "LOCAL_DIRECT");
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var registeredTokens = await ReadTokensAsync(registration);

        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { username = "alice", password = fixture.Password, deviceId = registeredTokens.DeviceId });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginTokens = await ReadTokensAsync(login);

        var refreshRequest = new
        {
            deviceId = loginTokens.DeviceId,
            refreshToken = loginTokens.RefreshToken,
        };
        var refreshes = await Task.WhenAll(
            client.PostAsJsonAsync("/api/v1/auth/refresh", refreshRequest),
            client.PostAsJsonAsync("/api/v1/auth/refresh", refreshRequest));
        Assert.Contains(refreshes, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Contains(refreshes, response => response.StatusCode == HttpStatusCode.Unauthorized);
        foreach (var response in refreshes)
        {
            response.Dispose();
        }

        using var secondLogin = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { username = "alice", password = fixture.Password, deviceId = registeredTokens.DeviceId });
        var secondLoginTokens = await ReadTokensAsync(secondLogin);
        using var staleAccessLogoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
        {
            Content = JsonContent.Create(
                new
                {
                    deviceId = secondLoginTokens.DeviceId,
                    refreshToken = secondLoginTokens.RefreshToken,
                }),
        };
        staleAccessLogoutRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginTokens.AccessToken);
        using var staleAccessLogout = await client.SendAsync(staleAccessLogoutRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, staleAccessLogout.StatusCode);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
        {
            Content = JsonContent.Create(
                new
                {
                    deviceId = secondLoginTokens.DeviceId,
                    refreshToken = secondLoginTokens.RefreshToken,
                }),
        };
        logoutRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secondLoginTokens.AccessToken);
        using var logout = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var refreshAfterLogout = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new
            {
                deviceId = secondLoginTokens.DeviceId,
                refreshToken = secondLoginTokens.RefreshToken,
            });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
        await AssertErrorAsync(refreshAfterLogout, "AUTHENTICATION_REQUIRED");

        using var thirdLogin = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { username = "alice", password = fixture.Password, deviceId = registeredTokens.DeviceId });
        var thirdLoginTokens = await ReadTokensAsync(thirdLogin);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityService>();
            Assert.True(
                await identity.RevokeDeviceAsync(
                    userId,
                    thirdLoginTokens.DeviceId,
                    "integration-test",
                    CancellationToken.None));
        }

        using var refreshAfterRevocation = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new
            {
                deviceId = thirdLoginTokens.DeviceId,
                refreshToken = thirdLoginTokens.RefreshToken,
            });
        Assert.Equal(HttpStatusCode.Forbidden, refreshAfterRevocation.StatusCode);

        var forbiddenLogValues = new[]
        {
            fixture.Password,
            registeredTokens.AccessToken,
            registeredTokens.RefreshToken,
            loginTokens.AccessToken,
            loginTokens.RefreshToken,
            fixture.SigningKeyPem,
        };
        Assert.DoesNotContain(
            fixture.LogMessages,
            message => forbiddenLogValues.Any(
                value => message.Contains(value, StringComparison.Ordinal)));
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object body,
        string route)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-KuraStorage-Route", route);
        return await client.SendAsync(request);
    }

    private static async Task<TestTokenPair> ReadTokensAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;
        return new TestTokenPair(
            root.GetProperty("deviceId").GetGuid(),
            root.GetProperty("accessToken").GetString()!,
            root.GetProperty("refreshToken").GetString()!);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string expectedCode)
    {
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("details").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("requestId").GetString()));
    }

    private sealed record TestTokenPair(Guid DeviceId, string AccessToken, string RefreshToken);
}

public sealed class PostgreSqlAuthFlowFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("kurastorage_test")
        .WithUsername("kurastorage")
        .WithPassword("integration-only-password")
        .Build();
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"kurastorage-postgres-test-{Guid.NewGuid():N}");

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public string SigningKeyPem { get; private set; } = string.Empty;

    public string Password { get; } = $"S3cr3t-Not-In-Logs-{Guid.NewGuid():N}";

    public IReadOnlyCollection<string> LogMessages => logger.Messages;

    private readonly CollectingLoggerProvider logger = new();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        Directory.CreateDirectory(directory);
        var keyPath = Path.Combine(directory, "jwt-signing-key.pem");
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            SigningKeyPem = key.ExportECPrivateKeyPem();
            await File.WriteAllTextAsync(keyPath, SigningKeyPem);
        }

        Factory = new ConfiguredApiFactory(postgres.GetConnectionString(), directory, keyPath, logger);
        _ = Factory.CreateClient();
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await postgres.DisposeAsync();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public async Task<Guid> CreateUserAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IdentityService>().CreateUserAsync(
            "alice",
            "Alice",
            Password,
            UserRole.Member,
            CancellationToken.None);
        if (!result.IsSuccess && result.Failure?.Code == IdentityErrorCodes.UsernameAlreadyExists)
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            return await database.Users
                .Where(user => user.UsernameNormalized == "ALICE")
                .Select(user => user.Id)
                .SingleAsync();
        }

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class ConfiguredApiFactory(
        string connectionString,
        string directory,
        string keyPath,
        ILoggerProvider logger) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logger));
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Database:ConnectionString"] = connectionString,
                            ["Storage:RootPath"] = Path.Combine(directory, "not-mounted"),
                            ["Storage:StorageId"] = "test-storage",
                            ["Storage:MinimumFreeBytes"] = "1",
                            ["Authentication:JwtIssuer"] = "kurastorage-test",
                            ["Authentication:JwtAudience"] = "kurastorage-test-client",
                            ["Authentication:JwtSigningKeyFile"] = keyPath,
                        });
                });
        }
    }

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CollectingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CollectingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
                if (exception is not null)
                {
                    messages.Enqueue(exception.ToString());
                }
            }
        }
    }
}

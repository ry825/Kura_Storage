using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace KuraStorage.IntegrationTests;

public sealed class Argon2PasswordHasherTests
{
    [Fact]
    public void Hash_WhenSamePasswordIsHashedTwice_UsesIndependentSixteenByteSalts()
    {
        var hasher = CreateHasher();

        var first = hasher.Hash("correct horse battery staple");
        var second = hasher.Hash("correct horse battery staple");

        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify("correct horse battery staple", first).IsValid);
        Assert.True(hasher.Verify("correct horse battery staple", second).IsValid);
        Assert.False(hasher.Verify("incorrect", first).IsValid);
        Assert.Equal(16, Convert.FromBase64String(first.Split('$')[4]).Length);
        Assert.Contains("$argon2id$v=19$m=19456,t=2,p=1$", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_WhenStoredParametersAreWeaker_RequestsRehashAfterSuccessfulVerification()
    {
        var weakHasher = CreateHasher(memoryKiB: 1024, iterations: 1);
        var currentHasher = CreateHasher();
        var encoded = weakHasher.Hash("password");

        var result = currentHasher.Verify("password", encoded);

        Assert.True(result.IsValid);
        Assert.True(result.NeedsRehash);
    }

    private static Argon2PasswordHasher CreateHasher(int memoryKiB = 19_456, int iterations = 2) =>
        new(
            Options.Create(
                new AuthenticationOptions
                {
                    JwtIssuer = "test",
                    JwtAudience = "test",
                    JwtSigningKeyFile = "unused",
                    Argon2MemoryKiB = memoryKiB,
                    Argon2Iterations = iterations,
                    Argon2Parallelism = 1,
                }));
}

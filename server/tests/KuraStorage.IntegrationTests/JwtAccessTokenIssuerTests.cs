using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using KuraStorage.Domain.Identity;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace KuraStorage.IntegrationTests;

public sealed class JwtAccessTokenIssuerTests
{
    [Theory]
    [InlineData(UserRole.Admin, "ADMIN")]
    [InlineData(UserRole.Member, "MEMBER")]
    public void Issue_IncludesDatabaseRoleClaim(UserRole role, string expected)
    {
        var keyPath = Path.Combine(Path.GetTempPath(), $"kurastorage-jwt-{Guid.NewGuid():N}.pem");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(keyPath, key.ExportECPrivateKeyPem());
        try
        {
            using var issuer = new JwtAccessTokenIssuer(
                Options.Create(
                    new AuthenticationOptions
                    {
                        JwtIssuer = "issuer",
                        JwtAudience = "audience",
                        JwtSigningKeyFile = keyPath,
                        AccessTokenMinutes = 15,
                    }));

            var token = issuer.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), role, DateTimeOffset.UtcNow);
            var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);

            Assert.Equal(expected, parsed.Claims.Single(claim => claim.Type == "role").Value);
        }
        finally
        {
            File.Delete(keyPath);
        }
    }
}

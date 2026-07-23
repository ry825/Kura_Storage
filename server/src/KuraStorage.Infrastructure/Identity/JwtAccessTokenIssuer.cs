using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using KuraStorage.Application.Abstractions;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace KuraStorage.Infrastructure.Identity;

public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer, IDisposable
{
    private readonly AuthenticationOptions options;
    private readonly ECDsa signingKey;

    public JwtAccessTokenIssuer(IOptions<AuthenticationOptions> options)
    {
        this.options = options.Value;
        signingKey = ECDsa.Create();
        signingKey.ImportFromPem(File.ReadAllText(this.options.JwtSigningKeyFile));
    }

    public AccessToken Issue(Guid userId, Guid deviceId, Guid sessionFamilyId, DateTimeOffset now)
    {
        var expiresAt = now.AddMinutes(options.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("device_id", deviceId.ToString()),
                new Claim("session_family_id", sessionFamilyId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
            Issuer = options.JwtIssuer,
            Audience = options.JwtAudience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(signingKey), SecurityAlgorithms.EcdsaSha256),
        };
        var handler = new JwtSecurityTokenHandler();
        return new AccessToken(handler.WriteToken(handler.CreateToken(descriptor)), expiresAt);
    }

    public void Dispose() => signingKey.Dispose();
}

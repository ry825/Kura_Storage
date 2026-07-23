using System.Security.Cryptography;
using KuraStorage.Application.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace KuraStorage.Infrastructure.Identity;

public sealed class RefreshTokenService : IRefreshTokenService
{
    public string Generate() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public byte[] Hash(string token) => SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(token));
}

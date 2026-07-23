using System.ComponentModel.DataAnnotations;

namespace KuraStorage.Infrastructure.Configuration;

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    [Required]
    public string JwtIssuer { get; init; } = string.Empty;

    [Required]
    public string JwtAudience { get; init; } = string.Empty;

    [Required]
    public string JwtSigningKeyFile { get; init; } = string.Empty;

    [Range(1, 60)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 168)]
    public int RefreshTokenHours { get; init; } = 24;

    [Range(19_456, int.MaxValue)]
    public int Argon2MemoryKiB { get; init; } = 19_456;

    [Range(2, int.MaxValue)]
    public int Argon2Iterations { get; init; } = 2;

    [Range(1, int.MaxValue)]
    public int Argon2Parallelism { get; init; } = 1;
}

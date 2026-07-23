using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using KuraStorage.Application.Abstractions;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KuraStorage.Infrastructure.Identity;

public sealed class Argon2PasswordHasher(IOptions<AuthenticationOptions> options) : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private readonly AuthenticationOptions options = options.Value;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, options.Argon2MemoryKiB, options.Argon2Iterations, options.Argon2Parallelism);
        return $"$argon2id$v=19$m={options.Argon2MemoryKiB},t={options.Argon2Iterations},p={options.Argon2Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public PasswordVerification Verify(string password, string encodedHash)
    {
        if (!TryParse(encodedHash, out var parameters))
        {
            var dummySalt = new byte[SaltSize];
            _ = Derive(password, dummySalt, options.Argon2MemoryKiB, options.Argon2Iterations, options.Argon2Parallelism);
            return new PasswordVerification(false, false);
        }

        var derived = Derive(password, parameters.Salt, parameters.MemoryKiB, parameters.Iterations, parameters.Parallelism);
        var valid = CryptographicOperations.FixedTimeEquals(derived, parameters.Hash);
        var needsRehash =
            parameters.MemoryKiB < options.Argon2MemoryKiB ||
            parameters.Iterations < options.Argon2Iterations ||
            parameters.Parallelism < options.Argon2Parallelism;
        return new PasswordVerification(valid, valid && needsRehash);
    }

    private static byte[] Derive(string password, byte[] salt, int memoryKiB, int iterations, int parallelism)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon2.GetBytes(HashSize);
    }

    private static bool TryParse(string encoded, out Parameters parameters)
    {
        parameters = default;
        var parts = encoded.Split('$', StringSplitOptions.None);
        if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19")
        {
            return false;
        }

        var values = parts[3].Split(',');
        if (values.Length != 3 ||
            !int.TryParse(values[0].AsSpan(2), out var memory) ||
            !int.TryParse(values[1].AsSpan(2), out var iterations) ||
            !int.TryParse(values[2].AsSpan(2), out var parallelism))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[4]);
            var hash = Convert.FromBase64String(parts[5]);
            if (salt.Length != SaltSize || hash.Length != HashSize)
            {
                return false;
            }

            parameters = new Parameters(memory, iterations, parallelism, salt, hash);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private readonly record struct Parameters(int MemoryKiB, int Iterations, int Parallelism, byte[] Salt, byte[] Hash);
}

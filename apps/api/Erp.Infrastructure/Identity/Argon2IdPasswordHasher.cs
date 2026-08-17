using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace Erp.Infrastructure.Identity;

public sealed class Argon2IdPasswordHasher : IPasswordHasher<ApplicationUser>
{
    private const int MemorySizeKb = 65_536;
    private const int Iterations = 3;
    private const int Parallelism = 2;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string HashPassword(ApplicationUser user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, MemorySizeKb, Iterations, Parallelism, HashSize);
        return $"argon2id$v=19$m={MemorySizeKb},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(hashedPassword);
        ArgumentNullException.ThrowIfNull(providedPassword);

        try
        {
            var parts = hashedPassword.Split('$', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5 || parts[0] != "argon2id" || parts[1] != "v=19")
            {
                return PasswordVerificationResult.Failed;
            }

            var parameters = parts[2]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Split('=', 2))
                .ToDictionary(value => value[0], value => int.Parse(value[1], System.Globalization.CultureInfo.InvariantCulture));

            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = Derive(providedPassword, salt, parameters["m"], parameters["t"], parameters["p"], expected.Length);

            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                return PasswordVerificationResult.Failed;
            }

            var needsRehash = parameters["m"] < MemorySizeKb
                || parameters["t"] < Iterations
                || parameters["p"] < Parallelism
                || expected.Length < HashSize;

            return needsRehash
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Success;
        }
        catch (Exception exception) when (exception is FormatException or KeyNotFoundException or ArgumentException)
        {
            return PasswordVerificationResult.Failed;
        }
    }

    private static byte[] Derive(string password, byte[] salt, int memorySize, int iterations, int parallelism, int length)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memorySize,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon2.GetBytes(length);
    }
}


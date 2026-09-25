using System.Globalization;
using System.Security.Cryptography;

namespace PMate.Auth;

/// <summary>
/// Hashes passwords with PBKDF2-HMAC-SHA256 and a random salt (built into .NET, no extra package).
/// Format: "pbkdf2-sha256$iterations$salt$hash". The iteration count is stored, so it can be raised later
/// without breaking existing passwords.
/// </summary>
public static class PasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int SaltSize = 16, HashSize = 32;
    /// <summary>OWASP recommendation for PBKDF2-HMAC-SHA256.</summary>
    public const int DefaultIterations = 600_000;

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return string.Join('$', Prefix, iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}

using System.Security.Cryptography;
using SharedCore.Models;

namespace SharedCore.Services;

public sealed class TeacherPasswordHasher
{
    public const int Iterations = 210_000;
    private const int SaltBytes = 32;
    private const int HashBytes = 32;

    public (string PasswordHash, string PasswordSalt) HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password must not be empty.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public bool VerifyPassword(string password, TeacherAccount account)
    {
        if (string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(account.PasswordHash) ||
            string.IsNullOrWhiteSpace(account.PasswordSalt))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(account.PasswordSalt);
            var expectedHash = Convert.FromBase64String(account.PasswordHash);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using SharedCore.Models;

namespace SharedCore.Services;

public sealed class TeacherPasswordHasher
{
    private readonly PasswordHasher<TeacherAccount> _passwordHasher = new();

    private const int LegacyIterations = 210_000;

    public (string PasswordHash, string PasswordSalt) HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password must not be empty.", nameof(password));
        }

        var account = new TeacherAccount();
        return (_passwordHasher.HashPassword(account, password), string.Empty);
    }

    public TeacherPasswordVerificationResult VerifyPassword(string password, TeacherAccount account)
    {
        if (string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(account.PasswordHash))
        {
            return TeacherPasswordVerificationResult.Failed;
        }

        var identityResult = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (identityResult is PasswordVerificationResult.Success)
        {
            return TeacherPasswordVerificationResult.Success;
        }

        if (identityResult is PasswordVerificationResult.SuccessRehashNeeded)
        {
            return TeacherPasswordVerificationResult.SuccessRehashNeeded;
        }

        if (string.IsNullOrWhiteSpace(account.PasswordSalt))
        {
            return TeacherPasswordVerificationResult.Failed;
        }

        try
        {
            var salt = Convert.FromBase64String(account.PasswordSalt);
            var expectedHash = Convert.FromBase64String(account.PasswordHash);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                LegacyIterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash)
                ? TeacherPasswordVerificationResult.SuccessRehashNeeded
                : TeacherPasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return TeacherPasswordVerificationResult.Failed;
        }
    }
}

public enum TeacherPasswordVerificationResult
{
    Failed,
    Success,
    SuccessRehashNeeded
}

using System.Security.Cryptography;
using System.Text;
using SharedCore.Models;

namespace SharedCore.Services;

internal static class PendingTemporaryPinProtector
{
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int KeyDerivationIterations = 100_000;
    private const string CurrentPrefix = "xplat-v1:";
    private const string LegacyAesPrefix = "v1:";
    private const string LegacyDpapiPrefix = "dpapi-v2:";

    public static string Protect(string pin, StudentAccount account)
    {
        var plaintext = Encoding.UTF8.GetBytes(pin);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(DeriveCurrentKey(account), TagBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var payload = new byte[NonceBytes + TagBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceBytes);
        Buffer.BlockCopy(tag, 0, payload, NonceBytes, TagBytes);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceBytes + TagBytes, ciphertext.Length);

        return CurrentPrefix + Convert.ToBase64String(payload);
    }

    public static bool TryUnprotect(string encryptedPin, StudentAccount account, out string pin)
    {
        pin = string.Empty;
        if (string.IsNullOrWhiteSpace(encryptedPin))
        {
            return false;
        }

        if (encryptedPin.StartsWith(CurrentPrefix, StringComparison.Ordinal))
        {
            return TryUnprotectAesGcm(
                encryptedPin[CurrentPrefix.Length..],
                DeriveCurrentKey(account),
                out pin);
        }

        if (encryptedPin.StartsWith(LegacyAesPrefix, StringComparison.Ordinal))
        {
            return TryUnprotectAesGcm(
                encryptedPin[LegacyAesPrefix.Length..],
                DeriveLegacyAesKey(),
                out pin);
        }

        if (encryptedPin.StartsWith(LegacyDpapiPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return false;
    }

    private static bool TryUnprotectAesGcm(string base64Payload, byte[] key, out string pin)
    {
        pin = string.Empty;
        try
        {
            var payload = Convert.FromBase64String(base64Payload);
            if (payload.Length <= NonceBytes + TagBytes)
            {
                return false;
            }

            var nonce = payload.AsSpan(0, NonceBytes).ToArray();
            var tag = payload.AsSpan(NonceBytes, TagBytes).ToArray();
            var ciphertext = payload.AsSpan(NonceBytes + TagBytes).ToArray();
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            pin = Encoding.UTF8.GetString(plaintext);
            return IsValidPin(pin);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            pin = string.Empty;
            return false;
        }
    }

    private static byte[] DeriveCurrentKey(StudentAccount account)
    {
        var password = Encoding.UTF8.GetBytes(string.Join(
            "|",
            "SchoolMathTrainer.PendingTemporaryPin.CrossPlatform.v1",
            account.StudentId,
            account.LoginCode,
            account.PinSalt,
            account.PinHash));
        var salt = Encoding.UTF8.GetBytes("Aplikace_skola_pocitani.SchoolMathTrainer.PendingTemporaryPin.xplat-v1");

        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            KeyDerivationIterations,
            HashAlgorithmName.SHA256,
            KeyBytes);
    }

    private static byte[] DeriveLegacyAesKey()
    {
        var password = Encoding.UTF8.GetBytes("SchoolMathTrainer.PendingTemporaryPin.v1");
        var salt = Encoding.UTF8.GetBytes("Aplikace_skola_pocitani.SchoolMathTrainer.LocalSharedData.v1");

        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            KeyDerivationIterations,
            HashAlgorithmName.SHA256,
            KeyBytes);
    }

    private static bool IsValidPin(string pin)
    {
        return pin.Length == 4 && pin.All(char.IsDigit);
    }
}

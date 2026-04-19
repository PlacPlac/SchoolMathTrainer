using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharedCore.Models;

namespace SharedCore.Services;

public sealed class TeacherTokenService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly TeacherAccountStore _accountStore;

    public TeacherTokenService(TeacherAccountStore accountStore)
    {
        _accountStore = accountStore;
    }

    public TeacherLoginResponse IssueToken(TeacherAccount account)
    {
        var settings = _accountStore.LoadOrCreateSettings();
        var issuedUtc = DateTime.UtcNow;
        var expiresUtc = issuedUtc.AddMinutes(Math.Clamp(settings.TokenLifetimeMinutes, 15, 24 * 60));
        var payload = new TeacherTokenPayload(
            account.Username,
            account.DisplayName,
            issuedUtc,
            expiresUtc,
            Guid.NewGuid().ToString("N"));
        var payloadSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions));
        var signatureSegment = Sign(payloadSegment, settings.TokenSigningKey);
        return new TeacherLoginResponse(
            true,
            "Přihlášení učitele proběhlo úspěšně.",
            $"{payloadSegment}.{signatureSegment}",
            expiresUtc,
            account.Username,
            account.DisplayName);
    }

    public TeacherTokenValidationResult ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new TeacherTokenValidationResult(false, Message: "Token is missing.");
        }

        var parts = token.Split('.', 2);
        if (parts.Length != 2)
        {
            return new TeacherTokenValidationResult(false, Message: "Token format is not valid.");
        }

        var settings = _accountStore.LoadOrCreateSettings();
        var expectedSignature = Sign(parts[0], settings.TokenSigningKey);
        if (!FixedTimeEquals(parts[1], expectedSignature))
        {
            return new TeacherTokenValidationResult(false, Message: "Token signature is not valid.");
        }

        TeacherTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TeacherTokenPayload>(
                Base64UrlDecode(parts[0]),
                SerializerOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return new TeacherTokenValidationResult(false, Message: "Token payload is not valid.");
        }

        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.Username) ||
            payload.ExpiresUtc <= DateTime.UtcNow)
        {
            return new TeacherTokenValidationResult(false, Message: "Token expired or incomplete.");
        }

        var account = _accountStore.FindTeacher(payload.Username);
        if (account is null || !account.IsActive)
        {
            return new TeacherTokenValidationResult(false, Message: "Teacher account is not active.");
        }

        return new TeacherTokenValidationResult(
            true,
            account.Username,
            account.DisplayName,
            payload.ExpiresUtc);
    }

    private static string Sign(string payloadSegment, string signingKey)
    {
        var key = Convert.FromBase64String(signingKey);
        using var hmac = new HMACSHA256(key);
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadSegment)));
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private sealed record TeacherTokenPayload(
        string Username,
        string DisplayName,
        DateTime IssuedUtc,
        DateTime ExpiresUtc,
        string Nonce);
}

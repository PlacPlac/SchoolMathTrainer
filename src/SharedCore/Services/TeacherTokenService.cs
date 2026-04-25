using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharedCore.Models;

namespace SharedCore.Services;

public sealed class TeacherTokenService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private readonly TeacherAccountStore _accountStore;

    public TeacherTokenService(TeacherAccountStore accountStore)
    {
        _accountStore = accountStore;
    }

    public TeacherLoginResponse IssueToken(TeacherAccount account)
    {
        var settings = _accountStore.LoadOrCreateSettings();
        var createdUtc = DateTime.UtcNow;
        var expiresUtc = createdUtc.AddMinutes(Math.Clamp(settings.TokenLifetimeMinutes, 15, 24 * 60));
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        lock (_sync)
        {
            var sessions = LoadSessionsUnsafe()
                .Where(session => session.ExpiresUtc > createdUtc)
                .ToList();
            sessions.Add(new TeacherSessionRecord
            {
                TokenHash = HashToken(token),
                Username = account.Username,
                CreatedUtc = createdUtc,
                ExpiresUtc = expiresUtc
            });
            SaveSessionsUnsafe(sessions);
        }

        return new TeacherLoginResponse(token, expiresUtc);
    }

    public TeacherTokenValidationResult ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new TeacherTokenValidationResult(false, Message: "Token is missing.");
        }

        var tokenHash = HashToken(token.Trim());
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            var sessions = LoadSessionsUnsafe();
            var activeSessions = sessions
                .Where(session => session.ExpiresUtc > now)
                .ToList();
            var session = activeSessions.FirstOrDefault(item =>
                FixedTimeEquals(item.TokenHash, tokenHash));

            if (activeSessions.Count != sessions.Count)
            {
                SaveSessionsUnsafe(activeSessions);
            }

            if (session is null || string.IsNullOrWhiteSpace(session.Username))
            {
                return new TeacherTokenValidationResult(false, Message: "Token expired or was not found.");
            }

            var account = _accountStore.FindTeacher(session.Username);
            if (account is null || !account.IsActive)
            {
                activeSessions.Remove(session);
                SaveSessionsUnsafe(activeSessions);
                return new TeacherTokenValidationResult(false, Message: "Teacher account is not active.");
            }

            return new TeacherTokenValidationResult(
                true,
                account.Username,
                TeacherRoles.Normalize(account.Role),
                session.ExpiresUtc);
        }
    }

    public bool RevokeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var tokenHash = HashToken(token.Trim());
        lock (_sync)
        {
            var sessions = LoadSessionsUnsafe();
            var remainingSessions = sessions
                .Where(session => !FixedTimeEquals(session.TokenHash, tokenHash))
                .ToList();
            if (remainingSessions.Count == sessions.Count)
            {
                return false;
            }

            SaveSessionsUnsafe(remainingSessions);
            return true;
        }
    }

    public int RevokeTokensForTeacher(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return 0;
        }

        var normalizedUsername = username.Trim().ToLowerInvariant();
        lock (_sync)
        {
            var sessions = LoadSessionsUnsafe();
            var remainingSessions = sessions
                .Where(session => !string.Equals(session.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var revokedCount = sessions.Count - remainingSessions.Count;
            if (revokedCount > 0)
            {
                SaveSessionsUnsafe(remainingSessions);
            }

            return revokedCount;
        }
    }

    private string SessionsFilePath => Path.Combine(_accountStore.DataRoot, "teacher-sessions.json");

    private List<TeacherSessionRecord> LoadSessionsUnsafe()
    {
        Directory.CreateDirectory(_accountStore.DataRoot);
        if (!File.Exists(SessionsFilePath))
        {
            return [];
        }

        var sessions = JsonSerializer.Deserialize<List<TeacherSessionRecord>>(
            File.ReadAllText(SessionsFilePath, Encoding.UTF8),
            SerializerOptions);
        return sessions ?? [];
    }

    private void SaveSessionsUnsafe(List<TeacherSessionRecord> sessions)
    {
        Directory.CreateDirectory(_accountStore.DataRoot);
        var tempPath = $"{SessionsFilePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(
            tempPath,
            JsonSerializer.Serialize(sessions.OrderBy(session => session.Username).ToList(), SerializerOptions),
            Encoding.UTF8);
        if (File.Exists(SessionsFilePath))
        {
            File.Replace(tempPath, SessionsFilePath, null);
        }
        else
        {
            File.Move(tempPath, SessionsFilePath);
        }
    }

    private static string HashToken(string token) =>
        Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

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
}

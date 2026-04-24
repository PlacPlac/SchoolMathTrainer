using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharedCore.Services;

public sealed class StudentSessionTokenService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private readonly string _securityDirectory;
    private readonly TimeSpan _tokenLifetime = TimeSpan.FromHours(8);

    public StudentSessionTokenService(string securityDirectory)
    {
        _securityDirectory = string.IsNullOrWhiteSpace(securityDirectory)
            ? throw new ArgumentException("Security directory must not be empty.", nameof(securityDirectory))
            : Path.GetFullPath(securityDirectory);
    }

    public StudentSessionTokenIssueResult IssueToken(string classId, string studentId)
    {
        var normalizedClassId = NormalizeRequired(classId, nameof(classId));
        var normalizedStudentId = NormalizeRequired(studentId, nameof(studentId));
        var createdUtc = DateTime.UtcNow;
        var expiresUtc = createdUtc.Add(_tokenLifetime);
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        lock (_sync)
        {
            var sessions = LoadSessionsUnsafe()
                .Where(session => session.ExpiresUtc > createdUtc)
                .Where(session =>
                    !string.Equals(session.ClassId, normalizedClassId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(session.StudentId, normalizedStudentId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            sessions.Add(new StudentSessionRecord
            {
                TokenHash = HashToken(token),
                ClassId = normalizedClassId,
                StudentId = normalizedStudentId,
                CreatedUtc = createdUtc,
                ExpiresUtc = expiresUtc
            });
            SaveSessionsUnsafe(sessions);
        }

        return new StudentSessionTokenIssueResult(token, expiresUtc);
    }

    public StudentSessionTokenValidationResult ValidateToken(string token, string classId, string studentId)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new StudentSessionTokenValidationResult(false);
        }

        var normalizedClassId = NormalizeRequired(classId, nameof(classId));
        var normalizedStudentId = NormalizeRequired(studentId, nameof(studentId));
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

            if (session is null)
            {
                return new StudentSessionTokenValidationResult(false);
            }

            if (!string.Equals(session.ClassId, normalizedClassId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(session.StudentId, normalizedStudentId, StringComparison.OrdinalIgnoreCase))
            {
                return new StudentSessionTokenValidationResult(false);
            }

            return new StudentSessionTokenValidationResult(true, session.ExpiresUtc);
        }
    }

    private string SessionsFilePath => Path.Combine(_securityDirectory, "student-sessions.json");

    private List<StudentSessionRecord> LoadSessionsUnsafe()
    {
        Directory.CreateDirectory(_securityDirectory);
        if (!File.Exists(SessionsFilePath))
        {
            return [];
        }

        var sessions = JsonSerializer.Deserialize<List<StudentSessionRecord>>(
            File.ReadAllText(SessionsFilePath, Encoding.UTF8),
            SerializerOptions);
        return sessions ?? [];
    }

    private void SaveSessionsUnsafe(List<StudentSessionRecord> sessions)
    {
        Directory.CreateDirectory(_securityDirectory);
        var tempPath = $"{SessionsFilePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(
            tempPath,
            JsonSerializer.Serialize(
                sessions
                    .OrderBy(session => session.ClassId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(session => session.StudentId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                SerializerOptions),
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

    private static string NormalizeRequired(string value, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return normalized;
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

    private sealed class StudentSessionRecord
    {
        public string TokenHash { get; init; } = string.Empty;
        public string ClassId { get; init; } = string.Empty;
        public string StudentId { get; init; } = string.Empty;
        public DateTime CreatedUtc { get; init; }
        public DateTime ExpiresUtc { get; init; }
    }
}

public sealed record StudentSessionTokenIssueResult(
    string Token,
    DateTime ExpiresUtc);

public sealed record StudentSessionTokenValidationResult(
    bool Success,
    DateTime? ExpiresUtc = null);

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharedCore.Services;

public sealed class StudentLoginLockoutStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private readonly string _securityDirectory;
    private readonly TimeSpan _window = TimeSpan.FromMinutes(10);
    private readonly TimeSpan _lockout = TimeSpan.FromMinutes(15);
    private readonly int _maxFailures = 5;

    public StudentLoginLockoutStore(string securityDirectory)
    {
        _securityDirectory = string.IsNullOrWhiteSpace(securityDirectory)
            ? throw new ArgumentException("Security directory must not be empty.", nameof(securityDirectory))
            : Path.GetFullPath(securityDirectory);
    }

    public bool IsLocked(string classId, string loginCode, string remoteAddress, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var now = DateTime.UtcNow;
        lock (_sync)
        {
            var records = LoadRecordsUnsafe();
            var changed = RemoveExpiredLocksUnsafe(records, now);
            var record = FindRecord(records, BuildRecordKey(classId, loginCode, remoteAddress));
            if (changed)
            {
                SaveRecordsUnsafe(records);
            }

            if (record?.LockedUntilUtc is not { } lockedUntil || lockedUntil <= now)
            {
                return false;
            }

            retryAfter = lockedUntil - now;
            return true;
        }
    }

    public StudentLoginFailureRegistration RegisterFailure(string classId, string loginCode, string remoteAddress)
    {
        var now = DateTime.UtcNow;
        lock (_sync)
        {
            var records = LoadRecordsUnsafe();
            RemoveExpiredLocksUnsafe(records, now);
            var key = BuildRecordKey(classId, loginCode, remoteAddress);
            var record = GetOrCreateRecord(records, key, now);
            var lockoutStarted = RegisterFailureUnsafe(record, now);
            SaveRecordsUnsafe(records);

            return new StudentLoginFailureRegistration(
                lockoutStarted,
                record.LockedUntilUtc,
                record.FailureCount);
        }
    }

    public void RegisterSuccess(string classId, string loginCode, string remoteAddress)
    {
        lock (_sync)
        {
            var records = LoadRecordsUnsafe();
            var removed = records.RemoveAll(record =>
                string.Equals(record.Key, BuildRecordKey(classId, loginCode, remoteAddress), StringComparison.Ordinal)) > 0;
            if (removed)
            {
                SaveRecordsUnsafe(records);
            }
        }
    }

    private string LockoutFilePath => Path.Combine(_securityDirectory, "student-login-lockouts.json");

    private bool RegisterFailureUnsafe(StudentLoginLockoutRecord record, DateTime now)
    {
        if (now - record.FirstFailureUtc > _window)
        {
            record.FirstFailureUtc = now;
            record.FailureCount = 0;
            record.LockedUntilUtc = null;
        }

        record.FailureCount++;
        record.LastFailureUtc = now;
        if (record.FailureCount < _maxFailures)
        {
            return false;
        }

        var lockedUntil = now.Add(_lockout);
        var wasAlreadyLocked = record.LockedUntilUtc.HasValue && record.LockedUntilUtc.Value > now;
        record.LockedUntilUtc = lockedUntil;
        return !wasAlreadyLocked;
    }

    private bool RemoveExpiredLocksUnsafe(List<StudentLoginLockoutRecord> records, DateTime now)
    {
        var removed = records.RemoveAll(record =>
            record.LockedUntilUtc.HasValue && record.LockedUntilUtc.Value <= now &&
            now - record.FirstFailureUtc > _window) > 0;
        var unlocked = false;
        foreach (var record in records.Where(record =>
                     record.LockedUntilUtc.HasValue && record.LockedUntilUtc.Value <= now))
        {
            record.LockedUntilUtc = null;
            unlocked = true;
        }

        return removed || unlocked;
    }

    private static StudentLoginLockoutRecord? FindRecord(
        List<StudentLoginLockoutRecord> records,
        string key) =>
        records.FirstOrDefault(record =>
            string.Equals(record.Key, key, StringComparison.Ordinal));

    private static StudentLoginLockoutRecord GetOrCreateRecord(
        List<StudentLoginLockoutRecord> records,
        string key,
        DateTime now)
    {
        var record = FindRecord(records, key);
        if (record is not null)
        {
            return record;
        }

        record = new StudentLoginLockoutRecord
        {
            Key = key,
            FirstFailureUtc = now,
            LastFailureUtc = now
        };
        records.Add(record);
        return record;
    }

    private List<StudentLoginLockoutRecord> LoadRecordsUnsafe()
    {
        Directory.CreateDirectory(_securityDirectory);
        if (!File.Exists(LockoutFilePath))
        {
            return [];
        }

        var records = JsonSerializer.Deserialize<List<StudentLoginLockoutRecord>>(
            File.ReadAllText(LockoutFilePath, Encoding.UTF8),
            SerializerOptions);
        return records ?? [];
    }

    private void SaveRecordsUnsafe(List<StudentLoginLockoutRecord> records)
    {
        Directory.CreateDirectory(_securityDirectory);
        var tempPath = $"{LockoutFilePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(
            tempPath,
            JsonSerializer.Serialize(records.OrderBy(record => record.Key).ToList(), SerializerOptions),
            Encoding.UTF8);
        if (File.Exists(LockoutFilePath))
        {
            File.Replace(tempPath, LockoutFilePath, null);
        }
        else
        {
            File.Move(tempPath, LockoutFilePath);
        }
    }

    private static string BuildRecordKey(string classId, string loginCode, string remoteAddress)
    {
        return string.Join(
            "|",
            NormalizeClassIdKey(classId),
            NormalizeLoginCodeKey(loginCode),
            NormalizeIpKey(remoteAddress));
    }

    private static string NormalizeClassIdKey(string classId)
    {
        var value = (classId ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length is > 0 and <= 128 &&
            value.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' or '@'))
        {
            return value;
        }

        return $"invalid-class:{HashKey(value)}";
    }

    private static string NormalizeLoginCodeKey(string loginCode)
    {
        var value = (loginCode ?? string.Empty).Trim().ToUpperInvariant();
        if (value.Length is > 0 and <= 128 &&
            value.All(char.IsLetterOrDigit))
        {
            return value;
        }

        return $"invalid-login:{HashKey(value)}";
    }

    private static string NormalizeIpKey(string remoteAddress)
    {
        var value = string.IsNullOrWhiteSpace(remoteAddress) ? "unknown" : remoteAddress.Trim();
        return value.Length <= 128 ? value : $"long:{HashKey(value)}";
    }

    private static string HashKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record StudentLoginFailureRegistration(
    bool LockoutStarted,
    DateTime? LockedUntilUtc,
    int FailureCount);

public sealed class StudentLoginLockoutRecord
{
    public string Key { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public DateTime FirstFailureUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastFailureUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LockedUntilUtc { get; set; }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharedCore.Services;

public sealed class StudentLoginLockoutStore
{
    private static readonly StudentLoginLockoutPolicy[] Policies =
    [
        new("student-ip", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), 5),
        new("student", TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30), 10),
        new("class-ip", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), 30)
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private readonly string _securityDirectory;
    private readonly Func<DateTime> _utcNowProvider;

    public StudentLoginLockoutStore(string securityDirectory, Func<DateTime>? utcNowProvider = null)
    {
        _securityDirectory = string.IsNullOrWhiteSpace(securityDirectory)
            ? throw new ArgumentException("Security directory must not be empty.", nameof(securityDirectory))
            : Path.GetFullPath(securityDirectory);
        _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
    }

    public bool IsLocked(string classId, string loginCode, string remoteAddress, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var now = _utcNowProvider();
        lock (_sync)
        {
            var records = LoadRecordsUnsafe();
            var changed = RemoveExpiredLocksUnsafe(records, now);
            var lockout = GetActiveLockout(records, BuildRecordKeys(classId, loginCode, remoteAddress), now);
            if (changed)
            {
                SaveRecordsUnsafe(records);
            }

            if (!lockout.HasValue)
            {
                return false;
            }

            retryAfter = lockout.Value - now;
            return true;
        }
    }

    public StudentLoginFailureRegistration RegisterFailure(string classId, string loginCode, string remoteAddress)
    {
        var now = _utcNowProvider();
        lock (_sync)
        {
            var records = LoadRecordsUnsafe();
            RemoveExpiredLocksUnsafe(records, now);
            var lockoutStarted = false;
            DateTime? longestLockout = null;
            var primaryFailureCount = 0;

            foreach (var key in BuildRecordKeys(classId, loginCode, remoteAddress))
            {
                var record = GetOrCreateRecord(records, key, now);
                var policy = GetPolicy(record.Scope);
                var started = RegisterFailureUnsafe(record, policy, now);
                lockoutStarted = lockoutStarted || started;
                primaryFailureCount = string.Equals(record.Scope, "student-ip", StringComparison.Ordinal)
                    ? record.FailureCount
                    : primaryFailureCount;

                if (record.LockedUntilUtc.HasValue &&
                    (!longestLockout.HasValue || record.LockedUntilUtc.Value > longestLockout.Value))
                {
                    longestLockout = record.LockedUntilUtc.Value;
                }
            }

            SaveRecordsUnsafe(records);

            return new StudentLoginFailureRegistration(
                lockoutStarted,
                longestLockout,
                primaryFailureCount);
        }
    }

    public void RegisterSuccess(string classId, string loginCode, string remoteAddress)
    {
        var now = _utcNowProvider();
        lock (_sync)
        {
            var records = LoadRecordsUnsafe();
            var keys = BuildRecordKeys(classId, loginCode, remoteAddress)
                .Where(key => !string.Equals(key.Scope, "class-ip", StringComparison.Ordinal))
                .Select(key => key.Key)
                .ToHashSet(StringComparer.Ordinal);
            var removed = records.RemoveAll(record =>
                keys.Contains(record.Key) &&
                (!record.LockedUntilUtc.HasValue || record.LockedUntilUtc.Value <= now)) > 0;
            if (removed)
            {
                SaveRecordsUnsafe(records);
            }
        }
    }

    private string LockoutFilePath => Path.Combine(_securityDirectory, "student-login-lockouts.json");

    private bool RegisterFailureUnsafe(StudentLoginLockoutRecord record, StudentLoginLockoutPolicy policy, DateTime now)
    {
        if (now - record.FirstFailureUtc > policy.Window)
        {
            record.FirstFailureUtc = now;
            record.FailureCount = 0;
            record.LockedUntilUtc = null;
        }

        record.FailureCount++;
        record.LastFailureUtc = now;
        if (record.FailureCount < policy.MaxFailures)
        {
            return false;
        }

        var lockedUntil = now.Add(policy.Lockout);
        var wasAlreadyLocked = record.LockedUntilUtc.HasValue && record.LockedUntilUtc.Value > now;
        record.LockedUntilUtc = lockedUntil;
        return !wasAlreadyLocked;
    }

    private bool RemoveExpiredLocksUnsafe(List<StudentLoginLockoutRecord> records, DateTime now)
    {
        var removed = records.RemoveAll(record =>
            record.LockedUntilUtc.HasValue && record.LockedUntilUtc.Value <= now &&
            now - record.FirstFailureUtc > GetPolicy(record.Scope).Window) > 0;
        var unlocked = false;
        foreach (var record in records.Where(record =>
                     record.LockedUntilUtc.HasValue && record.LockedUntilUtc.Value <= now))
        {
            record.LockedUntilUtc = null;
            unlocked = true;
        }

        return removed || unlocked;
    }

    private static DateTime? GetActiveLockout(
        List<StudentLoginLockoutRecord> records,
        IReadOnlyList<StudentLoginLockoutKey> keys,
        DateTime now)
    {
        DateTime? lockedUntil = null;
        var keySet = keys.Select(key => key.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var record in records.Where(record => keySet.Contains(record.Key)))
        {
            if (record.LockedUntilUtc.HasValue &&
                record.LockedUntilUtc.Value > now &&
                (!lockedUntil.HasValue || record.LockedUntilUtc.Value > lockedUntil.Value))
            {
                lockedUntil = record.LockedUntilUtc.Value;
            }
        }

        return lockedUntil;
    }

    private static StudentLoginLockoutRecord? FindRecord(
        List<StudentLoginLockoutRecord> records,
        string key) =>
        records.FirstOrDefault(record =>
            string.Equals(record.Key, key, StringComparison.Ordinal));

    private static StudentLoginLockoutRecord GetOrCreateRecord(
        List<StudentLoginLockoutRecord> records,
        StudentLoginLockoutKey key,
        DateTime now)
    {
        var record = FindRecord(records, key.Key);
        if (record is not null)
        {
            return record;
        }

        record = new StudentLoginLockoutRecord
        {
            Key = key.Key,
            Scope = key.Scope,
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
        return MigrateRecords(records ?? []);
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

    private static IReadOnlyList<StudentLoginLockoutKey> BuildRecordKeys(
        string classId,
        string loginCode,
        string remoteAddress)
    {
        var normalizedClassId = NormalizeClassIdKey(classId);
        var normalizedLoginCode = NormalizeLoginCodeKey(loginCode);
        var normalizedIp = NormalizeIpKey(remoteAddress);
        return
        [
            BuildScopedKey("student-ip", normalizedClassId, normalizedLoginCode, normalizedIp),
            BuildScopedKey("student", normalizedClassId, normalizedLoginCode),
            BuildScopedKey("class-ip", normalizedClassId, normalizedIp)
        ];
    }

    private static StudentLoginLockoutKey BuildScopedKey(string scope, params string[] parts) =>
        new(scope, $"{scope}:{HashKey(string.Join("|", parts))}");

    private static List<StudentLoginLockoutRecord> MigrateRecords(List<StudentLoginLockoutRecord> records)
    {
        foreach (var record in records)
        {
            if (!string.IsNullOrWhiteSpace(record.Scope))
            {
                continue;
            }

            record.Scope = "student-ip";
            record.Key = record.Key.StartsWith("student-ip:", StringComparison.Ordinal)
                ? record.Key
                : $"student-ip:{HashKey(record.Key)}";
        }

        return records;
    }

    private static StudentLoginLockoutPolicy GetPolicy(string? scope) =>
        Policies.FirstOrDefault(policy => string.Equals(policy.Scope, scope, StringComparison.Ordinal)) ??
        Policies[0];

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

public sealed record StudentLoginLockoutPolicy(
    string Scope,
    TimeSpan Window,
    TimeSpan Lockout,
    int MaxFailures);

public sealed record StudentLoginLockoutKey(
    string Scope,
    string Key);

public sealed record StudentLoginFailureRegistration(
    bool LockoutStarted,
    DateTime? LockedUntilUtc,
    int FailureCount);

public sealed class StudentLoginLockoutRecord
{
    public string Scope { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public DateTime FirstFailureUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastFailureUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LockedUntilUtc { get; set; }
}

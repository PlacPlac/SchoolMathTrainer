using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharedCore.Services;

public sealed class TeacherLoginLockoutStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private readonly TeacherAccountStore _accountStore;
    private readonly TimeSpan _window = TimeSpan.FromMinutes(15);
    private readonly TimeSpan _lockout = TimeSpan.FromMinutes(15);
    private readonly int _maxFailures = 5;

    public TeacherLoginLockoutStore(TeacherAccountStore accountStore)
    {
        _accountStore = accountStore;
    }

    public bool IsLocked(string username, string remoteAddress, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var now = DateTime.UtcNow;
        lock (_sync)
        {
            var records = LoadRecordsUnsafe();
            var changed = RemoveExpiredLocksUnsafe(records, now);
            var usernameRecord = FindRecord(records, "username", NormalizeUsernameKey(username));
            var ipRecord = FindRecord(records, "ip", NormalizeIpKey(remoteAddress));
            var lockedUntil = new[] { usernameRecord?.LockedUntilUtc, ipRecord?.LockedUntilUtc }
                .Where(value => value.HasValue && value.Value > now)
                .Select(value => value!.Value)
                .DefaultIfEmpty()
                .Max();

            if (changed)
            {
                SaveRecordsUnsafe(records);
            }

            if (lockedUntil == default)
            {
                return false;
            }

            retryAfter = lockedUntil - now;
            return true;
        }
    }

    public TeacherLoginFailureRegistration RegisterFailure(string username, string remoteAddress)
    {
        var now = DateTime.UtcNow;
        lock (_sync)
        {
            var records = LoadRecordsUnsafe();
            RemoveExpiredLocksUnsafe(records, now);
            var usernameRecord = GetOrCreateRecord(records, "username", NormalizeUsernameKey(username), now);
            var ipRecord = GetOrCreateRecord(records, "ip", NormalizeIpKey(remoteAddress), now);

            var usernameLocked = RegisterFailureUnsafe(usernameRecord, now);
            var ipLocked = RegisterFailureUnsafe(ipRecord, now);
            SaveRecordsUnsafe(records);

            var lockedUntil = new[] { usernameRecord.LockedUntilUtc, ipRecord.LockedUntilUtc }
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .DefaultIfEmpty()
                .Max();

            return new TeacherLoginFailureRegistration(
                usernameLocked || ipLocked,
                lockedUntil == default ? null : lockedUntil,
                usernameRecord.FailureCount,
                ipRecord.FailureCount);
        }
    }

    public void RegisterSuccess(string username, string remoteAddress)
    {
        lock (_sync)
        {
            var records = LoadRecordsUnsafe();
            var removed = records.RemoveAll(record =>
                (record.Kind == "username" && record.Key == NormalizeUsernameKey(username)) ||
                (record.Kind == "ip" && record.Key == NormalizeIpKey(remoteAddress))) > 0;
            if (removed)
            {
                SaveRecordsUnsafe(records);
            }
        }
    }

    private string LockoutFilePath => Path.Combine(_accountStore.SecurityDirectory, "teacher-login-lockouts.json");

    private bool RegisterFailureUnsafe(TeacherLoginLockoutRecord record, DateTime now)
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

    private bool RemoveExpiredLocksUnsafe(List<TeacherLoginLockoutRecord> records, DateTime now)
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

    private static TeacherLoginLockoutRecord? FindRecord(
        List<TeacherLoginLockoutRecord> records,
        string kind,
        string key) =>
        records.FirstOrDefault(record =>
            string.Equals(record.Kind, kind, StringComparison.Ordinal) &&
            string.Equals(record.Key, key, StringComparison.Ordinal));

    private static TeacherLoginLockoutRecord GetOrCreateRecord(
        List<TeacherLoginLockoutRecord> records,
        string kind,
        string key,
        DateTime now)
    {
        var record = FindRecord(records, kind, key);
        if (record is not null)
        {
            return record;
        }

        record = new TeacherLoginLockoutRecord
        {
            Kind = kind,
            Key = key,
            FirstFailureUtc = now,
            LastFailureUtc = now
        };
        records.Add(record);
        return record;
    }

    private List<TeacherLoginLockoutRecord> LoadRecordsUnsafe()
    {
        Directory.CreateDirectory(_accountStore.SecurityDirectory);
        if (!File.Exists(LockoutFilePath))
        {
            return [];
        }

        var records = JsonSerializer.Deserialize<List<TeacherLoginLockoutRecord>>(
            File.ReadAllText(LockoutFilePath, Encoding.UTF8),
            SerializerOptions);
        return records ?? [];
    }

    private void SaveRecordsUnsafe(List<TeacherLoginLockoutRecord> records)
    {
        Directory.CreateDirectory(_accountStore.SecurityDirectory);
        var tempPath = $"{LockoutFilePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(
            tempPath,
            JsonSerializer.Serialize(records.OrderBy(record => record.Kind).ThenBy(record => record.Key).ToList(), SerializerOptions),
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

    private static string NormalizeUsernameKey(string username)
    {
        var value = (username ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length is > 0 and <= 64 &&
            value.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' or '@'))
        {
            return value;
        }

        return $"invalid:{HashKey(value)}";
    }

    private static string NormalizeIpKey(string remoteAddress)
    {
        var value = string.IsNullOrWhiteSpace(remoteAddress) ? "unknown" : remoteAddress.Trim();
        return value.Length <= 128 ? value : $"long:{HashKey(value)}";
    }

    private static string HashKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record TeacherLoginFailureRegistration(
    bool LockoutStarted,
    DateTime? LockedUntilUtc,
    int UsernameFailureCount,
    int IpFailureCount);

public sealed class TeacherLoginLockoutRecord
{
    public string Kind { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public DateTime FirstFailureUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastFailureUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LockedUntilUtc { get; set; }
}

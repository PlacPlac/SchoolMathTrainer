using System.Text;
using System.Text.Json;
using SharedCore.Models;

namespace SharedCore.Services;

public sealed class TeacherAccountStore
{
    public const string DefaultDataRoot = "/var/lib/schoolmath/data";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private readonly TeacherPasswordHasher _passwordHasher;

    public TeacherAccountStore(string dataRoot, TeacherPasswordHasher? passwordHasher = null)
    {
        DataRoot = string.IsNullOrWhiteSpace(dataRoot)
            ? DefaultDataRoot
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(dataRoot.Trim().Trim('"')));
        _passwordHasher = passwordHasher ?? new TeacherPasswordHasher();
    }

    public string DataRoot { get; }
    public string SecurityDirectory => Path.Combine(DataRoot, "security");
    public string TeachersFilePath => Path.Combine(SecurityDirectory, "teachers.json");
    public string SettingsFilePath => Path.Combine(SecurityDirectory, "teacher-auth-settings.json");

    public IReadOnlyList<TeacherAccount> ListTeachers()
    {
        lock (_sync)
        {
            return LoadTeachersUnsafe()
                .OrderBy(account => account.Username, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public TeacherAccount? FindTeacher(string username)
    {
        var normalizedUsername = NormalizeUsername(username);
        lock (_sync)
        {
            return LoadTeachersUnsafe().FirstOrDefault(account =>
                string.Equals(account.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));
        }
    }

    public TeacherAccount CreateTeacher(string username, string displayName, string password, string role = TeacherRoles.Teacher)
    {
        var normalizedUsername = NormalizeUsername(username);
        var normalizedDisplayName = NormalizeDisplayName(displayName, normalizedUsername);
        var normalizedRole = TeacherRoles.Normalize(role);

        lock (_sync)
        {
            var teachers = LoadTeachersUnsafe();
            if (teachers.Any(account => string.Equals(account.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Teacher account already exists.");
            }

            var hash = _passwordHasher.HashPassword(password);
            var now = DateTime.UtcNow;
            var account = new TeacherAccount
            {
                Username = normalizedUsername,
                DisplayName = normalizedDisplayName,
                PasswordHash = hash.PasswordHash,
                PasswordSalt = hash.PasswordSalt,
                Role = normalizedRole,
                IsActive = true,
                CreatedUtc = now,
                UpdatedUtc = now
            };

            teachers.Add(account);
            SaveTeachersUnsafe(teachers);
            return account;
        }
    }

    public TeacherAccount SetTeacherPassword(string username, string password)
    {
        var normalizedUsername = NormalizeUsername(username);

        lock (_sync)
        {
            var teachers = LoadTeachersUnsafe();
            var account = teachers.FirstOrDefault(item =>
                string.Equals(item.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                throw new InvalidOperationException("Teacher account was not found.");
            }

            var hash = _passwordHasher.HashPassword(password);
            account.PasswordHash = hash.PasswordHash;
            account.PasswordSalt = hash.PasswordSalt;
            account.UpdatedUtc = DateTime.UtcNow;
            SaveTeachersUnsafe(teachers);
            return account;
        }
    }

    public TeacherAccount UpdateTeacher(string username, string? displayName, string? role)
    {
        var normalizedUsername = NormalizeUsername(username);
        var hasDisplayName = displayName is not null;
        var hasRole = role is not null;
        var normalizedDisplayName = hasDisplayName
            ? NormalizeDisplayName(displayName!, normalizedUsername, requireExplicitValue: true)
            : string.Empty;
        var normalizedRole = hasRole ? TeacherRoles.Normalize(role) : string.Empty;

        lock (_sync)
        {
            var teachers = LoadTeachersUnsafe();
            var account = FindTeacherUnsafe(teachers, normalizedUsername);
            if (account is null)
            {
                throw new InvalidOperationException("Teacher account was not found.");
            }

            if (hasRole &&
                account.IsActive &&
                TeacherRoles.IsAdmin(account.Role) &&
                !TeacherRoles.IsAdmin(normalizedRole) &&
                CountActiveAdmins(teachers) <= 1)
            {
                throw new InvalidOperationException("The last active admin cannot lose the Admin role.");
            }

            if (hasDisplayName)
            {
                account.DisplayName = normalizedDisplayName;
            }

            if (hasRole)
            {
                account.Role = normalizedRole;
            }

            account.UpdatedUtc = DateTime.UtcNow;
            SaveTeachersUnsafe(teachers);
            return account;
        }
    }

    public TeacherAccount SetTeacherActive(string username, bool isActive)
    {
        var normalizedUsername = NormalizeUsername(username);

        lock (_sync)
        {
            var teachers = LoadTeachersUnsafe();
            var account = teachers.FirstOrDefault(item =>
                string.Equals(item.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                throw new InvalidOperationException("Teacher account was not found.");
            }

            if (!isActive &&
                account.IsActive &&
                TeacherRoles.IsAdmin(account.Role) &&
                CountActiveAdmins(teachers) <= 1)
            {
                throw new InvalidOperationException("The last active admin cannot be deactivated.");
            }

            account.IsActive = isActive;
            account.UpdatedUtc = DateTime.UtcNow;
            SaveTeachersUnsafe(teachers);
            return account;
        }
    }

    public TeacherAccount DeleteTeacher(string username)
    {
        var normalizedUsername = NormalizeUsername(username);

        lock (_sync)
        {
            var teachers = LoadTeachersUnsafe();
            var account = FindTeacherUnsafe(teachers, normalizedUsername);
            if (account is null)
            {
                throw new InvalidOperationException("Teacher account was not found.");
            }

            if (TeacherRoles.IsAdmin(account.Role))
            {
                var adminCount = CountAdmins(teachers);
                var activeAdminCount = CountActiveAdmins(teachers);
                if (adminCount <= 1)
                {
                    throw new InvalidOperationException("The last admin cannot be deleted.");
                }

                if (account.IsActive && activeAdminCount <= 1)
                {
                    throw new InvalidOperationException("The last active admin cannot be deleted.");
                }
            }

            teachers.Remove(account);
            SaveTeachersUnsafe(teachers);
            return account;
        }
    }

    public TeacherAccount? VerifyCredentials(string username, string password)
    {
        var normalizedUsername = NormalizeUsername(username);
        TeacherAccount? account;
        lock (_sync)
        {
            account = LoadTeachersUnsafe().FirstOrDefault(item =>
                string.Equals(item.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));
        }

        if (account is null || !account.IsActive)
        {
            return null;
        }

        var passwordResult = _passwordHasher.VerifyPassword(password, account);
        if (passwordResult is TeacherPasswordVerificationResult.Failed)
        {
            return null;
        }

        if (passwordResult is TeacherPasswordVerificationResult.SuccessRehashNeeded)
        {
            lock (_sync)
            {
                var teachers = LoadTeachersUnsafe();
                var storedAccount = teachers.FirstOrDefault(item =>
                    string.Equals(item.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));
                if (storedAccount is not null)
                {
                    var upgradedHash = _passwordHasher.HashPassword(password);
                    storedAccount.PasswordHash = upgradedHash.PasswordHash;
                    storedAccount.PasswordSalt = upgradedHash.PasswordSalt;
                    storedAccount.UpdatedUtc = DateTime.UtcNow;
                    SaveTeachersUnsafe(teachers);
                    account = storedAccount;
                }
            }
        }

        return account;
    }

    public TeacherAuthSettings LoadOrCreateSettings()
    {
        lock (_sync)
        {
            Directory.CreateDirectory(SecurityDirectory);
            if (File.Exists(SettingsFilePath))
            {
                var settings = JsonSerializer.Deserialize<TeacherAuthSettings>(
                    File.ReadAllText(SettingsFilePath, Encoding.UTF8),
                    SerializerOptions);
                if (settings is not null)
                {
                    return settings;
                }
            }

            var now = DateTime.UtcNow;
            var newSettings = new TeacherAuthSettings
            {
                Version = 1,
                TokenLifetimeMinutes = 480,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            SaveJsonAtomic(SettingsFilePath, newSettings);
            return newSettings;
        }
    }

    private List<TeacherAccount> LoadTeachersUnsafe()
    {
        Directory.CreateDirectory(SecurityDirectory);
        if (!File.Exists(TeachersFilePath))
        {
            return [];
        }

        var teachers = JsonSerializer.Deserialize<List<TeacherAccount>>(
            File.ReadAllText(TeachersFilePath, Encoding.UTF8),
            SerializerOptions);
        foreach (var teacher in teachers ?? [])
        {
            teacher.Role = TeacherRoles.Normalize(teacher.Role);
        }

        return teachers ?? [];
    }

    private void SaveTeachersUnsafe(List<TeacherAccount> teachers)
    {
        SaveJsonAtomic(
            TeachersFilePath,
            teachers.OrderBy(account => account.Username, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static TeacherAccount? FindTeacherUnsafe(List<TeacherAccount> teachers, string normalizedUsername) =>
        teachers.FirstOrDefault(item =>
            string.Equals(item.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

    private static int CountActiveAdmins(List<TeacherAccount> teachers) =>
        teachers.Count(account => account.IsActive && TeacherRoles.IsAdmin(account.Role));

    private static int CountAdmins(List<TeacherAccount> teachers) =>
        teachers.Count(account => TeacherRoles.IsAdmin(account.Role));

    private static void SaveJsonAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(value, SerializerOptions), Encoding.UTF8);
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private static string NormalizeUsername(string username)
    {
        var value = username.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 64 ||
            value.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' or '@')))
        {
            throw new ArgumentException("Teacher username is not valid.", nameof(username));
        }

        return value;
    }

    private static string NormalizeDisplayName(string displayName, string fallback, bool requireExplicitValue = false)
    {
        var value = string.IsNullOrWhiteSpace(displayName) ? fallback : displayName.Trim();
        if (requireExplicitValue && string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Teacher display name must not be empty.", nameof(displayName));
        }

        if (value.Length > 120)
        {
            throw new ArgumentException("Teacher display name is too long.", nameof(displayName));
        }

        return value;
    }
}

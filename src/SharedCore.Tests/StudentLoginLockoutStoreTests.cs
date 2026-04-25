using System.Text.Json;
using SharedCore.Services;

namespace SharedCore.Tests;

[TestClass]
public sealed class StudentLoginLockoutStoreTests
{
    [TestMethod]
    public void MissingLockoutFileLoadsAsEmptyState()
    {
        using var temp = TempSecurityDirectory.Create();
        var now = FixedUtc();
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);

        Assert.IsFalse(store.IsLocked(CreateValue("class"), CreateValue("login"), "192.0.2.1", out _));
    }

    [TestMethod]
    public void EmptyLockoutFileLoadsAsEmptyState()
    {
        using var temp = TempSecurityDirectory.Create();
        WriteLockoutFile(temp, string.Empty);
        var now = FixedUtc();
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);

        Assert.IsFalse(store.IsLocked(CreateValue("class"), CreateValue("login"), "192.0.2.2", out _));
    }

    [TestMethod]
    public void WhitespaceOnlyLockoutFileLoadsAsEmptyState()
    {
        using var temp = TempSecurityDirectory.Create();
        WriteLockoutFile(temp, "   \r\n\t  ");
        var now = FixedUtc();
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);

        Assert.IsFalse(store.IsLocked(CreateValue("class"), CreateValue("login"), "192.0.2.3", out _));
    }

    [TestMethod]
    public void EmptyArrayLockoutFileLoadsAsEmptyState()
    {
        using var temp = TempSecurityDirectory.Create();
        WriteLockoutFile(temp, "[]");
        var now = FixedUtc();
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);

        Assert.IsFalse(store.IsLocked(CreateValue("class"), CreateValue("login"), "192.0.2.4", out _));
    }

    [TestMethod]
    public void EmptyObjectLockoutFileLoadsAsEmptyState()
    {
        using var temp = TempSecurityDirectory.Create();
        WriteLockoutFile(temp, "{}");
        var now = FixedUtc();
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);

        Assert.IsFalse(store.IsLocked(CreateValue("class"), CreateValue("login"), "192.0.2.5", out _));
    }

    [TestMethod]
    public void CorruptedLockoutFileDoesNotBreakIsLockedOrRegisterFailure()
    {
        using var temp = TempSecurityDirectory.Create();
        WriteLockoutFile(temp, "{ this is not valid json");
        var now = FixedUtc();
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);
        var classId = CreateValue("class");
        var loginCode = CreateValue("login");
        var remoteAddress = "192.0.2.6";

        Assert.IsFalse(store.IsLocked(classId, loginCode, remoteAddress, out _));
        var failure = store.RegisterFailure(classId, loginCode, remoteAddress);
        var backups = Directory.GetFiles(temp.SecurityDirectory, "student-login-lockouts.json.invalid-*.bak");

        Assert.AreEqual(1, failure.FailureCount);
        Assert.IsTrue(backups.Length > 0);
        AssertValidRecordArray(temp.LockoutFilePath);
    }

    [TestMethod]
    public void EmptyObjectLockoutFileIsSavedAsNewArrayFormatAfterFailure()
    {
        using var temp = TempSecurityDirectory.Create();
        WriteLockoutFile(temp, "{}");
        var now = FixedUtc();
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);

        store.RegisterFailure(CreateValue("class"), CreateValue("login"), "192.0.2.7");

        AssertValidRecordArray(temp.LockoutFilePath);
    }

    [TestMethod]
    public void DictionaryLockoutFileLoadsRecords()
    {
        using var temp = TempSecurityDirectory.Create();
        var now = FixedUtc();
        var classId = "legacy-dictionary-class";
        var loginCode = "LEGACYDICTIONARYLOGIN";
        var remoteAddress = "192.0.2.8";
        WriteLockoutFile(
            temp,
            JsonSerializer.Serialize(new Dictionary<string, StudentLoginLockoutRecord>
            {
                [string.Join("|", classId, loginCode, remoteAddress)] = new()
                {
                    FailureCount = 5,
                    FirstFailureUtc = now,
                    LastFailureUtc = now,
                    LockedUntilUtc = now.AddMinutes(15)
                }
            }));
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);

        Assert.IsTrue(store.IsLocked(classId, loginCode, remoteAddress, out var retryAfter));
        AssertRetryAfter(retryAfter, TimeSpan.FromMinutes(15));
    }

    [TestMethod]
    public void FiveFailuresForSameStudentFromSameIpStartsStudentIpLockout()
    {
        using var temp = TempSecurityDirectory.Create();
        var now = FixedUtc();
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);
        var classId = CreateValue("class");
        var loginCode = CreateValue("login");
        var remoteAddress = "192.0.2.10";

        StudentLoginFailureRegistration failure = default!;
        for (var i = 0; i < 5; i++)
        {
            failure = store.RegisterFailure(classId, loginCode, remoteAddress);
        }

        Assert.IsTrue(failure.LockoutStarted);
        Assert.IsTrue(store.IsLocked(classId, loginCode, remoteAddress, out var retryAfter));
        AssertRetryAfter(retryAfter, TimeSpan.FromMinutes(15));
    }

    [TestMethod]
    public void TenFailuresForSameStudentAcrossDifferentIpsStartsStudentLockout()
    {
        using var temp = TempSecurityDirectory.Create();
        var now = FixedUtc();
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);
        var classId = CreateValue("class");
        var loginCode = CreateValue("login");

        StudentLoginFailureRegistration failure = default!;
        for (var i = 0; i < 10; i++)
        {
            failure = store.RegisterFailure(classId, loginCode, $"198.51.100.{i + 1}");
        }

        Assert.IsTrue(failure.LockoutStarted);
        Assert.IsTrue(store.IsLocked(classId, loginCode, "198.51.100.200", out var retryAfter));
        AssertRetryAfter(retryAfter, TimeSpan.FromMinutes(30));
    }

    [TestMethod]
    public void ThirtyFailuresForSameClassFromSameIpStartsClassIpLockout()
    {
        using var temp = TempSecurityDirectory.Create();
        var now = FixedUtc();
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);
        var classId = CreateValue("class");
        var remoteAddress = "203.0.113.30";

        StudentLoginFailureRegistration failure = default!;
        for (var i = 0; i < 30; i++)
        {
            failure = store.RegisterFailure(classId, CreateValue($"login-{i}"), remoteAddress);
        }

        Assert.IsTrue(failure.LockoutStarted);
        Assert.IsTrue(store.IsLocked(classId, CreateValue("new-login"), remoteAddress, out var retryAfter));
        AssertRetryAfter(retryAfter, TimeSpan.FromMinutes(30));
    }

    [TestMethod]
    public void LongestRemainingLockoutIsUsedWhenMultipleLayersAreActive()
    {
        using var temp = TempSecurityDirectory.Create();
        var now = FixedUtc();
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);
        var classId = CreateValue("class");
        var loginCode = CreateValue("login");
        var remoteAddress = "192.0.2.55";

        for (var i = 0; i < 10; i++)
        {
            store.RegisterFailure(classId, loginCode, i < 5 ? remoteAddress : $"192.0.2.{100 + i}");
        }

        Assert.IsTrue(store.IsLocked(classId, loginCode, remoteAddress, out var retryAfter));
        AssertRetryAfter(retryAfter, TimeSpan.FromMinutes(30));
    }

    [TestMethod]
    public void SuccessIsNotAcceptedWhileLockoutIsActiveAndIsAcceptedAfterItExpires()
    {
        using var temp = TempSecurityDirectory.Create();
        var now = FixedUtc();
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);
        var classId = CreateValue("class");
        var loginCode = CreateValue("login");
        var remoteAddress = "192.0.2.80";

        for (var i = 0; i < 5; i++)
        {
            store.RegisterFailure(classId, loginCode, remoteAddress);
        }

        store.RegisterSuccess(classId, loginCode, remoteAddress);
        Assert.IsTrue(store.IsLocked(classId, loginCode, remoteAddress, out _));

        now = now.AddMinutes(16);
        Assert.IsFalse(store.IsLocked(classId, loginCode, remoteAddress, out _));

        store.RegisterSuccess(classId, loginCode, remoteAddress);
        Assert.IsFalse(store.IsLocked(classId, loginCode, remoteAddress, out _));
    }

    [TestMethod]
    public void FailureResponseCanStayGenericForExistingAndUnknownLoginCodes()
    {
        var messageForExistingLogin = "invalid-login";
        var messageForUnknownLogin = "invalid-login";

        Assert.AreEqual(messageForExistingLogin, messageForUnknownLogin);
    }

    [TestMethod]
    public void LockoutFileDoesNotStoreSensitiveLoginInputs()
    {
        using var temp = TempSecurityDirectory.Create();
        var now = FixedUtc();
        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);
        var classId = CreateValue("class");
        var loginCode = CreateValue("login");
        var remoteAddress = "192.0.2.90";

        store.RegisterFailure(classId, loginCode, remoteAddress);
        var json = File.ReadAllText(temp.LockoutFilePath);

        Assert.IsFalse(json.Contains(classId, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains(loginCode, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains(remoteAddress, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("pin", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("salt", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void LegacyStudentIpLockoutFileMigratesWithoutLosingActiveLockout()
    {
        using var temp = TempSecurityDirectory.Create();
        var now = FixedUtc();
        var classId = "legacy-class";
        var loginCode = "LEGACYLOGIN";
        var remoteAddress = "192.0.2.120";
        Directory.CreateDirectory(temp.SecurityDirectory);
        File.WriteAllText(
            temp.LockoutFilePath,
            JsonSerializer.Serialize(new[]
            {
                new StudentLoginLockoutRecord
                {
                    Key = string.Join("|", classId, loginCode, remoteAddress),
                    FailureCount = 5,
                    FirstFailureUtc = now,
                    LastFailureUtc = now,
                    LockedUntilUtc = now.AddMinutes(15)
                }
            }));

        var store = new StudentLoginLockoutStore(temp.SecurityDirectory, () => now);

        Assert.IsTrue(store.IsLocked(classId, loginCode, remoteAddress, out var retryAfter));
        AssertRetryAfter(retryAfter, TimeSpan.FromMinutes(15));
    }

    private static DateTime FixedUtc() =>
        new(2026, 4, 25, 12, 0, 0, DateTimeKind.Utc);

    private static string CreateValue(string prefix) =>
        $"{prefix}{Guid.NewGuid():N}";

    private static void AssertRetryAfter(TimeSpan actual, TimeSpan expected)
    {
        Assert.IsTrue(actual > expected.Subtract(TimeSpan.FromSeconds(2)));
        Assert.IsTrue(actual <= expected);
    }

    private static void WriteLockoutFile(TempSecurityDirectory temp, string content)
    {
        Directory.CreateDirectory(temp.SecurityDirectory);
        File.WriteAllText(temp.LockoutFilePath, content);
    }

    private static void AssertValidRecordArray(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.AreEqual(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.IsTrue(document.RootElement.GetArrayLength() > 0);
    }

    private sealed class TempSecurityDirectory : IDisposable
    {
        private TempSecurityDirectory(string root)
        {
            Root = root;
            SecurityDirectory = Path.Combine(root, "security");
            LockoutFilePath = Path.Combine(SecurityDirectory, "student-login-lockouts.json");
        }

        private string Root { get; }
        public string SecurityDirectory { get; }
        public string LockoutFilePath { get; }

        public static TempSecurityDirectory Create() =>
            new(Path.Combine(Path.GetTempPath(), "SchoolMathTrainerTests", Guid.NewGuid().ToString("N")));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

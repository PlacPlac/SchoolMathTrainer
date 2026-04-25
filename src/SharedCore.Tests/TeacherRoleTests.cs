using System.Text.Json;
using SharedCore.Models;
using SharedCore.Services;

namespace SharedCore.Tests;

[TestClass]
public sealed class TeacherRoleTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [TestMethod]
    public void OldTeacherAccountJsonWithoutRoleLoadsAsTeacher()
    {
        using var temp = TestDataRoot.Create();
        Directory.CreateDirectory(temp.Store.SecurityDirectory);
        File.WriteAllText(
            temp.Store.TeachersFilePath,
            """
            [
              {
                "username": "legacy.teacher",
                "displayName": "Legacy Teacher",
                "passwordHash": "",
                "passwordSalt": "",
                "isActive": true,
                "createdUtc": "2026-01-01T00:00:00Z",
                "updatedUtc": "2026-01-01T00:00:00Z"
              }
            ]
            """);

        var account = temp.Store.FindTeacher("legacy.teacher");

        Assert.IsNotNull(account);
        Assert.AreEqual(TeacherRoles.Teacher, account.Role);
    }

    [TestMethod]
    public void AdminTeacherAccountRoundTripsWithAdminRole()
    {
        using var temp = TestDataRoot.Create();
        var generatedPassword = Guid.NewGuid().ToString("N");

        temp.Store.CreateTeacher(
            "admin.user",
            "Admin User",
            generatedPassword,
            TeacherRoles.Admin);
        var reloadedStore = new TeacherAccountStore(temp.DataRoot);

        var account = reloadedStore.FindTeacher("admin.user");

        Assert.IsNotNull(account);
        Assert.AreEqual(TeacherRoles.Admin, account.Role);
    }

    [TestMethod]
    public void InvalidRoleDoesNotBecomeAdmin()
    {
        using var temp = TestDataRoot.Create();
        Directory.CreateDirectory(temp.Store.SecurityDirectory);
        WriteTeachers(temp.Store, [
            new TeacherAccount
            {
                Username = "invalid.role",
                DisplayName = "Invalid Role",
                Role = "Owner"
            }
        ]);

        var account = temp.Store.FindTeacher("invalid.role");

        Assert.IsNotNull(account);
        Assert.AreEqual(TeacherRoles.Teacher, account.Role);
        Assert.IsFalse(TeacherRoles.IsAdmin(account.Role));
    }

    [TestMethod]
    public void TokenValidationReturnsRoleFromCurrentTeacherAccount()
    {
        using var temp = TestDataRoot.Create();
        var generatedPassword = Guid.NewGuid().ToString("N");
        var account = temp.Store.CreateTeacher(
            "role.source",
            "Role Source",
            generatedPassword,
            TeacherRoles.Teacher);
        var tokens = new TeacherTokenService(temp.Store);
        var issued = tokens.IssueToken(account);

        var storedAccounts = temp.Store.ListTeachers().ToList();
        storedAccounts[0].Role = TeacherRoles.Admin;
        WriteTeachers(temp.Store, storedAccounts);

        var validation = tokens.ValidateToken(issued.Token);

        Assert.IsTrue(validation.Success);
        Assert.AreEqual("role.source", validation.Username);
        Assert.AreEqual(TeacherRoles.Admin, validation.Role);
    }

    [TestMethod]
    public void LastActiveAdminCannotBeDeactivated()
    {
        using var temp = TestDataRoot.Create();
        var generatedPassword = Guid.NewGuid().ToString("N");
        temp.Store.CreateTeacher(
            "only.admin",
            "Only Admin",
            generatedPassword,
            TeacherRoles.Admin);

        Assert.ThrowsException<InvalidOperationException>(() =>
            temp.Store.SetTeacherActive("only.admin", false));
    }

    [TestMethod]
    public void LastActiveAdminCannotBeChangedToTeacher()
    {
        using var temp = TestDataRoot.Create();
        var generatedPassword = Guid.NewGuid().ToString("N");
        temp.Store.CreateTeacher(
            "only.admin",
            "Only Admin",
            generatedPassword,
            TeacherRoles.Admin);

        Assert.ThrowsException<InvalidOperationException>(() =>
            temp.Store.UpdateTeacher("only.admin", null, TeacherRoles.Teacher));
    }

    [TestMethod]
    public void RegularTeacherRoleIsNotAdmin()
    {
        Assert.IsFalse(TeacherRoles.IsAdmin(TeacherRoles.Teacher));
    }

    [TestMethod]
    public void AdminTeacherListItemDoesNotSerializeSensitiveFields()
    {
        var item = new AdminTeacherListItem(
            "admin.user",
            "Admin User",
            TeacherRoles.Admin,
            true,
            DateTime.UtcNow,
            DateTime.UtcNow);

        var json = JsonSerializer.Serialize(item, JsonOptions);

        Assert.IsFalse(json.Contains("passwordHash", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("passwordSalt", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("session", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void LastActiveAdminCannotBeDeleted()
    {
        using var temp = TestDataRoot.Create();
        var generatedPassword = Guid.NewGuid().ToString("N");
        temp.Store.CreateTeacher(
            "only.admin",
            "Only Admin",
            generatedPassword,
            TeacherRoles.Admin);

        Assert.ThrowsException<InvalidOperationException>(() =>
            temp.Store.DeleteTeacher("only.admin"));
    }

    [TestMethod]
    public void LastAdminCannotBeDeletedEvenWhenInactive()
    {
        using var temp = TestDataRoot.Create();
        var generatedPassword = Guid.NewGuid().ToString("N");
        var admin = temp.Store.CreateTeacher(
            "only.admin",
            "Only Admin",
            generatedPassword,
            TeacherRoles.Admin);
        WriteTeachers(temp.Store, [
            new TeacherAccount
            {
                Username = admin.Username,
                DisplayName = admin.DisplayName,
                PasswordHash = admin.PasswordHash,
                PasswordSalt = admin.PasswordSalt,
                Role = admin.Role,
                IsActive = false,
                CreatedUtc = admin.CreatedUtc,
                UpdatedUtc = admin.UpdatedUtc
            }
        ]);

        Assert.ThrowsException<InvalidOperationException>(() =>
            temp.Store.DeleteTeacher("only.admin"));
    }

    [TestMethod]
    public void RegularTeacherCanBeDeletedAndDisappearsFromList()
    {
        using var temp = TestDataRoot.Create();
        var generatedPassword = Guid.NewGuid().ToString("N");
        temp.Store.CreateTeacher("admin.user", "Admin User", generatedPassword, TeacherRoles.Admin);
        temp.Store.CreateTeacher("teacher.user", "Teacher User", generatedPassword, TeacherRoles.Teacher);

        var deleted = temp.Store.DeleteTeacher("teacher.user");
        var remaining = temp.Store.ListTeachers();

        Assert.AreEqual("teacher.user", deleted.Username);
        Assert.IsFalse(remaining.Any(account => account.Username == "teacher.user"));
    }

    [TestMethod]
    public void RevokingDeletedTeacherSessionsInvalidatesIssuedToken()
    {
        using var temp = TestDataRoot.Create();
        var generatedPassword = Guid.NewGuid().ToString("N");
        temp.Store.CreateTeacher("admin.user", "Admin User", generatedPassword, TeacherRoles.Admin);
        var teacher = temp.Store.CreateTeacher("teacher.user", "Teacher User", generatedPassword, TeacherRoles.Teacher);
        var tokens = new TeacherTokenService(temp.Store);
        var issued = tokens.IssueToken(teacher);

        temp.Store.DeleteTeacher("teacher.user");
        var revokedCount = tokens.RevokeTokensForTeacher("teacher.user");
        var validation = tokens.ValidateToken(issued.Token);

        Assert.AreEqual(1, revokedCount);
        Assert.IsFalse(validation.Success);
    }

    private static void WriteTeachers(TeacherAccountStore store, IReadOnlyList<TeacherAccount> accounts)
    {
        Directory.CreateDirectory(store.SecurityDirectory);
        File.WriteAllText(
            store.TeachersFilePath,
            JsonSerializer.Serialize(accounts, JsonOptions));
    }

    private sealed class TestDataRoot : IDisposable
    {
        private TestDataRoot(string dataRoot)
        {
            DataRoot = dataRoot;
            Store = new TeacherAccountStore(dataRoot);
        }

        public string DataRoot { get; }
        public TeacherAccountStore Store { get; }

        public static TestDataRoot Create()
        {
            var path = Path.Combine(Path.GetTempPath(), "SchoolMathTrainerTests", Guid.NewGuid().ToString("N"));
            return new TestDataRoot(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(DataRoot))
            {
                Directory.Delete(DataRoot, recursive: true);
            }
        }
    }
}

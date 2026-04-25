namespace SharedCore.Models;

public sealed class TeacherAccount
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string Role { get; set; } = TeacherRoles.Teacher;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public static class TeacherRoles
{
    public const string Admin = "Admin";
    public const string Teacher = "Teacher";

    public static string Normalize(string? role)
    {
        return TryNormalize(role, out var normalizedRole)
            ? normalizedRole
            : Teacher;
    }

    public static bool TryNormalize(string? role, out string normalizedRole)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            normalizedRole = Teacher;
            return true;
        }

        if (string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase))
        {
            normalizedRole = Admin;
            return true;
        }

        if (string.Equals(role, Teacher, StringComparison.OrdinalIgnoreCase))
        {
            normalizedRole = Teacher;
            return true;
        }

        normalizedRole = Teacher;
        return false;
    }

    public static bool IsAdmin(string? role) =>
        string.Equals(Normalize(role), Admin, StringComparison.Ordinal);
}

using System.Globalization;
using SharedCore.Models;

namespace TeacherApp.Data;

public sealed class AdminTeacherListItemView
{
    private static readonly CultureInfo CzechCulture = CultureInfo.GetCultureInfo("cs-CZ");

    public AdminTeacherListItemView(AdminTeacherListItem item)
    {
        Username = item.Username;
        DisplayName = item.DisplayName;
        Role = TeacherRoles.Normalize(item.Role);
        RoleDisplay = ToRoleDisplay(Role);
        RoleListDisplay = TeacherRoles.IsAdmin(Role) ? "Admin" : "Učitel";
        Active = item.Active;
        CreatedUtc = item.CreatedUtc;
        UpdatedUtc = item.UpdatedUtc;
    }

    public string Username { get; }
    public string DisplayName { get; }
    public string Role { get; }
    public string RoleDisplay { get; }
    public string RoleListDisplay { get; }
    public bool Active { get; }
    public DateTime CreatedUtc { get; }
    public DateTime UpdatedUtc { get; }
    public string ActiveText => Active ? "Aktivní" : "Neaktivní";
    public string CreatedText => FormatDateTime(CreatedUtc);
    public string UpdatedText => FormatDateTime(UpdatedUtc);

    private static string FormatDateTime(DateTime value)
    {
        var local = value.Kind == DateTimeKind.Unspecified ? value : value.ToLocalTime();
        return local.ToString("dd.MM.yyyy HH:mm", CzechCulture);
    }

    private static string ToRoleDisplay(string role) =>
        TeacherRoles.IsAdmin(role) ? "Administrátor" : "Učitel";
}

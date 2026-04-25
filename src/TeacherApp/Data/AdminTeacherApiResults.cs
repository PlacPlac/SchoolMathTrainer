using SharedCore.Models;

namespace TeacherApp.Data;

public sealed record AdminTeacherListResult(
    bool Success,
    string Message,
    IReadOnlyList<AdminTeacherListItem> Teachers);

public sealed record AdminTeacherOperationResult(
    bool Success,
    string Message,
    AdminTeacherListItem? Teacher = null);

namespace TeacherApp.Data;

public sealed class StudentAccountReadResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<StudentListItem> Students { get; init; } = [];
}

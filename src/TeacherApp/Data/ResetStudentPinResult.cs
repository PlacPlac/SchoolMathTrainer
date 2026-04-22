using SharedCore.Models;

namespace TeacherApp.Data;

public sealed class ResetStudentPinResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public StudentAccount? Account { get; init; }
    public string TemporaryPin { get; init; } = string.Empty;
}

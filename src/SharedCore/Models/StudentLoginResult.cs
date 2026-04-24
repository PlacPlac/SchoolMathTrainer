namespace SharedCore.Models;

public sealed class StudentLoginResult
{
    public bool Success { get; init; }
    public bool RequiresPinChange { get; init; }
    public bool RequiresStudentConfigurationReload { get; init; }
    public string Message { get; init; } = string.Empty;
    public string StudentId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string StudentSessionToken { get; init; } = string.Empty;
    public DateTime? StudentSessionExpiresUtc { get; init; }

    public static StudentLoginResult Failed(string message) => new()
    {
        Success = false,
        Message = message
    };

    public static StudentLoginResult PinChangeRequired(string message) => new()
    {
        Success = false,
        RequiresPinChange = true,
        Message = message
    };

    public static StudentLoginResult StudentConfigurationMismatch(string message) => new()
    {
        Success = false,
        RequiresStudentConfigurationReload = true,
        Message = message
    };

    public static StudentLoginResult LoggedIn(string studentId, string displayName, string message) => new()
    {
        Success = true,
        StudentId = studentId,
        DisplayName = displayName,
        Message = message
    };
}

namespace TeacherApp.Settings;

public sealed class TeacherSshConnectionTestResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    public static TeacherSshConnectionTestResult Ok(string message) =>
        new()
        {
            Success = true,
            Message = message
        };

    public static TeacherSshConnectionTestResult Failed(string message) =>
        new()
        {
            Success = false,
            Message = message
        };
}

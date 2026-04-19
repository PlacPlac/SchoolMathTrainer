using System.Text;

namespace SharedCore.Services;

public static class DiagnosticLogService
{
    public static void Log(string applicationName, string message)
    {
        try
        {
            var path = GetLogPath(applicationName);
            var line = $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}";
            File.AppendAllText(path, line, Encoding.UTF8);
        }
        catch
        {
            // Diagnostics must never affect application flow.
        }
    }

    public static void LogError(string applicationName, string operation, Exception exception)
    {
        Log(applicationName, $"{operation}: {exception.GetType().Name} - {exception.Message}");
    }

    private static string GetLogPath(string applicationName)
    {
        var safeName = string.Join(
            "_",
            applicationName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "SchoolMathTrainer";
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SchoolMathTrainer",
            "Diagnostics");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{safeName}-{DateTime.UtcNow:yyyy-MM-dd}.log");
    }
}

using SharedCore.Models;

namespace SharedCore.Services;

public sealed class LoggingService
{
    private readonly FileSystemStorageService _storageService;
    private readonly AppConfiguration _configuration;

    public LoggingService(FileSystemStorageService storageService, AppConfiguration configuration)
    {
        _storageService = storageService;
        _configuration = configuration;
        _storageService.EnsureDirectory(_configuration.LogDirectory);
    }

    public void Log(string message)
    {
        try
        {
            var fileName = $"{DateTime.UtcNow:yyyy-MM-dd}.log";
            var path = Path.Combine(_configuration.LogDirectory, fileName);
            var line = $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}";
            _storageService.AppendLine(path, line);
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    public void LogError(string scope, Exception exception)
    {
        Log($"{scope}: {exception.GetType().Name} - {exception.Message}");
    }
}

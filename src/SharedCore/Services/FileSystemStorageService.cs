using System.Text;
using System.Text.Json;

namespace SharedCore.Services;

public sealed class FileSystemStorageService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly RetryFileAccessService _retryFileAccessService;
    private readonly IOnlineDataService _onlineDataService;
    private readonly int _retries;
    private readonly int _delayMilliseconds;

    public FileSystemStorageService(RetryFileAccessService retryFileAccessService, int retries, int delayMilliseconds)
        : this(retryFileAccessService, new OnlineDataService(), retries, delayMilliseconds)
    {
    }

    public FileSystemStorageService(
        RetryFileAccessService retryFileAccessService,
        IOnlineDataService onlineDataService,
        int retries,
        int delayMilliseconds)
    {
        _retryFileAccessService = retryFileAccessService;
        _onlineDataService = onlineDataService;
        _retries = retries;
        _delayMilliseconds = delayMilliseconds;
    }

    public void EnsureDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        _retryFileAccessService.Execute(() => _onlineDataService.EnsureDirectory(directoryPath), _retries, _delayMilliseconds);
    }

    public void SaveJson<T>(string path, T data)
    {
        EnsureDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        _retryFileAccessService.Execute(() =>
        {
            var tempPath = $"{path}.tmp";
            var json = JsonSerializer.Serialize(data, SerializerOptions);
            _onlineDataService.WriteFile(tempPath, json);
            try
            {
                _onlineDataService.CopyFile(tempPath, path, true);
            }
            finally
            {
                _onlineDataService.DeleteFile(tempPath);
            }
        }, _retries, _delayMilliseconds);
    }

    public bool TryLoadJson<T>(string path, out T? data, out Exception? error)
    {
        data = default;
        error = null;

        if (!_onlineDataService.FileExists(path))
        {
            return false;
        }

        try
        {
            data = _retryFileAccessService.Execute(() =>
            {
                var json = _onlineDataService.ReadFile(path);
                return JsonSerializer.Deserialize<T>(json, SerializerOptions);
            }, _retries, _delayMilliseconds);

            return data is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex;
            return false;
        }
    }

    public IReadOnlyList<T> LoadJsonFiles<T>(string directoryPath, Action<string, Exception>? onError = null)
    {
        EnsureDirectory(directoryPath);
        var files = _onlineDataService.ListFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly);
        var items = new List<T>();

        foreach (var file in files)
        {
            if (TryLoadJson<T>(file, out var data, out var error) && data is not null)
            {
                items.Add(data);
            }
            else if (error is not null)
            {
                onError?.Invoke(file, error);
            }
        }

        return items;
    }

    public void AppendLine(string path, string line)
    {
        EnsureDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        _retryFileAccessService.Execute(() => _onlineDataService.AppendLine(path, line), _retries, _delayMilliseconds);
    }

    public void WriteText(string path, string content)
    {
        EnsureDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        _retryFileAccessService.Execute(() => _onlineDataService.WriteFile(path, content), _retries, _delayMilliseconds);
    }
}

using System.Text;
using SharedCore.Models;
using SharedCore.Services;

namespace SchoolMathTrainer.Api.Services;

internal sealed class ConfiguredApiDataService : IOnlineDataService
{
    private const string DataRootConfigurationKey = "DataConnection:DataRoot";
    private const string DefaultClassIdConfigurationKey = "DataConnection:ClassId";

    private readonly string _dataRoot;
    private readonly string _defaultClassId;

    public ConfiguredApiDataService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _dataRoot = ResolveDataRoot(configuration[DataRootConfigurationKey], environment.ContentRootPath);
        _defaultClassId = string.IsNullOrWhiteSpace(configuration[DefaultClassIdConfigurationKey])
            ? DataConnectionSettings.DefaultClassId
            : configuration[DefaultClassIdConfigurationKey]!.Trim();
    }

    public string ResolveClassDataRoot(string classId)
    {
        var safeClassId = NormalizeSegment(classId);
        var dataRootFolderName = Path.GetFileName(_dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if ((string.Equals(safeClassId, _defaultClassId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(safeClassId, dataRootFolderName, StringComparison.OrdinalIgnoreCase)) &&
            File.Exists(Path.Combine(_dataRoot, "Config", "student-accounts.json")))
        {
            return _dataRoot;
        }

        return Path.Combine(_dataRoot, safeClassId);
    }

    public string BuildStudentDataPath(string classId, string studentId, params string[] segments)
    {
        var parts = new List<string>
        {
            ResolveClassDataRoot(classId),
            "Data",
            "StudentResults",
            NormalizeSegment(studentId)
        };
        parts.AddRange(segments.Select(NormalizeSegment));
        return Path.Combine(parts.ToArray());
    }

    public void EnsureDirectory(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public bool FileExists(string path) => File.Exists(path);

    public string ReadFile(string path) => File.ReadAllText(path, Encoding.UTF8);

    public void WriteFile(string path, string content)
    {
        EnsureDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
    {
        EnsureDirectory(Path.GetDirectoryName(destinationPath) ?? string.Empty);
        File.Copy(sourcePath, destinationPath, overwrite);
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void AppendLine(string path, string line)
    {
        EnsureDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        File.AppendAllText(path, line, Encoding.UTF8);
    }

    public IReadOnlyList<string> ListFiles(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        return Directory.GetFiles(directoryPath, searchPattern, searchOption);
    }

    private static string ResolveDataRoot(string? configuredValue, string contentRootPath)
    {
        var value = string.IsNullOrWhiteSpace(configuredValue)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SchoolMathTrainer",
                "api-data")
            : configuredValue.Trim().Trim('"');

        var expanded = Environment.ExpandEnvironmentVariables(value);
        var fullPath = Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(contentRootPath, expanded);

        return Path.GetFullPath(fullPath);
    }

    private static string NormalizeSegment(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            Path.IsPathRooted(trimmed) ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains(Path.DirectorySeparatorChar) ||
            trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Data segment is not valid.", nameof(value));
        }

        return trimmed;
    }
}

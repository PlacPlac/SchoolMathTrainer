using System.Text;

namespace SharedCore.Services;

public sealed class OnlineDataService : IOnlineDataService
{
    private const string OnlineDataRootEnvironmentVariable = "SCHOOLMATH_ONLINE_DATA_ROOT";

    public string ResolveClassDataRoot(string classId)
    {
        var safeClassId = NormalizeSegment(classId);
        var root = Environment.GetEnvironmentVariable(OnlineDataRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SchoolMathTrainer",
                "online-data");
        }

        return Path.Combine(Path.GetFullPath(root), safeClassId);
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

    private static string NormalizeSegment(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            Path.IsPathRooted(trimmed) ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains(Path.DirectorySeparatorChar) ||
            trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Online data segment is not valid.", nameof(value));
        }

        return trimmed;
    }
}

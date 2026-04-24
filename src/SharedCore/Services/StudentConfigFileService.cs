using System.Text;
using System.Text.Json;
using SharedCore.Models;

namespace SharedCore.Services;

public sealed class StudentConfigFileService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void SaveConfigFile(string path, string classId, string studentId, string apiBaseUrl = DataConnectionSettings.DefaultApiBaseUrl)
    {
        var config = new StudentConfigExport
        {
            Version = 1,
            ClassId = classId.Trim(),
            StudentId = studentId.Trim(),
            ApiBaseUrl = ResolveApiBaseUrl(apiBaseUrl)
        };

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        File.WriteAllText(path, JsonSerializer.Serialize(config, SerializerOptions), Encoding.UTF8);
    }

    private static string ResolveApiBaseUrl(string apiBaseUrl)
    {
        return DataConnectionSettings.NormalizeClientApiBaseUrl(apiBaseUrl);
    }

    private sealed class StudentConfigExport
    {
        public int Version { get; init; }
        public string ClassId { get; init; } = string.Empty;
        public string StudentId { get; init; } = string.Empty;
        public string ApiBaseUrl { get; init; } = string.Empty;
    }
}

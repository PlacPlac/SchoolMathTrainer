using System.Text;
using System.Text.Json;

namespace TeacherApp.Settings;

public sealed class TeacherServerSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public string SettingsFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SchoolMathTrainer",
        "teacher-server-settings.json");

    public TeacherServerSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return TeacherServerSettings.CreateDefault();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<TeacherServerSettings>(
                File.ReadAllText(SettingsFilePath, Encoding.UTF8),
                SerializerOptions);

            return Normalize(settings ?? TeacherServerSettings.CreateDefault());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return TeacherServerSettings.CreateDefault();
        }
    }

    public void Save(TeacherServerSettings settings)
    {
        var normalized = Normalize(settings);
        normalized.SavedAtUtc = DateTime.UtcNow;

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
        File.WriteAllText(
            SettingsFilePath,
            JsonSerializer.Serialize(normalized, SerializerOptions),
            Encoding.UTF8);
    }

    private static TeacherServerSettings Normalize(TeacherServerSettings settings)
    {
        var host = string.IsNullOrWhiteSpace(settings.Host)
            ? TeacherServerSettings.DefaultHost
            : settings.Host.Trim();
        var port = settings.Port is > 0 and <= 65535
            ? settings.Port
            : TeacherServerSettings.DefaultPort;
        var username = string.IsNullOrWhiteSpace(settings.Username)
            ? TeacherServerSettings.DefaultUsername
            : settings.Username.Trim();
        var remoteDataPath = string.IsNullOrWhiteSpace(settings.RemoteDataPath)
            ? TeacherServerSettings.DefaultRemoteDataPath
            : settings.RemoteDataPath.Trim();

        return new TeacherServerSettings
        {
            Host = host,
            Port = port,
            Username = username,
            RemoteDataPath = remoteDataPath,
            SavedAtUtc = settings.SavedAtUtc
        };
    }
}

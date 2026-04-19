using System.Text;
using System.Text.Json;
using SharedCore.Models;

namespace SharedCore.Services;

public sealed class ConfigurationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AppConfiguration LoadFromFile(string fileName, bool useSharedDataFolderOverride = true)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Configuration file was not found: {path}");
        }

        var configuration = JsonSerializer.Deserialize<AppConfiguration>(File.ReadAllText(path, Encoding.UTF8), SerializerOptions)
            ?? new AppConfiguration();

        configuration.SharedDataRoot = ResolvePath(configuration.SharedDataRoot);
        var sharedDataFolderSetting = useSharedDataFolderOverride
            ? new SharedDataFolderSettingsService().Load()
            : null;
        if (sharedDataFolderSetting?.IsValid == true)
        {
            configuration.SharedDataRoot = sharedDataFolderSetting.DataFolderPath;
        }

        if (string.IsNullOrWhiteSpace(configuration.SharedDataRoot))
        {
            configuration.SharedDataRoot = AppContext.BaseDirectory;
        }

        configuration.StudentDataDirectory = ResolvePathOrDefault(configuration.StudentDataDirectory, configuration.SharedDataRoot, "Data", "Students");
        configuration.SessionDataDirectory = ResolvePathOrDefault(configuration.SessionDataDirectory, configuration.SharedDataRoot, "Data", "Sessions");
        configuration.StudentResultsDirectory = ResolvePathOrDefault(configuration.StudentResultsDirectory, configuration.SharedDataRoot, "Data", "StudentResults");
        configuration.StudentAccountFilePath = ResolvePathOrDefault(configuration.StudentAccountFilePath, configuration.SharedDataRoot, "Config", "student-accounts.json");
        configuration.PublicClassOverviewFilePath = ResolvePathOrDefault(configuration.PublicClassOverviewFilePath, configuration.SharedDataRoot, "Data", "Public", "class-overview.json");
        configuration.ExportDirectory = ResolvePathOrDefault(configuration.ExportDirectory, configuration.SharedDataRoot, "Data", "Exports");
        configuration.ConfigDirectory = ResolvePathOrDefault(configuration.ConfigDirectory, configuration.SharedDataRoot, "Config");
        configuration.LogDirectory = ResolvePathOrDefault(configuration.LogDirectory, configuration.SharedDataRoot, "Logs");

        return configuration;
    }

    private static string ResolvePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, value));
    }

    private static string ResolvePathOrDefault(string configuredValue, string root, params string[] segments)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return ResolvePath(configuredValue);
        }

        return Path.Combine(root, Path.Combine(segments));
    }
}

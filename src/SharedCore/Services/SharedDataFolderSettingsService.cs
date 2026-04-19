using System.Text;
using System.Text.Json;
using SharedCore.Models;

namespace SharedCore.Services;

public sealed class SharedDataFolderSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IOnlineDataService _onlineDataService;

    public SharedDataFolderSettingsService()
        : this(new OnlineDataService())
    {
    }

    public SharedDataFolderSettingsService(IOnlineDataService onlineDataService)
    {
        _onlineDataService = onlineDataService;
    }

    public string SettingsFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SchoolMathTrainer",
        "shared-data-folder.json");

    public SharedDataFolderSettingsResult Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new SharedDataFolderSettingsResult
            {
                HasSetting = false,
                IsValid = false,
                Message = "Společné nastavení dat není nastaveno."
            };
        }

        try
        {
            var settings = JsonSerializer.Deserialize<SharedDataFolderSettings>(
                File.ReadAllText(SettingsFilePath, Encoding.UTF8),
                SerializerOptions);
            var configuredPath = settings?.DataFolderPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return CreateInvalidResult(
                    "Společná datová složka není ve sdíleném nastavení vyplněna.",
                    string.Empty,
                    settings?.ClassId,
                    settings?.ClassFolderName,
                    settings?.StudentId,
                    settings?.ApiBaseUrl);
            }

            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"')));
            if (!Directory.Exists(fullPath))
            {
                return CreateInvalidResult(
                    $"Společná datová složka neexistuje: {fullPath}",
                    fullPath,
                    settings?.ClassId,
                    settings?.ClassFolderName,
                    settings?.StudentId,
                    settings?.ApiBaseUrl);
            }

            return new SharedDataFolderSettingsResult
            {
                HasSetting = true,
                IsValid = true,
                DataFolderPath = fullPath,
                ClassId = settings?.ClassId ?? string.Empty,
                ClassFolderName = settings?.ClassFolderName ?? Path.GetFileName(fullPath),
                StudentId = settings?.StudentId ?? string.Empty,
                ApiBaseUrl = settings?.ApiBaseUrl ?? string.Empty,
                IsStudentConfigurationImported = settings?.IsStudentConfigurationImported == true,
                Message = $"Používá se společná datová složka: {fullPath}"
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return CreateInvalidResult("Sdílené nastavení datové složky nejde bezpečně načíst.", string.Empty);
        }
    }

    public void Save(string dataFolderPath)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dataFolderPath.Trim().Trim('"')));
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Data folder was not found: {fullPath}");
        }

        var classId = Path.GetFileName(fullPath);
        SaveSettings(new SharedDataFolderSettings
        {
            Version = 1,
            ClassId = classId,
            ClassFolderName = classId,
            DataFolderPath = fullPath
        });
    }

    public SharedDataFolderSettingsResult ImportFromFile(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return CreateInvalidResult("Konfigurační soubor se nepodařilo načíst.", string.Empty);
            }

            if (!string.Equals(Path.GetExtension(filePath), ".smtcfg", StringComparison.OrdinalIgnoreCase))
            {
                return CreateInvalidResult("Konfigurační soubor musí mít příponu .smtcfg.", string.Empty);
            }

            var settings = JsonSerializer.Deserialize<SharedDataFolderSettings>(
                File.ReadAllText(filePath, Encoding.UTF8),
                SerializerOptions);
            if (settings is null)
            {
                return CreateInvalidResult("Konfigurační soubor není platný.", string.Empty);
            }

            var studentId = settings.StudentId.Trim();
            if (settings.Version != 1 || string.IsNullOrWhiteSpace(studentId))
            {
                return CreateInvalidResult("Konfigurační soubor není platný.", string.Empty);
            }

            if (!string.IsNullOrWhiteSpace(settings.DataFolderPath))
            {
                SaveImportedStudentSettings(settings.DataFolderPath, settings.ClassId, settings.ClassFolderName, studentId, settings.ApiBaseUrl);
                return Load();
            }

            var classId = settings.ClassId.Trim();
            if (IsSafeConfigSegment(classId))
            {
                SaveOnlineSettings(classId, studentId, settings.ApiBaseUrl);
                return Load();
            }

            var legacyClassFolderName = settings.ClassFolderName.Trim();
            if (IsSafeConfigSegment(legacyClassFolderName))
            {
                SaveOnlineSettings(legacyClassFolderName, studentId, settings.ApiBaseUrl);
                return Load();
            }

            return CreateInvalidResult("Konfigurační soubor není platný.", string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return CreateInvalidResult("Konfigurační soubor není platný.", string.Empty);
        }
    }

    private void SaveOnlineSettings(string classId, string studentId, string apiBaseUrl)
    {
        var dataFolderPath = _onlineDataService.ResolveClassDataRoot(classId);
        _onlineDataService.EnsureDirectory(dataFolderPath);
        SaveSettings(new SharedDataFolderSettings
        {
            Version = 1,
            ClassId = classId,
            ClassFolderName = classId,
            StudentId = studentId,
            ApiBaseUrl = ResolveApiBaseUrl(apiBaseUrl),
            DataFolderPath = dataFolderPath,
            IsStudentConfigurationImported = true
        });
    }

    private void SaveImportedStudentSettings(
        string dataFolderPath,
        string classId,
        string classFolderName,
        string studentId,
        string apiBaseUrl)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dataFolderPath.Trim().Trim('"')));
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Data folder was not found: {fullPath}");
        }

        var resolvedClassId = string.IsNullOrWhiteSpace(classId)
            ? Path.GetFileName(fullPath)
            : classId.Trim();
        var resolvedClassFolderName = string.IsNullOrWhiteSpace(classFolderName)
            ? resolvedClassId
            : classFolderName.Trim();
        SaveSettings(new SharedDataFolderSettings
        {
            Version = 1,
            ClassId = resolvedClassId,
            ClassFolderName = resolvedClassFolderName,
            StudentId = studentId,
            ApiBaseUrl = ResolveApiBaseUrl(apiBaseUrl),
            DataFolderPath = fullPath,
            IsStudentConfigurationImported = true
        });
    }

    private void SaveSettings(SharedDataFolderSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, SerializerOptions), Encoding.UTF8);
    }

    private static bool IsSafeConfigSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
        {
            return false;
        }

        return value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            !value.Contains(Path.DirectorySeparatorChar) &&
            !value.Contains(Path.AltDirectorySeparatorChar);
    }

    private static string ResolveApiBaseUrl(string apiBaseUrl)
    {
        return string.IsNullOrWhiteSpace(apiBaseUrl)
            ? DataConnectionSettings.DefaultApiBaseUrl
            : apiBaseUrl.Trim();
    }

    private static SharedDataFolderSettingsResult CreateInvalidResult(
        string message,
        string dataFolderPath,
        string? classId = null,
        string? classFolderName = null,
        string? studentId = null,
        string? apiBaseUrl = null)
    {
        return new SharedDataFolderSettingsResult
        {
            HasSetting = true,
            IsValid = false,
            DataFolderPath = dataFolderPath,
            ClassId = classId ?? string.Empty,
            ClassFolderName = classFolderName ?? string.Empty,
            StudentId = studentId ?? string.Empty,
            ApiBaseUrl = apiBaseUrl ?? string.Empty,
            IsStudentConfigurationImported = false,
            Message = message
        };
    }
}

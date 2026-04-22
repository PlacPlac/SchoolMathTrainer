using System.Text;
using System.Text.Json;
using SharedCore.Models;

namespace TeacherApp.Data;

public sealed class TeacherDataFolderValidator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DataFolderValidationResult Validate(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return CreateResult(
                DataFolderValidationStatus.EmptyPath,
                string.Empty,
                "Cesta ke složce dat není vyplněna.");
        }

        var normalizedPath = NormalizePath(folderPath);
        if (normalizedPath is null)
        {
            return CreateResult(
                DataFolderValidationStatus.DirectoryNotFound,
                folderPath.Trim(),
                "Zadaná cesta není platná cesta ke složce.");
        }

        if (!Directory.Exists(normalizedPath))
        {
            return CreateResult(
                DataFolderValidationStatus.DirectoryNotFound,
                normalizedPath,
                "Složka neexistuje. Zkontrolujte cestu k lokálně synchronizované datové složce.");
        }

        try
        {
            return ValidateExistingDirectory(normalizedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CreateResult(
                DataFolderValidationStatus.UnrecognizedData,
                normalizedPath,
                "Složku se nepodařilo bezpečně přečíst. Zkontrolujte oprávnění a dostupnost souborů.");
        }
    }

    private static DataFolderValidationResult ValidateExistingDirectory(string rootPath)
    {
        var configDirectory = Path.Combine(rootPath, "Config");
        var dataDirectory = Path.Combine(rootPath, "Data");
        var studentDataDirectory = Path.Combine(dataDirectory, "Students");
        var sessionDataDirectory = Path.Combine(dataDirectory, "Sessions");
        var studentResultsDirectory = Path.Combine(dataDirectory, "StudentResults");
        var publicDataDirectory = Path.Combine(dataDirectory, "Public");

        var studentAccountFilePath = Path.Combine(configDirectory, "student-accounts.json");
        var classOverviewFilePath = Path.Combine(publicDataDirectory, "class-overview.json");

        var hasExpectedStructure =
            Directory.Exists(configDirectory) ||
            Directory.Exists(dataDirectory) ||
            Directory.Exists(studentDataDirectory) ||
            Directory.Exists(sessionDataDirectory) ||
            Directory.Exists(studentResultsDirectory) ||
            Directory.Exists(publicDataDirectory);

        if (File.Exists(studentAccountFilePath))
        {
            return TryLoadJson<List<StudentAccount>>(studentAccountFilePath, rootPath, "student-accounts.json");
        }

        if (File.Exists(classOverviewFilePath))
        {
            return TryLoadJson<List<ClassOverviewItem>>(classOverviewFilePath, rootPath, "class-overview.json");
        }

        if (TryFindReadableJson<StudentSummary>(studentDataDirectory, "*.json", SearchOption.TopDirectoryOnly, out var summaryError))
        {
            return CreateResult(
                DataFolderValidationStatus.RecognizedData,
                rootPath,
                "Složka existuje a byla rozpoznána podle souborů souhrnů žáků.");
        }

        if (TryFindReadableJson<StudentSummary>(studentResultsDirectory, "summary.json", SearchOption.AllDirectories, out var resultSummaryError))
        {
            return CreateResult(
                DataFolderValidationStatus.RecognizedData,
                rootPath,
                "Složka existuje a byla rozpoznána podle výsledkových souhrnů.");
        }

        var readError = summaryError ?? resultSummaryError;
        if (readError is not null)
        {
            return CreateResult(
                DataFolderValidationStatus.UnrecognizedData,
                rootPath,
                "Složka existuje, ale nalezené datové soubory nejdou bezpečně načíst.");
        }

        if (hasExpectedStructure)
        {
            return CreateResult(
                DataFolderValidationStatus.RecognizedData,
                rootPath,
                "Složka existuje a má očekávanou strukturu pro data aplikace.");
        }

        return CreateResult(
            DataFolderValidationStatus.UnrecognizedData,
            rootPath,
            "Složka existuje, ale data projektu nebyla rozpoznána.");
    }

    private static DataFolderValidationResult TryLoadJson<T>(string path, string rootPath, string fileName)
    {
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<T>(json, SerializerOptions);
            if (data is not null)
            {
                return CreateResult(
                    DataFolderValidationStatus.RecognizedData,
                    rootPath,
                    $"Složka existuje a data byla rozpoznána podle souboru {fileName}.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return CreateResult(
                DataFolderValidationStatus.UnrecognizedData,
                rootPath,
                $"Složka existuje, ale soubor {fileName} nejde bezpečně načíst.");
        }

        return CreateResult(
            DataFolderValidationStatus.UnrecognizedData,
            rootPath,
            $"Složka existuje, ale soubor {fileName} nemá očekávaný obsah.");
    }

    private static bool TryFindReadableJson<T>(
        string directoryPath,
        string searchPattern,
        SearchOption searchOption,
        out Exception? error)
    {
        error = null;
        if (!Directory.Exists(directoryPath))
        {
            return false;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(directoryPath, searchPattern, searchOption))
            {
                try
                {
                    var json = File.ReadAllText(file, Encoding.UTF8);
                    if (JsonSerializer.Deserialize<T>(json, SerializerOptions) is not null)
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
                {
                    error = ex;
                    return false;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex;
            return false;
        }

        return false;
    }

    private static string? NormalizePath(string folderPath)
    {
        try
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(folderPath.Trim().Trim('"'));
            return Path.GetFullPath(expandedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static DataFolderValidationResult CreateResult(
        DataFolderValidationStatus status,
        string folderPath,
        string message)
    {
        return new DataFolderValidationResult
        {
            Status = status,
            FolderPath = folderPath,
            Message = message
        };
    }
}

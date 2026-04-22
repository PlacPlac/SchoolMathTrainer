using System.Text;
using System.Text.Json;
using SharedCore.Models;
using SharedCore.Services;

namespace TeacherApp.Data;

public sealed class TeacherStudentAccountCreator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CreateStudentAccountResult CreateStudent(string dataFolderPath, string studentName)
    {
        if (string.IsNullOrWhiteSpace(dataFolderPath) || !Directory.Exists(dataFolderPath))
        {
            return CreateFailure("Nejdřív načtěte platnou datovou složku.");
        }

        if (string.IsNullOrWhiteSpace(studentName))
        {
            return CreateFailure("Jméno žáka je povinné.");
        }

        var accountFilePath = Path.Combine(dataFolderPath, "Config", "student-accounts.json");
        if (!ValidateAccountFile(accountFilePath, out var validationMessage))
        {
            return CreateFailure(validationMessage);
        }

        try
        {
            var progressService = CreateProgressService(dataFolderPath);
            var loginCode = progressService.CreateLoginCodeBase(studentName);
            if (!progressService.IsLoginCodeAvailable(loginCode))
            {
                loginCode = progressService.GetLoginCodeSuggestions(loginCode).FirstOrDefault() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(loginCode))
            {
                return CreateFailure("Nepodařilo se vytvořit unikátní LoginCode.");
            }

            var result = progressService.CreateStudentAccount(studentName.Trim(), loginCode);
            return new CreateStudentAccountResult
            {
                Success = true,
                Message = $"Účet byl vytvořen. LoginCode: {result.Account.LoginCode}, dočasný PIN: {result.TemporaryPin}",
                Account = result.Account,
                TemporaryPin = result.TemporaryPin
            };
        }
        catch (InvalidOperationException ex)
        {
            return CreateFailure(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or System.Security.Cryptography.CryptographicException)
        {
            return CreateFailure("Účet se nepodařilo bezpečně vytvořit. Zkontrolujte dostupnost a oprávnění datové složky.");
        }
    }

    internal static bool ValidateAccountFile(string accountFilePath, out string message)
    {
        message = string.Empty;
        if (!File.Exists(accountFilePath))
        {
            return true;
        }

        try
        {
            var json = File.ReadAllText(accountFilePath, Encoding.UTF8);
            var accounts = JsonSerializer.Deserialize<List<StudentAccount>>(json, SerializerOptions);
            if (accounts is not null)
            {
                return true;
            }

            message = "Soubor student-accounts.json nemá očekávaný obsah. Účet nebyl vytvořen.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            message = "Soubor student-accounts.json nejde bezpečně načíst. Účet nebyl vytvořen a data nebyla změněna.";
            return false;
        }
    }

    internal static StudentProgressService CreateProgressService(string dataFolderPath)
    {
        var configuration = new AppConfiguration
        {
            SharedDataRoot = dataFolderPath,
            StudentDataDirectory = Path.Combine(dataFolderPath, "Data", "Students"),
            SessionDataDirectory = Path.Combine(dataFolderPath, "Data", "Sessions"),
            StudentResultsDirectory = Path.Combine(dataFolderPath, "Data", "StudentResults"),
            StudentAccountFilePath = Path.Combine(dataFolderPath, "Config", "student-accounts.json"),
            PublicClassOverviewFilePath = Path.Combine(dataFolderPath, "Data", "Public", "class-overview.json"),
            ExportDirectory = Path.Combine(dataFolderPath, "Data", "Exports"),
            ConfigDirectory = Path.Combine(dataFolderPath, "Config"),
            LogDirectory = Path.Combine(dataFolderPath, "Logs"),
            RetryCount = 4,
            RetryDelayMilliseconds = 250
        };

        var retryFileAccessService = new RetryFileAccessService();
        var storageService = new FileSystemStorageService(
            retryFileAccessService,
            configuration.RetryCount,
            configuration.RetryDelayMilliseconds);
        var loggingService = new LoggingService(storageService, configuration);
        var statisticsService = new StatisticsService();
        var csvExportService = new CsvExportService(storageService, configuration);

        return new StudentProgressService(
            configuration,
            storageService,
            statisticsService,
            loggingService,
            csvExportService,
            canWritePublicOverview: false);
    }

    private static CreateStudentAccountResult CreateFailure(string message)
    {
        return new CreateStudentAccountResult
        {
            Success = false,
            Message = message
        };
    }
}

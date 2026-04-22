using System.Text.Json;

namespace TeacherApp.Data;

public sealed class TeacherStudentPinResetter
{
    public ResetStudentPinResult ResetPin(string dataFolderPath, string studentId)
    {
        if (string.IsNullOrWhiteSpace(dataFolderPath) || !Directory.Exists(dataFolderPath))
        {
            return CreateFailure("Nejdřív načtěte platnou datovou složku.");
        }

        if (string.IsNullOrWhiteSpace(studentId))
        {
            return CreateFailure("Nejdřív vyberte žáka.");
        }

        var accountFilePath = Path.Combine(dataFolderPath, "Config", "student-accounts.json");
        if (!TeacherStudentAccountCreator.ValidateAccountFile(accountFilePath, out var validationMessage))
        {
            return CreateFailure(validationMessage);
        }

        try
        {
            var progressService = TeacherStudentAccountCreator.CreateProgressService(dataFolderPath);
            var result = progressService.ResetStudentPin(studentId);
            if (result is null)
            {
                return CreateFailure("Žák nebyl nalezen. PIN nebyl resetován.");
            }

            return new ResetStudentPinResult
            {
                Success = true,
                Message = $"PIN byl resetován. Nový dočasný PIN: {result.TemporaryPin}",
                Account = result.Account,
                TemporaryPin = result.TemporaryPin
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or System.Security.Cryptography.CryptographicException)
        {
            return CreateFailure("PIN se nepodařilo bezpečně resetovat. Zkontrolujte dostupnost a oprávnění datové složky.");
        }
    }

    public string GetPendingTemporaryPin(string dataFolderPath, string studentId)
    {
        if (string.IsNullOrWhiteSpace(dataFolderPath) ||
            string.IsNullOrWhiteSpace(studentId) ||
            !Directory.Exists(dataFolderPath))
        {
            return string.Empty;
        }

        try
        {
            var progressService = TeacherStudentAccountCreator.CreateProgressService(dataFolderPath);
            return progressService.GetPendingTemporaryPin(studentId) ?? string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or System.Security.Cryptography.CryptographicException)
        {
            return string.Empty;
        }
    }

    private static ResetStudentPinResult CreateFailure(string message)
    {
        return new ResetStudentPinResult
        {
            Success = false,
            Message = message
        };
    }
}

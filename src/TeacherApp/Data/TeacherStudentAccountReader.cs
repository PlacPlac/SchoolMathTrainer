using System.Text;
using System.Text.Json;
using SharedCore.Models;

namespace TeacherApp.Data;

public sealed class TeacherStudentAccountReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public StudentAccountReadResult ReadStudents(string dataFolderPath)
    {
        var accountFilePath = Path.Combine(dataFolderPath, "Config", "student-accounts.json");
        if (!File.Exists(accountFilePath))
        {
            return CreateResult(false, "Ve složce nebyl nalezen soubor student-accounts.json.", []);
        }

        try
        {
            var json = File.ReadAllText(accountFilePath, Encoding.UTF8);
            var accounts = JsonSerializer.Deserialize<List<StudentAccount>>(json, SerializerOptions);
            if (accounts is null)
            {
                return CreateResult(false, "Soubor student-accounts.json nemá očekávaný obsah.", []);
            }

            var students = accounts
                .OrderBy(account => account.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(account => new StudentListItem
                {
                    StudentId = string.IsNullOrWhiteSpace(account.StudentId)
                        ? "(není uvedeno)"
                        : account.StudentId,
                    DisplayName = string.IsNullOrWhiteSpace(account.DisplayName)
                        ? "(bez jména)"
                        : account.DisplayName,
                    LoginCode = string.IsNullOrWhiteSpace(account.LoginCode)
                        ? "(není uveden)"
                        : account.LoginCode,
                    AccountStatus = account.IsActive ? "Aktivní" : "Neaktivní",
                    MustChangePin = account.MustChangePin,
                    MustChangePinStatus = account.MustChangePin ? "Ano" : "Ne",
                    TemporaryPinPending = account.TemporaryPinPending,
                    TemporaryPinPendingStatus = account.TemporaryPinPending ? "Ano" : "Ne",
                    CreatedAtText = FormatDateTime(account.CreatedAt),
                    PinResetAtText = account.PinResetAt.HasValue ? FormatDateTime(account.PinResetAt.Value) : "Není uvedeno"
                })
                .ToList();

            if (students.Count == 0)
            {
                return CreateResult(true, "Soubor účtů byl načten, ale neobsahuje žádné žáky.", students);
            }

            return CreateResult(true, $"Seznam žáků byl načten. Počet žáků: {students.Count}.", students);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return CreateResult(false, "Soubor student-accounts.json nejde bezpečně načíst. Seznam žáků zůstává prázdný.", []);
        }
    }

    private static StudentAccountReadResult CreateResult(
        bool success,
        string message,
        IReadOnlyList<StudentListItem> students)
    {
        return new StudentAccountReadResult
        {
            Success = success,
            Message = message,
            Students = students
        };
    }

    private static string FormatDateTime(DateTime value)
    {
        if (value == default)
        {
            return "Není uvedeno";
        }

        var localTime = value.Kind == DateTimeKind.Unspecified ? value : value.ToLocalTime();
        return localTime.ToString("dd.MM.yyyy HH:mm");
    }
}

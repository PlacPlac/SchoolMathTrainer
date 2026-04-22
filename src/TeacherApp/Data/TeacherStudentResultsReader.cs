using System.Text;
using System.Globalization;
using System.Text.Json;
using SharedCore.Models;

namespace TeacherApp.Data;

public sealed class TeacherStudentResultsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public StudentResultsReadResult ReadResults(string dataFolderPath, string studentId)
    {
        if (string.IsNullOrWhiteSpace(dataFolderPath) || string.IsNullOrWhiteSpace(studentId))
        {
            return CreateEmptyResult(false, "Výsledky žáka nejsou k dispozici.");
        }

        try
        {
            var studentResultsDirectory = Path.Combine(dataFolderPath, "Data", "StudentResults", studentId);
            var summary = ReadSummary(Path.Combine(studentResultsDirectory, "summary.json"));
            var sessions = ReadSessions(dataFolderPath, studentId);

            if (summary is null && sessions.Count == 0)
            {
                return CreateEmptyResult(true, "Výsledky žáka nejsou k dispozici.");
            }

            return CreateResult(summary, sessions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return CreateEmptyResult(false, "Výsledky žáka nejdou bezpečně načíst. Sekce výsledků zůstává prázdná.");
        }
    }

    private static StudentSummary? ReadSummary(string summaryPath)
    {
        if (!File.Exists(summaryPath))
        {
            return null;
        }

        var json = File.ReadAllText(summaryPath, Encoding.UTF8);
        return JsonSerializer.Deserialize<StudentSummary>(json, SerializerOptions);
    }

    private static List<StudentSession> ReadSessions(string dataFolderPath, string studentId)
    {
        var sessions = new List<StudentSession>();
        var legacySessionsDirectory = Path.Combine(dataFolderPath, "Data", "Sessions");
        var studentSessionsDirectory = Path.Combine(dataFolderPath, "Data", "StudentResults", studentId, "Sessions");

        sessions.AddRange(ReadSessionFiles(legacySessionsDirectory, studentId, SearchOption.TopDirectoryOnly));
        sessions.AddRange(ReadSessionFiles(studentSessionsDirectory, studentId, SearchOption.TopDirectoryOnly));

        return sessions
            .Where(session => !string.IsNullOrWhiteSpace(session.SessionId))
            .GroupBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(session => session.CompletedAt).First())
            .OrderBy(session => session.CompletedAt)
            .ToList();
    }

    private static IEnumerable<StudentSession> ReadSessionFiles(
        string directoryPath,
        string studentId,
        SearchOption searchOption)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        var sessions = new List<StudentSession>();
        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.json", searchOption))
        {
            var json = File.ReadAllText(file, Encoding.UTF8);
            var session = JsonSerializer.Deserialize<StudentSession>(json, SerializerOptions);
            if (session is not null &&
                string.Equals(session.StudentId, studentId, StringComparison.OrdinalIgnoreCase))
            {
                sessions.Add(session);
            }
        }

        return sessions;
    }

    private static StudentResultsReadResult CreateResult(
        StudentSummary? summary,
        IReadOnlyList<StudentSession> sessions)
    {
        var totalAttempts = sessions.Count > 0
            ? sessions.Sum(GetAttemptCount)
            : summary?.TotalAnswers;
        var correctAnswers = sessions.Count > 0
            ? sessions.Sum(GetCorrectCount)
            : summary?.CorrectAnswers;
        var incorrectAnswers = sessions.Count > 0
            ? sessions.Sum(GetWrongCount)
            : summary?.IncorrectAnswers;
        var lastActivity = sessions.Count > 0
            ? sessions.Max(session => session.LastActivityUtc)
            : summary?.LastSessionAt;
        var sessionCount = sessions.Count > 0
            ? sessions.Count
            : summary?.SessionsCompleted;

        var accuracyText = totalAttempts is > 0 && correctAnswers.HasValue
            ? FormatPercent(correctAnswers.Value * 100d / totalAttempts.Value)
            : "Nelze určit z dat";

        var recentActivities = sessions
            .OrderByDescending(session => session.LastActivityUtc)
            .Take(5)
            .Select(session => new StudentActivityListItem
            {
                CompletedAtText = FormatDateTime(session.LastActivityUtc),
                ModeText = GetModeText(session.Mode),
                ResultText = $"{GetCorrectCount(session)} správně, {GetWrongCount(session)} chyb, {GetAttemptCount(session)} pokusů"
            })
            .ToList();

            return new StudentResultsReadResult
        {
            Success = true,
            HasResults = true,
            Message = "Výsledky žáka byly načteny pouze pro čtení.",
            SessionCount = sessionCount,
            AttemptCount = totalAttempts,
            CorrectAnswers = correctAnswers,
            IncorrectAnswers = incorrectAnswers,
            AccuracyPercent = totalAttempts is > 0 && correctAnswers.HasValue
                ? correctAnswers.Value * 100d / totalAttempts.Value
                : null,
            LastActivity = lastActivity,
            SessionCountText = FormatNumber(sessionCount),
            AttemptCountText = FormatNumber(totalAttempts),
            LastActivityText = FormatDateTime(lastActivity),
            CorrectAnswersText = FormatNumber(correctAnswers),
            IncorrectAnswersText = FormatNumber(incorrectAnswers),
            AccuracyText = accuracyText,
            ModeOverviewText = BuildModeOverview(sessions, summary),
            ModeResults = BuildModeResults(sessions, summary),
            RecentActivities = recentActivities
        };
    }

    private static IReadOnlyList<StudentModeResultItem> BuildModeResults(
        IReadOnlyList<StudentSession> sessions,
        StudentSummary? summary)
    {
        if (sessions.Count > 0)
        {
            return sessions
                .GroupBy(session => session.Mode)
                .OrderBy(group => group.Key)
                .Select(group => new StudentModeResultItem
                {
                    ModeText = GetModeText(group.Key),
                    Attempts = group.Sum(GetAttemptCount),
                    CorrectAnswers = group.Sum(GetCorrectCount),
                    IncorrectAnswers = group.Sum(GetWrongCount)
                })
                .ToList();
        }

        if (summary is null)
        {
            return [];
        }

        return [];
    }

    private static string BuildModeOverview(IReadOnlyList<StudentSession> sessions, StudentSummary? summary)
    {
        if (sessions.Count > 0)
        {
            var lines = sessions
                .GroupBy(session => session.Mode)
                .OrderBy(group => group.Key)
                .Select(group =>
                {
                    var attempts = group.Sum(GetAttemptCount);
                    var correct = group.Sum(GetCorrectCount);
                    var wrong = group.Sum(GetWrongCount);
                    var accuracy = attempts > 0 ? FormatPercent(correct * 100d / attempts) : "Nelze určit z dat";
                    return $"{GetModeText(group.Key)}: {attempts} pokusů, {correct} správně, {wrong} chyb, úspěšnost {accuracy}";
                })
                .ToList();

            return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "Nelze určit z dat";
        }

        if (summary is not null && (summary.BeginnerAnswers > 0 || summary.AdvancedAnswers > 0))
        {
            return string.Join(
                Environment.NewLine,
                $"Začátečník: {summary.BeginnerAnswers} pokusů, úspěšnost {FormatPercent(summary.BeginnerAccuracyPercent)}",
                $"Pokročilý: {summary.AdvancedAnswers} pokusů, úspěšnost {FormatPercent(summary.AdvancedAccuracyPercent)}");
        }

        return "Nelze určit z dat";
    }

    private static int GetAttemptCount(StudentSession session)
    {
        return session.Answers.Count > 0 ? session.Answers.Count : session.RunningTotalCount;
    }

    private static int GetCorrectCount(StudentSession session)
    {
        return session.Answers.Count > 0
            ? session.Answers.Count(answer => answer.IsCorrect)
            : session.RunningCorrectCount;
    }

    private static int GetWrongCount(StudentSession session)
    {
        return session.Answers.Count > 0
            ? session.Answers.Count(answer => !answer.IsCorrect)
            : session.RunningWrongCount;
    }

    private static string GetModeText(LearningMode mode)
    {
        return mode switch
        {
            LearningMode.Beginner => "Začátečník",
            LearningMode.Advanced => "Pokročilý",
            _ => "Nelze určit z dat"
        };
    }

    private static string FormatNumber(int? value)
    {
        return value.HasValue
            ? value.Value.ToString(CultureInfo.GetCultureInfo("cs-CZ"))
            : "Nelze určit z dat";
    }

    private static string FormatPercent(double value)
    {
        return $"{Math.Round(value, 1).ToString("0.0", CultureInfo.GetCultureInfo("cs-CZ"))} %";
    }

    private static string FormatDateTime(DateTime? value)
    {
        if (!value.HasValue || value.Value == default)
        {
            return "Nelze určit z dat";
        }

        var localTime = value.Value.Kind == DateTimeKind.Unspecified ? value.Value : value.Value.ToLocalTime();
        return localTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("cs-CZ"));
    }

    private static StudentResultsReadResult CreateEmptyResult(bool success, string message)
    {
        return new StudentResultsReadResult
        {
            Success = success,
            HasResults = false,
            Message = message
        };
    }
}

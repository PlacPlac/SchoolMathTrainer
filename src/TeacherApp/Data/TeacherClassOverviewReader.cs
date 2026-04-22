using System.Globalization;

namespace TeacherApp.Data;

public sealed class TeacherClassOverviewReader
{
    private readonly TeacherStudentResultsReader _studentResultsReader = new();

    public ClassOverviewReadResult ReadClassOverview(string dataFolderPath, IReadOnlyList<StudentListItem> students)
    {
        if (students.Count == 0)
        {
            return new ClassOverviewReadResult
            {
                Message = "Třídní přehled není k dispozici. Nejsou načtení žádní žáci."
            };
        }

        var classStudents = new List<ClassOverviewStudentItem>();
        var modeTotals = new Dictionary<string, (int Attempts, int CorrectAnswers, int IncorrectAnswers)>();
        var studentsWithResults = 0;
        var readErrors = 0;
        var sessionTotal = 0;
        var attemptTotal = 0;
        var correctTotal = 0;
        var incorrectTotal = 0;
        DateTime? lastActivity = null;

        foreach (var student in students)
        {
            var result = _studentResultsReader.ReadResults(dataFolderPath, student.StudentId);
            if (!result.Success)
            {
                readErrors++;
            }

            if (result.HasResults)
            {
                studentsWithResults++;
                sessionTotal += result.SessionCount ?? 0;
                attemptTotal += result.AttemptCount ?? 0;
                correctTotal += result.CorrectAnswers ?? 0;
                incorrectTotal += result.IncorrectAnswers ?? 0;

                if (result.LastActivity.HasValue &&
                    (!lastActivity.HasValue || result.LastActivity.Value > lastActivity.Value))
                {
                    lastActivity = result.LastActivity.Value;
                }

                foreach (var mode in result.ModeResults)
                {
                    var totals = modeTotals.TryGetValue(mode.ModeText, out (int Attempts, int CorrectAnswers, int IncorrectAnswers) existing)
                        ? existing
                        : (0, 0, 0);

                    modeTotals[mode.ModeText] = (
                        totals.Item1 + mode.Attempts,
                        totals.Item2 + mode.CorrectAnswers,
                        totals.Item3 + mode.IncorrectAnswers);
                }
            }

            classStudents.Add(new ClassOverviewStudentItem
            {
                DisplayName = student.DisplayName,
                LoginCode = student.LoginCode,
                ResultsStatus = !result.Success
                    ? "Data nelze načíst"
                    : result.HasResults ? "Výsledky existují" : "Bez výsledků",
                SessionCountText = result.SessionCountText,
                AttemptCountText = result.AttemptCountText,
                LastActivityText = result.LastActivityText,
                AccuracyText = result.AccuracyText
            });
        }

        var studentsWithoutResults = students.Count - studentsWithResults;
        var message = readErrors > 0
            ? $"Třídní přehled byl načten. Některá poškozená nebo nečitelná data byla bezpečně přeskočena: {readErrors}."
            : "Třídní přehled byl načten pouze pro čtení.";

        return new ClassOverviewReadResult
        {
            Message = message,
            LoadedStudentsText = FormatNumber(students.Count),
            StudentsWithResultsText = FormatNumber(studentsWithResults),
            StudentsWithoutResultsText = FormatNumber(studentsWithoutResults),
            SessionCountText = studentsWithResults > 0 ? FormatNumber(sessionTotal) : "Nelze určit z dat",
            AttemptCountText = studentsWithResults > 0 ? FormatNumber(attemptTotal) : "Nelze určit z dat",
            CorrectAnswersText = studentsWithResults > 0 ? FormatNumber(correctTotal) : "Nelze určit z dat",
            IncorrectAnswersText = studentsWithResults > 0 ? FormatNumber(incorrectTotal) : "Nelze určit z dat",
            AccuracyText = attemptTotal > 0 ? FormatPercent(correctTotal * 100d / attemptTotal) : "Nelze určit z dat",
            LastActivityText = FormatDateTime(lastActivity),
            ModeOverviewText = BuildModeOverview(modeTotals),
            Students = classStudents
        };
    }

    private static string BuildModeOverview(
        IReadOnlyDictionary<string, (int Attempts, int CorrectAnswers, int IncorrectAnswers)> modeTotals)
    {
        if (modeTotals.Count == 0)
        {
            return "Nelze určit z dat";
        }

        return string.Join(
            Environment.NewLine,
            modeTotals
                .OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(item =>
                {
                    var accuracy = item.Value.Attempts > 0
                        ? FormatPercent(item.Value.CorrectAnswers * 100d / item.Value.Attempts)
                        : "Nelze určit z dat";
                    return $"{item.Key}: {item.Value.Attempts} pokusů, {item.Value.CorrectAnswers} správně, {item.Value.IncorrectAnswers} chyb, úspěšnost {accuracy}";
                }));
    }

    private static string FormatNumber(int value)
    {
        return value.ToString(CultureInfo.GetCultureInfo("cs-CZ"));
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
}

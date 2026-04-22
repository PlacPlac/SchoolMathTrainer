namespace TeacherApp.Data;

public sealed class StudentResultsReadResult
{
    public bool Success { get; init; }
    public bool HasResults { get; init; }
    public string Message { get; init; } = string.Empty;
    public int? SessionCount { get; init; }
    public int? AttemptCount { get; init; }
    public int? CorrectAnswers { get; init; }
    public int? IncorrectAnswers { get; init; }
    public double? AccuracyPercent { get; init; }
    public DateTime? LastActivity { get; init; }
    public string SessionCountText { get; init; } = "Nelze určit z dat";
    public string AttemptCountText { get; init; } = "Nelze určit z dat";
    public string LastActivityText { get; init; } = "Nelze určit z dat";
    public string CorrectAnswersText { get; init; } = "Nelze určit z dat";
    public string IncorrectAnswersText { get; init; } = "Nelze určit z dat";
    public string AccuracyText { get; init; } = "Nelze určit z dat";
    public string ModeOverviewText { get; init; } = "Nelze určit z dat";
    public IReadOnlyList<StudentModeResultItem> ModeResults { get; init; } = [];
    public IReadOnlyList<StudentActivityListItem> RecentActivities { get; init; } = [];
}

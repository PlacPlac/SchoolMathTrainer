namespace TeacherApp.Data;

public sealed class ClassOverviewReadResult
{
    public string Message { get; init; } = string.Empty;
    public string LoadedStudentsText { get; init; } = "0";
    public string StudentsWithResultsText { get; init; } = "0";
    public string StudentsWithoutResultsText { get; init; } = "0";
    public string SessionCountText { get; init; } = "Nelze určit z dat";
    public string AttemptCountText { get; init; } = "Nelze určit z dat";
    public string CorrectAnswersText { get; init; } = "Nelze určit z dat";
    public string IncorrectAnswersText { get; init; } = "Nelze určit z dat";
    public string AccuracyText { get; init; } = "Nelze určit z dat";
    public string LastActivityText { get; init; } = "Nelze určit z dat";
    public string ModeOverviewText { get; init; } = "Nelze určit z dat";
    public IReadOnlyList<ClassOverviewStudentItem> Students { get; init; } = [];
}

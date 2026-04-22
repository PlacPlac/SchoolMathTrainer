namespace TeacherApp.Data;

public sealed class ClassOverviewStudentItem
{
    public string DisplayName { get; init; } = string.Empty;
    public string LoginCode { get; init; } = string.Empty;
    public string ResultsStatus { get; init; } = string.Empty;
    public string SessionCountText { get; init; } = "Nelze určit z dat";
    public string AttemptCountText { get; init; } = "Nelze určit z dat";
    public string LastActivityText { get; init; } = "Nelze určit z dat";
    public string AccuracyText { get; init; } = "Nelze určit z dat";
}

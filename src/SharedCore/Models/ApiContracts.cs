namespace SharedCore.Models;

public sealed record HealthResponse(
    string Status,
    string Service,
    DateTime Utc);

public sealed record ApiMessageResponse(string Message);

public sealed record ClassStudentsResponse(
    string ClassId,
    IReadOnlyList<StudentProfileResponse> Students,
    string? Message = null);

public sealed record StudentProfileResponse(
    string StudentId,
    string DisplayName,
    string LoginCode,
    bool IsActive,
    bool MustChangePin,
    bool TemporaryPinPending,
    StudentSummaryResponse? Summary,
    string PendingTemporaryPin = "");

public sealed record StudentSummaryResponse(
    int TotalAnswers,
    int CorrectAnswers,
    int IncorrectAnswers,
    double AccuracyPercent,
    int SessionsCompleted,
    DateTime? LastSessionAt);

public sealed record SaveStudentResultResponse(
    string Message,
    string SessionId);

public sealed record CreateStudentRequest(string DisplayName);

public sealed record TeacherStudentChangeResponse(
    bool Success,
    string Message,
    StudentProfileResponse? Student,
    string TemporaryPin = "",
    bool ResultsDeleted = false);

public sealed record ClassOverviewResponse(
    string ClassId,
    IReadOnlyList<ClassOverviewItem> Students,
    string? Message = null);

public sealed record StudentResultDetailResponse(
    string ClassId,
    StudentProfileResponse Student,
    StudentSummaryResponse? Summary,
    IReadOnlyList<StudentSessionSummaryResponse> RecentSessions,
    int TotalSessions,
    int TotalAnswers,
    DateTime? LastSessionAt,
    string? Message = null);

public sealed record ClassActivityResponse(
    string ClassId,
    IReadOnlyList<ClassActivityItemResponse> Activities,
    string? Message = null);

public sealed record ClassActivityItemResponse(
    string StudentId,
    string DisplayName,
    string SessionId,
    LearningMode Mode,
    DateTime StartedAt,
    DateTime CompletedAt,
    DateTime LastActivityUtc,
    int TotalAnswers,
    int CorrectAnswers,
    int IncorrectAnswers,
    double AccuracyPercent);

public sealed record StudentSessionSummaryResponse(
    string SessionId,
    LearningMode Mode,
    DateTime StartedAt,
    DateTime CompletedAt,
    DateTime LastActivityUtc,
    int TotalAnswers,
    int CorrectAnswers,
    int IncorrectAnswers,
    double AccuracyPercent);

public sealed record StudentLoginRequest(
    string LoginCode,
    string Pin,
    string NewPin,
    string StudentId = "");

public sealed class DataConnectionSettings
{
    public const string DefaultApiBaseUrl = "http://89.221.212.49";
    public const string DefaultClassId = "production";

    public ApplicationDataMode Mode { get; set; } = ApplicationDataMode.LocalFiles;
    public string ApiBaseUrl { get; set; } = DefaultApiBaseUrl;
    public string ClassId { get; set; } = DefaultClassId;

    public static string NormalizeApiBaseUrl(string? apiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return DefaultApiBaseUrl;
        }

        return apiBaseUrl.Trim();
    }
}

namespace TeacherApp.Data;

public sealed class StudentListItem
{
    public string StudentId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string LoginCode { get; init; } = string.Empty;
    public string AccountStatus { get; init; } = string.Empty;
    public bool MustChangePin { get; init; }
    public string MustChangePinStatus { get; init; } = string.Empty;
    public bool TemporaryPinPending { get; init; }
    public string TemporaryPinPendingStatus { get; init; } = string.Empty;
    public string PendingTemporaryPin { get; init; } = string.Empty;
    public string CreatedAtText { get; init; } = string.Empty;
    public string PinResetAtText { get; init; } = string.Empty;
}

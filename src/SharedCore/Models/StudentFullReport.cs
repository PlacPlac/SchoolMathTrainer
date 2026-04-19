namespace SharedCore.Models;

public sealed class StudentFullReport
{
    public StudentSummary Summary { get; set; } = new();
    public StudentProgressSnapshot Snapshot { get; set; } = new();
    public List<StudentSession> Sessions { get; set; } = [];
}

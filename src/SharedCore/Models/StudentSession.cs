using System.Globalization;

namespace SharedCore.Models;

public sealed class StudentSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public LearningMode Mode { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
    public int RunningCorrectCount { get; set; }
    public int RunningWrongCount { get; set; }
    public int RunningTotalCount { get; set; }
    public double RunningSuccessPercent { get; set; }
    public List<AnswerRecord> Answers { get; set; } = [];
    public string ModeDisplay => Mode switch
    {
        LearningMode.Beginner => "Začátečník",
        LearningMode.Advanced => "Pokročilý",
        _ => string.Empty
    };

    public string StartedAtDisplay => StartedAt.ToLocalTime().ToString("d.M.yyyy H:mm", CultureInfo.GetCultureInfo("cs-CZ"));
    public string CompletedAtDisplay => CompletedAt.ToLocalTime().ToString("d.M.yyyy H:mm", CultureInfo.GetCultureInfo("cs-CZ"));
}

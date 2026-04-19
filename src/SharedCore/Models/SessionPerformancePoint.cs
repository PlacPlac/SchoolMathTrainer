using System.Globalization;

namespace SharedCore.Models;

public sealed class SessionPerformancePoint
{
    public string SessionId { get; set; } = string.Empty;
    public LearningMode LearningMode { get; set; }
    public DateTime CompletedAt { get; set; }
    public int CorrectAnswers { get; set; }
    public int IncorrectAnswers { get; set; }
    public int TotalAnswers { get; set; }
    public double AccuracyPercent { get; set; }
    public string LearningModeDisplay => LearningMode switch
    {
        LearningMode.Beginner => "Za\u010D\u00E1te\u010Dn\u00EDk",
        LearningMode.Advanced => "Pokro\u010Dil\u00FD",
        _ => string.Empty
    };

    public string CompletedAtDisplay => CompletedAt.ToLocalTime().ToString("d.M.yyyy H:mm", CultureInfo.GetCultureInfo("cs-CZ"));
}

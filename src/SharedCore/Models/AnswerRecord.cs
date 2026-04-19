using System.Globalization;

namespace SharedCore.Models;

public sealed class AnswerRecord
{
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string SessionId { get; set; } = string.Empty;
    public LearningMode LearningMode { get; set; }
    public OperationType OperationType { get; set; }
    public string ExampleText { get; set; } = string.Empty;
    public List<int> OfferedAnswers { get; set; } = [];
    public int ChosenAnswer { get; set; }
    public int CorrectAnswer { get; set; }
    public bool IsCorrect { get; set; }
    public string InputMethod { get; set; } = string.Empty;
    public int RunningCorrectCount { get; set; }
    public int RunningWrongCount { get; set; }
    public int RunningTotalCount { get; set; }
    public double RunningSuccessPercent { get; set; }
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
    public string LearningModeDisplay => LearningMode switch
    {
        LearningMode.Beginner => "Začátečník",
        LearningMode.Advanced => "Pokročilý",
        _ => string.Empty
    };

    public string TimestampDisplay => Timestamp.ToLocalTime().ToString("d.M.yyyy H:mm", CultureInfo.GetCultureInfo("cs-CZ"));
}

namespace SharedCore.Models;

public sealed class StudentSummary
{
    public string StudentId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int TotalAnswers { get; set; }
    public int CorrectAnswers { get; set; }
    public int IncorrectAnswers { get; set; }
    public double AccuracyPercent { get; set; }
    public int SessionsCompleted { get; set; }
    public DateTime? LastSessionAt { get; set; }
    public double ImprovementTrend { get; set; }
    public double BeginnerAccuracyPercent { get; set; }
    public double AdvancedAccuracyPercent { get; set; }
    public int BeginnerAnswers { get; set; }
    public int AdvancedAnswers { get; set; }
    public List<int> AccuracyTrend { get; set; } = [];
    public List<int> BeginnerTrend { get; set; } = [];
    public List<int> AdvancedTrend { get; set; } = [];
}

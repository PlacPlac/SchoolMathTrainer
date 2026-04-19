namespace SharedCore.Models;

public sealed class StudentProgressSnapshot
{
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public int TotalAnswers { get; set; }
    public int CorrectAnswers { get; set; }
    public int IncorrectAnswers { get; set; }
    public double AccuracyPercent { get; set; }
    public double BeginnerAccuracyPercent { get; set; }
    public double AdvancedAccuracyPercent { get; set; }
    public double ImprovementPercent { get; set; }
    public DateTime? LastActivity { get; set; }
    public List<int> AccuracyTrend { get; set; } = [];
    public List<int> BeginnerTrend { get; set; } = [];
    public List<int> AdvancedTrend { get; set; } = [];
    public List<SessionPerformancePoint> SessionPerformance { get; set; } = [];
}

using System.Globalization;
using System.Text.Json.Serialization;

namespace SharedCore.Models;

public sealed class ClassOverviewItem
{
    public string StudentId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    [JsonIgnore]
    public string LoginCode { get; set; } = string.Empty;
    [JsonIgnore]
    public bool IsActive { get; set; } = true;
    public int SolvedProblems { get; set; }
    public int CorrectAnswers { get; set; }
    public int IncorrectAnswers { get; set; }
    public double AccuracyPercent { get; set; }
    public double ImprovementTrend { get; set; }
    public int SessionCount { get; set; }
    public DateTime? LastActivity { get; set; }
    public double BeginnerAccuracyPercent { get; set; }
    public double AdvancedAccuracyPercent { get; set; }
    public string LastActivityDateDisplay => LastActivity?.ToLocalTime().ToString("d.M.yyyy", CultureInfo.GetCultureInfo("cs-CZ")) ?? string.Empty;
}

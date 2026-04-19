using System.Text;
using SharedCore.Models;

namespace SharedCore.Services;

public sealed class CsvExportService
{
    private readonly FileSystemStorageService _storageService;
    private readonly AppConfiguration _configuration;

    public CsvExportService(FileSystemStorageService storageService, AppConfiguration configuration)
    {
        _storageService = storageService;
        _configuration = configuration;
        _storageService.EnsureDirectory(_configuration.ExportDirectory);
    }

    public string ExportClassOverview(IEnumerable<ClassOverviewItem> items)
    {
        var path = Path.Combine(_configuration.ExportDirectory, $"class-overview-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        var builder = new StringBuilder();
        builder.AppendLine("StudentId,DisplayName,SolvedProblems,CorrectAnswers,IncorrectAnswers,AccuracyPercent,ImprovementTrend,SessionCount,LastActivity");

        foreach (var item in items)
        {
            builder.AppendLine(string.Join(",",
                Escape(item.StudentId),
                Escape(item.DisplayName),
                item.SolvedProblems,
                item.CorrectAnswers,
                item.IncorrectAnswers,
                item.AccuracyPercent,
                item.ImprovementTrend,
                item.SessionCount,
                item.LastActivity?.ToString("O") ?? string.Empty));
        }

        _storageService.WriteText(path, builder.ToString());
        return path;
    }

    public string ExportStudentDetail(StudentFullReport report)
    {
        var path = Path.Combine(_configuration.ExportDirectory, $"student-detail-{report.Summary.StudentId}-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        var builder = new StringBuilder();
        builder.AppendLine("StudentId,StudentName,SessionId,Mode,Timestamp,ExampleText,ChosenAnswer,CorrectAnswer,IsCorrect,InputMethod,RunningSuccessPercent");

        foreach (var session in report.Sessions.OrderBy(session => session.CompletedAt))
        {
            foreach (var answer in session.Answers.OrderBy(answer => answer.Timestamp))
            {
                builder.AppendLine(string.Join(",",
                    Escape(report.Summary.StudentId),
                    Escape(report.Summary.DisplayName),
                    Escape(session.SessionId),
                    session.Mode,
                    answer.Timestamp.ToString("O"),
                    Escape(answer.ExampleText),
                    answer.ChosenAnswer,
                    answer.CorrectAnswer,
                    answer.IsCorrect,
                    Escape(answer.InputMethod),
                    answer.RunningSuccessPercent));
            }
        }

        _storageService.WriteText(path, builder.ToString());
        return path;
    }

    public string ExportTrends(IEnumerable<ClassOverviewItem> items)
    {
        var path = Path.Combine(_configuration.ExportDirectory, $"trend-overview-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        var builder = new StringBuilder();
        builder.AppendLine("StudentId,DisplayName,AccuracyPercent,ImprovementTrend,BeginnerAccuracyPercent,AdvancedAccuracyPercent");

        foreach (var item in items)
        {
            builder.AppendLine(string.Join(",",
                Escape(item.StudentId),
                Escape(item.DisplayName),
                item.AccuracyPercent,
                item.ImprovementTrend,
                item.BeginnerAccuracyPercent,
                item.AdvancedAccuracyPercent));
        }

        _storageService.WriteText(path, builder.ToString());
        return path;
    }

    private static string Escape(string? value)
    {
        var safeValue = value ?? string.Empty;
        return $"\"{safeValue.Replace("\"", "\"\"")}\"";
    }
}

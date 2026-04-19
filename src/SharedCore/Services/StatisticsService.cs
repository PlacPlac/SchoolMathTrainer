using SharedCore.Models;

namespace SharedCore.Services;

public sealed class StatisticsService
{
    public StudentSummary BuildStudentSummary(string studentId, string studentName, IEnumerable<StudentSession> sessions)
    {
        var sessionList = sessions.OrderBy(session => session.CompletedAt).ToList();
        var answers = sessionList.SelectMany(session => session.Answers).ToList();
        var total = answers.Count;
        var correct = answers.Count(answer => answer.IsCorrect);
        var beginnerAnswers = sessionList.Where(session => session.Mode == LearningMode.Beginner).SelectMany(session => session.Answers).ToList();
        var advancedAnswers = sessionList.Where(session => session.Mode == LearningMode.Advanced).SelectMany(session => session.Answers).ToList();
        var accuracyTrend = BuildTrend(sessionList, null);
        var beginnerTrend = BuildTrend(sessionList, LearningMode.Beginner);
        var advancedTrend = BuildTrend(sessionList, LearningMode.Advanced);

        return new StudentSummary
        {
            StudentId = studentId,
            DisplayName = studentName,
            TotalAnswers = total,
            CorrectAnswers = correct,
            IncorrectAnswers = total - correct,
            AccuracyPercent = GetPercent(correct, total),
            SessionsCompleted = sessionList.Count,
            LastSessionAt = sessionList.OrderByDescending(session => session.LastActivityUtc).FirstOrDefault()?.LastActivityUtc,
            ImprovementTrend = CalculateImprovement(accuracyTrend),
            BeginnerAccuracyPercent = GetPercent(beginnerAnswers.Count(answer => answer.IsCorrect), beginnerAnswers.Count),
            AdvancedAccuracyPercent = GetPercent(advancedAnswers.Count(answer => answer.IsCorrect), advancedAnswers.Count),
            BeginnerAnswers = beginnerAnswers.Count,
            AdvancedAnswers = advancedAnswers.Count,
            AccuracyTrend = accuracyTrend,
            BeginnerTrend = beginnerTrend,
            AdvancedTrend = advancedTrend
        };
    }

    public StudentProgressSnapshot BuildSnapshot(string studentId, string studentName, IEnumerable<StudentSession> sessions)
    {
        var sessionList = sessions.OrderBy(session => session.CompletedAt).ToList();
        var summary = BuildStudentSummary(studentId, studentName, sessionList);

        return new StudentProgressSnapshot
        {
            StudentId = summary.StudentId,
            StudentName = summary.DisplayName,
            TotalAnswers = summary.TotalAnswers,
            CorrectAnswers = summary.CorrectAnswers,
            IncorrectAnswers = summary.IncorrectAnswers,
            AccuracyPercent = summary.AccuracyPercent,
            BeginnerAccuracyPercent = summary.BeginnerAccuracyPercent,
            AdvancedAccuracyPercent = summary.AdvancedAccuracyPercent,
            ImprovementPercent = summary.ImprovementTrend,
            LastActivity = summary.LastSessionAt,
            AccuracyTrend = summary.AccuracyTrend,
            BeginnerTrend = summary.BeginnerTrend,
            AdvancedTrend = summary.AdvancedTrend,
            SessionPerformance = sessionList.Select(session =>
            {
                var total = session.Answers.Count;
                var correct = session.Answers.Count(answer => answer.IsCorrect);
                return new SessionPerformancePoint
                {
                    SessionId = session.SessionId,
                    LearningMode = session.Mode,
                    CompletedAt = session.CompletedAt,
                    CorrectAnswers = correct,
                    IncorrectAnswers = total - correct,
                    TotalAnswers = total,
                    AccuracyPercent = GetPercent(correct, total)
                };
            }).ToList()
        };
    }

    public IReadOnlyList<ClassOverviewItem> BuildClassOverview(IEnumerable<StudentSession> sessions)
    {
        return sessions
            .GroupBy(session => new { session.StudentId, session.StudentName })
            .Select(group =>
            {
                var sessionList = group.OrderBy(session => session.CompletedAt).ToList();
                var summary = BuildStudentSummary(group.Key.StudentId, group.Key.StudentName, sessionList);
                return new ClassOverviewItem
                {
                    StudentId = summary.StudentId,
                    DisplayName = summary.DisplayName,
                    SolvedProblems = summary.TotalAnswers,
                    CorrectAnswers = summary.CorrectAnswers,
                    IncorrectAnswers = summary.IncorrectAnswers,
                    AccuracyPercent = summary.AccuracyPercent,
                    ImprovementTrend = summary.ImprovementTrend,
                    SessionCount = summary.SessionsCompleted,
                    LastActivity = summary.LastSessionAt,
                    BeginnerAccuracyPercent = summary.BeginnerAccuracyPercent,
                    AdvancedAccuracyPercent = summary.AdvancedAccuracyPercent
                };
            })
            .OrderByDescending(item => item.AccuracyPercent)
            .ThenBy(item => item.DisplayName)
            .ToList();
    }

    private static List<int> BuildTrend(IEnumerable<StudentSession> sessions, LearningMode? mode)
    {
        return sessions
            .Where(session => mode is null || session.Mode == mode)
            .Select(session =>
            {
                var total = session.Answers.Count;
                var correct = session.Answers.Count(answer => answer.IsCorrect);
                return (int)Math.Round(GetPercent(correct, total));
            })
            .ToList();
    }

    private static double CalculateImprovement(IReadOnlyList<int> trend)
    {
        if (trend.Count < 2)
        {
            return 0;
        }

        return Math.Round((double)(trend[^1] - trend[0]), 1);
    }

    private static double GetPercent(int correct, int total)
    {
        return total == 0 ? 0 : Math.Round(correct * 100d / total, 1);
    }
}

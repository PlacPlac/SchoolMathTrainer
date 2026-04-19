using System.Globalization;
using System.Collections.ObjectModel;
using SharedCore.Helpers;
using SharedCore.Models;
using SharedCore.Services;

namespace StudentApp.ViewModels;

public sealed class MyResultsViewModel : BaseViewModel
{
    private readonly StudentProgressService _progressService;
    private string _studentName = string.Empty;
    private int _correctAnswers;
    private int _incorrectAnswers;
    private int _totalAnswers;
    private double _accuracyPercent;
    private double _beginnerAccuracy;
    private double _advancedAccuracy;
    private double _improvement;
    private string _lastActivity = "Bez aktivity";
    private string _beginnerTrendSummary = "A\u017E odehrajeme v\u00EDc p\u0159\u00EDklad\u016F, uk\u00E1\u017Eeme ti, jak se ti da\u0159\u00ED v za\u010D\u00E1te\u010Dn\u00EDkovi.";
    private string _advancedTrendSummary = "A\u017E odehrajeme v\u00EDc p\u0159\u00EDklad\u016F, uk\u00E1\u017Eeme ti, jak se ti da\u0159\u00ED v pokro\u010Dil\u00E9m.";
    private string _comparisonSummary = "A\u017E budeme m\u00EDt v\u00EDc v\u00FDsledk\u016F, porovn\u00E1me oba re\u017Eimy.";

    public MyResultsViewModel(StudentProgressService progressService)
    {
        _progressService = progressService;
        AccuracyTrend = new ObservableCollection<TrendBarItem>();
        SessionPerformance = new ObservableCollection<SessionPerformancePoint>();
        Refresh();
    }

    public string StudentName
    {
        get => _studentName;
        set => SetProperty(ref _studentName, value);
    }

    public int CorrectAnswers
    {
        get => _correctAnswers;
        set => SetProperty(ref _correctAnswers, value);
    }

    public int IncorrectAnswers
    {
        get => _incorrectAnswers;
        set => SetProperty(ref _incorrectAnswers, value);
    }

    public int TotalAnswers
    {
        get => _totalAnswers;
        set => SetProperty(ref _totalAnswers, value);
    }

    public double AccuracyPercent
    {
        get => _accuracyPercent;
        set => SetProperty(ref _accuracyPercent, value);
    }

    public double BeginnerAccuracy
    {
        get => _beginnerAccuracy;
        set => SetProperty(ref _beginnerAccuracy, value);
    }

    public double AdvancedAccuracy
    {
        get => _advancedAccuracy;
        set => SetProperty(ref _advancedAccuracy, value);
    }

    public double Improvement
    {
        get => _improvement;
        set => SetProperty(ref _improvement, value);
    }

    public string LastActivity
    {
        get => _lastActivity;
        set => SetProperty(ref _lastActivity, value);
    }

    public string BeginnerTrendSummary
    {
        get => _beginnerTrendSummary;
        set => SetProperty(ref _beginnerTrendSummary, value);
    }

    public string AdvancedTrendSummary
    {
        get => _advancedTrendSummary;
        set => SetProperty(ref _advancedTrendSummary, value);
    }

    public string ComparisonSummary
    {
        get => _comparisonSummary;
        set => SetProperty(ref _comparisonSummary, value);
    }

    public ObservableCollection<TrendBarItem> AccuracyTrend { get; }
    public ObservableCollection<SessionPerformancePoint> SessionPerformance { get; }

    public void Refresh()
    {
        var snapshot = _progressService.GetCurrentStudentSnapshot();
        StudentName = snapshot.StudentName;
        CorrectAnswers = snapshot.CorrectAnswers;
        IncorrectAnswers = snapshot.IncorrectAnswers;
        TotalAnswers = snapshot.TotalAnswers;
        AccuracyPercent = snapshot.AccuracyPercent;
        BeginnerAccuracy = snapshot.BeginnerAccuracyPercent;
        AdvancedAccuracy = snapshot.AdvancedAccuracyPercent;
        Improvement = snapshot.ImprovementPercent;
        LastActivity = snapshot.LastActivity?.ToLocalTime().ToString("d.M.yyyy H:mm", CultureInfo.GetCultureInfo("cs-CZ")) ?? "Bez aktivity";

        ReplaceTrendValues(AccuracyTrend, snapshot.AccuracyTrend);
        BeginnerTrendSummary = BuildModeTrendSummary(snapshot.BeginnerTrend, "za\u010D\u00E1te\u010Dn\u00EDkovi");
        AdvancedTrendSummary = BuildModeTrendSummary(snapshot.AdvancedTrend, "pokro\u010Dil\u00E9m");
        ComparisonSummary = BuildComparisonSummary(snapshot.BeginnerTrend, snapshot.AdvancedTrend);

        SessionPerformance.Clear();
        foreach (var item in snapshot.SessionPerformance.OrderByDescending(item => item.CompletedAt))
        {
            SessionPerformance.Add(item);
        }
    }

    private static void ReplaceTrendValues(ObservableCollection<TrendBarItem> target, IEnumerable<int> source)
    {
        target.Clear();
        var values = source.ToList();
        if (values.Count == 0)
        {
            return;
        }

        for (var index = 0; index < values.Count; index++)
        {
            var value = Math.Clamp(values[index], 0, 100);
            target.Add(new TrendBarItem
            {
                Label = $"Pokus {index + 1}",
                Value = value,
                BarHeight = Math.Max(36, value * 2)
            });
        }
    }

    private static string BuildModeTrendSummary(IReadOnlyList<int> values, string modeLabel)
    {
        if (values.Count < 2)
        {
            return $"V {modeLabel} zat\u00EDm nem\u00E1me dost v\u00FDsledk\u016F na porovn\u00E1n\u00ED. Zkus je\u0161t\u011B p\u00E1r p\u0159\u00EDklad\u016F.";
        }

        var recentCount = Math.Min(3, values.Count);
        var recentAverage = values.TakeLast(recentCount).Average();
        var previousCount = Math.Min(3, values.Count - recentCount);
        if (previousCount <= 0)
        {
            return $"V {modeLabel} u\u017E m\u00E1\u0161 prvn\u00ED v\u00FDsledky. Pokra\u010Duj a brzy uvid\u00EDme, jak se zlep\u0161uje\u0161.";
        }

        var previousAverage = values.Skip(Math.Max(0, values.Count - recentCount - previousCount)).Take(previousCount).Average();
        var difference = recentAverage - previousAverage;

        if (difference >= 8)
        {
            return $"V {modeLabel} se zlep\u0161uje\u0161. Posledn\u00ED pokusy ti jdou l\u00EDp ne\u017E p\u0159edt\u00EDm.";
        }

        if (difference <= -8)
        {
            return $"V {modeLabel} to bylo te\u010F o trochu t\u011B\u017E\u0161\u00ED. Je\u0161t\u011B p\u00E1r pokus\u016F a zase se rozjede\u0161.";
        }

        return $"V {modeLabel} se ti da\u0159\u00ED podobn\u011B jako minule. Dr\u017E\u00ED\u0161 si p\u011Bkn\u00FD v\u00FDsledek.";
    }

    private static string BuildComparisonSummary(IReadOnlyList<int> beginnerValues, IReadOnlyList<int> advancedValues)
    {
        if (beginnerValues.Count == 0 && advancedValues.Count == 0)
        {
            return "A\u017E budeme m\u00EDt v\u00EDc v\u00FDsledk\u016F, porovn\u00E1me oba re\u017Eimy.";
        }

        if (beginnerValues.Count == 0 || advancedValues.Count == 0)
        {
            return "Zat\u00EDm m\u00E1\u0161 v\u00FDsledky jen z jednoho re\u017Eimu. A\u017E vyzkou\u0161\u00ED\u0161 oba, uk\u00E1\u017Eeme srovn\u00E1n\u00ED.";
        }

        var beginnerAverage = beginnerValues.TakeLast(Math.Min(3, beginnerValues.Count)).Average();
        var advancedAverage = advancedValues.TakeLast(Math.Min(3, advancedValues.Count)).Average();
        var difference = beginnerAverage - advancedAverage;

        if (difference >= 8)
        {
            return "Te\u010F se ti da\u0159\u00ED v\u00EDc v za\u010D\u00E1te\u010Dn\u00EDkovi ne\u017E v pokro\u010Dil\u00E9m.";
        }

        if (difference <= -8)
        {
            return "Te\u010F se ti da\u0159\u00ED v\u00EDc v pokro\u010Dil\u00E9m ne\u017E v za\u010D\u00E1te\u010Dn\u00EDkovi.";
        }

        return "V obou re\u017Eimech se ti da\u0159\u00ED podobn\u011B dob\u0159e.";
    }
}

public sealed class TrendBarItem
{
    public string Label { get; init; } = string.Empty;
    public int Value { get; init; }
    public int BarHeight { get; init; }
}

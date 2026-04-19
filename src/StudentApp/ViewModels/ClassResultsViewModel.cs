using System.Collections.ObjectModel;
using SharedCore.Helpers;
using SharedCore.Models;
using SharedCore.Services;

namespace StudentApp.ViewModels;

public sealed class ClassResultsViewModel : BaseViewModel
{
    private readonly StudentProgressService _progressService;
    private string _searchText = string.Empty;
    private string _selectedSort = "\u00DAsp\u011B\u0161nost";

    public ClassResultsViewModel(StudentProgressService progressService)
    {
        _progressService = progressService;
        ClassItems = new ObservableCollection<ClassOverviewItem>();
        SortOptions = new ObservableCollection<string>(new[]
        {
            "\u00DAsp\u011B\u0161nost",
            "Aktivita",
            "Po\u010Det odpov\u011Bd\u00ED"
        });
        Refresh();
    }

    public ObservableCollection<ClassOverviewItem> ClassItems { get; }
    public ObservableCollection<string> SortOptions { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                Refresh();
            }
        }
    }

    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value))
            {
                Refresh();
            }
        }
    }

    public void Refresh()
    {
        IEnumerable<ClassOverviewItem> items = _progressService.GetPublicClassOverview();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            items = items.Where(item =>
                item.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                item.StudentId.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        items = SelectedSort switch
        {
            "Aktivita" => items.OrderByDescending(item => item.LastActivity).ThenBy(item => item.DisplayName),
            "Po\u010Det odpov\u011Bd\u00ED" => items.OrderByDescending(item => item.SolvedProblems).ThenByDescending(item => item.AccuracyPercent),
            _ => items.OrderByDescending(item => item.AccuracyPercent).ThenByDescending(item => item.ImprovementTrend)
        };

        ClassItems.Clear();
        foreach (var item in items)
        {
            ClassItems.Add(item);
        }
    }
}

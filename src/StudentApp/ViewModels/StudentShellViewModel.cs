using System.Windows;
using SharedCore.Helpers;
using SharedCore.Services;
using StudentApp.Services;

namespace StudentApp.ViewModels;

public sealed class StudentShellViewModel : BaseViewModel
{
    private readonly StudentProgressService _progressService;
    private readonly Action _closeAction;
    private readonly Func<bool> _importStudentConfiguration;
    private readonly string _configuredStudentId;
    private string _headerText = "Školní počítání";
    private string _loggedInStudentText = "Nejsi přihlášen";
    private string _headerStatusTitle = "Načtený soubor";
    private string _headerStatusValue = "Není dostupné";
    private string _headerStatusDetail = "Nepřihlášen";
    private int _selectedTabIndex;
    private bool _isLoggedIn;

    public StudentShellViewModel(
        StudentProgressService progressService,
        MathProblemGenerator mathProblemGenerator,
        LoggingService loggingService,
        StudentOnlineLoginService? onlineLoginService,
        StudentOnlineResultService? onlineResultService,
        string configuredStudentId,
        Func<bool> importStudentConfiguration,
        Action closeAction)
    {
        _progressService = progressService;
        _configuredStudentId = configuredStudentId.Trim();
        _importStudentConfiguration = importStudentConfiguration;
        _closeAction = closeAction;

        Login = new StudentLoginViewModel(_progressService, onlineLoginService, onlineResultService, _configuredStudentId);
        BeginnerQuiz = new BeginnerQuizViewModel(_progressService, mathProblemGenerator, loggingService, onlineResultService);
        AdvancedDragDrop = new AdvancedDragDropViewModel(_progressService, mathProblemGenerator, loggingService, onlineResultService);
        MyResults = new MyResultsViewModel(_progressService);
        ClassResults = new ClassResultsViewModel(_progressService);
        Login.LoggedIn += (_, _) => HandleStudentLogin();
        BeginnerQuiz.BackRequested += (_, _) => ShowMyResults();
        AdvancedDragDrop.BackRequested += (_, _) => ShowMyResults();

        ShowBeginnerCommand = new RelayCommand(ShowBeginner, () => IsLoggedIn);
        ShowAdvancedCommand = new RelayCommand(ShowAdvanced, () => IsLoggedIn);
        ShowMyResultsCommand = new RelayCommand(ShowMyResults, () => IsLoggedIn);
        ShowClassResultsCommand = new RelayCommand(ShowClassResults, () => IsLoggedIn);
        NewGameCommand = new RelayCommand(StartNewGame, () => IsLoggedIn);
        ChangeStudentCommand = new RelayCommand(ChangeStudent);
        CloseCommand = new RelayCommand(() => _closeAction());

        _progressService.DataChanged += (_, _) => RefreshAll();
        RefreshAll();
    }

    public string HeaderText
    {
        get => _headerText;
        set => SetProperty(ref _headerText, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public string LoggedInStudentText
    {
        get => _loggedInStudentText;
        set => SetProperty(ref _loggedInStudentText, value);
    }

    public string HeaderStatusTitle
    {
        get => _headerStatusTitle;
        set => SetProperty(ref _headerStatusTitle, value);
    }

    public string HeaderStatusValue
    {
        get => _headerStatusValue;
        set => SetProperty(ref _headerStatusValue, value);
    }

    public string HeaderStatusDetail
    {
        get => _headerStatusDetail;
        set => SetProperty(ref _headerStatusDetail, value);
    }

    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        set
        {
            if (SetProperty(ref _isLoggedIn, value))
            {
                ShowBeginnerCommand.RaiseCanExecuteChanged();
                ShowAdvancedCommand.RaiseCanExecuteChanged();
                ShowMyResultsCommand.RaiseCanExecuteChanged();
                ShowClassResultsCommand.RaiseCanExecuteChanged();
                NewGameCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public StudentLoginViewModel Login { get; }
    public BeginnerQuizViewModel BeginnerQuiz { get; }
    public AdvancedDragDropViewModel AdvancedDragDrop { get; }
    public MyResultsViewModel MyResults { get; }
    public ClassResultsViewModel ClassResults { get; }

    public RelayCommand ShowBeginnerCommand { get; }
    public RelayCommand ShowAdvancedCommand { get; }
    public RelayCommand ShowMyResultsCommand { get; }
    public RelayCommand ShowClassResultsCommand { get; }
    public RelayCommand NewGameCommand { get; }
    public RelayCommand ChangeStudentCommand { get; }
    public RelayCommand CloseCommand { get; }

    private void StartNewGame()
    {
        if (SelectedTabIndex == 1)
        {
            AdvancedDragDrop.StartNewGame();
        }
        else
        {
            BeginnerQuiz.StartNewGame();
            SelectedTabIndex = 0;
        }
    }

    private void ChangeStudent()
    {
        var choice = MessageBox.Show(
            "Pro změnu žáka je potřeba načíst nový soubor od paní učitelky (*.smtcfg).\n\nAno = načíst nový soubor.\nNe = zůstat u současného žáka.",
            "Změnit žáka",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Yes)
        {
            if (_importStudentConfiguration())
            {
                MessageBox.Show(
                    "Soubor od paní učitelky byl načten. Spusť aplikaci znovu, aby se použila nová konfigurace žáka.",
                    "Konfigurace načtena",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _closeAction();
            }

            return;
        }
    }

    private void RefreshAll()
    {
        IsLoggedIn = _progressService.IsLoggedIn;
        HeaderText = "Školní počítání";
        LoggedInStudentText = IsLoggedIn
            ? $"Přihlášen jako: {_progressService.CurrentStudentName}"
            : "Nejsi přihlášen";
        HeaderStatusTitle = IsLoggedIn ? "Přihlášený žák" : "Načtený soubor";
        HeaderStatusValue = IsLoggedIn
            ? _progressService.CurrentStudentName
            : string.IsNullOrWhiteSpace(_configuredStudentId)
                ? "Není dostupné"
                : _configuredStudentId;
        HeaderStatusDetail = IsLoggedIn ? "Přihlášen" : "Nepřihlášen";
        BeginnerQuiz.UpdateCurrentStudent();
        MyResults.Refresh();
        ClassResults.Refresh();
    }

    private void HandleStudentLogin()
    {
        IsLoggedIn = true;
        BeginnerQuiz.UpdateCurrentStudent();
        BeginnerQuiz.StartNewGame();
        AdvancedDragDrop.StartNewGame();
        MyResults.Refresh();
        ClassResults.Refresh();
        SelectedTabIndex = 0;
    }

    private void ShowBeginner()
    {
        BeginnerQuiz.UpdateCurrentStudent();
        SelectedTabIndex = 0;
    }

    private void ShowAdvanced()
    {
        SelectedTabIndex = 1;
    }

    private void ShowMyResults()
    {
        MyResults.Refresh();
        SelectedTabIndex = 2;
    }

    private void ShowClassResults()
    {
        ClassResults.Refresh();
        SelectedTabIndex = 3;
    }
}

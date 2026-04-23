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
    private string _headerText = "Školní počítání do 20";
    private string _loggedInStudentText = "Přihlášený žák: -";
    private int _selectedTabIndex;
    private bool _isLoggedIn;

    public StudentShellViewModel(
        StudentProgressService progressService,
        MathProblemGenerator mathProblemGenerator,
        LoggingService loggingService,
        StudentOnlineLoginService? onlineLoginService,
        StudentOnlineResultService? onlineResultService,
        Func<bool> importStudentConfiguration,
        Action closeAction)
    {
        _progressService = progressService;
        _importStudentConfiguration = importStudentConfiguration;
        _closeAction = closeAction;

        Login = new StudentLoginViewModel(_progressService, onlineLoginService);
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
        HeaderText = IsLoggedIn
            ? $"Školní počítání do 20 - {_progressService.CurrentStudentName}"
            : "Školní počítání do 20";
        LoggedInStudentText = IsLoggedIn
            ? $"Přihlášen jako: {_progressService.CurrentStudentName}"
            : "Přihlášený žák: -";
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

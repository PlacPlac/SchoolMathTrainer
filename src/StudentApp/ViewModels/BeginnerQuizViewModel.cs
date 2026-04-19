using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using SharedCore.Helpers;
using SharedCore.Models;
using SharedCore.Services;
using StudentApp.Services;

namespace StudentApp.ViewModels;

public sealed class BeginnerQuizViewModel : BaseViewModel
{
    private readonly StudentProgressService _progressService;
    private readonly MathProblemGenerator _mathProblemGenerator;
    private readonly LoggingService _loggingService;
    private readonly StudentOnlineResultService? _onlineResultService;
    private StudentSession? _session;
    private MathProblem? _currentProblem;
    private string _feedbackMessage = "Vyber jednu ze 4 odpovědí.";
    private string _studentName = "Anna";
    private string _expression = string.Empty;
    private string _scoreLine = $"0/{GameSettings.QuestionsPerRound} otázek | 0 správně | 0 špatně";
    private string _roundSummary = string.Empty;
    private string _roundSaveStatus = string.Empty;
    private bool _isRoundActive;
    private bool _isRoundSummaryVisible;
    private int _answerOption1;
    private int _answerOption2;
    private int _answerOption3;
    private int _answerOption4;

    public BeginnerQuizViewModel(
        StudentProgressService progressService,
        MathProblemGenerator mathProblemGenerator,
        LoggingService loggingService,
        StudentOnlineResultService? onlineResultService = null)
    {
        _progressService = progressService;
        _mathProblemGenerator = mathProblemGenerator;
        _loggingService = loggingService;
        _onlineResultService = onlineResultService;
        SubmitAnswerCommand = new RelayCommand(SubmitAnswer);
        StartNewRoundCommand = new RelayCommand(StartNewGame);
        BackCommand = new RelayCommand(() => BackRequested?.Invoke(this, EventArgs.Empty));
        AnswerOptions = new ObservableCollection<int>();
        UpdateCurrentStudent();
        StartNewGame();
    }

    public ObservableCollection<int> AnswerOptions { get; }

    public string StudentName
    {
        get => _studentName;
        set => SetProperty(ref _studentName, value);
    }

    public string Expression
    {
        get => _expression;
        set => SetProperty(ref _expression, value);
    }

    public string FeedbackMessage
    {
        get => _feedbackMessage;
        set => SetProperty(ref _feedbackMessage, value);
    }

    public string ScoreLine
    {
        get => _scoreLine;
        set => SetProperty(ref _scoreLine, value);
    }

    public string RoundSummary
    {
        get => _roundSummary;
        set => SetProperty(ref _roundSummary, value);
    }

    public string RoundSaveStatus
    {
        get => _roundSaveStatus;
        set => SetProperty(ref _roundSaveStatus, value);
    }

    public bool IsRoundActive
    {
        get => _isRoundActive;
        set => SetProperty(ref _isRoundActive, value);
    }

    public bool IsRoundSummaryVisible
    {
        get => _isRoundSummaryVisible;
        set => SetProperty(ref _isRoundSummaryVisible, value);
    }

    public int AnswerOption1
    {
        get => _answerOption1;
        set => SetProperty(ref _answerOption1, value);
    }

    public int AnswerOption2
    {
        get => _answerOption2;
        set => SetProperty(ref _answerOption2, value);
    }

    public int AnswerOption3
    {
        get => _answerOption3;
        set => SetProperty(ref _answerOption3, value);
    }

    public int AnswerOption4
    {
        get => _answerOption4;
        set => SetProperty(ref _answerOption4, value);
    }

    public RelayCommand SubmitAnswerCommand { get; }
    public RelayCommand StartNewRoundCommand { get; }
    public RelayCommand BackCommand { get; }

    public event EventHandler? BackRequested;

    public void UpdateCurrentStudent()
    {
        StudentName = _progressService.CurrentStudentName;
    }

    public void StartNewGame()
    {
        FinishCurrentSessionIfNeeded();
        _session = _progressService.StartSession(LearningMode.Beginner);
        IsRoundActive = true;
        IsRoundSummaryVisible = false;
        RoundSummary = string.Empty;
        RoundSaveStatus = string.Empty;
        ReloadAnswerOptions(Array.Empty<int>());
        FeedbackMessage = "Vyber jednu ze 4 odpovědí.";
        UpdateScoreLine();
        LoadNextProblem();
    }

    private void SubmitAnswer(object? parameter)
    {
        if (!IsRoundActive || _currentProblem is null || parameter is not int selectedAnswer || _session is null)
        {
            return;
        }

        var isCorrect = selectedAnswer == _currentProblem.CorrectAnswer;
        var record = new AnswerRecord
        {
            OperationType = _currentProblem.OperationType,
            ExampleText = _currentProblem.Expression,
            OfferedAnswers = _currentProblem.Options.ToList(),
            ChosenAnswer = selectedAnswer,
            CorrectAnswer = _currentProblem.CorrectAnswer,
            IsCorrect = isCorrect,
            InputMethod = "click"
        };

        if (!TryStoreAnswer(record))
        {
            return;
        }

        FeedbackMessage = isCorrect ? "Správně. Automaticky pokračujeme dál." : "Nevadí, další příklad hned přichází.";
        UpdateScoreLine();
        if (_session.RunningTotalCount >= GameSettings.QuestionsPerRound)
        {
            _ = CompleteRoundAsync();
            return;
        }

        LoadNextProblem();
    }

    private async Task CompleteRoundAsync()
    {
        if (_session is null)
        {
            return;
        }

        IsRoundActive = false;
        _currentProblem = null;
        ReloadAnswerOptions(Array.Empty<int>());
        _progressService.FinishSession(_session);
        var total = _session.RunningTotalCount;
        var correct = _session.RunningCorrectCount;
        var incorrect = _session.RunningWrongCount;
        var successRate = total == 0 ? 0 : Math.Round(correct * 100d / total, 1);
        RoundSummary = $"Celkem otázek: {total}\nSprávně: {correct}\nŠpatně: {incorrect}\nÚspěšnost: {successRate:0.#} %";
        FeedbackMessage = "Kolo je dokončené.";
        IsRoundSummaryVisible = true;
        RoundSaveStatus = "Výsledek kola je uložený lokálně.";

        if (_onlineResultService?.IsAvailable != true)
        {
            return;
        }

        try
        {
            await _onlineResultService.SaveCompletedRoundAsync(_session);
            RoundSaveStatus = "Výsledek kola byl odeslán na server.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or NotSupportedException)
        {
            _loggingService.LogError("Beginner round upload", ex);
            RoundSaveStatus = "Výsledek kola zůstal uložený lokálně. Odeslání na server se nepodařilo.";
        }
    }

    private bool TryStoreAnswer(AnswerRecord record)
    {
        try
        {
            _progressService.RecordAnswer(_session!, record);
            return true;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Beginner save first try", ex);
            Thread.Sleep(300);

            try
            {
                _progressService.RecordAnswer(_session!, record);
                return true;
            }
            catch (Exception retryException)
            {
                _loggingService.LogError("Beginner save retry", retryException);
                FeedbackMessage = "Nepodařilo se uložit odpověď. Zkus to prosím znovu.";
                MessageBox.Show(
                    "Odpověď se nepodařilo uložit ani po opakování. Data zůstanou v bezpečí a můžeš zkusit další odpověď.",
                    "Chyba zápisu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
        }
    }

    private void FinishCurrentSessionIfNeeded()
    {
        if (_session is not null && _session.Answers.Count > 0 && IsRoundActive)
        {
            _progressService.FinishSession(_session);
        }
    }

    private void UpdateScoreLine()
    {
        var correct = _session?.RunningCorrectCount ?? 0;
        var wrong = _session?.RunningWrongCount ?? 0;
        var total = _session?.RunningTotalCount ?? 0;
        ScoreLine = $"{total}/{GameSettings.QuestionsPerRound} otázek | {correct} správně | {wrong} špatně";
    }

    private void LoadNextProblem()
    {
        _currentProblem = _mathProblemGenerator.CreateBeginnerProblem(
            _progressService.Configuration.MaxOperandValue,
            _progressService.Configuration.AnswerOptionCount);

        Expression = $"{_currentProblem.Expression} = ?";
        ReloadAnswerOptions(_currentProblem.Options);
    }

    private void ReloadAnswerOptions(IEnumerable<int> options)
    {
        AnswerOptions.Clear();
        var visibleOptions = options.Take(4).ToArray();
        foreach (var option in visibleOptions)
        {
            AnswerOptions.Add(option);
        }

        AnswerOption1 = visibleOptions.ElementAtOrDefault(0);
        AnswerOption2 = visibleOptions.ElementAtOrDefault(1);
        AnswerOption3 = visibleOptions.ElementAtOrDefault(2);
        AnswerOption4 = visibleOptions.ElementAtOrDefault(3);
    }
}

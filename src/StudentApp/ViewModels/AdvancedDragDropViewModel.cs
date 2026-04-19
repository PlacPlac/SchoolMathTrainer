using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using SharedCore.Helpers;
using SharedCore.Models;
using SharedCore.Services;
using StudentApp.Services;

namespace StudentApp.ViewModels;

public sealed class AdvancedDragDropViewModel : BaseViewModel
{
    private const string InstructionFeedbackState = "Instruction";
    private const string SuccessFeedbackState = "Success";
    private const string ErrorFeedbackState = "Error";
    private const string InstructionFeedbackMessage = "Přetáhni číslo nebo klepni na číselnou kartičku.";
    private const string SuccessFeedbackMessage = "Správně. Hned následuje další příklad.";
    private const string ErrorFeedbackMessage = "To nevadí. Zkus další příklad.";
    private static readonly string DiagnosticsLogPath = Path.Combine(AppContext.BaseDirectory, "advanced-feedback.log");
    private static readonly object DiagnosticsLock = new();
    private readonly StudentProgressService _progressService;
    private readonly MathProblemGenerator _mathProblemGenerator;
    private readonly LoggingService _loggingService;
    private readonly StudentOnlineResultService? _onlineResultService;
    private StudentSession? _session;
    private MathProblem? _currentProblem;
    private CancellationTokenSource? _advanceCts;
    private string _expression = string.Empty;
    private string _feedbackMessage = InstructionFeedbackMessage;
    private string _feedbackState = InstructionFeedbackState;
    private string _dropHint = "Pusť číslo sem";
    private string _scoreLine = $"0/{GameSettings.QuestionsPerRound} otázek | 0 správně | 0 špatně";
    private string _roundSummary = string.Empty;
    private string _roundSaveStatus = string.Empty;
    private bool _isRoundActive;
    private bool _isRoundSummaryVisible;
    private string _lastFeedbackWriter = "InitialState";
    private bool _isTransitioningToNextProblem;

    public AdvancedDragDropViewModel(
        StudentProgressService progressService,
        MathProblemGenerator mathProblemGenerator,
        LoggingService loggingService,
        StudentOnlineResultService? onlineResultService = null)
    {
        _progressService = progressService;
        _mathProblemGenerator = mathProblemGenerator;
        _loggingService = loggingService;
        _onlineResultService = onlineResultService;
        NumberTiles = new ObservableCollection<int>();
        StartNewRoundCommand = new RelayCommand(StartNewGame);
        BackCommand = new RelayCommand(() => BackRequested?.Invoke(this, EventArgs.Empty));
        StartNewGame();
    }

    public ObservableCollection<int> NumberTiles { get; }

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

    public string FeedbackState
    {
        get => _feedbackState;
        set => SetProperty(ref _feedbackState, value);
    }

    public string DropHint
    {
        get => _dropHint;
        set => SetProperty(ref _dropHint, value);
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

    public RelayCommand StartNewRoundCommand { get; }
    public RelayCommand BackCommand { get; }

    public event EventHandler? BackRequested;

    public void StartNewGame()
    {
        CancelPendingAdvance("StartNewGame");
        FinishCurrentSessionIfNeeded();
        _session = _progressService.StartSession(LearningMode.Advanced);
        IsRoundActive = true;
        IsRoundSummaryVisible = false;
        RoundSummary = string.Empty;
        RoundSaveStatus = string.Empty;
        _isTransitioningToNextProblem = false;
        ReloadNumberTiles();
        ResetFeedback();
        DropHint = "Pusť číslo sem";
        UpdateScoreLine();
        LoadNextProblem();
        LogDiagnostic("StartNewGame", $"New advanced game started. CurrentExpression='{Expression}', FeedbackState='{FeedbackState}', FeedbackMessage='{FeedbackMessage}'.");
    }

    public void SubmitDroppedValue(int value)
    {
        SubmitAnswer(value, "drag-drop");
    }

    public void SubmitTappedValue(int value)
    {
        SubmitAnswer(value, "tap");
    }

    private void SubmitAnswer(int value, string inputMethod)
    {
        if (!IsRoundActive || _currentProblem is null || _session is null)
        {
            LogDiagnostic("SubmitAnswer", $"Ignored {inputMethod} submission because current problem or session is null. Selected={value}.");
            return;
        }

        if (_isTransitioningToNextProblem)
        {
            LogDiagnostic("SubmitAnswer", $"Ignored {inputMethod} submission while waiting for next problem. Selected={value}, CurrentExpected={_currentProblem.CorrectAnswer}, LastFeedbackWriter='{_lastFeedbackWriter}', FeedbackState='{FeedbackState}', FeedbackMessage='{FeedbackMessage}'.");
            return;
        }

        var isCorrect = value == _currentProblem.CorrectAnswer;
        LogDiagnostic("SubmitAnswer", $"Evaluating {inputMethod} submission. Selected={value}, Expected={_currentProblem.CorrectAnswer}, IsCorrect={isCorrect}, Expression='{_currentProblem.Expression}', PreviousFeedbackState='{FeedbackState}', PreviousFeedbackMessage='{FeedbackMessage}', PreviousWriter='{_lastFeedbackWriter}'.");
        var record = new AnswerRecord
        {
            OperationType = _currentProblem.OperationType,
            ExampleText = _currentProblem.Expression,
            ChosenAnswer = value,
            CorrectAnswer = _currentProblem.CorrectAnswer,
            IsCorrect = isCorrect,
            InputMethod = inputMethod
        };

        if (!TryStoreAnswer(record))
        {
            return;
        }

        DropHint = value.ToString();
        SetFeedback(isCorrect ? SuccessFeedbackMessage : ErrorFeedbackMessage, isCorrect ? SuccessFeedbackState : ErrorFeedbackState);
        UpdateScoreLine();
        if (_session.RunningTotalCount >= GameSettings.QuestionsPerRound)
        {
            _ = CompleteRoundAsync();
            return;
        }

        _isTransitioningToNextProblem = true;
        _advanceCts = ReplaceAdvanceToken();
        _ = AdvanceToNextProblemAsync(_advanceCts.Token);
    }

    private async Task CompleteRoundAsync()
    {
        if (_session is null)
        {
            return;
        }

        CancelPendingAdvance("CompleteRound");
        IsRoundActive = false;
        _isTransitioningToNextProblem = false;
        _currentProblem = null;
        ReloadNumberTiles();
        _progressService.FinishSession(_session);
        var total = _session.RunningTotalCount;
        var correct = _session.RunningCorrectCount;
        var incorrect = _session.RunningWrongCount;
        var successRate = total == 0 ? 0 : Math.Round(correct * 100d / total, 1);
        RoundSummary = $"Celkem otázek: {total}\nSprávně: {correct}\nŠpatně: {incorrect}\nÚspěšnost: {successRate:0.#} %";
        DropHint = "Kolo dokončeno";
        SetFeedback("Kolo je dokončené.", SuccessFeedbackState);
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
            _loggingService.LogError("Advanced round upload", ex);
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
            _loggingService.LogError("Advanced save first try", ex);
            Thread.Sleep(300);

            try
            {
                _progressService.RecordAnswer(_session!, record);
                return true;
            }
            catch (Exception retryException)
            {
                _loggingService.LogError("Advanced save retry", retryException);
                SetFeedback("Nepodařilo se uložit odpověď. Zkus to prosím znovu.", ErrorFeedbackState);
                MessageBox.Show(
                    "Odpověď se nepodařilo uložit ani po opakování. Můžeš pokračovat, jen odpověď zkus zadat znovu.",
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
        LogDiagnostic("UpdateScoreLine", $"Score updated to '{ScoreLine}'.");
    }

    private void LoadNextProblem()
    {
        _currentProblem = _mathProblemGenerator.CreateAdvancedProblem(_progressService.Configuration.MaxOperandValue);
        ReloadNumberTiles(_currentProblem.CorrectAnswer);
        DropHint = "Pusť číslo sem";
        Expression = $"{_currentProblem.Expression} = ?";
        LogDiagnostic("LoadNextProblem", $"Loaded next problem. Expression='{Expression}', CorrectAnswer={_currentProblem.CorrectAnswer}, TileCount={NumberTiles.Count}.");
    }

    private void ReloadNumberTiles(int correctAnswer = 0)
    {
        NumberTiles.Clear();
        if (!IsRoundActive)
        {
            return;
        }

        foreach (var number in _mathProblemGenerator.CreateAdvancedAnswerOptions(correctAnswer, _progressService.Configuration.MaxOperandValue))
        {
            NumberTiles.Add(number);
        }
    }

    private void ResetFeedback()
    {
        SetFeedback(InstructionFeedbackMessage, InstructionFeedbackState);
    }

    private void SetFeedback(string message, string state, [CallerMemberName] string writer = "")
    {
        FeedbackMessage = message;
        FeedbackState = state;
        _lastFeedbackWriter = writer;
        LogDiagnostic(writer, $"Feedback updated. State='{FeedbackState}', Message='{FeedbackMessage}', LastWriter='{_lastFeedbackWriter}'.");
    }

    private async Task AdvanceToNextProblemAsync(CancellationToken cancellationToken)
    {
        try
        {
            LogDiagnostic("AdvanceToNextProblemAsync", $"Waiting before next problem. Current feedback state='{FeedbackState}', message='{FeedbackMessage}'.");
            await Task.Delay(TimeSpan.FromMilliseconds(650), cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                LogDiagnostic("AdvanceToNextProblemAsync", "Advance canceled before loading the next problem.");
                return;
            }

            LoadNextProblem();
            ResetFeedback();
        }
        catch (OperationCanceledException)
        {
            LogDiagnostic("AdvanceToNextProblemAsync", "Advance canceled by token.");
        }
        finally
        {
            _isTransitioningToNextProblem = false;
            LogDiagnostic("AdvanceToNextProblemAsync", $"Advance finished. FeedbackState='{FeedbackState}', FeedbackMessage='{FeedbackMessage}', LastWriter='{_lastFeedbackWriter}'.");
        }
    }

    private CancellationTokenSource ReplaceAdvanceToken()
    {
        CancelPendingAdvance("ReplaceAdvanceToken");
        return new CancellationTokenSource();
    }

    private void CancelPendingAdvance(string reason)
    {
        if (_advanceCts is null)
        {
            return;
        }

        if (!_advanceCts.IsCancellationRequested)
        {
            _advanceCts.Cancel();
            LogDiagnostic("CancelPendingAdvance", $"Canceled pending advance. Reason='{reason}'.");
        }

        _advanceCts.Dispose();
        _advanceCts = null;
    }

    private static void LogDiagnostic(string source, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}: {message}{Environment.NewLine}";
        lock (DiagnosticsLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticsLogPath)!);
            File.AppendAllText(DiagnosticsLogPath, line, Encoding.UTF8);
        }
    }
}

using SharedCore.Helpers;
using SharedCore.Services;
using StudentApp.Services;

namespace StudentApp.ViewModels;

public sealed class StudentLoginViewModel : BaseViewModel
{
    private readonly StudentProgressService _progressService;
    private readonly StudentOnlineLoginService? _onlineLoginService;
    private readonly StudentOnlineResultService? _onlineResultService;
    private readonly string _configuredStudentId;
    private string _loginCode = string.Empty;
    private string _pin = string.Empty;
    private string _newPin = string.Empty;
    private string _welcomeMessage = "Zadej přihlašovací kód a PIN.";
    private string _loadedStudentFileText = "Načtený soubor pro žáka: není dostupné";
    private bool _isNewPinRequired;
    private bool _isLoginInProgress;

    public StudentLoginViewModel(
        StudentProgressService progressService,
        StudentOnlineLoginService? onlineLoginService,
        StudentOnlineResultService? onlineResultService = null,
        string configuredStudentId = "")
    {
        _progressService = progressService;
        _onlineLoginService = onlineLoginService;
        _onlineResultService = onlineResultService;
        _configuredStudentId = configuredStudentId.Trim();
        LoadedStudentFileText = string.IsNullOrWhiteSpace(_configuredStudentId)
            ? "Načtený soubor pro žáka: není dostupné"
            : $"Načtený soubor pro žáka: {_configuredStudentId}";
        LoginCommand = new RelayCommand(LoginStudent);
    }

    public string LoginCode
    {
        get => _loginCode;
        set => SetProperty(ref _loginCode, value);
    }

    public string Pin
    {
        get => _pin;
        set => SetProperty(ref _pin, value);
    }

    public string NewPin
    {
        get => _newPin;
        set => SetProperty(ref _newPin, value);
    }

    public string WelcomeMessage
    {
        get => _welcomeMessage;
        set => SetProperty(ref _welcomeMessage, value);
    }

    public string LoadedStudentFileText
    {
        get => _loadedStudentFileText;
        set => SetProperty(ref _loadedStudentFileText, value);
    }

    public bool IsNewPinRequired
    {
        get => _isNewPinRequired;
        set => SetProperty(ref _isNewPinRequired, value);
    }

    public bool IsLoginInProgress
    {
        get => _isLoginInProgress;
        set => SetProperty(ref _isLoginInProgress, value);
    }

    public RelayCommand LoginCommand { get; }

    public event EventHandler? LoggedIn;

    public void PrepareForStudentChange()
    {
        _progressService.LogoutStudent();
        _onlineResultService?.ClearSessionAuthorization();
        Pin = string.Empty;
        NewPin = string.Empty;
        IsNewPinRequired = false;
        WelcomeMessage = "Zadej přihlašovací kód a PIN.";
    }

    public async void LoginStudent()
    {
        if (IsLoginInProgress)
        {
            return;
        }

        IsLoginInProgress = true;
        SharedCore.Models.StudentLoginResult result;
        try
        {
            result = _onlineLoginService?.IsAvailable == true
                ? await _onlineLoginService.LoginAsync(LoginCode, Pin, NewPin)
                : _progressService.LoginStudent(LoginCode, Pin, NewPin);
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogError("StudentApp", "Student login command failed", ex);
            result = SharedCore.Models.StudentLoginResult.Failed("Přihlášení se nepodařilo dokončit. Zkus to prosím znovu.");
        }
        finally
        {
            IsLoginInProgress = false;
        }

        WelcomeMessage = result.RequiresStudentConfigurationReload
            ? $"{result.Message} Klikni na Změnit žáka a načti správný soubor od paní učitelky."
            : result.Message;
        IsNewPinRequired = result.RequiresPinChange;

        if (!result.Success)
        {
            _onlineResultService?.ClearSessionAuthorization();
            return;
        }

        Pin = string.Empty;
        NewPin = string.Empty;
        IsNewPinRequired = false;
        if (_onlineLoginService?.IsAvailable == true)
        {
            _onlineResultService?.SetSessionAuthorization(
                result.StudentSessionToken,
                result.StudentSessionExpiresUtc,
                result.StudentId);
            _progressService.CompleteExternalLogin(result.StudentId, result.DisplayName);
        }

        LoggedIn?.Invoke(this, EventArgs.Empty);
    }
}

using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SharedCore.Models;
using SharedCore.Services;
using TeacherApp.Data;
using TeacherApp.Settings;

namespace TeacherApp;

public partial class MainWindow : Window
{
    private readonly TeacherDataFolderValidator _dataFolderValidator = new();
    private readonly TeacherStudentAccountReader _studentAccountReader = new();
    private readonly TeacherStudentResultsReader _studentResultsReader = new();
    private readonly TeacherClassOverviewReader _classOverviewReader = new();
    private readonly TeacherStudentAccountCreator _studentAccountCreator = new();
    private readonly TeacherStudentPinResetter _studentPinResetter = new();
    private readonly StudentConfigFileService _studentConfigFileService = new();
    private readonly TeacherServerSettingsService _serverSettingsService = new();
    private readonly TeacherSftpReadOnlyDataSource _sftpReadOnlyDataSource = new();
    private readonly AppConfiguration _configuration = LoadConfiguration();
    private readonly TeacherOnlineApiDataSource _onlineApiDataSource;
    private readonly ObservableCollection<StudentListItem> _students = [];
    private readonly ObservableCollection<StudentActivityListItem> _recentActivities = [];
    private readonly ObservableCollection<StudentActivityListItem> _studentRecentActivities = [];
    private readonly ObservableCollection<ClassOverviewStudentItem> _classOverviewStudents = [];
    private TeacherServerSettings _serverSettings = TeacherServerSettings.CreateDefault();
    private string _currentDataFolderPath = string.Empty;
    private string _selectedStudentId = string.Empty;
    private bool _isRemoteReadOnlyData;

    public MainWindow()
    {
        InitializeComponent();
        StudentListBox.ItemsSource = _students;
        RecentActivitiesListBox.ItemsSource = _recentActivities;
        StudentRecentActivitiesListBox.ItemsSource = _studentRecentActivities;
        ClassOverviewStudentsListBox.ItemsSource = _classOverviewStudents;
        UpdateEmptyStates();
        _onlineApiDataSource = new TeacherOnlineApiDataSource(_configuration.DataConnection);
        _serverSettings = _serverSettingsService.Load();

        if (IsOnlineApiMode)
        {
            ApplyTeacherLoggedOutState("Přihlaste se učitelským účtem. Data třídy se bez přihlášení nenačítají.");
        }
        else
        {
            TeacherLoginPanel.IsVisible = false;
            TeacherLoginStatusTextBlock.IsVisible = false;
            TeacherLogoutButton.IsVisible = false;
            LoadConfiguredLocalDataOnStartup();
        }
    }

    private bool IsOnlineApiMode => _configuration.DataConnection.Mode == ApplicationDataMode.OnlineApi;

    private bool IsTeacherAuthenticated => !IsOnlineApiMode || _onlineApiDataSource.IsAuthenticated;

    private void ApplyTeacherLoggedOutState(string message)
    {
        if (IsOnlineApiMode)
        {
            _onlineApiDataSource.ClearAuthentication();
        }

        ClearTeacherData(message);
        TeacherLoginStatusTextBlock.IsVisible = false;
        TeacherLoginStatusTextBlock.Text = string.Empty;
        UpdateTeacherAuthUi();
    }

    private bool EnsureTeacherAuthenticated()
    {
        if (IsTeacherAuthenticated)
        {
            return true;
        }

        ValidationStatusTextBlock.Text = "Nejdřív se přihlaste učitelským účtem.";
        TeacherLoginStatusTextBlock.IsVisible = false;
        TeacherLoginStatusTextBlock.Text = string.Empty;
        UpdateTeacherAuthUi();
        return false;
    }

    private void HandleOnlineAuthorizationFailureIfNeeded()
    {
        if (IsOnlineApiMode && _onlineApiDataSource.LastAuthorizationFailed)
        {
            ApplyTeacherLoggedOutState(_onlineApiDataSource.LastErrorMessage);
        }
    }

    private void UpdateTeacherAuthUi()
    {
        var showOnlineLogin = IsOnlineApiMode;
        var isAuthenticated = IsTeacherAuthenticated;
        TeacherLoginPanel.IsVisible = showOnlineLogin;
        TeacherLoginButton.IsVisible = showOnlineLogin && !isAuthenticated;
        TeacherLogoutButton.IsVisible = showOnlineLogin && isAuthenticated;
        TeacherUsernameTextBox.IsVisible = showOnlineLogin && !isAuthenticated;
        TeacherPasswordTextBox.IsVisible = showOnlineLogin && !isAuthenticated;
        TeacherUsernameTextBox.IsEnabled = !isAuthenticated;
        TeacherPasswordTextBox.IsEnabled = !isAuthenticated;

        var teacherActionsEnabled = !IsOnlineApiMode || isAuthenticated;
        var studentActionEnabled = teacherActionsEnabled && StudentListBox.SelectedItem is StudentListItem &&
            !string.IsNullOrWhiteSpace(_selectedStudentId);
        RefreshDataButton.IsEnabled = teacherActionsEnabled;
        LoadFromServerButton.IsEnabled = teacherActionsEnabled;
        CreateStudentButton.IsEnabled = teacherActionsEnabled;
        ResetPinButton.IsEnabled = studentActionEnabled;
        DeleteStudentButton.IsEnabled = studentActionEnabled;
        GenerateStudentConfigButton.IsEnabled = studentActionEnabled;
        UpdateStatusCardVisibility();
    }

    private void ClearTeacherData(string message)
    {
        _students.Clear();
        _recentActivities.Clear();
        _studentRecentActivities.Clear();
        _classOverviewStudents.Clear();
        _currentDataFolderPath = string.Empty;
        _selectedStudentId = string.Empty;
        _isRemoteReadOnlyData = false;
        ClearTransientStudentActionMessages();
        ClearStudentDetail();
        ClearStudentResults("Vyberte žáka ze seznamu.");
        ClearClassOverview("Třídní přehled není načten.");
        ValidationStatusTextBlock.Text = message;
        UpdateEmptyStates();
    }

    private static AppConfiguration LoadConfiguration()
    {
        try
        {
            return new ConfigurationService().LoadFromFile("appsettings.json", useSharedDataFolderOverride: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Text.Json.JsonException)
        {
            return new AppConfiguration();
        }
    }

    private void OnRefreshDataClick(object? sender, RoutedEventArgs e)
    {
        if (IsOnlineApiMode)
        {
            if (!EnsureTeacherAuthenticated())
            {
                return;
            }

            LoadStudentsFromOnlineApi(_selectedStudentId);
            return;
        }

        _isRemoteReadOnlyData = false;
        LoadConfiguredLocalDataOnStartup();
    }

    private async void OnServerSettingsClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new ServerSettingsWindow(_serverSettingsService);
        var saved = await dialog.ShowDialog<bool>(this);
        if (!saved)
        {
            return;
        }

        _serverSettings = dialog.SavedSettings ?? _serverSettingsService.Load();
        AppendServerSettingsStatus();
    }

    private void OnTeacherLoginClick(object? sender, RoutedEventArgs e)
    {
        if (!IsOnlineApiMode)
        {
            return;
        }

        TeacherLoginStatusBorder.IsVisible = true;
        TeacherLoginStatusTextBlock.IsVisible = true;
        TeacherLoginStatusTextBlock.Text = "Ověřuji přihlášení učitele...";
        var login = _onlineApiDataSource.LoginTeacher(
            TeacherUsernameTextBox.Text ?? string.Empty,
            TeacherPasswordTextBox.Text ?? string.Empty);
        TeacherPasswordTextBox.Text = string.Empty;

        if (!login.Success)
        {
            ClearTeacherData("Data třídy nejsou načtena.");
            TeacherLoginStatusTextBlock.Text = string.IsNullOrWhiteSpace(login.Message)
                ? "Přihlášení učitele se nepodařilo."
                : login.Message;
            TeacherLoginStatusTextBlock.IsVisible = true;
            UpdateTeacherAuthUi();
            return;
        }

        TeacherLoginStatusTextBlock.Text = string.IsNullOrWhiteSpace(login.Username)
            ? "Přihlášení učitele proběhlo úspěšně."
            : $"Přihlášeno: {login.Username}.";
        UpdateTeacherAuthUi();
        LoadStudentsFromOnlineApi();
    }

    private void OnTeacherLogoutClick(object? sender, RoutedEventArgs e)
    {
        if (IsOnlineApiMode)
        {
            _onlineApiDataSource.LogoutTeacher();
        }

        ApplyTeacherLoggedOutState("Učitel byl odhlášen. Pro práci s daty se znovu přihlaste.");
    }

    private async void OnLoadFromServerClick(object? sender, RoutedEventArgs e)
    {
        if (IsOnlineApiMode)
        {
            if (!EnsureTeacherAuthenticated())
            {
                return;
            }

            LoadStudentsFromOnlineApi(_selectedStudentId);
            return;
        }

        _students.Clear();
        _recentActivities.Clear();
        _studentRecentActivities.Clear();
        _classOverviewStudents.Clear();
        _currentDataFolderPath = string.Empty;
        _selectedStudentId = string.Empty;
        _isRemoteReadOnlyData = false;
        ClearStudentDetail();
        ClearStudentResults("Načítám data ze serveru.");
        ClearClassOverview("Načítám data ze serveru.");
        CreateStudentStatusTextBlock.Text = string.Empty;
        ValidationStatusTextBlock.Text = "Načítám data ze serveru přes SFTP pouze pro čtení...";

        _serverSettings = _serverSettingsService.Load();
        var result = await _sftpReadOnlyDataSource.LoadAsync(_serverSettings);
        ValidationStatusTextBlock.Text = result.Message;
        if (!result.Success)
        {
            ClearStudentResults("Data ze serveru nejsou k dispozici.");
            ClearClassOverview("Data ze serveru nejsou k dispozici.");
            return;
        }

        _isRemoteReadOnlyData = true;
        LoadStudentsFromFolder(result.LocalCacheRoot, string.Empty);
        ValidationStatusTextBlock.Text = $"{result.Message} Režim je pouze pro čtení.";
    }

    private void OnCreateStudentClick(object? sender, RoutedEventArgs e)
    {
        CreateStudentStatusBorder.IsVisible = true;

        if (IsOnlineApiMode)
        {
            if (!EnsureTeacherAuthenticated())
            {
                return;
            }

            var onlineCreateResult = _onlineApiDataSource.CreateStudent(NewStudentNameTextBox.Text ?? string.Empty);
            CreateStudentStatusTextBlock.Text = onlineCreateResult.Message;
            if (!onlineCreateResult.Success || onlineCreateResult.Account is null)
            {
                DiagnosticLogService.Log("TeacherApp", $"Online create student failed. Message: {onlineCreateResult.Message}");
                HandleOnlineAuthorizationFailureIfNeeded();
                return;
            }

            NewStudentNameTextBox.Text = string.Empty;
            LoadStudentsFromOnlineApi(onlineCreateResult.Account.StudentId);
            CreateStudentStatusTextBlock.Text =
                $"Účet byl vytvořen pro žáka {onlineCreateResult.Account.DisplayName}. LoginCode: {onlineCreateResult.Account.LoginCode}. Dočasný PIN: {onlineCreateResult.TemporaryPin}. PIN je určen pro první přihlášení a žák ho potom změní.";
            DiagnosticLogService.Log("TeacherApp", $"Online create student completed for student '{onlineCreateResult.Account.StudentId}'. Temporary credential value was not logged.");
            return;
        }

        if (_isRemoteReadOnlyData)
        {
            CreateStudentStatusTextBlock.Text = "Data ze serveru jsou načtená pouze pro čtení. Vytváření účtů je dostupné jen v LocalFiles.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentDataFolderPath))
        {
            CreateStudentStatusTextBlock.Text = "Nejdřív načtěte platnou datovou složku.";
            return;
        }

        var createResult = _studentAccountCreator.CreateStudent(_currentDataFolderPath, NewStudentNameTextBox.Text ?? string.Empty);
        CreateStudentStatusTextBlock.Text = createResult.Message;

        if (!createResult.Success || createResult.Account is null)
        {
            DiagnosticLogService.Log("TeacherApp", $"Create student failed. Message: {createResult.Message}");
            return;
        }

        NewStudentNameTextBox.Text = string.Empty;
        LoadStudentsFromFolder(_currentDataFolderPath, createResult.Account.StudentId);
        CreateStudentStatusTextBlock.Text =
            $"Účet byl vytvořen pro žáka {createResult.Account.DisplayName}. LoginCode: {createResult.Account.LoginCode}. Dočasný PIN: {createResult.TemporaryPin}. PIN je určen pro první přihlášení a žák ho potom změní.";
        DiagnosticLogService.Log("TeacherApp", $"Create student completed for student '{createResult.Account.StudentId}'. Temporary credential value was not logged.");
    }

    private void LoadStudentsFromFolder(string folderPath, string studentIdToSelect)
    {
        _students.Clear();
        _recentActivities.Clear();
        _studentRecentActivities.Clear();
        _classOverviewStudents.Clear();
        ClearTransientStudentActionMessages();
        ClearStudentDetail();
        ClearStudentResults("Vyberte žáka ze seznamu.");
        ClearClassOverview("Třídní přehled není načten.");

        var studentReadResult = _studentAccountReader.ReadStudents(folderPath);
        ValidationStatusTextBlock.Text = studentReadResult.Message;

        if (!studentReadResult.Success)
        {
            var accountFilePath = Path.Combine(folderPath, "Config", "student-accounts.json");
            _currentDataFolderPath = File.Exists(accountFilePath) ? string.Empty : folderPath;
            if (!File.Exists(accountFilePath))
            {
                CreateStudentStatusTextBlock.Text = "Soubor účtů zatím neexistuje. Můžete vytvořit prvního žáka.";
            }

            return;
        }

        _currentDataFolderPath = folderPath;
        foreach (var student in studentReadResult.Students)
        {
            _students.Add(student);
        }

        LoadClassOverview();
        UpdateEmptyStates();

        if (!string.IsNullOrWhiteSpace(studentIdToSelect))
        {
            StudentListBox.SelectedItem = _students.FirstOrDefault(student =>
                string.Equals(student.StudentId, studentIdToSelect, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void LoadStudentsFromOnlineApi()
    {
        LoadStudentsFromOnlineApi(string.Empty);
    }

    private void LoadStudentsFromOnlineApi(string studentIdToSelect)
    {
        if (!EnsureTeacherAuthenticated())
        {
            return;
        }

        _students.Clear();
        _recentActivities.Clear();
        _studentRecentActivities.Clear();
        _classOverviewStudents.Clear();
        _currentDataFolderPath = string.Empty;
        ClearTransientStudentActionMessages();
        ClearStudentDetail();
        ClearStudentResults("Vyberte žáka ze seznamu.");

        var studentReadResult = _onlineApiDataSource.ReadStudents();
        ValidationStatusTextBlock.Text = $"{studentReadResult.Message} Režim: OnlineApi, URL: {_configuration.DataConnection.ApiBaseUrl}.";
        AppendServerSettingsStatus();

        if (!studentReadResult.Success)
        {
            ClearClassOverview("OnlineApi třídní přehled není k dispozici.");
            HandleOnlineAuthorizationFailureIfNeeded();
            return;
        }

        foreach (var student in studentReadResult.Students)
        {
            _students.Add(student);
        }

        LoadClassOverviewFromOnlineApi();
        LoadClassActivitiesFromOnlineApi();
        UpdateEmptyStates();
        if (!string.IsNullOrWhiteSpace(studentIdToSelect))
        {
            StudentListBox.SelectedItem = _students.FirstOrDefault(student =>
                string.Equals(student.StudentId, studentIdToSelect, StringComparison.OrdinalIgnoreCase));
        }

        if (StudentListBox.SelectedItem is null)
        {
            _selectedStudentId = string.Empty;
            ClearStudentDetail();
            ClearStudentResults("Vyberte žáka ze seznamu.");
        }
    }

    private void LoadConfiguredLocalDataOnStartup()
    {
        _students.Clear();
        _recentActivities.Clear();
        _studentRecentActivities.Clear();
        _classOverviewStudents.Clear();
        _currentDataFolderPath = string.Empty;
        ClearTransientStudentActionMessages();
        ClearStudentDetail();
        ClearStudentResults("Vyberte žáka ze seznamu.");
        ClearClassOverview("Třídní přehled není načten.");
        CreateStudentStatusTextBlock.Text = string.Empty;

        var result = _dataFolderValidator.Validate(_configuration.SharedDataRoot);
        if (result.Status != DataFolderValidationStatus.RecognizedData)
        {
            ValidationStatusTextBlock.Text = ToTeacherFriendlyDataStatus(result.Status);
            return;
        }

        LoadStudentsFromFolder(result.FolderPath, string.Empty);
        ValidationStatusTextBlock.Text = "Data třídy byla automaticky načtena.";
        AppendServerSettingsStatus();
    }

    private void AppendServerSettingsStatus()
    {
        var currentStatus = ValidationStatusTextBlock.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentStatus))
        {
            ValidationStatusTextBlock.Text = _serverSettings.DisplayText;
            return;
        }

        ValidationStatusTextBlock.Text = $"{currentStatus} {_serverSettings.DisplayText}.";
    }

    private static string ToTeacherFriendlyDataStatus(DataFolderValidationStatus status) => status switch
    {
        DataFolderValidationStatus.EmptyPath => "Data třídy nejsou nastavena v konfiguraci aplikace.",
        DataFolderValidationStatus.DirectoryNotFound => "Data třídy nejsou dostupná. Zkontrolujte konfiguraci aplikace nebo synchronizaci dat.",
        DataFolderValidationStatus.UnrecognizedData => "Data třídy nejsou ve správném formátu.",
        _ => "Data třídy nejsou dostupná."
    };

    private void OnStudentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ClearTransientStudentActionMessages();

        var student = e.AddedItems.OfType<StudentListItem>().FirstOrDefault()
            ?? StudentListBox.SelectedItem as StudentListItem;
        if (student is null)
        {
            _selectedStudentId = string.Empty;
            ClearStudentDetail();
            ClearStudentResults("Vyberte žáka ze seznamu.");
            RefreshTeacherUiState();
            return;
        }

        _selectedStudentId = student.StudentId;
        DetailNameTextBlock.Text = $"Jméno: {student.DisplayName}";
        DetailLoginCodeTextBlock.Text = $"LoginCode: {student.LoginCode}";
        DetailAccountStatusTextBlock.Text = $"Stav účtu: {student.AccountStatus}";
        DetailMustChangePinTextBlock.Text = $"Vyžaduje změnu PINu: {student.MustChangePinStatus}";
        DetailTemporaryPinPendingTextBlock.Text = $"Čeká dočasný PIN: {student.TemporaryPinPendingStatus}";
        DetailCreatedAtTextBlock.Text = $"Vytvořeno: {student.CreatedAtText}";
        DetailPinResetAtTextBlock.Text = $"Poslední reset PINu: {student.PinResetAtText}";
        LoadPendingTemporaryPin(student);

        LoadStudentResults(student);
        RefreshTeacherUiState();
    }

    private void OnResetPinClick(object? sender, RoutedEventArgs e)
    {
        StudentActionStatusBorder.IsVisible = true;

        if (IsOnlineApiMode)
        {
            if (!EnsureTeacherAuthenticated())
            {
                return;
            }

            if (StudentListBox.SelectedItem is not StudentListItem ||
                string.IsNullOrWhiteSpace(_selectedStudentId))
            {
                ResetPinStatusTextBlock.Text = "Nejdřív vyberte žáka.";
                return;
            }

            var onlineResetResult = _onlineApiDataSource.ResetStudentPin(_selectedStudentId);
            ResetPinStatusTextBlock.Text = onlineResetResult.Message;
            if (!onlineResetResult.Success || onlineResetResult.Account is null)
            {
                DiagnosticLogService.Log("TeacherApp", $"Online credential reset failed for student '{_selectedStudentId}'. Message: {onlineResetResult.Message}");
                HandleOnlineAuthorizationFailureIfNeeded();
                return;
            }

            LoadStudentsFromOnlineApi(onlineResetResult.Account.StudentId);
            ResetPinStatusTextBlock.Text =
                $"PIN byl resetován pro žáka {onlineResetResult.Account.DisplayName} ({onlineResetResult.Account.LoginCode}). Nový dočasný PIN: {onlineResetResult.TemporaryPin}. PIN je určen pro první přihlášení a žák ho potom změní.";
            DiagnosticLogService.Log("TeacherApp", $"Online credential reset completed for student '{onlineResetResult.Account.StudentId}'. Temporary credential value was not logged.");
            return;
        }

        if (_isRemoteReadOnlyData)
        {
            ResetPinStatusTextBlock.Text = "Data ze serveru jsou načtená pouze pro čtení. Reset PINu je dostupný jen v LocalFiles.";
            return;
        }

        if (StudentListBox.SelectedItem is not StudentListItem selectedStudent ||
            string.IsNullOrWhiteSpace(_selectedStudentId))
        {
            ResetPinStatusTextBlock.Text = "Nejdřív vyberte žáka.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentDataFolderPath))
        {
            ResetPinStatusTextBlock.Text = "Nejdřív načtěte platnou datovou složku.";
            return;
        }

        var resetResult = _studentPinResetter.ResetPin(_currentDataFolderPath, _selectedStudentId);
        ResetPinStatusTextBlock.Text = resetResult.Message;
        if (!resetResult.Success || resetResult.Account is null)
        {
            DiagnosticLogService.Log("TeacherApp", $"Credential reset failed for student '{_selectedStudentId}'. Message: {resetResult.Message}");
            return;
        }

        LoadStudentsFromFolder(_currentDataFolderPath, resetResult.Account.StudentId);
        ResetPinStatusTextBlock.Text =
            $"PIN byl resetován pro žáka {resetResult.Account.DisplayName} ({resetResult.Account.LoginCode}). Nový dočasný PIN: {resetResult.TemporaryPin}. PIN je určen pro první přihlášení a žák ho potom změní.";
        DiagnosticLogService.Log("TeacherApp", $"Credential reset completed for student '{resetResult.Account.StudentId}'. Temporary credential value was not logged.");
    }

    private void OnDeleteStudentClick(object? sender, RoutedEventArgs e)
    {
        StudentActionStatusBorder.IsVisible = true;

        if (StudentListBox.SelectedItem is not StudentListItem selectedStudent ||
            string.IsNullOrWhiteSpace(_selectedStudentId))
        {
            ResetPinStatusTextBlock.Text = "Nejdřív vyberte žáka.";
            return;
        }

        if (IsOnlineApiMode)
        {
            if (!EnsureTeacherAuthenticated())
            {
                return;
            }

            var deleteResult = _onlineApiDataSource.DeleteStudent(_selectedStudentId);
            ResetPinStatusTextBlock.Text = deleteResult.Message;
            if (!deleteResult.Success)
            {
                HandleOnlineAuthorizationFailureIfNeeded();
                return;
            }

            LoadStudentsFromOnlineApi();
            ResetPinStatusTextBlock.Text = deleteResult.ResultsDeleted
                ? $"Žák {selectedStudent.DisplayName} byl smazán včetně výsledků."
                : $"Žák {selectedStudent.DisplayName} byl smazán, ale výsledky se nepodařilo úplně odstranit.";
            return;
        }

        ResetPinStatusTextBlock.Text = "Mazání žáka je v tomto kroku dostupné jen v OnlineApi režimu.";
    }

    private async void OnGenerateStudentConfigClick(object? sender, RoutedEventArgs e)
    {
        StudentActionStatusBorder.IsVisible = true;

        if (IsOnlineApiMode && !EnsureTeacherAuthenticated())
        {
            return;
        }

        if (_isRemoteReadOnlyData)
        {
            StudentConfigStatusTextBlock.Text = "Data ze serveru jsou načtená pouze pro čtení. Soubor pro žáka zatím generujte v LocalFiles.";
            return;
        }

        if (StudentListBox.SelectedItem is not StudentListItem selectedStudent)
        {
            StudentConfigStatusTextBlock.Text = "Nejdřív vyberte žáka.";
            return;
        }

        if (!IsOnlineApiMode && string.IsNullOrWhiteSpace(_currentDataFolderPath))
        {
            StudentConfigStatusTextBlock.Text = "Nejdřív načtěte platnou datovou složku.";
            return;
        }

        var classId = IsOnlineApiMode
            ? _configuration.DataConnection.ClassId
            : Path.GetFileName(_currentDataFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(classId) && IsOnlineApiMode)
        {
            classId = DataConnectionSettings.DefaultClassId;
        }
        if (string.IsNullOrWhiteSpace(classId))
        {
            StudentConfigStatusTextBlock.Text = "Nepodařilo se určit identifikaci třídy.";
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Uložit soubor pro žáka",
            SuggestedFileName = $"{selectedStudent.LoginCode}.smtcfg",
            FileTypeChoices =
            [
                new FilePickerFileType("Soubor pro žáka")
                {
                    Patterns = ["*.smtcfg"]
                }
            ]
        });
        if (file is null)
        {
            StudentConfigStatusTextBlock.Text = "Uložení souboru bylo zrušeno.";
            return;
        }

        var path = file.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            StudentConfigStatusTextBlock.Text = "Vybrané umístění souboru není podporované.";
            return;
        }

        try
        {
            _studentConfigFileService.SaveConfigFile(
                path,
                classId,
                selectedStudent.StudentId,
                _configuration.DataConnection.ApiBaseUrl);
            StudentConfigStatusTextBlock.Text =
                $"Soubor pro žáka {selectedStudent.DisplayName} byl uložen. Obsahuje server a neobsahuje PIN.";
            DiagnosticLogService.Log("TeacherApp", $"Student config file generated for class '{classId}', student '{selectedStudent.StudentId}'.");
        }
        catch (ArgumentException ex)
        {
            DiagnosticLogService.LogError("TeacherApp", $"Student config file generation failed for student '{selectedStudent.StudentId}'", ex);
            StudentConfigStatusTextBlock.Text = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            DiagnosticLogService.LogError("TeacherApp", $"Student config file generation failed for student '{selectedStudent.StudentId}'", ex);
            StudentConfigStatusTextBlock.Text = "Soubor pro žáka se nepodařilo bezpečně uložit.";
        }
    }

    private void ClearStudentDetail()
    {
        DetailNameTextBlock.Text = "Není vybraný žádný žák.";
        DetailLoginCodeTextBlock.Text = string.Empty;
        DetailAccountStatusTextBlock.Text = string.Empty;
        DetailMustChangePinTextBlock.Text = string.Empty;
        DetailTemporaryPinPendingTextBlock.Text = string.Empty;
        DetailCreatedAtTextBlock.Text = string.Empty;
        DetailPinResetAtTextBlock.Text = string.Empty;
        ResetPinStatusTextBlock.Text = string.Empty;
        StudentConfigStatusTextBlock.Text = string.Empty;
        UpdateStatusCardVisibility();
    }

    private void ClearTransientStudentActionMessages()
    {
        CreateStudentStatusTextBlock.Text = string.Empty;
        StudentConfigStatusTextBlock.Text = string.Empty;
    }

    private void LoadPendingTemporaryPin(StudentListItem student)
    {
        ResetPinStatusTextBlock.Text = string.Empty;

        if (!student.MustChangePin || !student.TemporaryPinPending)
        {
            return;
        }

        ResetPinStatusTextBlock.Text =
            "Dočasný PIN čeká na první přihlášení, ale z bezpečnostních důvodů ho už nelze znovu zobrazit. Pokud je potřeba, resetujte PIN znovu.";
    }

    private void LoadStudentResults(StudentListItem student)
    {
        _studentRecentActivities.Clear();

        if (IsOnlineApiMode)
        {
            var onlineResult = _onlineApiDataSource.ReadStudentResults(student.StudentId);
            ApplyStudentResults(onlineResult);
            HandleOnlineAuthorizationFailureIfNeeded();
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentDataFolderPath))
        {
            ClearStudentResults("Výsledky žáka nejsou k dispozici.");
            return;
        }

        var result = _studentResultsReader.ReadResults(_currentDataFolderPath, student.StudentId);
        ApplyStudentResults(result);
    }

    private void ApplyStudentResults(StudentResultsReadResult result)
    {
        ResultsStatusTextBlock.Text = result.Message;
        if (!result.Success)
        {
            ClearStudentResults(result.Message);
            return;
        }

        ResultsSessionCountTextBlock.Text = $"Počet her: {result.SessionCountText}";
        ResultsAttemptCountTextBlock.Text = $"Počet pokusů: {result.AttemptCountText}";
        ResultsLastActivityTextBlock.Text = $"Poslední aktivita: {result.LastActivityText}";
        ResultsCorrectAnswersTextBlock.Text = $"Správné odpovědi: {result.CorrectAnswersText}";
        ResultsIncorrectAnswersTextBlock.Text = $"Chyby: {result.IncorrectAnswersText}";
        ResultsAccuracyTextBlock.Text = $"Průměrná úspěšnost: {result.AccuracyText}";
        ResultsModeOverviewTextBlock.Text = $"Přehled podle režimu:{Environment.NewLine}{result.ModeOverviewText}";

        foreach (var activity in result.RecentActivities)
        {
            _studentRecentActivities.Add(activity);
        }

        RefreshTeacherUiState();
    }

    private void ClearStudentResults(string message)
    {
        _studentRecentActivities.Clear();
        ResultsStatusTextBlock.Text = message;
        ResultsSessionCountTextBlock.Text = string.Empty;
        ResultsAttemptCountTextBlock.Text = string.Empty;
        ResultsLastActivityTextBlock.Text = string.Empty;
        ResultsCorrectAnswersTextBlock.Text = string.Empty;
        ResultsIncorrectAnswersTextBlock.Text = string.Empty;
        ResultsAccuracyTextBlock.Text = string.Empty;
        ResultsModeOverviewTextBlock.Text = string.Empty;
        RefreshTeacherUiState();
    }

    private void LoadClassOverview()
    {
        _classOverviewStudents.Clear();

        if (IsOnlineApiMode)
        {
            LoadClassOverviewFromOnlineApi();
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentDataFolderPath))
        {
            ClearClassOverview("Třídní přehled není k dispozici.");
            return;
        }

        var result = _classOverviewReader.ReadClassOverview(_currentDataFolderPath, _students.ToList());
        ClassOverviewStatusTextBlock.Text = result.Message;
        ClassLoadedStudentsTextBlock.Text = $"Počet načtených žáků: {result.LoadedStudentsText}";
        ClassStudentsWithResultsTextBlock.Text = $"Žáci s výsledky: {result.StudentsWithResultsText}";
        ClassStudentsWithoutResultsTextBlock.Text = $"Žáci bez výsledků: {result.StudentsWithoutResultsText}";
        ClassSessionCountTextBlock.Text = $"Celkem her: {result.SessionCountText}";
        ClassAttemptCountTextBlock.Text = $"Celkem pokusů: {result.AttemptCountText}";
        ClassCorrectAnswersTextBlock.Text = $"Celkem správně: {result.CorrectAnswersText}";
        ClassIncorrectAnswersTextBlock.Text = $"Celkem chyb: {result.IncorrectAnswersText}";
        ClassAccuracyTextBlock.Text = $"Průměrná úspěšnost třídy: {result.AccuracyText}";
        ClassLastActivityTextBlock.Text = $"Poslední aktivita ve třídě: {result.LastActivityText}";
        ClassModeOverviewTextBlock.Text = $"Souhrn podle režimu:{Environment.NewLine}{result.ModeOverviewText}";

        foreach (var student in result.Students)
        {
            _classOverviewStudents.Add(student);
        }
    }

    private void LoadClassOverviewFromOnlineApi()
    {
        _classOverviewStudents.Clear();
        var result = _onlineApiDataSource.ReadClassOverview();
        if (_onlineApiDataSource.LastAuthorizationFailed)
        {
            HandleOnlineAuthorizationFailureIfNeeded();
            return;
        }

        ClassOverviewStatusTextBlock.Text = result.Message;
        ClassLoadedStudentsTextBlock.Text = $"Počet načtených žáků: {result.LoadedStudentsText}";
        ClassStudentsWithResultsTextBlock.Text = $"Žáci s výsledky: {result.StudentsWithResultsText}";
        ClassStudentsWithoutResultsTextBlock.Text = $"Žáci bez výsledků: {result.StudentsWithoutResultsText}";
        ClassSessionCountTextBlock.Text = $"Celkem her: {result.SessionCountText}";
        ClassAttemptCountTextBlock.Text = $"Celkem pokusů: {result.AttemptCountText}";
        ClassCorrectAnswersTextBlock.Text = $"Celkem správně: {result.CorrectAnswersText}";
        ClassIncorrectAnswersTextBlock.Text = $"Celkem chyb: {result.IncorrectAnswersText}";
        ClassAccuracyTextBlock.Text = $"Průměrná úspěšnost třídy: {result.AccuracyText}";
        ClassLastActivityTextBlock.Text = $"Poslední aktivita ve třídě: {result.LastActivityText}";
        ClassModeOverviewTextBlock.Text = result.ModeOverviewText;

        foreach (var student in result.Students)
        {
            _classOverviewStudents.Add(student);
        }
    }

    private void LoadClassActivitiesFromOnlineApi()
    {
        _recentActivities.Clear();
        foreach (var activity in _onlineApiDataSource.ReadClassActivities(10))
        {
            _recentActivities.Add(activity);
        }

        HandleOnlineAuthorizationFailureIfNeeded();
        UpdateEmptyStates();
    }

    private void ClearClassOverview(string message)
    {
        _recentActivities.Clear();
        _classOverviewStudents.Clear();
        ClassOverviewStatusTextBlock.Text = message;
        ClassLoadedStudentsTextBlock.Text = string.Empty;
        ClassStudentsWithResultsTextBlock.Text = string.Empty;
        ClassStudentsWithoutResultsTextBlock.Text = string.Empty;
        ClassSessionCountTextBlock.Text = string.Empty;
        ClassAttemptCountTextBlock.Text = string.Empty;
        ClassCorrectAnswersTextBlock.Text = string.Empty;
        ClassIncorrectAnswersTextBlock.Text = string.Empty;
        ClassAccuracyTextBlock.Text = string.Empty;
        ClassLastActivityTextBlock.Text = string.Empty;
        ClassModeOverviewTextBlock.Text = string.Empty;
        UpdateEmptyStates();
    }

    private void UpdateEmptyStates()
    {
        var hasStudents = _students.Count > 0;
        StudentListBox.IsVisible = hasStudents;
        StudentListPlaceholderBorder.IsVisible = !hasStudents;

        var hasClassMetrics =
            !string.IsNullOrWhiteSpace(ClassLoadedStudentsTextBlock.Text) ||
            !string.IsNullOrWhiteSpace(ClassSessionCountTextBlock.Text) ||
            !string.IsNullOrWhiteSpace(ClassAccuracyTextBlock.Text);
        ClassOverviewMetricsGrid.IsVisible = hasClassMetrics;
        ClassOverviewDetailsPanel.IsVisible = hasClassMetrics ||
            !string.IsNullOrWhiteSpace(ClassLastActivityTextBlock.Text) ||
            !string.IsNullOrWhiteSpace(ClassModeOverviewTextBlock.Text) ||
            _classOverviewStudents.Count > 0;
        ClassOverviewStudentsListBox.IsVisible = _classOverviewStudents.Count > 0;
        ClassOverviewStudentsEmptyTextBlock.IsVisible = ClassOverviewDetailsPanel.IsVisible &&
            _classOverviewStudents.Count == 0;

        ResultsMetricsGrid.IsVisible =
            !string.IsNullOrWhiteSpace(ResultsSessionCountTextBlock.Text) ||
            !string.IsNullOrWhiteSpace(ResultsAttemptCountTextBlock.Text) ||
            !string.IsNullOrWhiteSpace(ResultsAccuracyTextBlock.Text);

        var hasStudentActivities = _studentRecentActivities.Count > 0;
        StudentRecentActivitiesListBox.IsVisible = hasStudentActivities;
        StudentRecentActivitiesEmptyTextBlock.IsVisible = !hasStudentActivities;

        var hasClassActivities = _recentActivities.Count > 0;
        RecentActivitiesListBox.IsVisible = hasClassActivities;
        RecentActivitiesEmptyTextBlock.IsVisible = !hasClassActivities;
        UpdateStatusCardVisibility();
    }

    private void RefreshTeacherUiState()
    {
        UpdateEmptyStates();
        UpdateTeacherAuthUi();
    }

    private void UpdateStatusCardVisibility()
    {
        TeacherLoginStatusBorder.IsVisible = TeacherLoginStatusTextBlock.IsVisible &&
            !string.IsNullOrWhiteSpace(TeacherLoginStatusTextBlock.Text);
        CreateStudentStatusBorder.IsVisible =
            !string.IsNullOrWhiteSpace(CreateStudentStatusTextBlock.Text);
        StudentActionStatusBorder.IsVisible =
            !string.IsNullOrWhiteSpace(ResetPinStatusTextBlock.Text) ||
            !string.IsNullOrWhiteSpace(StudentConfigStatusTextBlock.Text);
    }
}

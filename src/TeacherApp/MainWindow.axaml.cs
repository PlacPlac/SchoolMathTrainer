using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using SharedCore.Models;
using SharedCore.Services;
using TeacherApp.Data;
using TeacherApp.Settings;

namespace TeacherApp;

public partial class MainWindow : Window
{
    private static readonly GridLength TeacherMainColumnWidth = new(1, GridUnitType.Star);
    private static readonly GridLength TeacherDetailColumnWidth = new(440, GridUnitType.Pixel);
    private static readonly GridLength AdminMainColumnWidth = new(0.8, GridUnitType.Star);
    private static readonly GridLength AdminDetailColumnWidth = new(1.2, GridUnitType.Star);
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
    private readonly ObservableCollection<AdminTeacherListItemView> _adminTeachers = [];
    private static readonly RoleOption[] TeacherRoleOptions =
    [
        new(TeacherRoles.Admin, "Administrátor"),
        new(TeacherRoles.Teacher, "Učitel")
    ];
    private static readonly string TeacherPasswordRuleText = TeacherPasswordRules.RequirementsText;
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
        AdminTeachersListBox.ItemsSource = _adminTeachers;
        AdminTeacherRoleComboBox.ItemsSource = TeacherRoleOptions;
        AdminTeacherRoleComboBox.SelectedItem = FindRoleOption(TeacherRoles.Teacher);
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
        var adminActionsEnabled = IsOnlineApiMode && isAuthenticated && _onlineApiDataSource.IsAdmin;
        TeacherActionsPanel.IsVisible = teacherActionsEnabled;
        TeacherAdminMainArea.IsVisible = teacherActionsEnabled;
        TeacherAdminDetailArea.IsVisible = teacherActionsEnabled;
        AdminTeachersPanel.IsVisible = adminActionsEnabled;
        ApplyRoleLayout(adminActionsEnabled);

        var studentActionEnabled = teacherActionsEnabled && StudentListBox.SelectedItem is StudentListItem &&
            !string.IsNullOrWhiteSpace(_selectedStudentId);
        var adminTeacherSelected = adminActionsEnabled && AdminTeachersListBox.SelectedItem is AdminTeacherListItemView;
        RefreshDataButton.IsEnabled = teacherActionsEnabled;
        LoadFromServerButton.IsEnabled = teacherActionsEnabled;
        CreateStudentButton.IsEnabled = teacherActionsEnabled;
        ResetPinButton.IsEnabled = studentActionEnabled;
        DeleteStudentButton.IsEnabled = studentActionEnabled;
        GenerateStudentConfigButton.IsEnabled = studentActionEnabled;
        AdminRefreshTeachersButton.IsEnabled = adminActionsEnabled;
        AdminAddTeacherButton.IsEnabled = adminActionsEnabled;
        AdminUpdateDisplayNameButton.IsEnabled = adminTeacherSelected;
        AdminChangeRoleButton.IsEnabled = adminTeacherSelected;
        AdminResetTeacherPasswordButton.IsEnabled = adminTeacherSelected;
        AdminActivateTeacherButton.IsEnabled = adminTeacherSelected;
        AdminDeactivateTeacherButton.IsEnabled = adminTeacherSelected;
        AdminDeleteTeacherButton.IsEnabled = adminTeacherSelected;
        UpdateStatusCardVisibility();
    }

    private void ApplyRoleLayout(bool isAdmin)
    {
        TeacherAppLayoutGrid.ColumnDefinitions[1].Width = isAdmin ? AdminMainColumnWidth : TeacherMainColumnWidth;
        TeacherAppLayoutGrid.ColumnDefinitions[2].Width = isAdmin ? AdminDetailColumnWidth : TeacherDetailColumnWidth;
    }

    private void ClearTeacherData(string message)
    {
        _students.Clear();
        _recentActivities.Clear();
        _studentRecentActivities.Clear();
        _classOverviewStudents.Clear();
        ClearAdminTeacherData();
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

    private async void OnTeacherLoginClick(object? sender, RoutedEventArgs e)
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
            : $"Přihlášeno: {login.Username}. Role: {ToRoleDisplay(login.Role)}.";
        UpdateTeacherAuthUi();
        if (_onlineApiDataSource.IsAdmin)
        {
            await LoadAdminTeachersAsync();
        }

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

    private async void OnAdminRefreshTeachersClick(object? sender, RoutedEventArgs e)
    {
        await LoadAdminTeachersAsync();
    }

    private async void OnAdminAddTeacherClick(object? sender, RoutedEventArgs e)
    {
        if (!EnsureAdminAccess())
        {
            return;
        }

        var dialogResult = await ShowCreateTeacherDialogAsync();
        if (dialogResult is null)
        {
            SetAdminTeacherStatus("Vytvoření učitele bylo zrušeno.");
            return;
        }

        await LoadAdminTeachersAsync(dialogResult.UsernameToSelect);
        SetAdminTeacherStatus(dialogResult.Message);
    }

    private async void OnAdminUpdateTeacherClick(object? sender, RoutedEventArgs e)
    {
        if (!EnsureAdminAccess() || AdminTeachersListBox.SelectedItem is not AdminTeacherListItemView selectedTeacher)
        {
            SetAdminTeacherStatus("Nejdřív vyberte učitele.");
            return;
        }

        var role = GetSelectedRole(AdminTeacherRoleComboBox, selectedTeacher.Role);
        var result = await _onlineApiDataSource.UpdateAdminTeacherAsync(
            selectedTeacher.Username,
            AdminTeacherDisplayNameTextBox.Text ?? string.Empty,
            role);
        await ApplyAdminTeacherOperationResultAsync(result, selectedTeacher.Username);
    }

    private async void OnAdminResetTeacherPasswordClick(object? sender, RoutedEventArgs e)
    {
        if (!EnsureAdminAccess() || AdminTeachersListBox.SelectedItem is not AdminTeacherListItemView selectedTeacher)
        {
            SetAdminTeacherStatus("Nejdřív vyberte učitele.");
            return;
        }

        var dialogResult = await ShowPasswordResetDialogAsync(selectedTeacher.Username);
        if (dialogResult is null)
        {
            SetAdminTeacherStatus("Reset hesla byl zrušen.");
            return;
        }

        await LoadAdminTeachersAsync(dialogResult.UsernameToSelect);
        SetAdminTeacherStatus(dialogResult.Message);
    }

    private async void OnAdminDeactivateTeacherClick(object? sender, RoutedEventArgs e)
    {
        if (!EnsureAdminAccess() || AdminTeachersListBox.SelectedItem is not AdminTeacherListItemView selectedTeacher)
        {
            SetAdminTeacherStatus("Nejdřív vyberte učitele.");
            return;
        }

        var result = await _onlineApiDataSource.DeactivateAdminTeacherAsync(selectedTeacher.Username);
        await ApplyAdminTeacherOperationResultAsync(result, selectedTeacher.Username);
    }

    private async void OnAdminActivateTeacherClick(object? sender, RoutedEventArgs e)
    {
        if (!EnsureAdminAccess() || AdminTeachersListBox.SelectedItem is not AdminTeacherListItemView selectedTeacher)
        {
            SetAdminTeacherStatus("Nejdřív vyberte učitele.");
            return;
        }

        var result = await _onlineApiDataSource.ActivateAdminTeacherAsync(selectedTeacher.Username);
        await ApplyAdminTeacherOperationResultAsync(result, selectedTeacher.Username);
    }

    private async void OnAdminDeleteTeacherClick(object? sender, RoutedEventArgs e)
    {
        if (!EnsureAdminAccess() || AdminTeachersListBox.SelectedItem is not AdminTeacherListItemView selectedTeacher)
        {
            SetAdminTeacherStatus("Nejdřív vyberte učitele.");
            return;
        }

        var confirmed = await ShowDeleteTeacherConfirmationAsync(selectedTeacher.Username);
        if (!confirmed)
        {
            SetAdminTeacherStatus("Odstranění učitele bylo zrušeno.");
            return;
        }

        var result = await _onlineApiDataSource.DeleteAdminTeacherAsync(selectedTeacher.Username);
        await ApplyAdminTeacherOperationResultAsync(result, string.Empty);
    }

    private void OnAdminTeacherSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (AdminTeachersListBox.SelectedItem is not AdminTeacherListItemView selectedTeacher)
        {
            AdminTeacherDisplayNameTextBox.Text = string.Empty;
            AdminTeacherRoleComboBox.SelectedItem = FindRoleOption(TeacherRoles.Teacher);
            RefreshTeacherUiState();
            return;
        }

        AdminTeacherDisplayNameTextBox.Text = selectedTeacher.DisplayName;
        AdminTeacherRoleComboBox.SelectedItem = FindRoleOption(selectedTeacher.Role);
        RefreshTeacherUiState();
    }

    private bool EnsureAdminAccess()
    {
        if (!EnsureTeacherAuthenticated())
        {
            return false;
        }

        if (_onlineApiDataSource.IsAdmin)
        {
            return true;
        }

        ClearAdminTeacherData();
        SetAdminTeacherStatus("Správa učitelů je dostupná jen pro administrátora.");
        UpdateTeacherAuthUi();
        return false;
    }

    private async Task LoadAdminTeachersAsync(string usernameToSelect = "")
    {
        if (!EnsureAdminAccess())
        {
            return;
        }

        SetAdminTeacherStatus("Načítám seznam učitelů...");
        var result = await _onlineApiDataSource.GetAdminTeachersAsync();
        if (!result.Success)
        {
            SetAdminTeacherStatus(result.Message);
            HandleOnlineAuthorizationFailureIfNeeded();
            if (_onlineApiDataSource.LastForbidden)
            {
                ClearAdminTeacherData();
                UpdateTeacherAuthUi();
            }

            return;
        }

        _adminTeachers.Clear();
        foreach (var teacher in result.Teachers.OrderBy(item => item.Username, StringComparer.CurrentCultureIgnoreCase))
        {
            _adminTeachers.Add(new AdminTeacherListItemView(teacher));
        }

        if (!string.IsNullOrWhiteSpace(usernameToSelect))
        {
            AdminTeachersListBox.SelectedItem = _adminTeachers.FirstOrDefault(teacher =>
                string.Equals(teacher.Username, usernameToSelect, StringComparison.OrdinalIgnoreCase));
        }

        SetAdminTeacherStatus(result.Message);
        RefreshTeacherUiState();
    }

    private async Task ApplyAdminTeacherOperationResultAsync(AdminTeacherOperationResult result, string usernameToSelect)
    {
        SetAdminTeacherStatus(result.Message);
        if (!result.Success)
        {
            HandleOnlineAuthorizationFailureIfNeeded();
            if (_onlineApiDataSource.LastForbidden)
            {
                ClearAdminTeacherData();
                UpdateTeacherAuthUi();
            }

            return;
        }

        await LoadAdminTeachersAsync(usernameToSelect);
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            SetAdminTeacherStatus(result.Message);
        }
    }

    private void ClearAdminTeacherData()
    {
        _adminTeachers.Clear();
        AdminTeachersListBox.SelectedItem = null;
        AdminTeacherDisplayNameTextBox.Text = string.Empty;
        AdminTeacherRoleComboBox.SelectedItem = FindRoleOption(TeacherRoles.Teacher);
        AdminTeacherStatusTextBlock.Text = string.Empty;
    }

    private void SetAdminTeacherStatus(string message)
    {
        AdminTeacherStatusTextBlock.Text = message;
        UpdateStatusCardVisibility();
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

    private async void OnCreateStudentClick(object? sender, RoutedEventArgs e)
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
                DiagnosticLogService.Log("TeacherApp", $"Online create student failed. HasMessage={!string.IsNullOrWhiteSpace(onlineCreateResult.Message)}.");
                HandleOnlineAuthorizationFailureIfNeeded();
                return;
            }

            NewStudentNameTextBox.Text = string.Empty;
            LoadStudentsFromOnlineApi(onlineCreateResult.Account.StudentId);
            CreateStudentStatusTextBlock.Text =
                $"Účet byl vytvořen pro žáka {onlineCreateResult.Account.DisplayName}. Dočasný PIN byl zobrazen v samostatném okně.";
            await ShowTemporaryPinWindowAsync(
                onlineCreateResult.Account.DisplayName,
                onlineCreateResult.Account.LoginCode,
                onlineCreateResult.TemporaryPin);
            DiagnosticLogService.Log("TeacherApp", $"Online create student completed. HasStudentId={!string.IsNullOrWhiteSpace(onlineCreateResult.Account.StudentId)}. Temporary credential value was not logged.");
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
            DiagnosticLogService.Log("TeacherApp", $"Create student failed. HasMessage={!string.IsNullOrWhiteSpace(createResult.Message)}.");
            return;
        }

        NewStudentNameTextBox.Text = string.Empty;
        LoadStudentsFromFolder(_currentDataFolderPath, createResult.Account.StudentId);
        CreateStudentStatusTextBlock.Text =
            $"Účet byl vytvořen pro žáka {createResult.Account.DisplayName}. Dočasný PIN byl zobrazen v samostatném okně.";
        await ShowTemporaryPinWindowAsync(
            createResult.Account.DisplayName,
            createResult.Account.LoginCode,
            createResult.TemporaryPin);
        DiagnosticLogService.Log("TeacherApp", $"Create student completed. HasStudentId={!string.IsNullOrWhiteSpace(createResult.Account.StudentId)}. Temporary credential value was not logged.");
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

    private async void OnResetPinClick(object? sender, RoutedEventArgs e)
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
                DiagnosticLogService.Log("TeacherApp", $"Online credential reset failed. HasStudentId={!string.IsNullOrWhiteSpace(_selectedStudentId)}. HasMessage={!string.IsNullOrWhiteSpace(onlineResetResult.Message)}.");
                HandleOnlineAuthorizationFailureIfNeeded();
                return;
            }

            LoadStudentsFromOnlineApi(onlineResetResult.Account.StudentId);
            ResetPinStatusTextBlock.Text =
                $"PIN byl resetován pro žáka {onlineResetResult.Account.DisplayName}. Dočasný PIN byl zobrazen v samostatném okně.";
            await ShowTemporaryPinWindowAsync(
                onlineResetResult.Account.DisplayName,
                onlineResetResult.Account.LoginCode,
                onlineResetResult.TemporaryPin);
            DiagnosticLogService.Log("TeacherApp", $"Online credential reset completed. HasStudentId={!string.IsNullOrWhiteSpace(onlineResetResult.Account.StudentId)}. Temporary credential value was not logged.");
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
            DiagnosticLogService.Log("TeacherApp", $"Credential reset failed. HasStudentId={!string.IsNullOrWhiteSpace(_selectedStudentId)}. HasMessage={!string.IsNullOrWhiteSpace(resetResult.Message)}.");
            return;
        }

        LoadStudentsFromFolder(_currentDataFolderPath, resetResult.Account.StudentId);
        ResetPinStatusTextBlock.Text =
            $"PIN byl resetován pro žáka {resetResult.Account.DisplayName}. Dočasný PIN byl zobrazen v samostatném okně.";
        await ShowTemporaryPinWindowAsync(
            resetResult.Account.DisplayName,
            resetResult.Account.LoginCode,
            resetResult.TemporaryPin);
        DiagnosticLogService.Log("TeacherApp", $"Credential reset completed. HasStudentId={!string.IsNullOrWhiteSpace(resetResult.Account.StudentId)}. Temporary credential value was not logged.");
    }

    private async Task ShowTemporaryPinWindowAsync(string studentName, string loginCode, string temporaryPin)
    {
        if (string.IsNullOrWhiteSpace(temporaryPin))
        {
            return;
        }

        var dialog = new TemporaryPinWindow(studentName, loginCode, temporaryPin);
        await dialog.ShowDialog(this);
    }

    private async Task<AdminTeacherDialogResult?> ShowCreateTeacherDialogAsync()
    {
        var usernameBox = CreateDialogTextBox("Uživatelské jméno");
        var displayNameBox = CreateDialogTextBox("Zobrazované jméno");
        var passwordBox = CreateDialogTextBox("Heslo");
        var confirmPasswordBox = CreateDialogTextBox("Heslo znovu");
        passwordBox.PasswordChar = '*';
        confirmPasswordBox.PasswordChar = '*';
        var roleBox = CreateDialogRoleComboBox();
        var errorTextBlock = CreateDialogErrorTextBlock();

        var dialog = CreateAdminDialogWindow("Přidat učitele");
        var createButton = CreateDialogButton("Vytvořit", "primary");
        var cancelButton = CreateDialogButton("Zrušit", "secondary");
        createButton.Click += async (_, _) =>
        {
            if (!TryValidateCreateTeacherDialog(
                usernameBox.Text ?? string.Empty,
                displayNameBox.Text ?? string.Empty,
                passwordBox.Text ?? string.Empty,
                confirmPasswordBox.Text ?? string.Empty,
                errorTextBlock))
            {
                return;
            }

            createButton.IsEnabled = false;
            cancelButton.IsEnabled = false;
            SetDialogError(errorTextBlock, string.Empty);
            var username = usernameBox.Text ?? string.Empty;
            var result = await _onlineApiDataSource.CreateAdminTeacherAsync(
                username,
                displayNameBox.Text ?? string.Empty,
                passwordBox.Text ?? string.Empty,
                GetSelectedRole(roleBox, TeacherRoles.Teacher));
            passwordBox.Text = string.Empty;
            confirmPasswordBox.Text = string.Empty;
            createButton.IsEnabled = true;
            cancelButton.IsEnabled = true;

            if (!result.Success)
            {
                SetDialogError(errorTextBlock, result.Message);
                return;
            }

            dialog.Close(new AdminTeacherDialogResult(result.Message, username));
        };
        cancelButton.Click += (_, _) =>
        {
            passwordBox.Text = string.Empty;
            confirmPasswordBox.Text = string.Empty;
            dialog.Close(null);
        };

        dialog.Content = CreateDialogContent(
            "Nový učitelský účet",
            $"Vyplňte údaje učitele. {TeacherUsernameRules.HelpText} {TeacherPasswordRuleText}",
            [
                CreateLabeledField("Uživatelské jméno", usernameBox),
                CreateLabeledField("Zobrazované jméno", displayNameBox),
                CreateLabeledField("Heslo", passwordBox),
                CreateLabeledField("Heslo znovu", confirmPasswordBox),
                CreateLabeledField("Role", roleBox),
                errorTextBlock
            ],
            [cancelButton, createButton]);
        return await dialog.ShowDialog<AdminTeacherDialogResult?>(this);
    }

    private async Task<AdminTeacherDialogResult?> ShowPasswordResetDialogAsync(string username)
    {
        var passwordBox = CreateDialogTextBox("Nové heslo");
        var confirmPasswordBox = CreateDialogTextBox("Nové heslo znovu");
        passwordBox.PasswordChar = '*';
        confirmPasswordBox.PasswordChar = '*';
        var errorTextBlock = CreateDialogErrorTextBlock();
        var dialog = CreateAdminDialogWindow("Reset hesla");
        var saveButton = CreateDialogButton("Resetovat", "primary");
        var cancelButton = CreateDialogButton("Zrušit", "secondary");
        saveButton.Click += async (_, _) =>
        {
            if (!TryValidatePasswordPair(
                passwordBox.Text ?? string.Empty,
                confirmPasswordBox.Text ?? string.Empty,
                errorTextBlock))
            {
                return;
            }

            saveButton.IsEnabled = false;
            cancelButton.IsEnabled = false;
            SetDialogError(errorTextBlock, string.Empty);
            var result = await _onlineApiDataSource.ResetAdminTeacherPasswordAsync(username, passwordBox.Text ?? string.Empty);
            passwordBox.Text = string.Empty;
            confirmPasswordBox.Text = string.Empty;
            saveButton.IsEnabled = true;
            cancelButton.IsEnabled = true;

            if (!result.Success)
            {
                SetDialogError(errorTextBlock, result.Message);
                return;
            }

            dialog.Close(new AdminTeacherDialogResult(result.Message, username));
        };
        cancelButton.Click += (_, _) =>
        {
            passwordBox.Text = string.Empty;
            confirmPasswordBox.Text = string.Empty;
            dialog.Close(null);
        };

        dialog.Content = CreateDialogContent(
            "Reset hesla",
            $"Zadejte nové heslo pro učitele {username}. {TeacherPasswordRuleText}",
            [
                CreateLabeledField("Nové heslo", passwordBox),
                CreateLabeledField("Nové heslo znovu", confirmPasswordBox),
                errorTextBlock
            ],
            [cancelButton, saveButton]);
        return await dialog.ShowDialog<AdminTeacherDialogResult?>(this);
    }

    private async Task<bool> ShowDeleteTeacherConfirmationAsync(string username)
    {
        var dialog = CreateAdminDialogWindow("Odstranit učitele");
        var deleteButton = CreateDialogButton("Odstranit", "danger");
        var cancelButton = CreateDialogButton("Zrušit", "secondary");
        deleteButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);
        dialog.Content = CreateDialogContent(
            "Odstranit učitele",
            $"Opravdu odstranit učitelský účet {username}? Žákovská data ani audit se tím nemažou. Server nedovolí odstranit posledního administrátora ani právě přihlášený účet.",
            [],
            [cancelButton, deleteButton]);
        return await dialog.ShowDialog<bool>(this);
    }

    private static bool TryValidateCreateTeacherDialog(
        string username,
        string displayName,
        string password,
        string confirmPassword,
        TextBlock errorTextBlock)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            SetDialogError(errorTextBlock, "Uživatelské jméno nesmí být prázdné.");
            return false;
        }

        if (!TeacherUsernameRules.TryNormalize(username, out _, out var usernameError))
        {
            SetDialogError(errorTextBlock, usernameError);
            return false;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            SetDialogError(errorTextBlock, "Zobrazované jméno nesmí být prázdné.");
            return false;
        }

        return TryValidatePasswordPair(password, confirmPassword, errorTextBlock);
    }

    private static bool TryValidatePasswordPair(string password, string confirmPassword, TextBlock errorTextBlock)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            SetDialogError(errorTextBlock, "Heslo nesmí být prázdné.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(confirmPassword))
        {
            SetDialogError(errorTextBlock, "Heslo znovu nesmí být prázdné.");
            return false;
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            SetDialogError(errorTextBlock, "Zadaná hesla se neshodují.");
            return false;
        }

        if (!TeacherPasswordRules.TryValidate(password, out var passwordError))
        {
            SetDialogError(errorTextBlock, passwordError);
            return false;
        }

        SetDialogError(errorTextBlock, string.Empty);
        return true;
    }

    private static void SetDialogError(TextBlock errorTextBlock, string message)
    {
        errorTextBlock.Text = message;
        errorTextBlock.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private static Window CreateAdminDialogWindow(string title) =>
        new()
        {
            Title = title,
            Width = 460,
            Height = 620,
            MinWidth = 420,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#080D18"))
        };

    private static StackPanel CreateDialogContent(
        string title,
        string description,
        IReadOnlyList<Control> fields,
        IReadOnlyList<Button> buttons)
    {
        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 12
        };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFFFF")),
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#C9D5E8")),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        foreach (var field in fields)
        {
            panel.Children.Add(field);
        }

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        foreach (var button in buttons)
        {
            buttonPanel.Children.Add(button);
        }

        panel.Children.Add(buttonPanel);
        return panel;
    }

    private static TextBox CreateDialogTextBox(string watermark) =>
        new()
        {
            Watermark = watermark,
            Height = 40,
            Padding = new Avalonia.Thickness(12, 8),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0A1220")),
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFFFF")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#425A7D"))
        };

    private static StackPanel CreateLabeledField(string label, Control field)
    {
        var panel = new StackPanel
        {
            Spacing = 5
        };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E8EEF8")),
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        panel.Children.Add(field);
        return panel;
    }

    private static TextBlock CreateDialogErrorTextBlock() =>
        new()
        {
            Text = string.Empty,
            IsVisible = false,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FCA5A5")),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

    private static ComboBox CreateDialogRoleComboBox() =>
        new()
        {
            ItemsSource = TeacherRoleOptions,
            SelectedItem = FindRoleOption(TeacherRoles.Teacher),
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0A1220")),
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFFFF")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#425A7D"))
        };

    private static RoleOption FindRoleOption(string role)
    {
        var normalized = TeacherRoles.Normalize(role);
        return TeacherRoleOptions.First(option => string.Equals(option.Value, normalized, StringComparison.Ordinal));
    }

    private static string GetSelectedRole(ComboBox comboBox, string fallbackRole) =>
        comboBox.SelectedItem is RoleOption option
            ? option.Value
            : TeacherRoles.Normalize(fallbackRole);

    private static string ToRoleDisplay(string role) => FindRoleOption(role).DisplayName;

    private static Button CreateDialogButton(string text, string className)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 110,
            Height = 40,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFFFF"))
        };
        button.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(className == "danger" ? "#8B1E1E" : className == "primary" ? "#2563EB" : "#18263D"));
        button.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(className == "danger" ? "#EF4444" : className == "primary" ? "#60A5FA" : "#425A7D"));
        button.Classes.Add(className);
        return button;
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
            DiagnosticLogService.Log("TeacherApp", $"Student config file generated for class '{classId}', HasStudentId={!string.IsNullOrWhiteSpace(selectedStudent.StudentId)}.");
        }
        catch (ArgumentException ex)
        {
            DiagnosticLogService.LogError("TeacherApp", $"Student config file generation failed. HasStudentId={!string.IsNullOrWhiteSpace(selectedStudent.StudentId)}", ex);
            StudentConfigStatusTextBlock.Text = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            DiagnosticLogService.LogError("TeacherApp", $"Student config file generation failed. HasStudentId={!string.IsNullOrWhiteSpace(selectedStudent.StudentId)}", ex);
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
        AdminTeacherStatusBorder.IsVisible =
            AdminTeachersPanel.IsVisible &&
            !string.IsNullOrWhiteSpace(AdminTeacherStatusTextBlock.Text);
    }

    private sealed record AdminTeacherDialogResult(
        string Message,
        string UsernameToSelect);

    private sealed record RoleOption(string Value, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}

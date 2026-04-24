using System.Security.Cryptography;
using System.Text;
using SharedCore.Models;

namespace SharedCore.Services;

public sealed class StudentProgressService
{
    private const int PinHashIterations = 100_000;
    private const int PinSaltBytes = 16;
    private const int PinHashBytes = 32;
    private readonly AppConfiguration _configuration;
    private readonly FileSystemStorageService _storageService;
    private readonly StatisticsService _statisticsService;
    private readonly LoggingService _loggingService;
    private readonly CsvExportService _csvExportService;
    private readonly bool _canWritePublicOverview;

    public StudentProgressService(
        AppConfiguration configuration,
        FileSystemStorageService storageService,
        StatisticsService statisticsService,
        LoggingService loggingService,
        CsvExportService csvExportService,
        bool canWritePublicOverview = false)
    {
        _configuration = configuration;
        _storageService = storageService;
        _statisticsService = statisticsService;
        _loggingService = loggingService;
        _csvExportService = csvExportService;
        _canWritePublicOverview = canWritePublicOverview;

        EnsureRequiredDirectories();
        if (_canWritePublicOverview)
        {
            MigrateLegacyDataIfNeeded();
            EnsureAccountsForExistingStudents();
            RegeneratePublicClassOverview();
        }
    }

    public event EventHandler? DataChanged;

    public string CurrentStudentId { get; private set; } = string.Empty;
    public string CurrentStudentName { get; private set; } = "Nepřihlášený žák";
    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(CurrentStudentId);
    public AppConfiguration Configuration => _configuration;

    public void CompleteExternalLogin(string studentId, string displayName)
    {
        CurrentStudentId = studentId.Trim();
        CurrentStudentName = string.IsNullOrWhiteSpace(displayName)
            ? CurrentStudentId
            : displayName.Trim();
        OnDataChanged();
    }

    public StudentLoginResult LoginStudent(string loginCode, string pin, string newPin)
    {
        var account = GetStudentAccounts().FirstOrDefault(item =>
            item.IsActive &&
            string.Equals(item.LoginCode, loginCode.Trim(), StringComparison.OrdinalIgnoreCase));

        if (account is null)
        {
            return StudentLoginResult.Failed("Přihlašovací kód nebo PIN nesedí.");
        }

        if (!VerifyPin(pin, account.PinSalt, account.PinHash))
        {
            return StudentLoginResult.Failed("Přihlašovací kód nebo PIN nesedí.");
        }

        if (account.MustChangePin)
        {
            if (!IsValidPin(newPin))
            {
                return StudentLoginResult.PinChangeRequired("Zadej nový čtyřmístný PIN.");
            }

            SetAccountPin(account, newPin);
            account.MustChangePin = false;
            account.TemporaryPinPending = false;
            SaveStudentAccounts(ReplaceAccount(account));
        }

        CurrentStudentId = account.StudentId;
        CurrentStudentName = account.DisplayName;
        SaveStudentSummary(GetStudentSummary(CurrentStudentId, CurrentStudentName));
        _loggingService.Log($"Student login: {CurrentStudentName} ({CurrentStudentId})");
        OnDataChanged();

        return StudentLoginResult.LoggedIn(account.StudentId, account.DisplayName, $"Vítej, {account.DisplayName}. Můžeš začít.");
    }

    public void LogoutStudent()
    {
        CurrentStudentId = string.Empty;
        CurrentStudentName = "Nepřihlášený žák";
        OnDataChanged();
    }

    public StudentSession StartSession(LearningMode mode)
    {
        return new StudentSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            StudentId = CurrentStudentId,
            StudentName = CurrentStudentName,
            Mode = mode,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            LastActivityUtc = DateTime.UtcNow
        };
    }

    public void RecordAnswer(StudentSession session, AnswerRecord answerRecord)
    {
        if (!IsLoggedIn)
        {
            return;
        }

        session.StudentId = CurrentStudentId;
        session.StudentName = CurrentStudentName;
        session.LastActivityUtc = DateTime.UtcNow;
        session.CompletedAt = session.LastActivityUtc;
        session.RunningTotalCount++;

        if (answerRecord.IsCorrect)
        {
            session.RunningCorrectCount++;
        }
        else
        {
            session.RunningWrongCount++;
        }

        session.RunningSuccessPercent = session.RunningTotalCount == 0
            ? 0
            : Math.Round(session.RunningCorrectCount * 100d / session.RunningTotalCount, 1);

        answerRecord.StudentId = CurrentStudentId;
        answerRecord.StudentName = CurrentStudentName;
        answerRecord.SessionId = session.SessionId;
        answerRecord.LearningMode = session.Mode;
        answerRecord.Timestamp = DateTime.UtcNow;
        answerRecord.RunningCorrectCount = session.RunningCorrectCount;
        answerRecord.RunningWrongCount = session.RunningWrongCount;
        answerRecord.RunningTotalCount = session.RunningTotalCount;
        answerRecord.RunningSuccessPercent = session.RunningSuccessPercent;
        answerRecord.LastActivityUtc = session.LastActivityUtc;

        session.Answers.Add(answerRecord);
        SaveSession(session);
        SaveStudentSummary(GetStudentSummary(CurrentStudentId, CurrentStudentName));
        _loggingService.Log($"Answer stored for {CurrentStudentName}: {answerRecord.ExampleText} -> {answerRecord.ChosenAnswer}");
        OnDataChanged();
    }

    public void FinishSession(StudentSession session)
    {
        if (!IsLoggedIn)
        {
            return;
        }

        session.CompletedAt = DateTime.UtcNow;
        session.LastActivityUtc = session.CompletedAt;
        SaveSession(session);
        SaveStudentSummary(GetStudentSummary(CurrentStudentId, CurrentStudentName));
        OnDataChanged();
    }

    public StudentProgressSnapshot GetCurrentStudentSnapshot()
    {
        return IsLoggedIn
            ? GetStudentSnapshot(CurrentStudentId, CurrentStudentName)
            : new StudentProgressSnapshot { StudentName = CurrentStudentName };
    }

    public StudentProgressSnapshot GetStudentSnapshot(string studentId, string studentName)
    {
        return _statisticsService.BuildSnapshot(studentId, studentName, GetSessionsForStudent(studentId));
    }

    public StudentSummary GetStudentSummary(string studentId, string studentName)
    {
        return _statisticsService.BuildStudentSummary(studentId, studentName, GetSessionsForStudent(studentId));
    }

    public StudentFullReport GetStudentFullReport(string studentId, string studentName)
    {
        var displayName = GetStudentAccounts().FirstOrDefault(account => account.StudentId == studentId)?.DisplayName ?? studentName;
        var sessions = GetSessionsForStudent(studentId);
        return new StudentFullReport
        {
            Summary = GetStudentSummary(studentId, displayName),
            Snapshot = GetStudentSnapshot(studentId, displayName),
            Sessions = sessions.ToList()
        };
    }

    public IReadOnlyList<ClassOverviewItem> GetClassOverview()
    {
        return BuildClassOverviewFromStoredResults();
    }

    public IReadOnlyList<ClassOverviewItem> GetPublicClassOverview()
    {
        if (_storageService.TryLoadJson<List<ClassOverviewItem>>(_configuration.PublicClassOverviewFilePath, out var data, out var error) && data is not null)
        {
            return data;
        }

        if (error is not null)
        {
            _loggingService.LogError("Public class overview load", error);
        }

        return BuildClassOverviewFromStoredResults();
    }

    public IReadOnlyList<StudentAccount> GetStudentAccounts()
    {
        return LoadStudentAccounts();
    }

    public StudentAccountChangeResult CreateStudentAccount(string displayName, string loginCode = "")
    {
        var normalizedName = string.IsNullOrWhiteSpace(displayName) ? "Nový žák" : displayName.Trim();
        var accounts = LoadStudentAccounts();
        var normalizedLoginCode = NormalizeLoginCode(string.IsNullOrWhiteSpace(loginCode)
            ? CreateLoginCodeBase(normalizedName)
            : loginCode);

        if (!IsLoginCodeAvailable(normalizedLoginCode))
        {
            throw new InvalidOperationException("Toto přihlašovací jméno už existuje.");
        }

        var temporaryPin = GeneratePin();
        var account = new StudentAccount
        {
            StudentId = CreateUniqueStudentId(normalizedName, accounts),
            DisplayName = normalizedName,
            LoginCode = normalizedLoginCode,
            IsActive = true,
            MustChangePin = true,
            TemporaryPinPending = true,
            CreatedAt = DateTime.UtcNow,
            PinResetAt = DateTime.UtcNow
        };
        SetAccountPin(account, temporaryPin);

        accounts.Add(account);
        SaveStudentAccounts(accounts);
        SaveStudentSummary(new StudentSummary
        {
            StudentId = account.StudentId,
            DisplayName = account.DisplayName
        });
        RegeneratePublicClassOverviewIfAllowed();
        OnDataChanged();

        return new StudentAccountChangeResult { Account = account, TemporaryPin = temporaryPin };
    }

    public string CreateLoginCodeBase(string displayName)
    {
        var cleaned = RemoveDiacritics(displayName).ToUpperInvariant();
        var code = new string(cleaned.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(code) ? "ZAK" : code;
    }

    public string NormalizeLoginCode(string loginCode)
    {
        return CreateLoginCodeBase(loginCode);
    }

    public bool IsLoginCodeAvailable(string loginCode)
    {
        var normalizedLoginCode = NormalizeLoginCode(loginCode);
        return !LoadStudentAccounts().Any(account =>
            string.Equals(account.LoginCode, normalizedLoginCode, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> GetLoginCodeSuggestions(string loginCode)
    {
        var baseCode = NormalizeLoginCode(loginCode);
        var accounts = LoadStudentAccounts();
        var suggestions = new List<string>();
        var suffix = 1;

        while (suggestions.Count < 3)
        {
            var candidate = $"{baseCode}{suffix}";
            if (!accounts.Any(account => string.Equals(account.LoginCode, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                suggestions.Add(candidate);
            }

            suffix++;
        }

        return suggestions;
    }

    public void UpdateStudentAccount(string studentId, string displayName, bool isActive)
    {
        var accounts = LoadStudentAccounts();
        var account = accounts.FirstOrDefault(item => item.StudentId == studentId);
        if (account is null)
        {
            return;
        }

        account.DisplayName = string.IsNullOrWhiteSpace(displayName) ? account.DisplayName : displayName.Trim();
        account.IsActive = isActive;
        SaveStudentAccounts(accounts);
        SaveStudentSummary(GetStudentSummary(account.StudentId, account.DisplayName));
        RegeneratePublicClassOverviewIfAllowed();
        OnDataChanged();
    }

    public StudentAccountChangeResult? ResetStudentPin(string studentId)
    {
        var accounts = LoadStudentAccounts();
        var account = accounts.FirstOrDefault(item => item.StudentId == studentId);
        if (account is null)
        {
            return null;
        }

        var temporaryPin = GeneratePin();
        SetAccountPin(account, temporaryPin);
        account.MustChangePin = true;
        account.TemporaryPinPending = true;
        account.PinResetAt = DateTime.UtcNow;
        SaveStudentAccounts(accounts);
        OnDataChanged();

        return new StudentAccountChangeResult { Account = account, TemporaryPin = temporaryPin };
    }

    public (bool Success, bool ResultsDeleted) DeleteStudentAndResults(string studentId)
    {
        var accounts = LoadStudentAccounts();
        var account = accounts.FirstOrDefault(item => string.Equals(item.StudentId, studentId, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            _loggingService.Log($"Delete student failed: account not found for {studentId}.");
            return (false, false);
        }

        try
        {
            accounts.Remove(account);
            SaveStudentAccounts(accounts);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Delete student account failed for {account.DisplayName} ({account.StudentId})", ex);
            return (false, false);
        }

        var resultsDeleted = true;
        resultsDeleted &= DeleteLegacyStudentFiles(account.StudentId);
        var studentDirectory = GetStudentDirectory(account.StudentId);
        try
        {
            var resultsRoot = Path.GetFullPath(_configuration.StudentResultsDirectory);
            var fullStudentDirectory = Path.GetFullPath(studentDirectory);
            if (!fullStudentDirectory.StartsWith(resultsRoot, StringComparison.OrdinalIgnoreCase))
            {
                _loggingService.Log($"Delete student results skipped: path outside results root {fullStudentDirectory}.");
                resultsDeleted = false;
            }
            else if (Directory.Exists(fullStudentDirectory))
            {
                Directory.Delete(fullStudentDirectory, true);
            }
            else
            {
                _loggingService.Log($"Delete student results warning: directory not found {fullStudentDirectory}.");
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Delete student results failed for {account.DisplayName} ({account.StudentId}) at {studentDirectory}", ex);
            resultsDeleted = false;
        }

        try
        {
            RegeneratePublicClassOverviewIfAllowed();
            OnDataChanged();
            _loggingService.Log($"Student deleted: {account.DisplayName} ({account.StudentId}, {account.LoginCode}), resultsDeleted={resultsDeleted}.");
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Delete student overview refresh failed for {account.DisplayName} ({account.StudentId})", ex);
            return (false, resultsDeleted);
        }

        return (true, resultsDeleted);
    }

    public void RegeneratePublicClassOverview()
    {
        var publicItems = BuildClassOverviewFromStoredResults()
            .Select(item => new ClassOverviewItem
            {
                StudentId = item.StudentId,
                DisplayName = item.DisplayName,
                SolvedProblems = item.SolvedProblems,
                CorrectAnswers = item.CorrectAnswers,
                IncorrectAnswers = item.IncorrectAnswers,
                AccuracyPercent = item.AccuracyPercent,
                ImprovementTrend = item.ImprovementTrend,
                SessionCount = item.SessionCount,
                LastActivity = item.LastActivity,
                BeginnerAccuracyPercent = item.BeginnerAccuracyPercent,
                AdvancedAccuracyPercent = item.AdvancedAccuracyPercent
            })
            .ToList();

        _storageService.SaveJson(_configuration.PublicClassOverviewFilePath, publicItems);
    }

    private void RegeneratePublicClassOverviewIfAllowed()
    {
        if (_canWritePublicOverview)
        {
            RegeneratePublicClassOverview();
        }
    }

    public IReadOnlyList<StudentSummary> GetAllStudentSummaries()
    {
        var summaries = _storageService.LoadJsonFiles<StudentSummary>(_configuration.StudentDataDirectory, OnFileLoadError).ToList();

        foreach (var file in Directory.GetFiles(_configuration.StudentResultsDirectory, "summary.json", SearchOption.AllDirectories))
        {
            if (_storageService.TryLoadJson<StudentSummary>(file, out var summary, out var error) && summary is not null)
            {
                summaries.RemoveAll(item => string.Equals(item.StudentId, summary.StudentId, StringComparison.OrdinalIgnoreCase));
                summaries.Add(summary);
            }
            else if (error is not null)
            {
                OnFileLoadError(file, error);
            }
        }

        return summaries
            .GroupBy(summary => summary.StudentId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public IReadOnlyList<StudentSession> GetSessionsForStudent(string studentId)
    {
        return GetAllSessions()
            .Where(session => string.Equals(session.StudentId, studentId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(session => session.CompletedAt)
            .ToList();
    }

    public string ExportClassOverview() => _csvExportService.ExportClassOverview(BuildClassOverviewFromStoredResults());

    public string ExportStudentDetail(string studentId, string studentName) =>
        _csvExportService.ExportStudentDetail(GetStudentFullReport(studentId, studentName));

    public string ExportTrendOverview() => _csvExportService.ExportTrends(BuildClassOverviewFromStoredResults());

    private IReadOnlyList<ClassOverviewItem> BuildClassOverviewFromStoredResults()
    {
        var overview = _statisticsService.BuildClassOverview(GetAllSessions()).ToList();
        var activeAccounts = LoadStudentAccounts()
            .Where(account => account.IsActive)
            .ToDictionary(account => account.StudentId, StringComparer.OrdinalIgnoreCase);

        overview = overview
            .Where(item => activeAccounts.ContainsKey(item.StudentId))
            .ToList();

        foreach (var account in activeAccounts.Values)
        {
            var item = overview.FirstOrDefault(existing => string.Equals(existing.StudentId, account.StudentId, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                overview.Add(new ClassOverviewItem
                {
                    StudentId = account.StudentId,
                    DisplayName = account.DisplayName,
                    LoginCode = account.LoginCode,
                    IsActive = account.IsActive
                });
            }
            else
            {
                item.DisplayName = account.DisplayName;
                item.LoginCode = account.LoginCode;
                item.IsActive = account.IsActive;
            }
        }

        return overview
            .OrderBy(item => item.DisplayName)
            .ToList();
    }

    private IReadOnlyList<StudentSession> GetAllSessions()
    {
        var sessions = new List<StudentSession>();
        sessions.AddRange(_storageService.LoadJsonFiles<StudentSession>(_configuration.SessionDataDirectory, OnFileLoadError));

        foreach (var directory in Directory.GetDirectories(_configuration.StudentResultsDirectory))
        {
            var sessionsDirectory = Path.Combine(directory, "Sessions");
            if (Directory.Exists(sessionsDirectory))
            {
                sessions.AddRange(_storageService.LoadJsonFiles<StudentSession>(sessionsDirectory, OnFileLoadError));
            }
        }

        return sessions
            .Where(session => !string.IsNullOrWhiteSpace(session.SessionId))
            .GroupBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(session => session.CompletedAt).First())
            .OrderBy(session => session.CompletedAt)
            .ToList();
    }

    private void SaveSession(StudentSession session)
    {
        var sessionPath = Path.Combine(GetStudentDirectory(session.StudentId), "Sessions", $"{session.SessionId}.json");
        _storageService.SaveJson(sessionPath, session);
    }

    private void SaveStudentSummary(StudentSummary summary)
    {
        var path = Path.Combine(GetStudentDirectory(summary.StudentId), "summary.json");
        _storageService.SaveJson(path, summary);
    }

    private string GetStudentDirectory(string studentId)
    {
        return Path.Combine(_configuration.StudentResultsDirectory, NormalizeStudentId(studentId));
    }

    private void EnsureRequiredDirectories()
    {
        foreach (var directory in new[]
                 {
                     _configuration.SharedDataRoot,
                     _configuration.StudentDataDirectory,
                     _configuration.SessionDataDirectory,
                     _configuration.StudentResultsDirectory,
                     Path.GetDirectoryName(_configuration.StudentAccountFilePath) ?? string.Empty,
                     Path.GetDirectoryName(_configuration.PublicClassOverviewFilePath) ?? string.Empty,
                     _configuration.ExportDirectory,
                     _configuration.ConfigDirectory,
                     _configuration.LogDirectory
                 })
        {
            try
            {
                _storageService.EnsureDirectory(directory);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Directory init {directory}", ex);
            }
        }
    }

    private void MigrateLegacyDataIfNeeded()
    {
        foreach (var session in _storageService.LoadJsonFiles<StudentSession>(_configuration.SessionDataDirectory, OnFileLoadError))
        {
            if (string.IsNullOrWhiteSpace(session.StudentId))
            {
                continue;
            }

            var targetPath = Path.Combine(GetStudentDirectory(session.StudentId), "Sessions", $"{session.SessionId}.json");
            if (!File.Exists(targetPath))
            {
                _storageService.SaveJson(targetPath, session);
            }
        }

        foreach (var summary in _storageService.LoadJsonFiles<StudentSummary>(_configuration.StudentDataDirectory, OnFileLoadError))
        {
            if (string.IsNullOrWhiteSpace(summary.StudentId))
            {
                continue;
            }

            var targetPath = Path.Combine(GetStudentDirectory(summary.StudentId), "summary.json");
            if (!File.Exists(targetPath))
            {
                _storageService.SaveJson(targetPath, summary);
            }
        }
    }

    private void EnsureAccountsForExistingStudents()
    {
        var accounts = LoadStudentAccounts();
        var changed = false;
        var summaries = GetAllStudentSummaries().ToList();

        foreach (var item in _statisticsService.BuildClassOverview(GetAllSessions()))
        {
            if (summaries.Any(summary => string.Equals(summary.StudentId, item.StudentId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            summaries.Add(new StudentSummary
            {
                StudentId = item.StudentId,
                DisplayName = item.DisplayName
            });
        }

        foreach (var summary in summaries)
        {
            if (accounts.Any(account => string.Equals(account.StudentId, summary.StudentId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var account = new StudentAccount
            {
                StudentId = NormalizeStudentId(summary.StudentId),
                DisplayName = summary.DisplayName,
                LoginCode = CreateUniqueLoginCode(summary.DisplayName, accounts),
                IsActive = true,
                MustChangePin = true,
                TemporaryPinPending = true,
                CreatedAt = DateTime.UtcNow,
                PinResetAt = DateTime.UtcNow
            };
            var temporaryPin = GeneratePin();
            SetAccountPin(account, temporaryPin);
            accounts.Add(account);
            changed = true;
        }

        if (changed)
        {
            SaveStudentAccounts(accounts);
        }
    }

    private bool DeleteLegacyStudentFiles(string studentId)
    {
        var deleted = true;
        deleted &= DeleteMatchingJsonFiles<StudentSession>(
            _configuration.SessionDataDirectory,
            session => string.Equals(session.StudentId, studentId, StringComparison.OrdinalIgnoreCase),
            $"legacy sessions for {studentId}");
        deleted &= DeleteMatchingJsonFiles<StudentSummary>(
            _configuration.StudentDataDirectory,
            summary => string.Equals(summary.StudentId, studentId, StringComparison.OrdinalIgnoreCase),
            $"legacy summaries for {studentId}");
        return deleted;
    }

    private bool DeleteMatchingJsonFiles<T>(string directory, Func<T, bool> matches, string scope)
    {
        if (!Directory.Exists(directory))
        {
            _loggingService.Log($"Delete student warning: directory not found for {scope}: {directory}.");
            return true;
        }

        var deleted = true;
        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            if (!_storageService.TryLoadJson<T>(file, out var data, out var error))
            {
                if (error is not null)
                {
                    _loggingService.LogError($"Delete student read failed {file}", error);
                }

                continue;
            }

            if (data is null || !matches(data))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Delete student file failed {file}", ex);
                deleted = false;
            }
        }

        return deleted;
    }

    private List<StudentAccount> LoadStudentAccounts()
    {
        if (_storageService.TryLoadJson<List<StudentAccount>>(_configuration.StudentAccountFilePath, out var accounts, out var error) && accounts is not null)
        {
            return accounts;
        }

        if (error is not null)
        {
            _loggingService.LogError("Student accounts load", error);
        }

        return [];
    }

    private void SaveStudentAccounts(List<StudentAccount> accounts)
    {
        _storageService.SaveJson(_configuration.StudentAccountFilePath, accounts.OrderBy(account => account.DisplayName).ToList());
    }

    private List<StudentAccount> ReplaceAccount(StudentAccount account)
    {
        var accounts = LoadStudentAccounts();
        var index = accounts.FindIndex(item => item.StudentId == account.StudentId);
        if (index >= 0)
        {
            accounts[index] = account;
        }

        return accounts;
    }

    private static bool IsValidPin(string pin)
    {
        return pin.Length == 4 && pin.All(char.IsDigit);
    }

    private static string GeneratePin()
    {
        return RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
    }

    private static void SetAccountPin(StudentAccount account, string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(PinSaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, PinHashIterations, HashAlgorithmName.SHA256, PinHashBytes);
        account.PinSalt = Convert.ToBase64String(salt);
        account.PinHash = Convert.ToBase64String(hash);
    }

    private static bool VerifyPin(string pin, string saltText, string hashText)
    {
        if (!IsValidPin(pin) || string.IsNullOrWhiteSpace(saltText) || string.IsNullOrWhiteSpace(hashText))
        {
            return false;
        }

        var salt = Convert.FromBase64String(saltText);
        var expectedHash = Convert.FromBase64String(hashText);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, PinHashIterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(hash, expectedHash);
    }

    private static string CreateUniqueStudentId(string displayName, IReadOnlyCollection<StudentAccount> accounts)
    {
        var baseId = NormalizeStudentId(RemoveDiacritics(displayName));
        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = "ZAK";
        }

        var candidate = baseId;
        var suffix = 2;
        while (accounts.Any(account => string.Equals(account.StudentId, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseId}{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static string CreateUniqueLoginCode(string displayName, IReadOnlyCollection<StudentAccount> accounts)
    {
        var cleaned = RemoveDiacritics(displayName).ToUpperInvariant();
        var letters = new string(cleaned.Where(char.IsLetterOrDigit).ToArray());
        var baseCode = string.IsNullOrWhiteSpace(letters) ? "ZAK" : letters;
        var candidate = baseCode;
        var suffix = 1;

        while (accounts.Any(account => string.Equals(account.LoginCode, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseCode}{suffix}";
            suffix++;
        }

        return candidate;
    }

    private void OnFileLoadError(string path, Exception exception)
    {
        _loggingService.LogError($"Load failed {Path.GetFileName(path)}", exception);
    }

    private static string NormalizeStudentId(string value)
    {
        var filtered = new string(value
            .Trim()
            .ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());

        return string.IsNullOrWhiteSpace(filtered) ? "STUDENT" : filtered.Trim('-');
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var ch in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private void OnDataChanged()
    {
        DataChanged?.Invoke(this, EventArgs.Empty);
    }
}

using System.Text.Json;
using SharedCore.Models;
using SharedCore.Services;

namespace SchoolMathTrainer.Api.Services;

internal sealed class FileClassDataRepository : IClassDataRepository
{
    // Temporary file-backed implementation. Keep this isolated so the API can later
    // switch to a real online store without changing endpoint contracts.
    private readonly IOnlineDataService _onlineDataService;
    private readonly ILogger<FileClassDataRepository> _logger;

    public FileClassDataRepository(
        IOnlineDataService onlineDataService,
        ILogger<FileClassDataRepository> logger)
    {
        _onlineDataService = onlineDataService;
        _logger = logger;
    }

    public ClassDataReadResult GetClassStudents(string classId)
    {
        var classRoot = ResolveClassRoot(classId);
        var accountsResult = ReadAccounts(classRoot);
        if (!accountsResult.Success)
        {
            return new ClassDataReadResult(false, accountsResult.Message, []);
        }

        var progressService = CreateProgressService(classRoot);
        var students = accountsResult.Accounts
            .Select(account => ToSafeStudent(account, ReadSummary(classRoot, account.StudentId), progressService))
            .ToList();

        return new ClassDataReadResult(true, string.Empty, students);
    }

    public StudentProfileResponse? GetStudent(string classId, string studentId, out string? message)
    {
        message = null;
        ValidateSegment(studentId, nameof(studentId));

        var classRoot = ResolveClassRoot(classId);
        var accountsResult = ReadAccounts(classRoot);
        if (!accountsResult.Success)
        {
            message = accountsResult.Message;
            return null;
        }

        var account = accountsResult.Accounts.FirstOrDefault(item =>
            string.Equals(item.StudentId, studentId, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            message = "Student was not found.";
            return null;
        }

        return ToSafeStudent(account, ReadSummary(classRoot, account.StudentId), CreateProgressService(classRoot));
    }

    public IReadOnlyList<ClassOverviewItem> GetClassOverview(string classId)
    {
        var classRoot = ResolveClassRoot(classId);
        return CreateProgressService(classRoot).GetClassOverview();
    }

    public StudentResultDetailResponse? GetStudentResultDetail(string classId, string studentId, out string? message)
    {
        message = null;
        ValidateSegment(studentId, nameof(studentId));

        var classRoot = ResolveClassRoot(classId);
        var accountsResult = ReadAccounts(classRoot);
        if (!accountsResult.Success)
        {
            message = accountsResult.Message;
            return null;
        }

        var account = accountsResult.Accounts.FirstOrDefault(item =>
            string.Equals(item.StudentId, studentId, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            message = "Student was not found.";
            return null;
        }

        var summary = ReadSummary(classRoot, account.StudentId);
        var sessions = ReadSessions(classRoot, account.StudentId)
            .Where(session => GetAttemptCount(session) > 0)
            .OrderByDescending(GetSessionActivityTime)
            .ToList();
        var totalAnswers = sessions.Count > 0
            ? sessions.Sum(GetAttemptCount)
            : summary?.TotalAnswers ?? 0;
        var totalSessions = sessions.Count > 0
            ? sessions.Count
            : summary?.SessionsCompleted ?? 0;
        var lastSessionAt = sessions.Count > 0
            ? GetSessionActivityTime(sessions[0])
            : summary?.LastSessionAt;

        return new StudentResultDetailResponse(
            classId,
            ToSafeStudent(account, summary, CreateProgressService(classRoot)),
            ToSummaryResponse(summary),
            sessions.Take(10).Select(ToStudentSessionSummary).ToList(),
            totalSessions,
            totalAnswers,
            lastSessionAt,
            sessions.Count == 0 && summary is null ? "Student results are not available." : null);
    }

    public IReadOnlyList<ClassActivityItemResponse> GetClassActivities(string classId, int limit)
    {
        var classRoot = ResolveClassRoot(classId);
        var accounts = ReadAccounts(classRoot).Accounts
            .ToDictionary(account => account.StudentId, StringComparer.OrdinalIgnoreCase);

        return ReadAllSessions(classRoot)
            .Where(session => GetAttemptCount(session) > 0)
            .OrderByDescending(GetSessionActivityTime)
            .Take(Math.Max(1, limit))
            .Select(session =>
            {
                accounts.TryGetValue(session.StudentId, out var account);
                var displayName = account?.DisplayName;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = string.IsNullOrWhiteSpace(session.StudentName)
                        ? session.StudentId
                        : session.StudentName.Trim();
                }

                return new ClassActivityItemResponse(
                    session.StudentId,
                    displayName,
                    session.SessionId,
                    session.Mode,
                    session.StartedAt,
                    session.CompletedAt,
                    GetSessionActivityTime(session),
                    GetAttemptCount(session),
                    GetCorrectCount(session),
                    GetWrongCount(session),
                    GetAccuracyPercent(session));
            })
            .ToList();
    }

    public TeacherStudentChangeResponse CreateStudent(string classId, CreateStudentRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return new TeacherStudentChangeResponse(false, "Jméno žáka je povinné.", null);
        }

        var classRoot = ResolveClassRoot(classId);
        var progressService = CreateProgressService(classRoot);
        try
        {
            var loginCode = progressService.CreateLoginCodeBase(request.DisplayName);
            if (!progressService.IsLoginCodeAvailable(loginCode))
            {
                loginCode = progressService.GetLoginCodeSuggestions(loginCode).FirstOrDefault() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(loginCode))
            {
                return new TeacherStudentChangeResponse(false, "Nepodařilo se vytvořit unikátní LoginCode.", null);
            }

            var result = progressService.CreateStudentAccount(request.DisplayName.Trim(), loginCode);
            _logger.LogInformation("Student account created for class {ClassId}, student {StudentId}. Temporary PIN value was not logged.", classId, result.Account.StudentId);
            return new TeacherStudentChangeResponse(
                true,
                $"Účet byl vytvořen. LoginCode: {result.Account.LoginCode}, dočasný PIN: {result.TemporaryPin}",
                ToSafeStudent(result.Account, ReadSummary(classRoot, result.Account.StudentId), progressService),
                result.TemporaryPin);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Student account creation rejected for class {ClassId}.", classId);
            return new TeacherStudentChangeResponse(false, ex.Message, null);
        }
    }

    public TeacherStudentChangeResponse ResetStudentPin(string classId, string studentId)
    {
        ValidateSegment(studentId, nameof(studentId));

        var classRoot = ResolveClassRoot(classId);
        var progressService = CreateProgressService(classRoot);
        var beforeAccount = progressService.GetStudentAccounts()
            .FirstOrDefault(item => string.Equals(item.StudentId, studentId, StringComparison.OrdinalIgnoreCase));
        _logger.LogInformation(
            "Student PIN reset requested. Class {ClassId}, student {StudentId}, classRoot {ClassRoot}, accountFound={AccountFound}, loginCode={LoginCode}, mustChangePin={MustChangePin}, temporaryPinPending={TemporaryPinPending}, accountFile={AccountFile}.",
            classId,
            studentId,
            classRoot,
            beforeAccount is not null,
            beforeAccount?.LoginCode ?? "<missing>",
            beforeAccount?.MustChangePin,
            beforeAccount?.TemporaryPinPending,
            Path.Combine(classRoot, "Config", "student-accounts.json"));
        var result = progressService.ResetStudentPin(studentId);
        if (result is null)
        {
            _logger.LogWarning("Reset PIN failed because student was not found. Class {ClassId}, student {StudentId}.", classId, studentId);
            return new TeacherStudentChangeResponse(false, "Žák nebyl nalezen. PIN nebyl resetován.", null);
        }

        var persistedAccount = progressService.GetStudentAccounts()
            .FirstOrDefault(item => string.Equals(item.StudentId, studentId, StringComparison.OrdinalIgnoreCase));
        _logger.LogInformation(
            "Student PIN reset persisted. Class {ClassId}, student {StudentId}, loginCode={LoginCode}, mustChangePin={MustChangePin}, temporaryPinPending={TemporaryPinPending}, pinSalt={PinSalt}, pinHash={PinHash}.",
            classId,
            studentId,
            persistedAccount?.LoginCode ?? result.Account.LoginCode,
            persistedAccount?.MustChangePin ?? result.Account.MustChangePin,
            persistedAccount?.TemporaryPinPending ?? result.Account.TemporaryPinPending,
            DescribeStoredSecret(persistedAccount?.PinSalt ?? result.Account.PinSalt),
            DescribeStoredSecret(persistedAccount?.PinHash ?? result.Account.PinHash));
        _logger.LogInformation("Student PIN reset completed for class {ClassId}, student {StudentId}. Temporary PIN value was not logged.", classId, studentId);
        return new TeacherStudentChangeResponse(
            true,
            $"PIN byl resetován. Nový dočasný PIN: {result.TemporaryPin}",
            ToSafeStudent(result.Account, ReadSummary(classRoot, result.Account.StudentId), progressService),
            result.TemporaryPin);
    }

    public TeacherStudentChangeResponse DeleteStudent(string classId, string studentId)
    {
        ValidateSegment(studentId, nameof(studentId));

        var classRoot = ResolveClassRoot(classId);
        var progressService = CreateProgressService(classRoot);
        var account = progressService.GetStudentAccounts()
            .FirstOrDefault(item => string.Equals(item.StudentId, studentId, StringComparison.OrdinalIgnoreCase));
        var deleteResult = progressService.DeleteStudentAndResults(studentId);
        if (!deleteResult.Success)
        {
            return new TeacherStudentChangeResponse(false, "Žák nebyl nalezen nebo ho nešlo bezpečně smazat.", null, ResultsDeleted: deleteResult.ResultsDeleted);
        }

        return new TeacherStudentChangeResponse(
            true,
            account is null
                ? "Žák byl smazán."
                : $"Žák {account.DisplayName} byl smazán.",
            null,
            ResultsDeleted: deleteResult.ResultsDeleted);
    }

    public StudentLoginResult LoginStudent(string classId, StudentLoginRequest request)
    {
        if (request is null)
        {
            return StudentLoginResult.Failed("Přihlašovací údaje nejsou vyplněné.");
        }

        var classRoot = ResolveClassRoot(classId);
        var progressService = CreateProgressService(classRoot);
        var trimmedLoginCode = request.LoginCode?.Trim() ?? string.Empty;
        var trimmedStudentId = request.StudentId?.Trim() ?? string.Empty;
        var accountByLoginCode = progressService.GetStudentAccounts()
            .FirstOrDefault(item => string.Equals(item.LoginCode, trimmedLoginCode, StringComparison.OrdinalIgnoreCase));
        _logger.LogInformation(
            "Student login request received. Class {ClassId}, classRoot {ClassRoot}, requestStudentId={RequestStudentId}, requestLoginCode={RequestLoginCode}, pin={Pin}, newPin={NewPin}, accountByLoginCodeFound={AccountByLoginCodeFound}, accountByLoginCodeStudentId={AccountByLoginCodeStudentId}, accountByLoginCodeActive={AccountByLoginCodeActive}, accountByLoginCodeMustChangePin={AccountByLoginCodeMustChangePin}, accountByLoginCodeTemporaryPinPending={AccountByLoginCodeTemporaryPinPending}.",
            classId,
            classRoot,
            string.IsNullOrWhiteSpace(trimmedStudentId) ? "<missing>" : trimmedStudentId,
            trimmedLoginCode,
            DescribeRequestSecret(request.Pin),
            DescribeRequestSecret(request.NewPin),
            accountByLoginCode is not null,
            accountByLoginCode?.StudentId ?? "<missing>",
            accountByLoginCode?.IsActive,
            accountByLoginCode?.MustChangePin,
            accountByLoginCode?.TemporaryPinPending);
        if (!string.IsNullOrWhiteSpace(request.StudentId))
        {
            ValidateSegment(request.StudentId, nameof(request.StudentId));
            var configuredAccount = progressService.GetStudentAccounts()
                .FirstOrDefault(item => item.IsActive &&
                    string.Equals(item.StudentId, request.StudentId.Trim(), StringComparison.OrdinalIgnoreCase));
            _logger.LogInformation(
                "Student login configured account lookup. Class {ClassId}, requestStudentId={RequestStudentId}, configuredAccountFound={ConfiguredAccountFound}, configuredAccountLoginCode={ConfiguredAccountLoginCode}, configuredAccountMustChangePin={ConfiguredAccountMustChangePin}, configuredAccountTemporaryPinPending={ConfiguredAccountTemporaryPinPending}.",
                classId,
                trimmedStudentId,
                configuredAccount is not null,
                configuredAccount?.LoginCode ?? "<missing>",
                configuredAccount?.MustChangePin,
                configuredAccount?.TemporaryPinPending);
            if (configuredAccount is null)
            {
                _logger.LogWarning("Student login rejected because configured student was not found. Class {ClassId}, student {StudentId}.", classId, request.StudentId);
                return StudentLoginResult.StudentConfigurationMismatch("Soubor od paní učitelky pro tohoto žáka už není platný. Načti prosím nový soubor.");
            }

            if (!string.Equals(configuredAccount.LoginCode, trimmedLoginCode, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Student login rejected because login code {LoginCode} does not match configured student {StudentId}. Class {ClassId}.", request.LoginCode, request.StudentId, classId);
                return StudentLoginResult.StudentConfigurationMismatch("Zadaný přihlašovací kód patří jinému žákovi než soubor od paní učitelky. Načti správný soubor pro tohoto žáka.");
            }
        }

        var loginResult = progressService.LoginStudent(
            request.LoginCode ?? string.Empty,
            request.Pin ?? string.Empty,
            request.NewPin ?? string.Empty);
        _logger.LogInformation(
            "Student login result. Class {ClassId}, requestStudentId={RequestStudentId}, requestLoginCode={RequestLoginCode}, success={Success}, requiresPinChange={RequiresPinChange}, requiresStudentConfigurationReload={RequiresStudentConfigurationReload}, resultStudentId={ResultStudentId}, message={Message}.",
            classId,
            string.IsNullOrWhiteSpace(trimmedStudentId) ? "<missing>" : trimmedStudentId,
            trimmedLoginCode,
            loginResult.Success,
            loginResult.RequiresPinChange,
            loginResult.RequiresStudentConfigurationReload,
            string.IsNullOrWhiteSpace(loginResult.StudentId) ? "<missing>" : loginResult.StudentId,
            loginResult.Message);
        return loginResult;
    }

    public SaveStudentResultResponse SaveStudentResult(string classId, string studentId, StudentSession session)
    {
        ValidateSegment(studentId, nameof(studentId));

        if (string.IsNullOrWhiteSpace(session.SessionId))
        {
            session.SessionId = Guid.NewGuid().ToString("N");
        }

        var classRoot = ResolveClassRoot(classId);
        var progressService = CreateProgressService(classRoot);
        var account = progressService.GetStudentAccounts()
            .FirstOrDefault(item => string.Equals(item.StudentId, studentId, StringComparison.OrdinalIgnoreCase));
        var studentName = account?.DisplayName;
        if (string.IsNullOrWhiteSpace(studentName))
        {
            studentName = string.IsNullOrWhiteSpace(session.StudentName) ? studentId : session.StudentName.Trim();
        }

        session.StudentId = studentId;
        session.StudentName = studentName;
        session.LastActivityUtc = DateTime.UtcNow;
        session.CompletedAt = session.CompletedAt == default ? session.LastActivityUtc : session.CompletedAt;

        var sessionDirectory = Path.Combine(classRoot, "Data", "StudentResults", studentId, "Sessions");
        var sessionPath = Path.Combine(sessionDirectory, $"{session.SessionId}.json");
        _onlineDataService.WriteFile(sessionPath, JsonSerializer.Serialize(session, ApiJson.Options));

        var summary = progressService.GetStudentSummary(studentId, studentName);
        var summaryPath = Path.Combine(classRoot, "Data", "StudentResults", studentId, "summary.json");
        _onlineDataService.WriteFile(summaryPath, JsonSerializer.Serialize(summary, ApiJson.Options));
        progressService.RegeneratePublicClassOverview();

        return new SaveStudentResultResponse("Result was saved.", session.SessionId);
    }

    private static StudentProgressService CreateProgressService(string classRoot)
    {
        var configuration = new AppConfiguration
        {
            SharedDataRoot = classRoot,
            StudentDataDirectory = Path.Combine(classRoot, "Data", "Students"),
            SessionDataDirectory = Path.Combine(classRoot, "Data", "Sessions"),
            StudentResultsDirectory = Path.Combine(classRoot, "Data", "StudentResults"),
            StudentAccountFilePath = Path.Combine(classRoot, "Config", "student-accounts.json"),
            PublicClassOverviewFilePath = Path.Combine(classRoot, "Data", "Public", "class-overview.json"),
            ExportDirectory = Path.Combine(classRoot, "Data", "Exports"),
            ConfigDirectory = Path.Combine(classRoot, "Config"),
            LogDirectory = Path.Combine(classRoot, "Logs"),
            RetryCount = 4,
            RetryDelayMilliseconds = 250
        };

        var retryFileAccessService = new RetryFileAccessService();
        var storageService = new FileSystemStorageService(
            retryFileAccessService,
            configuration.RetryCount,
            configuration.RetryDelayMilliseconds);
        var loggingService = new LoggingService(storageService, configuration);
        var statisticsService = new StatisticsService();
        var csvExportService = new CsvExportService(storageService, configuration);

        return new StudentProgressService(
            configuration,
            storageService,
            statisticsService,
            loggingService,
            csvExportService,
            canWritePublicOverview: true);
    }

    private string ResolveClassRoot(string classId)
    {
        ValidateSegment(classId, nameof(classId));
        return _onlineDataService.ResolveClassDataRoot(classId);
    }

    private AccountReadResult ReadAccounts(string classRoot)
    {
        var accountFilePath = Path.Combine(classRoot, "Config", "student-accounts.json");
        if (!_onlineDataService.FileExists(accountFilePath))
        {
            _logger.LogWarning("Student accounts file is not available at {AccountFilePath}.", accountFilePath);
            return new AccountReadResult(false, "student-accounts.json is not available.", []);
        }

        try
        {
            var accounts = JsonSerializer.Deserialize<List<StudentAccount>>(
                _onlineDataService.ReadFile(accountFilePath),
                ApiJson.Options);

            return accounts is null
                ? new AccountReadResult(false, "student-accounts.json does not contain the expected data.", [])
                : new AccountReadResult(true, string.Empty, accounts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogError(ex, "Student accounts file could not be read safely at {AccountFilePath}.", accountFilePath);
            return new AccountReadResult(false, "student-accounts.json could not be read safely.", []);
        }
    }

    private StudentSummary? ReadSummary(string classRoot, string studentId)
    {
        var summaryPath = Path.Combine(classRoot, "Data", "StudentResults", studentId, "summary.json");
        if (!_onlineDataService.FileExists(summaryPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StudentSummary>(_onlineDataService.ReadFile(summaryPath), ApiJson.Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Student summary could not be read at {SummaryPath}.", summaryPath);
            return null;
        }
    }

    private IReadOnlyList<StudentSession> ReadSessions(string classRoot, string studentId)
    {
        var legacySessionsDirectory = Path.Combine(classRoot, "Data", "Sessions");
        var studentSessionsDirectory = Path.Combine(classRoot, "Data", "StudentResults", studentId, "Sessions");

        return ReadSessionFiles(legacySessionsDirectory, SearchOption.TopDirectoryOnly)
            .Concat(ReadSessionFiles(studentSessionsDirectory, SearchOption.TopDirectoryOnly))
            .Where(session => string.Equals(session.StudentId, studentId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(GetSessionActivityTime).First())
            .OrderBy(GetSessionActivityTime)
            .ToList();
    }

    private IReadOnlyList<StudentSession> ReadAllSessions(string classRoot)
    {
        var legacySessionsDirectory = Path.Combine(classRoot, "Data", "Sessions");
        var studentResultsDirectory = Path.Combine(classRoot, "Data", "StudentResults");

        return ReadSessionFiles(legacySessionsDirectory, SearchOption.TopDirectoryOnly)
            .Concat(ReadSessionFiles(studentResultsDirectory, SearchOption.AllDirectories))
            .Where(session => !string.IsNullOrWhiteSpace(session.StudentId))
            .Where(session => !string.IsNullOrWhiteSpace(session.SessionId))
            .GroupBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(GetSessionActivityTime).First())
            .ToList();
    }

    private IReadOnlyList<StudentSession> ReadSessionFiles(string directoryPath, SearchOption searchOption)
    {
        var sessions = new List<StudentSession>();
        IReadOnlyList<string> files;
        try
        {
            files = _onlineDataService.ListFiles(directoryPath, "*.json", searchOption);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _logger.LogWarning(ex, "Session file listing failed for directory {DirectoryPath}.", directoryPath);
            return sessions;
        }

        foreach (var file in files)
        {
            try
            {
                var session = JsonSerializer.Deserialize<StudentSession>(
                    _onlineDataService.ReadFile(file),
                    ApiJson.Options);
                if (session is not null)
                {
                    sessions.Add(session);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogWarning(ex, "Unreadable session file skipped: {SessionFile}.", file);
                // Skip unreadable session files; one corrupted file must not hide valid class data.
            }
        }

        return sessions;
    }

    private static StudentProfileResponse ToSafeStudent(
        StudentAccount account,
        StudentSummary? summary,
        StudentProgressService progressService) =>
        new(
            account.StudentId,
            account.DisplayName,
            account.LoginCode,
            account.IsActive,
            account.MustChangePin,
            account.TemporaryPinPending,
            ToSummaryResponse(summary),
            string.Empty);

    private static StudentSummaryResponse? ToSummaryResponse(StudentSummary? summary) =>
        summary is null
            ? null
            : new StudentSummaryResponse(
                summary.TotalAnswers,
                summary.CorrectAnswers,
                summary.IncorrectAnswers,
                summary.AccuracyPercent,
                summary.SessionsCompleted,
                summary.LastSessionAt);

    private static StudentSessionSummaryResponse ToStudentSessionSummary(StudentSession session) =>
        new(
            session.SessionId,
            session.Mode,
            session.StartedAt,
            session.CompletedAt,
            GetSessionActivityTime(session),
            GetAttemptCount(session),
            GetCorrectCount(session),
            GetWrongCount(session),
            GetAccuracyPercent(session));

    private static DateTime GetSessionActivityTime(StudentSession session)
    {
        if (session.LastActivityUtc != default)
        {
            return session.LastActivityUtc;
        }

        return session.CompletedAt == default ? session.StartedAt : session.CompletedAt;
    }

    private static int GetAttemptCount(StudentSession session) =>
        session.Answers.Count > 0 ? session.Answers.Count : session.RunningTotalCount;

    private static int GetCorrectCount(StudentSession session) =>
        session.Answers.Count > 0
            ? session.Answers.Count(answer => answer.IsCorrect)
            : session.RunningCorrectCount;

    private static int GetWrongCount(StudentSession session) =>
        session.Answers.Count > 0
            ? session.Answers.Count(answer => !answer.IsCorrect)
            : session.RunningWrongCount;

    private static double GetAccuracyPercent(StudentSession session)
    {
        var totalAnswers = GetAttemptCount(session);
        if (totalAnswers > 0)
        {
            return GetCorrectCount(session) * 100d / totalAnswers;
        }

        return session.RunningSuccessPercent;
    }

    private static void ValidateSegment(string value, string parameterName)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            Path.IsPathRooted(trimmed) ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains(Path.DirectorySeparatorChar) ||
            trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException($"{parameterName} is not valid.", parameterName);
        }
    }

    private static string DescribeStoredSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "missing";
        }

        return $"present(len={value.Length},prefix={value[..Math.Min(6, value.Length)]})";
    }

    private static string DescribeRequestSecret(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "missing";
        }

        return $"present(len={value.Length},last=*{value[^1]})";
    }

    private sealed record AccountReadResult(bool Success, string Message, IReadOnlyList<StudentAccount> Accounts);
}

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SharedCore.Models;
using SharedCore.Services;

namespace TeacherApp.Data;

public sealed class TeacherOnlineApiDataSource
{
    private const string NotAvailableText = "Není k dispozici";
    private const string LogName = "TeacherApp";
    private readonly HttpClient _httpClient;
    private readonly string _classId;
    private string _teacherToken = string.Empty;
    private string _teacherRole = TeacherRoles.Teacher;

    public TeacherOnlineApiDataSource(DataConnectionSettings settings)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = CreateBaseAddress(settings.ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(8)
        };
        _classId = string.IsNullOrWhiteSpace(settings.ClassId)
            ? DataConnectionSettings.DefaultClassId
            : settings.ClassId.Trim();
        DiagnosticLogService.Log(LogName, $"Online API data source initialized for class '{_classId}' at '{_httpClient.BaseAddress}'.");
    }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_teacherToken);
    public bool IsAdmin => IsAuthenticated && TeacherRoles.IsAdmin(_teacherRole);
    public string TeacherRole => _teacherRole;
    public bool LastAuthorizationFailed { get; private set; }
    public bool LastForbidden { get; private set; }
    public string LastErrorMessage { get; private set; } = string.Empty;

    public TeacherLoginClientResult LoginTeacher(string username, string password)
    {
        ClearAuthentication();
        ClearLastError();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return new TeacherLoginClientResult(false, "Vyplňte uživatelské jméno i heslo.");
        }

        try
        {
            using var response = SendTeacherLoginRequest(
                "api/teacher-auth/login",
                username,
                password);
            using var effectiveResponse = response.StatusCode == HttpStatusCode.NotFound
                ? SendTeacherLoginRequest("api/teachers/login", username, password)
                : response;

            if (effectiveResponse.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Locked)
            {
                LastErrorMessage = "Příliš mnoho pokusů o přihlášení. Zkuste to později.";
                return new TeacherLoginClientResult(false, LastErrorMessage);
            }

            if (IsUnauthorized(effectiveResponse.StatusCode))
            {
                LastErrorMessage = "Přihlášení se nepodařilo. Zkontrolujte jméno a heslo.";
                return new TeacherLoginClientResult(false, LastErrorMessage);
            }

            if (!effectiveResponse.IsSuccessStatusCode)
            {
                LastErrorMessage = "Přihlášení učitele se nepodařilo ověřit na serveru.";
                DiagnosticLogService.Log(LogName, $"Teacher login failed with HTTP {(int)effectiveResponse.StatusCode}.");
                return new TeacherLoginClientResult(false, LastErrorMessage);
            }

            var login = effectiveResponse.Content.ReadFromJsonAsync<TeacherLoginResponse>()
                .GetAwaiter()
                .GetResult();
            if (login is null || string.IsNullOrWhiteSpace(login.Token))
            {
                LastErrorMessage = "Server nevrátil platný učitelský token.";
                return new TeacherLoginClientResult(false, LastErrorMessage);
            }

            _teacherToken = login.Token;
            _teacherRole = TeacherRoles.Normalize(login.Role);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
            var normalizedUsername = username.Trim();
            DiagnosticLogService.Log(LogName, "Teacher login completed successfully. Session data remains in memory only.");
            return new TeacherLoginClientResult(
                true,
                "Přihlášení učitele proběhlo úspěšně.",
                login.Token,
                login.ExpiresUtc,
                normalizedUsername,
                _teacherRole);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or NotSupportedException)
        {
            LastErrorMessage = "Server pro přihlášení učitele není dostupný.";
            DiagnosticLogService.LogError(LogName, "Teacher login request failed", ex);
            return new TeacherLoginClientResult(false, LastErrorMessage);
        }
    }

    private HttpResponseMessage SendTeacherLoginRequest(string relativePath, string username, string password) =>
        _httpClient.PostAsJsonAsync(
                relativePath,
                new TeacherLoginRequest(username.Trim(), password))
            .GetAwaiter()
            .GetResult();

    public void ClearAuthentication()
    {
        _teacherToken = string.Empty;
        _teacherRole = TeacherRoles.Teacher;
        _httpClient.DefaultRequestHeaders.Authorization = null;
        ClearLastError();
    }

    public void LogoutTeacher()
    {
        ClearLastError();
        if (string.IsNullOrWhiteSpace(_teacherToken))
        {
            ClearAuthentication();
            return;
        }

        try
        {
            using var response = _httpClient.PostAsync("api/teachers/logout", null)
                .GetAwaiter()
                .GetResult();
            if (!response.IsSuccessStatusCode && !IsUnauthorized(response.StatusCode))
            {
                DiagnosticLogService.Log(LogName, $"Teacher logout returned HTTP {(int)response.StatusCode}.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            DiagnosticLogService.LogError(LogName, "Teacher logout request failed", ex);
        }
        finally
        {
            ClearAuthentication();
        }
    }

    public StudentAccountReadResult ReadStudents()
    {
        var response = ReadClassStudents();
        if (response is null)
        {
            return new StudentAccountReadResult
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(LastErrorMessage)
                    ? "OnlineApi data se nepodařilo načíst."
                    : LastErrorMessage,
                Students = []
            };
        }

        return new StudentAccountReadResult
        {
            Success = true,
            Message = $"OnlineApi seznam žáků byl načten. Počet žáků: {response.Students.Count}.",
            Students = response.Students
                .OrderBy(student => student.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(ToStudentListItem)
                .ToList()
        };
    }

    public CreateStudentAccountResult CreateStudent(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return new CreateStudentAccountResult
            {
                Success = false,
                Message = "Jméno žáka je povinné."
            };
        }

        var result = SendTeacherChangeRequest(
            () => _httpClient.PostAsJsonAsync(
                $"api/classes/{Uri.EscapeDataString(_classId)}/students",
                new CreateStudentRequest(displayName.Trim())));
        return new CreateStudentAccountResult
        {
            Success = result.Success,
            Message = result.Message,
            Account = result.Student is null ? null : ToStudentAccount(result.Student),
            TemporaryPin = result.TemporaryPin
        };
    }

    public ResetStudentPinResult ResetStudentPin(string studentId)
    {
        var result = SendTeacherChangeRequest(
            () => _httpClient.PostAsync(
                $"api/students/{Uri.EscapeDataString(_classId)}/{Uri.EscapeDataString(studentId)}/reset-pin",
                null));
        return new ResetStudentPinResult
        {
            Success = result.Success,
            Message = result.Message,
            Account = result.Student is null ? null : ToStudentAccount(result.Student),
            TemporaryPin = result.TemporaryPin
        };
    }

    public (bool Success, string Message, bool ResultsDeleted) DeleteStudent(string studentId)
    {
        var result = SendTeacherChangeRequest(
            () => _httpClient.DeleteAsync(
                $"api/students/{Uri.EscapeDataString(_classId)}/{Uri.EscapeDataString(studentId)}"));
        return (result.Success, result.Message, result.ResultsDeleted);
    }

    public async Task<AdminTeacherListResult> GetAdminTeachersAsync()
    {
        ClearLastError();
        try
        {
            using var response = await _httpClient.GetAsync("api/admin/teachers");
            if (HandleAuthorizationFailure(response.StatusCode))
            {
                return new AdminTeacherListResult(false, LastErrorMessage, []);
            }

            if (!response.IsSuccessStatusCode)
            {
                DiagnosticLogService.Log(LogName, $"Admin teacher list request failed with HTTP {(int)response.StatusCode}.");
                return new AdminTeacherListResult(false, "Seznam učitelů se nepodařilo načíst.", []);
            }

            var teachers = await response.Content.ReadFromJsonAsync<List<AdminTeacherListItem>>();
            return new AdminTeacherListResult(true, "Seznam učitelů byl načten.", teachers ?? []);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or NotSupportedException)
        {
            DiagnosticLogService.LogError(LogName, "Admin teacher list request failed", ex);
            return new AdminTeacherListResult(false, "Seznam učitelů se nepodařilo načíst.", []);
        }
    }

    public Task<AdminTeacherOperationResult> CreateAdminTeacherAsync(string username, string displayName, string password, string role) =>
        SendAdminTeacherRequestAsync(
            () => _httpClient.PostAsJsonAsync(
                "api/admin/teachers",
                new AdminCreateTeacherRequest(username.Trim(), displayName.Trim(), password, role)),
            "Učitelský účet byl vytvořen.",
            "Učitelský účet se nepodařilo vytvořit.");

    public Task<AdminTeacherOperationResult> UpdateAdminTeacherAsync(string username, string displayName, string role) =>
        SendAdminTeacherRequestAsync(
            () => _httpClient.PutAsJsonAsync(
                $"api/admin/teachers/{Uri.EscapeDataString(username)}",
                new AdminUpdateTeacherRequest(displayName.Trim(), role)),
            "Učitelský účet byl upraven.",
            "Učitelský účet se nepodařilo upravit.");

    public Task<AdminTeacherOperationResult> ResetAdminTeacherPasswordAsync(string username, string password) =>
        SendAdminTeacherRequestAsync(
            () => _httpClient.PostAsJsonAsync(
                $"api/admin/teachers/{Uri.EscapeDataString(username)}/reset-password",
                new AdminResetTeacherPasswordRequest(password)),
            "Heslo učitele bylo resetováno.",
            "Heslo učitele se nepodařilo resetovat.");

    public Task<AdminTeacherOperationResult> DeactivateAdminTeacherAsync(string username) =>
        SendAdminTeacherRequestAsync(
            () => _httpClient.PostAsync($"api/admin/teachers/{Uri.EscapeDataString(username)}/deactivate", null),
            "Učitel byl deaktivován.",
            "Učitele se nepodařilo deaktivovat.");

    public Task<AdminTeacherOperationResult> ActivateAdminTeacherAsync(string username) =>
        SendAdminTeacherRequestAsync(
            () => _httpClient.PostAsync($"api/admin/teachers/{Uri.EscapeDataString(username)}/activate", null),
            "Učitel byl aktivován.",
            "Učitele se nepodařilo aktivovat.");

    public Task<AdminTeacherOperationResult> DeleteAdminTeacherAsync(string username) =>
        SendAdminTeacherRequestAsync(
            () => _httpClient.DeleteAsync($"api/admin/teachers/{Uri.EscapeDataString(username)}"),
            "Učitelský účet byl odstraněn.",
            "Učitelský účet se nepodařilo odstranit.");

    private async Task<AdminTeacherOperationResult> SendAdminTeacherRequestAsync(
        Func<Task<HttpResponseMessage>> requestFactory,
        string successMessage,
        string fallbackErrorMessage)
    {
        ClearLastError();
        try
        {
            using var response = await requestFactory();
            if (HandleAuthorizationFailure(response.StatusCode))
            {
                return new AdminTeacherOperationResult(false, LastErrorMessage);
            }

            var body = await response.Content.ReadAsStringAsync();
            var serverMessage = ToSafeCzechAdminMessage(ReadApiMessage(body));
            if (!response.IsSuccessStatusCode)
            {
                DiagnosticLogService.Log(LogName, $"Admin teacher request failed with HTTP {(int)response.StatusCode}.");
                return new AdminTeacherOperationResult(
                    false,
                    string.IsNullOrWhiteSpace(serverMessage) ? fallbackErrorMessage : serverMessage);
            }

            var teacher = TryReadAdminTeacher(body);
            return new AdminTeacherOperationResult(
                true,
                string.IsNullOrWhiteSpace(serverMessage) ? successMessage : serverMessage,
                teacher);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or NotSupportedException)
        {
            DiagnosticLogService.LogError(LogName, "Admin teacher request failed", ex);
            return new AdminTeacherOperationResult(false, fallbackErrorMessage);
        }
    }

    public ClassOverviewReadResult ReadClassOverview()
    {
        var response = ReadClassStudents();
        if (response is null)
        {
            return new ClassOverviewReadResult
            {
                Message = "OnlineApi třídní přehled se nepodařilo načíst.",
                Students = []
            };
        }

        var students = response.Students;
        var studentsWithResults = students.Count(student => student.Summary is not null && student.Summary.TotalAnswers > 0);
        var attemptTotal = students.Sum(student => student.Summary?.TotalAnswers ?? 0);
        var correctTotal = students.Sum(student => student.Summary?.CorrectAnswers ?? 0);
        var incorrectTotal = students.Sum(student => student.Summary?.IncorrectAnswers ?? 0);
        var sessionTotal = students.Sum(student => student.Summary?.SessionsCompleted ?? 0);
        var lastActivity = students
            .Select(student => student.Summary?.LastSessionAt)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Max();

        return new ClassOverviewReadResult
        {
            Message = "OnlineApi třídní přehled byl načten pro čtení.",
            LoadedStudentsText = FormatNumber(students.Count),
            StudentsWithResultsText = FormatNumber(studentsWithResults),
            StudentsWithoutResultsText = FormatNumber(students.Count - studentsWithResults),
            SessionCountText = sessionTotal > 0 ? FormatNumber(sessionTotal) : NotAvailableText,
            AttemptCountText = attemptTotal > 0 ? FormatNumber(attemptTotal) : NotAvailableText,
            CorrectAnswersText = attemptTotal > 0 ? FormatNumber(correctTotal) : NotAvailableText,
            IncorrectAnswersText = attemptTotal > 0 ? FormatNumber(incorrectTotal) : NotAvailableText,
            AccuracyText = attemptTotal > 0 ? FormatPercent(correctTotal * 100d / attemptTotal) : NotAvailableText,
            LastActivityText = lastActivity == default ? NotAvailableText : FormatDateTime(lastActivity),
            ModeOverviewText = NotAvailableText,
            Students = students.Select(ToClassOverviewStudentItem).ToList()
        };
    }

    public StudentResultsReadResult ReadStudentResults(string studentId)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return CreateEmptyStudentResults("Výsledky žáka nejsou k dispozici.");
        }

        ClearLastError();
        try
        {
            using var httpResponse = _httpClient.GetAsync(
                    $"api/students/{Uri.EscapeDataString(_classId)}/{Uri.EscapeDataString(studentId)}/results")
                .GetAwaiter()
                .GetResult();
            if (HandleAuthorizationFailure(httpResponse.StatusCode))
            {
                return new StudentResultsReadResult
                {
                    Success = false,
                    HasResults = false,
                    Message = LastErrorMessage
                };
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                DiagnosticLogService.Log(LogName, $"Read student results failed with HTTP {(int)httpResponse.StatusCode} for class '{_classId}', HasStudentId={!string.IsNullOrWhiteSpace(studentId)}.");
                return new StudentResultsReadResult
                {
                    Success = false,
                    HasResults = false,
                    Message = "OnlineApi detail výsledků žáka se nepodařilo načíst."
                };
            }

            var response = httpResponse.Content.ReadFromJsonAsync<StudentResultDetailResponse>()
                .GetAwaiter()
                .GetResult();
            if (response is null)
            {
                return CreateEmptyStudentResults("OnlineApi nevrátilo detail výsledků žáka.");
            }

            return ToStudentResults(response);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or NotSupportedException)
        {
            DiagnosticLogService.LogError(LogName, $"Read student results failed for class '{_classId}', HasStudentId={!string.IsNullOrWhiteSpace(studentId)}", ex);
            return new StudentResultsReadResult
            {
                Success = false,
                HasResults = false,
                Message = "OnlineApi detail výsledků žáka se nepodařilo načíst."
            };
        }
    }

    public IReadOnlyList<StudentActivityListItem> ReadClassActivities(int limit = 10)
    {
        ClearLastError();
        try
        {
            using var httpResponse = _httpClient.GetAsync(
                    $"api/classes/{Uri.EscapeDataString(_classId)}/activities?limit={Math.Clamp(limit, 1, 50)}")
                .GetAwaiter()
                .GetResult();
            if (HandleAuthorizationFailure(httpResponse.StatusCode))
            {
                return [];
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                DiagnosticLogService.Log(LogName, $"Read class activities failed with HTTP {(int)httpResponse.StatusCode} for class '{_classId}'.");
                return [];
            }

            var response = httpResponse.Content.ReadFromJsonAsync<ClassActivityResponse>()
                .GetAwaiter()
                .GetResult();

            return response?.Activities
                .Select(activity => new StudentActivityListItem
                {
                    StudentNameText = activity.DisplayName,
                    ActivityTypeText = "Odehrané kolo",
                    CompletedAtText = FormatDateTime(activity.LastActivityUtc),
                    ModeText = GetModeText(activity.Mode),
                    ResultText = $"{activity.TotalAnswers} pokusů, {activity.CorrectAnswers} správně, {activity.IncorrectAnswers} špatně, úspěšnost {FormatPercent(activity.AccuracyPercent)}"
                })
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or NotSupportedException)
        {
            DiagnosticLogService.LogError(LogName, $"Read class activities failed for class '{_classId}'", ex);
            return [];
        }
    }

    private ClassStudentsResponse? ReadClassStudents()
    {
        ClearLastError();
        try
        {
            using var response = _httpClient.GetAsync($"api/classes/{Uri.EscapeDataString(_classId)}")
                .GetAwaiter()
                .GetResult();
            if (HandleAuthorizationFailure(response.StatusCode))
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                DiagnosticLogService.Log(LogName, $"Read class students failed with HTTP {(int)response.StatusCode} for class '{_classId}'.");
                LastErrorMessage = "OnlineApi seznam žáků se nepodařilo načíst.";
                return null;
            }

            return response.Content.ReadFromJsonAsync<ClassStudentsResponse>()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or NotSupportedException)
        {
            DiagnosticLogService.LogError(LogName, $"Read class students failed for class '{_classId}'", ex);
            LastErrorMessage = "OnlineApi seznam žáků se nepodařilo načíst.";
            return null;
        }
    }

    private TeacherStudentChangeResponse SendTeacherChangeRequest(Func<Task<HttpResponseMessage>> requestFactory)
    {
        ClearLastError();
        try
        {
            using var response = requestFactory().GetAwaiter().GetResult();
            if (HandleAuthorizationFailure(response.StatusCode))
            {
                return new TeacherStudentChangeResponse(false, LastErrorMessage, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                DiagnosticLogService.Log(LogName, $"Teacher change request failed with HTTP {(int)response.StatusCode} for class '{_classId}'.");
            }

            var result = response.Content.ReadFromJsonAsync<TeacherStudentChangeResponse>()
                .GetAwaiter()
                .GetResult();
            if (result is not null)
            {
                DiagnosticLogService.Log(LogName, $"Teacher change request completed for class '{_classId}', success={result.Success}.");
                return result;
            }

            DiagnosticLogService.Log(LogName, $"Teacher change request returned empty response for class '{_classId}'.");
            return new TeacherStudentChangeResponse(false, "OnlineApi nevrátilo platnou odpověď.", null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or NotSupportedException)
        {
            DiagnosticLogService.LogError(LogName, $"Teacher change request failed for class '{_classId}'", ex);
            LastErrorMessage = "OnlineApi operaci se nepodařilo dokončit.";
            return new TeacherStudentChangeResponse(false, "OnlineApi operaci se nepodařilo dokončit.", null);
        }
    }

    private static string ReadApiMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<ApiMessageResponse>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Message ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string ToSafeCzechAdminMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        if (message.Contains("Teacher username is not valid", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Parameter 'username'", StringComparison.OrdinalIgnoreCase))
        {
            return TeacherUsernameRules.InvalidUsernameMessage;
        }

        if (message.Contains("Parameter", StringComparison.OrdinalIgnoreCase))
        {
            return "Zadané údaje nejsou platné. Zkontrolujte formulář a zkuste to znovu.";
        }

        return message;
    }

    private static AdminTeacherListItem? TryReadAdminTeacher(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AdminTeacherListItem>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool HandleAuthorizationFailure(HttpStatusCode statusCode)
    {
        if (statusCode == HttpStatusCode.Forbidden)
        {
            LastForbidden = true;
            LastErrorMessage = "Nemáte oprávnění ke správě učitelů.";
            _teacherRole = TeacherRoles.Teacher;
            DiagnosticLogService.Log(LogName, $"Teacher authorization failed with HTTP {(int)statusCode} for class '{_classId}'.");
            return true;
        }

        if (statusCode != HttpStatusCode.Unauthorized)
        {
            return false;
        }

        LastAuthorizationFailed = true;
        LastErrorMessage = "Přihlášení učitele vypršelo nebo není platné. Přihlaste se znovu.";
        DiagnosticLogService.Log(LogName, $"Teacher authorization failed with HTTP {(int)statusCode} for class '{_classId}'.");
        return true;
    }

    private static bool IsUnauthorized(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.Unauthorized;

    private void ClearLastError()
    {
        LastAuthorizationFailed = false;
        LastForbidden = false;
        LastErrorMessage = string.Empty;
    }

    private static Uri CreateBaseAddress(string apiBaseUrl)
    {
        var value = DataConnectionSettings.NormalizeApiBaseUrl(apiBaseUrl);
        value = value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : new Uri($"{DataConnectionSettings.DefaultApiBaseUrl}/");
    }

    private static StudentListItem ToStudentListItem(StudentProfileResponse student) =>
        new()
        {
            StudentId = student.StudentId,
            DisplayName = student.DisplayName,
            LoginCode = student.LoginCode,
            AccountStatus = student.IsActive ? "Aktivní" : "Neaktivní",
            MustChangePin = student.MustChangePin,
            MustChangePinStatus = student.MustChangePin ? "Ano" : "Ne",
            TemporaryPinPending = student.TemporaryPinPending,
            TemporaryPinPendingStatus = student.TemporaryPinPending ? "Ano" : "Ne",
            PendingTemporaryPin = string.Empty,
            CreatedAtText = "OnlineApi",
            PinResetAtText = "OnlineApi"
        };

    private static StudentAccount ToStudentAccount(StudentProfileResponse student) =>
        new()
        {
            StudentId = student.StudentId,
            DisplayName = student.DisplayName,
            LoginCode = student.LoginCode,
            IsActive = student.IsActive,
            MustChangePin = student.MustChangePin,
            TemporaryPinPending = student.TemporaryPinPending
        };

    private static ClassOverviewStudentItem ToClassOverviewStudentItem(StudentProfileResponse student) =>
        new()
        {
            DisplayName = student.DisplayName,
            LoginCode = student.LoginCode,
            ResultsStatus = student.Summary is null || student.Summary.TotalAnswers == 0 ? "Bez výsledků" : "Výsledky existují",
            SessionCountText = student.Summary is null ? NotAvailableText : FormatNumber(student.Summary.SessionsCompleted),
            AttemptCountText = student.Summary is null ? NotAvailableText : FormatNumber(student.Summary.TotalAnswers),
            LastActivityText = FormatDateTime(student.Summary?.LastSessionAt),
            AccuracyText = student.Summary is null ? NotAvailableText : FormatPercent(student.Summary.AccuracyPercent)
        };

    private static StudentResultsReadResult ToStudentResults(StudentResultDetailResponse response)
    {
        var summary = response.Summary;
        var recentSessions = response.RecentSessions;
        var totalAnswers = response.TotalAnswers > 0 ? response.TotalAnswers : summary?.TotalAnswers;
        var totalSessions = response.TotalSessions > 0 ? response.TotalSessions : summary?.SessionsCompleted;
        var correctAnswers = summary?.CorrectAnswers ?? recentSessions.Sum(session => session.CorrectAnswers);
        var incorrectAnswers = summary?.IncorrectAnswers ?? recentSessions.Sum(session => session.IncorrectAnswers);
        var accuracy = totalAnswers is > 0
            ? correctAnswers * 100d / totalAnswers.Value
            : summary?.AccuracyPercent;

        return new StudentResultsReadResult
        {
            Success = true,
            HasResults = summary is not null || recentSessions.Count > 0,
            Message = response.Message ?? "OnlineApi detail výsledků žáka byl načten.",
            SessionCount = totalSessions,
            AttemptCount = totalAnswers,
            CorrectAnswers = correctAnswers,
            IncorrectAnswers = incorrectAnswers,
            AccuracyPercent = accuracy,
            LastActivity = response.LastSessionAt ?? summary?.LastSessionAt,
            SessionCountText = FormatNumber(totalSessions),
            AttemptCountText = FormatNumber(totalAnswers),
            LastActivityText = FormatDateTime(response.LastSessionAt ?? summary?.LastSessionAt),
            CorrectAnswersText = FormatNumber(correctAnswers),
            IncorrectAnswersText = FormatNumber(incorrectAnswers),
            AccuracyText = accuracy.HasValue ? FormatPercent(accuracy.Value) : NotAvailableText,
            ModeOverviewText = BuildModeOverview(recentSessions, summary),
            RecentActivities = recentSessions
                .OrderByDescending(session => session.LastActivityUtc)
                .Select(session => new StudentActivityListItem
                {
                    StudentNameText = response.Student.DisplayName,
                    ActivityTypeText = "Odehrané kolo",
                    CompletedAtText = FormatDateTime(session.LastActivityUtc),
                    ModeText = GetModeText(session.Mode),
                    ResultText = $"{session.TotalAnswers} pokusů, {session.CorrectAnswers} správně, {session.IncorrectAnswers} špatně, úspěšnost {FormatPercent(session.AccuracyPercent)}"
                })
                .ToList()
        };
    }

    private static string BuildModeOverview(
        IReadOnlyList<StudentSessionSummaryResponse> sessions,
        StudentSummaryResponse? summary)
    {
        if (sessions.Count > 0)
        {
            return string.Join(
                Environment.NewLine,
                sessions
                    .GroupBy(session => session.Mode)
                    .OrderBy(group => group.Key)
                    .Select(group =>
                    {
                        var attempts = group.Sum(session => session.TotalAnswers);
                        var correct = group.Sum(session => session.CorrectAnswers);
                        var wrong = group.Sum(session => session.IncorrectAnswers);
                        var accuracy = attempts > 0 ? FormatPercent(correct * 100d / attempts) : NotAvailableText;
                        return $"{GetModeText(group.Key)}: {attempts} pokusů, {correct} správně, {wrong} chyb, úspěšnost {accuracy}";
                    }));
        }

        return summary is null ? NotAvailableText : NotAvailableText;
    }

    private static StudentResultsReadResult CreateEmptyStudentResults(string message) =>
        new()
        {
            Success = true,
            HasResults = false,
            Message = message
        };

    private static string GetModeText(LearningMode mode) =>
        mode switch
        {
            LearningMode.Beginner => "Začátečník",
            LearningMode.Advanced => "Pokročilý",
            _ => NotAvailableText
        };

    private static string FormatNumber(int value) => value.ToString(CultureInfo.GetCultureInfo("cs-CZ"));

    private static string FormatNumber(int? value) =>
        value.HasValue
            ? value.Value.ToString(CultureInfo.GetCultureInfo("cs-CZ"))
            : NotAvailableText;

    private static string FormatPercent(double value) =>
        $"{Math.Round(value, 1).ToString("0.0", CultureInfo.GetCultureInfo("cs-CZ"))} %";

    private static string FormatDateTime(DateTime? value)
    {
        if (!value.HasValue || value.Value == default)
        {
            return NotAvailableText;
        }

        var localTime = value.Value.Kind == DateTimeKind.Unspecified ? value.Value : value.Value.ToLocalTime();
        return localTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("cs-CZ"));
    }
}

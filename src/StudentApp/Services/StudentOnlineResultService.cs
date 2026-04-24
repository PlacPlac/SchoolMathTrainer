using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SharedCore.Models;
using SharedCore.Services;

namespace StudentApp.Services;

public sealed class StudentOnlineResultService
{
    private const string LogName = "StudentApp";
    private readonly HttpClient _httpClient;
    private readonly string _classId;
    private string _studentSessionToken = string.Empty;
    private string _studentId = string.Empty;
    private DateTime? _studentSessionExpiresUtc;

    public StudentOnlineResultService(string apiBaseUrl, string classId)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = CreateBaseAddress(apiBaseUrl)
        };
        _classId = classId.Trim();
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_classId);

    public void SetSessionAuthorization(string token, DateTime? expiresUtc, string studentId)
    {
        _studentSessionToken = token?.Trim() ?? string.Empty;
        _studentSessionExpiresUtc = expiresUtc;
        _studentId = studentId?.Trim() ?? string.Empty;
    }

    public void ClearSessionAuthorization()
    {
        _studentSessionToken = string.Empty;
        _studentSessionExpiresUtc = null;
        _studentId = string.Empty;
    }

    public async Task<SaveStudentResultResponse?> SaveCompletedRoundAsync(StudentSession session)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(session.StudentId))
        {
            DiagnosticLogService.Log(LogName, "Online result upload skipped because class or student id is missing.");
            return null;
        }

        if (!HasValidSessionAuthorization(session.StudentId))
        {
            ClearSessionAuthorization();
            throw new StudentSessionAuthorizationException("Přihlášení žáka vypršelo. Přihlas se prosím znovu.");
        }

        var classId = Uri.EscapeDataString(_classId);
        var studentId = Uri.EscapeDataString(session.StudentId);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"api/students/{classId}/{studentId}/results")
            {
                Content = JsonContent.Create(session)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _studentSessionToken);
            var response = await _httpClient.SendAsync(request);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                ClearSessionAuthorization();
                DiagnosticLogService.Log(LogName, $"Online result upload rejected with HTTP {(int)response.StatusCode} for class '{_classId}', student '{session.StudentId}'.");
                throw new StudentSessionAuthorizationException(await ReadAuthorizationMessageAsync(response));
            }

            if (!response.IsSuccessStatusCode)
            {
                DiagnosticLogService.Log(LogName, $"Online result upload failed with HTTP {(int)response.StatusCode} for class '{_classId}', student '{session.StudentId}'.");
            }

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaveStudentResultResponse>();
            DiagnosticLogService.Log(LogName, $"Online result upload completed for class '{_classId}', student '{session.StudentId}', session '{session.SessionId}'.");
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or NotSupportedException)
        {
            DiagnosticLogService.LogError(LogName, $"Online result upload failed for class '{_classId}', student '{session.StudentId}'", ex);
            throw;
        }
    }

    private bool HasValidSessionAuthorization(string studentId)
    {
        if (string.IsNullOrWhiteSpace(_studentSessionToken) ||
            string.IsNullOrWhiteSpace(_studentId) ||
            !string.Equals(_studentId, studentId?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !_studentSessionExpiresUtc.HasValue || _studentSessionExpiresUtc.Value > DateTime.UtcNow;
    }

    private static Uri CreateBaseAddress(string apiBaseUrl)
    {
        var value = DataConnectionSettings.NormalizeApiBaseUrl(apiBaseUrl);
        value = value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : new Uri($"{DataConnectionSettings.DefaultApiBaseUrl}/");
    }

    private static async Task<string> ReadAuthorizationMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var message = await response.Content.ReadFromJsonAsync<ApiMessageResponse>();
            return string.IsNullOrWhiteSpace(message?.Message)
                ? "Přihlášení žáka vypršelo. Přihlas se prosím znovu."
                : message.Message;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or HttpRequestException)
        {
            return "Přihlášení žáka vypršelo. Přihlas se prosím znovu.";
        }
    }
}

public sealed class StudentSessionAuthorizationException : Exception
{
    public StudentSessionAuthorizationException(string message)
        : base(message)
    {
    }
}

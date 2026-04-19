using System.Net.Http;
using System.Net.Http.Json;
using SharedCore.Models;
using SharedCore.Services;

namespace StudentApp.Services;

public sealed class StudentOnlineResultService
{
    private const string LogName = "StudentApp";
    private readonly HttpClient _httpClient;
    private readonly string _classId;

    public StudentOnlineResultService(string apiBaseUrl, string classId)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = CreateBaseAddress(apiBaseUrl)
        };
        _classId = classId.Trim();
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_classId);

    public async Task<SaveStudentResultResponse?> SaveCompletedRoundAsync(StudentSession session)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(session.StudentId))
        {
            DiagnosticLogService.Log(LogName, "Online result upload skipped because class or student id is missing.");
            return null;
        }

        var classId = Uri.EscapeDataString(_classId);
        var studentId = Uri.EscapeDataString(session.StudentId);
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"api/students/{classId}/{studentId}/results", session);
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

    private static Uri CreateBaseAddress(string apiBaseUrl)
    {
        var value = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? DataConnectionSettings.DefaultApiBaseUrl
            : apiBaseUrl.Trim();
        value = value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : new Uri($"{DataConnectionSettings.DefaultApiBaseUrl}/");
    }
}

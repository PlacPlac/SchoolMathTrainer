using System.Net.Http;
using System.Net.Http.Json;
using SharedCore.Models;
using SharedCore.Services;

namespace StudentApp.Services;

public sealed class StudentOnlineLoginService
{
    private const string LogName = "StudentApp";
    private readonly HttpClient _httpClient;
    private readonly string _classId;
    private readonly string _configuredStudentId;

    public StudentOnlineLoginService(string apiBaseUrl, string classId, string configuredStudentId)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = CreateBaseAddress(apiBaseUrl)
        };
        _classId = classId.Trim();
        _configuredStudentId = configuredStudentId.Trim();
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_classId) &&
        !string.IsNullOrWhiteSpace(_configuredStudentId);

    public async Task<StudentLoginResult> LoginAsync(string loginCode, string pin, string newPin)
    {
        if (!IsAvailable)
        {
            return StudentLoginResult.Failed("Chybí soubor od paní učitelky pro tohoto žáka.");
        }

        try
        {
            var endpoint = $"api/classes/{Uri.EscapeDataString(_classId)}/login";
            var safeLoginCode = loginCode ?? string.Empty;
            var safePin = pin ?? string.Empty;
            var safeNewPin = newPin ?? string.Empty;
            DiagnosticLogService.Log(
                LogName,
                $"Online login request started for class '{_classId}', configured student '{_configuredStudentId}', endpoint '{endpoint}'.");
            var request = new StudentLoginRequest(safeLoginCode, safePin, safeNewPin, _configuredStudentId);
            var response = await _httpClient.PostAsJsonAsync(endpoint, request);
            if (response.StatusCode is System.Net.HttpStatusCode.Locked or System.Net.HttpStatusCode.TooManyRequests)
            {
                DiagnosticLogService.Log(LogName, $"Online login temporarily locked with HTTP {(int)response.StatusCode} for class '{_classId}'.");
                return StudentLoginResult.Failed(await ReadLockoutMessageAsync(response));
            }

            if (!response.IsSuccessStatusCode)
            {
                DiagnosticLogService.Log(LogName, $"Online login failed with HTTP {(int)response.StatusCode} for class '{_classId}'.");
                return StudentLoginResult.Failed("Přihlášení se nepodařilo ověřit na serveru.");
            }

            var result = await response.Content.ReadFromJsonAsync<StudentLoginResult>();
            DiagnosticLogService.Log(LogName, $"Online login response received for class '{_classId}', success={result?.Success.ToString() ?? "null"}, requiresCredentialChange={result?.RequiresPinChange.ToString() ?? "null"}.");
            if (result is null)
            {
                return StudentLoginResult.Failed("Server nevrátil platnou odpověď pro přihlášení.");
            }

            if (result.Success && string.IsNullOrWhiteSpace(result.StudentSessionToken))
            {
                return StudentLoginResult.Failed("Server nevrátil platnou autorizaci pro žáka.");
            }

            if (result.Success &&
                !string.Equals(result.StudentId, _configuredStudentId, StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticLogService.Log(LogName, $"Online login rejected because server returned a different student for class '{_classId}'.");
                return StudentLoginResult.Failed("Přihlášení neodpovídá souboru od paní učitelky. Načti správný soubor pro tohoto žáka.");
            }

            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or NotSupportedException)
        {
            DiagnosticLogService.LogError(LogName, $"Online login request failed for class '{_classId}'", ex);
            return StudentLoginResult.Failed("Server pro přihlášení není dostupný. Zkontroluj připojení a zkus to znovu.");
        }
    }

    private static Uri CreateBaseAddress(string apiBaseUrl)
    {
        var value = DataConnectionSettings.NormalizeApiBaseUrl(apiBaseUrl);
        value = value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : new Uri($"{DataConnectionSettings.DefaultApiBaseUrl}/");
    }

    private static async Task<string> ReadLockoutMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var message = await response.Content.ReadFromJsonAsync<ApiMessageResponse>();
            return string.IsNullOrWhiteSpace(message?.Message)
                ? "Příliš mnoho neúspěšných pokusů. Zkuste to prosím později."
                : message.Message;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or HttpRequestException)
        {
            return "Příliš mnoho neúspěšných pokusů. Zkuste to prosím později.";
        }
    }

}

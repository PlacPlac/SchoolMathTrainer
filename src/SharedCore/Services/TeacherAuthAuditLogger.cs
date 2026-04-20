using System.Text;
using System.Text.Json;

namespace SharedCore.Services;

public sealed class TeacherAuthAuditLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _sync = new();
    private readonly TeacherAccountStore _accountStore;

    public TeacherAuthAuditLogger(TeacherAccountStore accountStore)
    {
        _accountStore = accountStore;
    }

    public void Write(
        string eventType,
        string username,
        string remoteAddress,
        string route,
        int statusCode,
        string reason)
    {
        var entry = new TeacherAuthAuditEntry
        {
            TimestampUtc = DateTime.UtcNow,
            EventType = eventType,
            Username = NormalizeForLog(username),
            RemoteAddress = NormalizeForLog(remoteAddress),
            Route = NormalizeForLog(route),
            StatusCode = statusCode,
            Reason = NormalizeForLog(reason)
        };

        var line = JsonSerializer.Serialize(entry, SerializerOptions);
        lock (_sync)
        {
            Directory.CreateDirectory(_accountStore.SecurityDirectory);
            File.AppendAllText(AuditFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    private string AuditFilePath => Path.Combine(_accountStore.SecurityDirectory, "teacher-auth-audit.jsonl");

    private static string NormalizeForLog(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ReplaceLineEndings(" ");
        return normalized.Length <= 240 ? normalized : normalized[..240];
    }
}

public sealed class TeacherAuthAuditEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string EventType { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string RemoteAddress { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string Reason { get; set; } = string.Empty;
}

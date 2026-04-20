namespace SharedCore.Models;

public sealed class TeacherSessionRecord
{
    public string TokenHash { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime IssuedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}

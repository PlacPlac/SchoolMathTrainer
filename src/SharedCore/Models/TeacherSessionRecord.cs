namespace SharedCore.Models;

public sealed class TeacherSessionRecord
{
    public string TokenHash { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;
}

namespace SharedCore.Models;

public sealed class TeacherAuthSettings
{
    public int Version { get; set; } = 1;
    public string TokenSigningKey { get; set; } = string.Empty;
    public int TokenLifetimeMinutes { get; set; } = 480;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

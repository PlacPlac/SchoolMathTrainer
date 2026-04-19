namespace SharedCore.Models;

public sealed class StudentAccount
{
    public string StudentId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string LoginCode { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;
    public string PinSalt { get; set; } = string.Empty;
    public bool MustChangePin { get; set; } = true;
    public bool TemporaryPinPending { get; set; }
    public string? PendingTemporaryPinEncrypted { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PinResetAt { get; set; }
}

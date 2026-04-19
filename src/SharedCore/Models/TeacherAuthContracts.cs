namespace SharedCore.Models;

public sealed record TeacherLoginRequest(
    string Username,
    string Password);

public sealed record TeacherLoginResponse(
    bool Success,
    string Message,
    string Token = "",
    DateTime? ExpiresUtc = null,
    string Username = "",
    string DisplayName = "");

public sealed record TeacherTokenValidationResult(
    bool Success,
    string Username = "",
    string DisplayName = "",
    DateTime? ExpiresUtc = null,
    string Message = "");

public sealed record StudentOnboardingProfileResponse(
    string StudentId,
    string LoginCode,
    bool IsActive);

namespace SharedCore.Models;

public sealed record TeacherLoginRequest(
    string Username,
    string Password);

public sealed record TeacherLoginResponse(
    string Token,
    DateTime ExpiresUtc);

public sealed record TeacherLoginClientResult(
    bool Success,
    string Message,
    string Token = "",
    DateTime? ExpiresUtc = null,
    string Username = "");

public sealed record TeacherTokenValidationResult(
    bool Success,
    string Username = "",
    DateTime? ExpiresUtc = null,
    string Message = "");

public sealed record StudentOnboardingProfileResponse(
    string StudentId,
    string LoginCode,
    bool IsActive);

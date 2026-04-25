namespace SharedCore.Models;

public sealed record TeacherLoginRequest(
    string Username,
    string Password);

public sealed record TeacherLoginResponse(
    string Token,
    DateTime ExpiresUtc,
    string Role = TeacherRoles.Teacher);

public sealed record TeacherLoginClientResult(
    bool Success,
    string Message,
    string Token = "",
    DateTime? ExpiresUtc = null,
    string Username = "",
    string Role = TeacherRoles.Teacher);

public sealed record TeacherTokenValidationResult(
    bool Success,
    string Username = "",
    string Role = TeacherRoles.Teacher,
    DateTime? ExpiresUtc = null,
    string Message = "");

public sealed record AdminTeacherListItem(
    string Username,
    string DisplayName,
    string Role,
    bool Active,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record AdminCreateTeacherRequest(
    string Username,
    string DisplayName,
    string Password,
    string? Role);

public sealed record AdminUpdateTeacherRequest(
    string? DisplayName,
    string? Role);

public sealed record AdminResetTeacherPasswordRequest(
    string Password);

public sealed record StudentOnboardingProfileResponse(
    string StudentId,
    string LoginCode,
    bool IsActive);

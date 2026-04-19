using SharedCore.Models;

namespace SchoolMathTrainer.Api.Services;

internal sealed record ClassDataReadResult(
    bool Success,
    string Message,
    IReadOnlyList<StudentProfileResponse> Students);

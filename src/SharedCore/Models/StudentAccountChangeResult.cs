namespace SharedCore.Models;

public sealed class StudentAccountChangeResult
{
    public StudentAccount Account { get; init; } = new();
    public string TemporaryPin { get; init; } = string.Empty;
}

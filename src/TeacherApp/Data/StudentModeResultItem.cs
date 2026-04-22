namespace TeacherApp.Data;

public sealed class StudentModeResultItem
{
    public string ModeText { get; init; } = string.Empty;
    public int Attempts { get; init; }
    public int CorrectAnswers { get; init; }
    public int IncorrectAnswers { get; init; }
}

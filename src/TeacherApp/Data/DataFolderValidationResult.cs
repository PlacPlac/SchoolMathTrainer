namespace TeacherApp.Data;

public sealed class DataFolderValidationResult
{
    public DataFolderValidationStatus Status { get; init; }
    public string FolderPath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

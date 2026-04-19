namespace SharedCore.Models;

public sealed class SharedDataFolderSettingsResult
{
    public bool HasSetting { get; init; }
    public bool IsValid { get; init; }
    public string DataFolderPath { get; init; } = string.Empty;
    public string ClassId { get; init; } = string.Empty;
    public string ClassFolderName { get; init; } = string.Empty;
    public string StudentId { get; init; } = string.Empty;
    public string ApiBaseUrl { get; init; } = string.Empty;
    public bool IsStudentConfigurationImported { get; init; }
    public string Message { get; init; } = string.Empty;
}

namespace SharedCore.Models;

public sealed class SharedDataFolderSettings
{
    public int Version { get; set; }
    public string ClassId { get; set; } = string.Empty;
    public string ClassFolderName { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string DataFolderPath { get; set; } = string.Empty;
    public bool IsStudentConfigurationImported { get; set; }
}

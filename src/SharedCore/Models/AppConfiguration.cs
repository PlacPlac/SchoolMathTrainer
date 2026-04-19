namespace SharedCore.Models;

public sealed class AppConfiguration
{
    public string SharedDataRoot { get; set; } = string.Empty;
    public string StudentDataDirectory { get; set; } = string.Empty;
    public string SessionDataDirectory { get; set; } = string.Empty;
    public string StudentResultsDirectory { get; set; } = string.Empty;
    public string StudentAccountFilePath { get; set; } = string.Empty;
    public string PublicClassOverviewFilePath { get; set; } = string.Empty;
    public string ExportDirectory { get; set; } = string.Empty;
    public string ConfigDirectory { get; set; } = string.Empty;
    public string LogDirectory { get; set; } = string.Empty;
    public DataConnectionSettings DataConnection { get; set; } = new();
    public int MaxOperandValue { get; set; } = 20;
    public int AnswerOptionCount { get; set; } = 4;
    public int RetryCount { get; set; } = 4;
    public int RetryDelayMilliseconds { get; set; } = 250;
    public int AutoRefreshSeconds { get; set; } = 10;
}

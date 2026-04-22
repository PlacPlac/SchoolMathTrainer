namespace TeacherApp.Data;

public sealed class TeacherSftpReadOnlyLoadResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string LocalCacheRoot { get; init; } = string.Empty;
    public IReadOnlyList<string> RemoteFiles { get; init; } = [];
}

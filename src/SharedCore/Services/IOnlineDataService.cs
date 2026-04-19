namespace SharedCore.Services;

public interface IOnlineDataService
{
    string ResolveClassDataRoot(string classId);
    string BuildStudentDataPath(string classId, string studentId, params string[] segments);
    void EnsureDirectory(string path);
    bool FileExists(string path);
    string ReadFile(string path);
    void WriteFile(string path, string content);
    void CopyFile(string sourcePath, string destinationPath, bool overwrite);
    void DeleteFile(string path);
    void AppendLine(string path, string line);
    IReadOnlyList<string> ListFiles(string directoryPath, string searchPattern, SearchOption searchOption);
}

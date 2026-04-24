using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TeacherApp.Settings;

namespace TeacherApp.Data;

public sealed class TeacherSftpReadOnlyDataSource
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(25);

    public async Task<TeacherSftpReadOnlyLoadResult> LoadAsync(
        TeacherServerSettings settings,
        CancellationToken cancellationToken = default)
    {
        var validationMessage = Validate(settings);
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            return CreateFailure(validationMessage);
        }

        var knownHostsValidationMessage = ValidateKnownHosts(settings);
        if (!string.IsNullOrWhiteSpace(knownHostsValidationMessage))
        {
            return CreateFailure(knownHostsValidationMessage);
        }

        var cacheRoot = CreateCacheRoot(settings);
        Directory.CreateDirectory(cacheRoot);
        Directory.CreateDirectory(Path.Combine(cacheRoot, "Config"));
        Directory.CreateDirectory(Path.Combine(cacheRoot, "Data", "Public"));

        var listResult = await RunSftpAsync(settings, BuildListCommands(settings), cancellationToken);
        if (!listResult.Success)
        {
            return CreateFailure(listResult.Message);
        }

        var remoteFiles = ParseRemoteFileList(listResult.StandardOutput);
        if (remoteFiles.Count == 0)
        {
            return new TeacherSftpReadOnlyLoadResult
            {
                Success = false,
                Message = "Vzdálená datová složka je prázdná.",
                LocalCacheRoot = cacheRoot,
                RemoteFiles = []
            };
        }

        var downloadResult = await RunSftpAsync(settings, BuildDownloadCommands(
            settings,
            cacheRoot,
            "Config/student-accounts.json",
            "Config/student-accounts.json"), cancellationToken);
        if (!downloadResult.Success)
        {
            return CreateFailure(downloadResult.Message);
        }

        _ = await RunSftpAsync(settings, BuildDownloadCommands(
            settings,
            cacheRoot,
            "Data/Public/class-overview.json",
            "Data/Public/class-overview.json"), cancellationToken);

        var accountFilePath = Path.Combine(cacheRoot, "Config", "student-accounts.json");
        if (!File.Exists(accountFilePath))
        {
            return new TeacherSftpReadOnlyLoadResult
            {
                Success = false,
                Message = "Server je dostupný, ale ve vzdálených datech nebyl nalezen soubor Config/student-accounts.json.",
                LocalCacheRoot = cacheRoot,
                RemoteFiles = remoteFiles
            };
        }

        return new TeacherSftpReadOnlyLoadResult
        {
            Success = true,
            Message = $"Data ze serveru byla načtena pouze pro čtení. Počet položek ve vzdálené složce: {remoteFiles.Count}.",
            LocalCacheRoot = cacheRoot,
            RemoteFiles = remoteFiles
        };
    }

    private static string Validate(TeacherServerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            return "Host je povinný.";
        }

        if (settings.Port is <= 0 or > 65535)
        {
            return "Port musí být číslo od 1 do 65535.";
        }

        if (string.IsNullOrWhiteSpace(settings.Username))
        {
            return "Uživatel je povinný pro SFTP čtení.";
        }

        if (string.IsNullOrWhiteSpace(settings.RemoteDataPath))
        {
            return "Vzdálená datová složka je povinná pro SFTP čtení.";
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> BuildListCommands(TeacherServerSettings settings) =>
    [
        $"cd {QuoteSftpPath(settings.RemoteDataPath)}",
        "ls -1",
        "bye"
    ];

    private static IReadOnlyList<string> BuildDownloadCommands(
        TeacherServerSettings settings,
        string cacheRoot,
        string remotePath,
        string localPath) =>
    [
        $"cd {QuoteSftpPath(settings.RemoteDataPath)}",
        $"lcd {QuoteSftpPath(cacheRoot)}",
        $"get {QuoteSftpPath(remotePath)} {QuoteSftpPath(localPath)}",
        "bye"
    ];

    private static async Task<SftpCommandResult> RunSftpAsync(
        TeacherServerSettings settings,
        IReadOnlyList<string> commands,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);

            var startInfo = new ProcessStartInfo
            {
                FileName = "sftp",
                Arguments = BuildSftpArguments(settings),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return SftpCommandResult.Failed("SFTP klient se nepodařilo spustit.");
            }

            await process.StandardInput.WriteLineAsync(string.Join(Environment.NewLine, commands));
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            var standardOutput = await outputTask;
            var standardError = await errorTask;
            if (process.ExitCode == 0)
            {
                return SftpCommandResult.Ok(standardOutput);
            }

            var message = string.IsNullOrWhiteSpace(standardError)
                ? $"SFTP příkaz skončil chybou {process.ExitCode}."
                : standardError.Trim();
            return SftpCommandResult.Failed(message);
        }
        catch (OperationCanceledException)
        {
            return SftpCommandResult.Failed("SFTP čtení vypršelo. Zkontrolujte síť, host, port a SSH klíč.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return SftpCommandResult.Failed($"SFTP čtení nelze provést: {ex.Message}");
        }
    }

    private static string BuildSftpArguments(TeacherServerSettings settings)
    {
        var endpoint = $"{settings.Username.Trim()}@{settings.Host.Trim()}";
        var knownHostsPath = QuoteArgument(GetKnownHostsPath());
        return $"-q -oBatchMode=yes -oStrictHostKeyChecking=yes -oUserKnownHostsFile={knownHostsPath} -P {settings.Port} {endpoint}";
    }

    private static string ValidateKnownHosts(TeacherServerSettings settings)
    {
        var knownHostsPath = GetKnownHostsPath();
        if (!File.Exists(knownHostsPath))
        {
            return "Chybí ověřený SSH host key serveru. Nejdříve proveďte bezpečné ověření a uložení host key.";
        }

        if (!HasKnownHostsEntry(knownHostsPath, settings.Host, settings.Port))
        {
            return "Chybí ověřený SSH host key serveru. Nejdříve proveďte bezpečné ověření a uložení host key.";
        }

        return string.Empty;
    }

    private static bool HasKnownHostsEntry(string knownHostsPath, string host, int port)
    {
        var normalizedHost = host.Trim();
        if (string.IsNullOrWhiteSpace(normalizedHost))
        {
            return false;
        }

        var expectedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            normalizedHost
        };

        if (port != 22)
        {
            expectedHosts.Add($"[{normalizedHost}]:{port}");
        }

        foreach (var rawLine in File.ReadLines(knownHostsPath, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var firstWhitespace = line.IndexOfAny([' ', '\t']);
            if (firstWhitespace <= 0)
            {
                continue;
            }

            var hosts = line[..firstWhitespace]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (hosts.Any(expectedHosts.Contains))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetKnownHostsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SchoolMathTrainer",
            "ssh",
            "known_hosts");

    private static IReadOnlyList<string> ParseRemoteFileList(string output) =>
        output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("sftp>", StringComparison.OrdinalIgnoreCase))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

    private static string CreateCacheRoot(TeacherServerSettings settings)
    {
        var value = $"{settings.Host}:{settings.Port}:{settings.Username}:{settings.RemoteDataPath}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SchoolMathTrainer",
            "teacher-sftp-cache",
            hash);
    }

    private static string QuoteSftpPath(string path) =>
        $"\"{path.Replace("\\", "/", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string QuoteArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static TeacherSftpReadOnlyLoadResult CreateFailure(string message) =>
        new()
        {
            Success = false,
            Message = message,
            RemoteFiles = []
        };

    private sealed class SftpCommandResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public string StandardOutput { get; init; } = string.Empty;

        public static SftpCommandResult Ok(string standardOutput) =>
            new()
            {
                Success = true,
                StandardOutput = standardOutput
            };

        public static SftpCommandResult Failed(string message) =>
            new()
            {
                Success = false,
                Message = message
            };
    }
}

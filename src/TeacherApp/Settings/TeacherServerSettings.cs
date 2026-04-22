namespace TeacherApp.Settings;

public sealed class TeacherServerSettings
{
    public const string DefaultHost = "89.221.212.49";
    public const int DefaultPort = 22;
    public const string DefaultUsername = "schoolmath";
    public const string DefaultRemoteDataPath = "/srv/schoolmath/data";

    public string Host { get; set; } = DefaultHost;
    public int Port { get; set; } = DefaultPort;
    public string Username { get; set; } = DefaultUsername;
    public string RemoteDataPath { get; set; } = DefaultRemoteDataPath;
    public DateTime? SavedAtUtc { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && Port is > 0 and <= 65535;

    public string DisplayText
    {
        get
        {
            var host = string.IsNullOrWhiteSpace(Host) ? DefaultHost : Host.Trim();
            return IsConfigured
                ? $"Server: {host}:{Port}"
                : "Server zatím není nastaven.";
        }
    }

    public static TeacherServerSettings CreateDefault() =>
        new()
        {
            Host = DefaultHost,
            Port = DefaultPort,
            Username = DefaultUsername,
            RemoteDataPath = DefaultRemoteDataPath
        };
}

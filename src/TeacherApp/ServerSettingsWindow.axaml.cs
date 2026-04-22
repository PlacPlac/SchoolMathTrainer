using Avalonia.Controls;
using Avalonia.Interactivity;
using TeacherApp.Settings;

namespace TeacherApp;

public partial class ServerSettingsWindow : Window
{
    private readonly TeacherServerSettingsService _settingsService;
    private readonly TeacherSshConnectionTester _connectionTester = new();

    public ServerSettingsWindow()
        : this(new TeacherServerSettingsService())
    {
    }

    public ServerSettingsWindow(TeacherServerSettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        LoadSettings();
    }

    public TeacherServerSettings? SavedSettings { get; private set; }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        HostTextBox.Text = settings.Host;
        PortTextBox.Text = settings.Port.ToString();
        UsernameTextBox.Text = settings.Username;
        RemoteDataPathTextBox.Text = settings.RemoteDataPath;
        StatusTextBlock.Text = settings.SavedAtUtc.HasValue
            ? "Nastavení serveru je uložené."
            : "Zkontrolujte a uložte nastavení serveru pro budoucí SSH/SFTP režim.";
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void OnTestConnectionClick(object? sender, RoutedEventArgs e)
    {
        if (!TryReadSettingsFromInputs(out var settings))
        {
            return;
        }

        StatusTextBlock.Text = "Testuji SSH připojení...";
        var result = await _connectionTester.TestAsync(settings);
        StatusTextBlock.Text = result.Message;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (!TryReadSettingsFromInputs(out var settings))
        {
            return;
        }

        try
        {
            _settingsService.Save(settings);
            SavedSettings = _settingsService.Load();
            Close(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            StatusTextBlock.Text = "Nastavení serveru se nepodařilo bezpečně uložit.";
        }
    }

    private bool TryReadSettingsFromInputs(out TeacherServerSettings settings)
    {
        var host = HostTextBox.Text?.Trim() ?? string.Empty;
        var username = UsernameTextBox.Text?.Trim() ?? string.Empty;
        var remoteDataPath = RemoteDataPathTextBox.Text?.Trim() ?? string.Empty;
        settings = new TeacherServerSettings();

        if (string.IsNullOrWhiteSpace(host))
        {
            StatusTextBlock.Text = "Vyplňte host serveru.";
            return false;
        }

        if (!int.TryParse(PortTextBox.Text, out var port) || port is <= 0 or > 65535)
        {
            StatusTextBlock.Text = "Port musí být číslo od 1 do 65535.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            StatusTextBlock.Text = "Vyplňte uživatele pro SSH/SFTP.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(remoteDataPath))
        {
            StatusTextBlock.Text = "Vyplňte vzdálenou datovou složku.";
            return false;
        }

        settings = new TeacherServerSettings
        {
            Host = host,
            Port = port,
            Username = username,
            RemoteDataPath = remoteDataPath
        };

        return true;
    }
}

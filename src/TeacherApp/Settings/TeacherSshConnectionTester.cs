using System.Net.Sockets;
using System.Text;

namespace TeacherApp.Settings;

public sealed class TeacherSshConnectionTester
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);

    public async Task<TeacherSshConnectionTestResult> TestAsync(
        TeacherServerSettings settings,
        CancellationToken cancellationToken = default)
    {
        var validationMessage = Validate(settings);
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            return TeacherSshConnectionTestResult.Failed(validationMessage);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);

            using var client = new TcpClient();
            await client.ConnectAsync(settings.Host.Trim(), settings.Port, timeout.Token);

            await using var stream = client.GetStream();
            var banner = await ReadSshBannerAsync(stream, timeout.Token);
            if (string.IsNullOrWhiteSpace(banner))
            {
                return TeacherSshConnectionTestResult.Failed(
                    "Server odpověděl, ale neposlal očekávaný SSH identifikační řádek.");
            }

            if (!banner.StartsWith("SSH-", StringComparison.OrdinalIgnoreCase))
            {
                return TeacherSshConnectionTestResult.Failed(
                    $"Server odpověděl, ale nevypadá jako SSH server: {banner}");
            }

            return TeacherSshConnectionTestResult.Ok(
                $"Připojení úspěšné. Server odpověděl: {banner}. SFTP přihlášení zatím neproběhlo, protože heslo ani klíč se do konfigurace neukládají.");
        }
        catch (OperationCanceledException)
        {
            return TeacherSshConnectionTestResult.Failed("Test připojení vypršel. Zkontrolujte host, port a síť.");
        }
        catch (SocketException ex)
        {
            return TeacherSshConnectionTestResult.Failed($"Připojení se nepodařilo: {ex.Message}");
        }
        catch (IOException ex)
        {
            return TeacherSshConnectionTestResult.Failed($"Serverové spojení se nepodařilo přečíst: {ex.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return TeacherSshConnectionTestResult.Failed($"Test připojení nelze provést: {ex.Message}");
        }
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
            return "Uživatel je povinný pro test SSH/SFTP připojení.";
        }

        if (string.IsNullOrWhiteSpace(settings.RemoteDataPath))
        {
            return "Vzdálená datová složka je povinná pro test SSH/SFTP připojení.";
        }

        return string.Empty;
    }

    private static async Task<string> ReadSshBannerAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[256];
        var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
        if (bytesRead <= 0)
        {
            return string.Empty;
        }

        return Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
    }
}

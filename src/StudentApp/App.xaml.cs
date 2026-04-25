using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SharedCore.Services;
using StudentApp.Services;
using StudentApp.ViewModels;
using StudentApp.Views;

namespace StudentApp;

public partial class App : Application
{
    private static readonly string StartupLogPath = Path.Combine(AppContext.BaseDirectory, "studentapp-startup.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterGlobalDiagnostics();
        LogStartup("OnStartup entered.");

        try
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            LogStartup("Application base startup completed.");

            var configurationService = new ConfigurationService();
            var onlineDataService = new OnlineDataService();
            var sharedDataFolderSettingsService = new SharedDataFolderSettingsService(onlineDataService);
            var sharedDataFolderSetting = sharedDataFolderSettingsService.Load();
            if (!HasValidStudentConfiguration(sharedDataFolderSetting))
            {
                LogStartup("Student configuration is missing or invalid. Showing first run config window.");
                var configured = ShowFirstRunConfigWindow();
                sharedDataFolderSetting = sharedDataFolderSettingsService.Load();
                if (!configured || !HasValidStudentConfiguration(sharedDataFolderSetting))
                {
                    LogStartup("First run configuration was not completed.");
                    Shutdown();
                    return;
                }
            }

            var configuration = configurationService.LoadFromFile("appsettings.json");
            LogStartup("Configuration loaded.");
            if (sharedDataFolderSetting.HasSetting && !sharedDataFolderSetting.IsValid)
            {
                MessageBox.Show(
                    $"{sharedDataFolderSetting.Message}\nStudentApp použije výchozí datovou složku z appsettings.json.",
                    "Datová složka",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                LogStartup(sharedDataFolderSetting.Message);
            }

            var retryFileAccessService = new RetryFileAccessService();
            var storageService = new FileSystemStorageService(
                retryFileAccessService,
                onlineDataService,
                configuration.RetryCount,
                configuration.RetryDelayMilliseconds);
            var loggingService = new LoggingService(storageService, configuration);
            var statisticsService = new StatisticsService();
            var mathProblemGenerator = new MathProblemGenerator();
            var csvExportService = new CsvExportService(storageService, configuration);
            var progressService = new StudentProgressService(
                configuration,
                storageService,
                statisticsService,
                loggingService,
                csvExportService);
            var apiBaseUrl = string.IsNullOrWhiteSpace(sharedDataFolderSetting.ApiBaseUrl)
                ? configuration.DataConnection.ApiBaseUrl
                : sharedDataFolderSetting.ApiBaseUrl;
            var onlineLoginService = new StudentOnlineLoginService(
                apiBaseUrl,
                sharedDataFolderSetting.ClassId,
                sharedDataFolderSetting.StudentId);
            var onlineResultService = new StudentOnlineResultService(
                apiBaseUrl,
                sharedDataFolderSetting.ClassId);
            LogStartup("Services initialized.");

            StudentShellWindow? shellWindow = null;
            var shellViewModel = new StudentShellViewModel(
                progressService,
                mathProblemGenerator,
                loggingService,
                onlineLoginService,
                onlineResultService,
                sharedDataFolderSetting.StudentId,
                () => MainWindow is null ? false : ImportStudentConfiguration(MainWindow),
                () => shellWindow?.Close());
            shellWindow = new StudentShellWindow(shellViewModel);
            LogStartup("StudentShellWindow instance created.");

            MainWindow = shellWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            shellWindow.Show();
            shellWindow.UpdateLayout();
            shellWindow.Activate();

            var windowHandle = new WindowInteropHelper(shellWindow).EnsureHandle();
            LogStartup($"StudentShellWindow shown. Handle={windowHandle}, Visible={shellWindow.IsVisible}, Loaded={shellWindow.IsLoaded}, WindowState={shellWindow.WindowState}.");
        }
        catch (Exception ex)
        {
            LogException("Startup failure", ex);
            MessageBox.Show("Aplikaci se nepodařilo spustit. Podrobnosti jsou uložené v diagnostickém logu.", "StudentApp", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static bool HasValidStudentConfiguration(SharedCore.Models.SharedDataFolderSettingsResult setting)
    {
        return setting.IsValid &&
            setting.IsStudentConfigurationImported &&
            !string.IsNullOrWhiteSpace(setting.ClassId) &&
            !string.IsNullOrWhiteSpace(setting.StudentId);
    }

    private static bool ShowFirstRunConfigWindow()
    {
        var firstRunWindow = new FirstRunConfigWindow
        {
            ShowInTaskbar = true
        };

        return firstRunWindow.ShowDialog() == true;
    }

    public static bool ImportStudentConfiguration(Window owner)
    {
        var firstRunWindow = new FirstRunConfigWindow
        {
            Owner = owner,
            ShowInTaskbar = false
        };

        return firstRunWindow.ShowDialog() == true;
    }

    private void RegisterGlobalDiagnostics()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        LogStartup("Global diagnostics registered.");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("DispatcherUnhandledException", e.Exception);
        MessageBox.Show("V aplikaci nastala neočekávaná chyba. Podrobnosti jsou uložené v diagnostickém logu.", "StudentApp", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(-1);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogException("AppDomain.CurrentDomain.UnhandledException", exception);
            MessageBox.Show("V aplikaci nastala závažná chyba. Podrobnosti jsou uložené v diagnostickém logu.", "StudentApp", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LogStartup($"Unhandled non-exception object: {e.ExceptionObject}");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void LogStartup(string message)
    {
        WriteLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
    }

    private static void LogException(string source, Exception exception)
    {
        WriteLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}{Environment.NewLine}{exception}{Environment.NewLine}");
    }

    private static void WriteLog(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StartupLogPath)!);
        File.AppendAllText(StartupLogPath, content + Environment.NewLine, Encoding.UTF8);
    }
}

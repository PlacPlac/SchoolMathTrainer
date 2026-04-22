using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SharedCore.Services;

namespace TeacherApp;

public partial class App : Application
{
    private const string LogName = "TeacherApp";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        RegisterDiagnostics();
        DiagnosticLogService.Log(LogName, "TeacherApp startup.");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterDiagnostics()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                DiagnosticLogService.LogError(LogName, "Unhandled exception", exception);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DiagnosticLogService.LogError(LogName, "Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            DiagnosticLogService.LogError(LogName, "UI thread exception", args.Exception);
        };
    }
}

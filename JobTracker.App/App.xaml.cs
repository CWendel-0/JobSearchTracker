using System.IO;
using System.Windows;
using System.Windows.Threading;
using Serilog;

namespace JobTracker.App;

/// <summary>
/// Configures the rotating log file on startup and ensures it is flushed on
/// exit. Any exception that escapes all other handlers is caught here,
/// written to the log, and shown to the user before the process exits.
///
/// Log location: %APPDATA%\JobTracker\logs\jobtracker-YYYYMMDD.log
/// Retention:    7 rolling daily files.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigureLogging();

        // Catch any exception that bubbles past all other handlers.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        Log.Information("JobTracker {Version} started",
            typeof(App).Assembly.GetName().Version);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("JobTracker exiting (code {Code})", e.ApplicationExitCode);
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    // ── Logging setup ─────────────────────────────────────────────────────────

    private static void ConfigureLogging()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JobTracker", "logs");

        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: Path.Combine(logDir, "jobtracker-.log"),
                rollingInterval:        RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}" +
                    "{NewLine}{Exception}")
            .CreateLogger();
    }

    // ── Unhandled exception handler ───────────────────────────────────────────

    private static void OnDispatcherUnhandledException(
        object                                sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception on UI thread");
        Log.CloseAndFlush();

        MessageBox.Show(
            $"An unexpected error occurred and JobTracker must close.\n\n" +
            $"{e.Exception.Message}\n\n" +
            $"Details have been written to the log file:\n" +
            $@"%APPDATA%\JobTracker\logs\",
            "Unexpected Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        Current.Shutdown(1);
    }
}

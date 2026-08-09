using Avalonia;

namespace Aphelion.Desktop.UI;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Last-resort crash reporting. An unhandled exception on any thread kills
        // the process; without this, the only trace is whatever the console showed.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog(e.ExceptionObject as Exception);

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void WriteCrashLog(Exception? exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Aphelion",
                "crashes");

            Directory.CreateDirectory(directory);

            File.WriteAllText(
                Path.Combine(directory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log"),
                exception?.ToString() ?? "Unknown failure.");
        }
        catch (Exception)
        {
            // The crash log is best effort; failing to write it must not mask the
            // original crash.
        }
    }
}

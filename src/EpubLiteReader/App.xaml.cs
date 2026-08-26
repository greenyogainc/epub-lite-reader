using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;

namespace EpubLiteReader;

public partial class App : Application
{
    /// <summary>EPUB passed on the command line (e.g. via "Open with" / file association).</summary>
    public string? StartupFile { get; private set; }

    /// <summary>
    /// Opt-in automation hook (--statefile=&lt;path&gt;): the UI mirrors its state to
    /// this JSON file so scripted captures can wait deterministically. Null in
    /// normal interactive use.
    /// </summary>
    internal static string? AutomationStateFile { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        foreach (var arg in e.Args)
        {
            if (arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var culture = new CultureInfo(arg["--lang=".Length..]);
                    Thread.CurrentThread.CurrentUICulture = culture;
                    CultureInfo.DefaultThreadCurrentUICulture = culture;
                }
                catch (CultureNotFoundException) { }
            }
            else if (arg.StartsWith("--statefile=", StringComparison.OrdinalIgnoreCase))
            {
                var file = arg["--statefile=".Length..];
                if (!string.IsNullOrWhiteSpace(file))
                    AutomationStateFile = Path.GetFullPath(file);
            }
            else if (StartupFile is null && File.Exists(arg))
            {
                StartupFile = arg;
            }
        }

        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            Strings.ShowError(null, string.Format(Strings.Get("UnhandledErrorMessage"), args.Exception.Message, LogPath));
            args.Handled = true;
        };

        base.OnStartup(e);
    }

    private static string LogPath =>
        Path.Combine(Path.GetTempPath(), "EpubLiteReader.log");

    internal static void LogError(Exception ex)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}

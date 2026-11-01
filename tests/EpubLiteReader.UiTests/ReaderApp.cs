using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace EpubLiteReader.UiTests;

/// <summary>
/// The one WPF <see cref="Application"/> a process may create, on one STA thread. It starts on
/// first use, so a run with the window suite skipped starts nothing, and it is never shut down:
/// nothing WPF can follow a shutdown, and the thread ends with the test host.
/// </summary>
internal static class ReaderApp
{
    private static readonly Lazy<Dispatcher> Shared = new(Start);
    private static Exception? _unhandled;

    public static Dispatcher Dispatcher => Shared.Value;

    /// <summary>The last exception no dispatcher frame handled, kept until it is taken.</summary>
    public static Exception? Unhandled => Volatile.Read(ref _unhandled);

    public static Exception? TakeUnhandled() => Interlocked.Exchange(ref _unhandled, null);

    private static Dispatcher Start()
    {
        var profile = Path.Combine(Path.GetTempPath(), "elr-ui-webview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", profile);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(profile);

        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                // App.xaml's StartupUri would open a second, production window, and its
                // setter rejects null, so clear the field the property reads.
                typeof(System.Windows.Application).GetField("_startupUri", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(app, null);
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += (_, args) =>
                {
                    Volatile.Write(ref _unhandled, args.Exception);
                    args.Handled = true;
                };
                app.Startup += async (_, _) =>
                {
                    try
                    {
                        await KeepBrowserProcessAliveAsync();
                        ready.TrySetResult(Dispatcher.CurrentDispatcher);
                    }
                    catch (Exception ex)
                    {
                        ready.TrySetException(ex);
                    }
                };
                app.Run();
            }
            catch (Exception ex)
            {
                ready.TrySetException(ex);
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Task.Wait(TimeSpan.FromSeconds(60)))
            throw new TimeoutException("The reader application did not start.");
        return ready.Task.Result;
    }

    /// <summary>A browser process exits asynchronously after its last WebView2 closes, and a new
    /// WebView2 racing that exit fails on the same profile. This one hidden view keeps it running.</summary>
    private static async Task KeepBrowserProcessAliveAsync()
    {
        var view = new WebView2();
        var holder = new Window
        {
            Width = 1,
            Height = 1,
            Left = -32000,
            Top = -32000,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = view
        };
        HolderHandle = new System.Windows.Interop.WindowInteropHelper(holder).EnsureHandle();
        holder.Show();
        await view.EnsureCoreWebView2Async();
    }

    /// <summary>The hidden keep-alive window, which never counts as a stray window.</summary>
    public static IntPtr HolderHandle { get; private set; }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // WebView2 can keep the profile folder locked briefly after shutdown.
        }
    }
}

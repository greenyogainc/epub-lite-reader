using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace EpubLiteReader.UiTests;

/// <summary>Hosts one real <see cref="MainWindow"/> on an STA thread so coverage sees the window and WebView2.</summary>
internal sealed class StaReadingSession : IDisposable
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _dataDir;
    private Thread? _thread;
    private MainWindow? _window;
    private Exception? _dispatcherError;

    private StaReadingSession(string dataDir, string stateFile)
    {
        _dataDir = dataDir;
        StateFile = stateFile;
    }

    public string StateFile { get; }

    public IntPtr Handle { get; private set; }

    public Exception? DispatcherError => _dispatcherError;

    public static StaReadingSession Start(string epubPath, string dataDir, string stateFile)
    {
        Directory.CreateDirectory(dataDir);
        var webViewDir = Path.Combine(dataDir, "webview");
        Directory.CreateDirectory(webViewDir);
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webViewDir);

        var session = new StaReadingSession(dataDir, stateFile);
        try
        {
            session.Launch(epubPath);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public T Invoke<T>(Func<MainWindow, T> action)
    {
        var window = _window ?? throw new InvalidOperationException("The reading window is not open.");
        return window.Dispatcher.Invoke(() => action(window));
    }

    public bool IsRightPaneVisible() =>
        Invoke(window => window.WebRight.Visibility == Visibility.Visible);

    public void Dispose()
    {
        var window = _window;
        if (window is not null)
        {
            try
            {
                window.Dispatcher.Invoke(() =>
                {
                    foreach (Window owned in window.OwnedWindows)
                        owned.Close();
                    window.Close();
                    window.Dispatcher.InvokeShutdown();
                }, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(10));
            }
            catch (Exception ex) when (ex is TimeoutException or TaskCanceledException or InvalidOperationException)
            {
                // The dispatcher is already down, or a modal dialog did not yield in time.
            }
        }

        _thread?.Join(TimeSpan.FromSeconds(10));
        try
        {
            if (Directory.Exists(_dataDir))
                Directory.Delete(_dataDir, recursive: true);
        }
        catch
        {
            // WebView2 can keep the profile folder locked briefly after shutdown.
        }
    }

    private void Launch(string epubPath)
    {
        _thread = new Thread(() =>
        {
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                app.DispatcherUnhandledException += (_, args) =>
                {
                    _dispatcherError = args.Exception;
                    args.Handled = true;
                };
                // WebView2 initialization needs the dispatcher already pumping.
                // Showing the window before Dispatcher.Run leaves EnsureCoreWebView2Async
                // waiting on a queue nothing is reading.
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        App.ApplyArgs(new[]
                        {
                            epubPath,
                            "--statefile=" + StateFile,
                            "--datadir=" + _dataDir
                        });
                        _window = new MainWindow
                        {
                            Left = 40,
                            Top = 30,
                            WindowStartupLocation = WindowStartupLocation.Manual
                        };
                        _window.Show();
                        Handle = new WindowInteropHelper(_window).EnsureHandle();
                        _window.Activate();
                        _ready.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        _ready.TrySetException(ex);
                    }
                }));
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                _ready.TrySetException(ex);
            }
        });
        _thread.IsBackground = true;
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        try
        {
            if (!_ready.Task.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("The reading window did not start.");
        }
        catch (AggregateException ex)
        {
            throw ex.GetBaseException();
        }
    }
}

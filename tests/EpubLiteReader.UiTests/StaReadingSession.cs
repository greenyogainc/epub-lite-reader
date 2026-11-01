using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace EpubLiteReader.UiTests;

/// <summary>
/// One real <see cref="MainWindow"/> on the shared <see cref="ReaderApp"/> thread, with its own data
/// folder, state file and copy of the book, so a late write from a closed window cannot reach the
/// next test and no two tests share a persisted reading position.
/// </summary>
internal sealed class StaReadingSession : IDisposable
{
    private readonly string _dataDir;
    private readonly MainWindow _window;

    private StaReadingSession(string dataDir, string stateFile, string? bookPath, MainWindow window)
    {
        _dataDir = dataDir;
        StateFile = stateFile;
        BookPath = bookPath;
        _window = window;
    }

    public string StateFile { get; }

    public string? BookPath { get; }

    public string DataDir => _dataDir;

    public IntPtr Handle { get; private set; }

    public Exception? DispatcherError => ReaderApp.Unhandled;

    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "EpubLiteReader.slnx")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate EpubLiteReader.slnx by walking up from '{AppContext.BaseDirectory}'.");
        }
    }

    public static string SampleEpub => Path.Combine(RepoRoot, "tools", "fixtures", "sample.epub");

    /// <summary>Opens a window on a private copy of <paramref name="epub"/>, or with no book when null.</summary>
    public static StaReadingSession Open(string? epub = null, Action<MainWindow>? beforeShow = null, params string[] extraArgs)
    {
        var dispatcher = ReaderApp.Dispatcher;
        ReaderApp.TakeUnhandled();

        var dataDir = Path.Combine(Path.GetTempPath(), "elr-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        var stateFile = Path.Combine(dataDir, "state.json");
        string? bookPath = null;
        if (epub is not null)
        {
            bookPath = Path.Combine(dataDir, Path.GetFileName(epub));
            File.Copy(epub, bookPath);
        }

        var args = new List<string> { "--statefile=" + stateFile, "--datadir=" + dataDir };
        if (bookPath is not null)
            args.Insert(0, bookPath);
        args.AddRange(extraArgs);

        return dispatcher.Invoke(() =>
        {
            // On the app thread, so a late timer tick cannot see half-applied statics.
            App.ApplyArgs(args.ToArray());
            var window = new MainWindow
            {
                Left = 40,
                Top = 30,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            beforeShow?.Invoke(window);
            var session = new StaReadingSession(dataDir, stateFile, bookPath, window);
            window.Show();
            session.Handle = new WindowInteropHelper(window).EnsureHandle();
            window.Activate();
            return session;
        });
    }

    /// <summary>Runs a synchronous step on the app thread.</summary>
    public T Invoke<T>(Func<MainWindow, T> action)
    {
        var result = ReaderApp.Dispatcher.Invoke(() => action(_window));
        ThrowIfDispatcherFailed();
        return result;
    }

    public void Invoke(Action<MainWindow> action) => Invoke(window =>
    {
        action(window);
        return 0;
    });

    /// <summary>Runs an async step on the app thread and blocks the test until it completes.</summary>
    public void Run(Func<MainWindow, Task> action, int timeoutSec = 60)
    {
        var task = ReaderApp.Dispatcher.InvokeAsync(() => action(_window)).Task.Unwrap();
        Wait(task, timeoutSec);
    }

    public T Run<T>(Func<MainWindow, Task<T>> action, int timeoutSec = 60)
    {
        var task = ReaderApp.Dispatcher.InvokeAsync(() => action(_window)).Task.Unwrap();
        Wait(task, timeoutSec);
        return task.Result;
    }

    /// <summary>Waits for the window's startup: both WebViews ready and any startup book displayed.</summary>
    public void WaitUntilReady(int timeoutSec = 60)
    {
        Run(async window =>
        {
            await PollAsync(() => window.HostsReady && (BookPath is null || window.CurrentDoc is not null),
                "the reader to start", timeoutSec);
            await window.WhenNavIdleAsync();
        }, timeoutSec + 5);
    }

    /// <summary>Polls on the app thread, yielding so dispatcher work keeps running.</summary>
    public static async Task PollAsync(Func<bool> condition, string what, int timeoutSec = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Timed out waiting for " + what + ".");
            await Task.Delay(25);
        }
    }

    /// <summary>Evaluates script in a pane; the result is the WebView2 JSON encoding of its value.</summary>
    public static Task<string> EvalAsync(WebView2 view, string script) => view.ExecuteScriptAsync(script);

    /// <summary>Runs script that publishes a value to <c>window[name]</c> later, and waits for it.</summary>
    public static async Task<string> EvalLaterAsync(WebView2 view, string name, string script, int timeoutSec = 15)
    {
        await view.ExecuteScriptAsync($"window['{name}'] = null;");
        await view.ExecuteScriptAsync(script);
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (true)
        {
            var json = await view.ExecuteScriptAsync($"window['{name}']");
            if (json != "null")
                return JsonSerializer.Deserialize<string>(json) ?? "";
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Script did not publish window['{name}'].");
            await Task.Delay(25);
        }
    }

    public bool IsRightPaneVisible() =>
        Invoke(window => window.WebRight.Visibility == Visibility.Visible);

    public void Dispose()
    {
        NativeDialogs.CloseAll();
        try
        {
            ReaderApp.Dispatcher.Invoke(() =>
            {
                foreach (Window owned in _window.OwnedWindows)
                    owned.Close();
                _window.Close();
            }, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(10));
            ReaderApp.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle,
                CancellationToken.None, TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is TimeoutException or TaskCanceledException or InvalidOperationException)
        {
            // A dialog did not yield in time; the check below reports the stray window.
        }

        NativeDialogs.CloseAll();
        try
        {
            if (Directory.Exists(_dataDir))
                Directory.Delete(_dataDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A book's response stream or the state file can outlive the window briefly.
        }
    }

    private void ThrowIfDispatcherFailed()
    {
        if (ReaderApp.TakeUnhandled() is Exception ex)
            ExceptionDispatchInfo.Capture(ex).Throw();
    }

    private void Wait(Task task, int timeoutSec)
    {
        try
        {
            if (!task.Wait(TimeSpan.FromSeconds(timeoutSec)))
                throw new TimeoutException($"A step on the reader thread did not finish within {timeoutSec}s.");
        }
        catch (AggregateException ex)
        {
            ExceptionDispatchInfo.Capture(ex.GetBaseException()).Throw();
        }

        ThrowIfDispatcherFailed();
    }
}

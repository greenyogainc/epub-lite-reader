using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;

namespace EpubLiteReader.UiTests;

/// <summary>Drives the hosted window the way the store-screenshot script does: UI Automation, SendKeys, and the state file.</summary>
internal sealed class UiDriver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly StaReadingSession _session;
    private readonly int _pid = Environment.ProcessId;

    public UiDriver(StaReadingSession session) => _session = session;

    public void WaitUntil(Func<ReaderState, bool> predicate, string what, int timeoutSec = 30)
    {
        var sw = Stopwatch.StartNew();
        ReaderState? last = null;
        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            if (_session.DispatcherError is Exception ex)
                throw new InvalidOperationException("The window dispatcher failed while waiting for " + what + ".", ex);

            last = TryReadState(_session.StateFile);
            if (last is not null && predicate(last))
                return;
            Thread.Sleep(100);
        }

        var dump = last is null ? "<no state file>" : JsonSerializer.Serialize(last, JsonOptions);
        throw new TimeoutException(
            $"Timed out waiting for {what}. Last state: {dump}. " +
            $"State file '{_session.StateFile}' exists={File.Exists(_session.StateFile)}. " +
            $"AutomationStateFile='{App.AutomationStateFile}'.");
    }

    public void WaitAbout(bool open, string what, int timeoutSec = 15)
    {
        var path = _session.StateFile + ".about";
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            if (_session.DispatcherError is Exception ex)
                throw new InvalidOperationException("The window dispatcher failed while waiting for " + what + ".", ex);

            if (TryReadAbout(path) is bool current && current == open)
                return;
            Thread.Sleep(100);
        }

        throw new TimeoutException("Timed out waiting for " + what + ".");
    }

    public void SendKeys(string keys)
    {
        RequireForeground(_session.Handle);
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                // The foreground unlock holds Alt. A leftover Alt turns {F4} into Alt+F4 and closes the window.
                keybd_event(0x12, 0, 2, 0);
                System.Windows.Forms.SendKeys.SendWait(keys);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("SendKeys timed out: " + keys);
        if (error is not null)
            throw new InvalidOperationException("SendKeys failed for " + keys + ".", error);
    }

    public void FocusDocument()
    {
        var main = MainWindow();
        var document = main.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
        if (document is not null)
        {
            try
            {
                document.SetFocus();
                return;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
            {
                // The reading pane is not focusable yet; the chrome still receives shortcuts.
            }
        }

        FocusChrome();
    }

    public void FocusChrome()
    {
        Find(MainWindow(), "Theme", 10).SetFocus();
    }

    public void Invoke(string name, int timeoutSec = 10)
    {
        var element = FindInProcess(name, timeoutSec);
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
            return;
        }

        if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
        {
            ((TogglePattern)toggle).Toggle();
            return;
        }

        throw new InvalidOperationException($"'{name}' is not invokable.");
    }

    public void InvokeAboutAndClose()
    {
        Invoke("Settings");
        var opening = Task.Run(() => Invoke("About EPUB Lite Reader", 15));
        WaitAbout(open: true, "about window");
        CloseAbout();
        WaitAbout(open: false, "about window closed");
        if (!opening.Wait(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("Opening About did not return.");
        if (opening.IsFaulted)
            throw new InvalidOperationException("Opening About failed.", opening.Exception!.InnerException);
    }

    public bool IsSearchBoxOffscreen()
    {
        var box = FindOrNull(MainWindow(), "Find in book…");
        return box is null || box.Current.IsOffscreen;
    }

    private void CloseAbout()
    {
        var about = FindTopLevel("About EPUB Lite Reader", ControlType.Window, 10);
        var close = about.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, "Close"),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)));
        if (close is null || !close.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            throw new InvalidOperationException("The About window has no Close button.");
        ((InvokePattern)pattern).Invoke();
    }

    private AutomationElement MainWindow() =>
        AutomationElement.FromHandle(_session.Handle);

    private AutomationElement FindInProcess(string name, int timeoutSec)
    {
        var sw = Stopwatch.StartNew();
        var pidCond = new PropertyCondition(AutomationElement.ProcessIdProperty, _pid);
        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            var tops = AutomationElement.RootElement.FindAll(TreeScope.Children, pidCond);
            foreach (AutomationElement top in tops)
            {
                var found = FindOrNull(top, name);
                if (found is not null)
                    return found;
            }

            Thread.Sleep(100);
        }

        throw new InvalidOperationException($"Could not find '{name}'.");
    }

    private static AutomationElement? FindOrNull(AutomationElement root, string name)
    {
        return root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, name));
    }

    private static AutomationElement Find(AutomationElement root, string name, int timeoutSec)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            var found = FindOrNull(root, name);
            if (found is not null)
                return found;
            Thread.Sleep(100);
        }

        throw new InvalidOperationException($"Could not find '{name}'.");
    }

    private AutomationElement FindTopLevel(string name, ControlType controlType, int timeoutSec)
    {
        var sw = Stopwatch.StartNew();
        var pidCond = new PropertyCondition(AutomationElement.ProcessIdProperty, _pid);
        var match = new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, name),
            new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));
        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            var tops = AutomationElement.RootElement.FindAll(TreeScope.Children, pidCond);
            foreach (AutomationElement top in tops)
            {
                if (top.Current.Name == name && top.Current.ControlType == controlType)
                    return top;
                var nested = top.FindFirst(TreeScope.Descendants, match);
                if (nested is not null)
                    return nested;
            }

            Thread.Sleep(100);
        }

        throw new InvalidOperationException($"Could not find a {controlType.ProgrammaticName} named '{name}'.");
    }

    private static ReaderState? TryReadState(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ReaderState>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool? TryReadAbout(string path)
    {
        try
        {
            var state = JsonSerializer.Deserialize<AboutState>(File.ReadAllText(path), JsonOptions);
            return state?.AboutOpen;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void RequireForeground(IntPtr hwnd)
    {
        if (GetForegroundWindow() == hwnd)
            return;

        SetForegroundWindow(hwnd);
        Thread.Sleep(250);
        if (GetForegroundWindow() == hwnd)
            return;

        keybd_event(0x12, 0, 0, 0);
        SetForegroundWindow(hwnd);
        keybd_event(0x12, 0, 2, 0);
        Thread.Sleep(250);
        if (GetForegroundWindow() == hwnd)
            return;

        if (GetWindowRect(hwnd, out var rect))
        {
            SetCursorPos((rect.Left + rect.Right) / 2, rect.Top + 15);
            Thread.Sleep(150);
            mouse_event(0x0002, 0, 0, 0, 0);
            mouse_event(0x0004, 0, 0, 0, 0);
            Thread.Sleep(300);
        }

        if (GetForegroundWindow() != hwnd)
            throw new InvalidOperationException("Could not bring the reading window to the foreground.");
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal sealed class ReaderState
{
    public string? Book { get; init; }
    public string? Mode { get; init; }
    public int Spine { get; init; }
    public int SpineCount { get; init; }
    public string? Theme { get; init; }
    public double FontScale { get; init; }
    public bool NavIdle { get; init; }
    public string? SearchStatus { get; init; }
    public bool Fullscreen { get; init; }
    public bool ChapterPane { get; init; }
    public int BookmarkCount { get; init; }
}

internal sealed class AboutState
{
    public bool AboutOpen { get; init; }
}

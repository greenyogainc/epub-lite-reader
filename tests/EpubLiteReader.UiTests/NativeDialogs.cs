using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace EpubLiteReader.UiTests;

/// <summary>Finds and closes this process's Win32 dialogs from a test thread, without needing focus.</summary>
internal static class NativeDialogs
{
    private const string DialogClass = "#32770";
    private const int MessageBoxTextId = 0xFFFF;
    private const uint WmClose = 0x0010;

    public static IReadOnlyList<IntPtr> TopLevelWindows(bool dialogsOnly)
    {
        var found = new List<IntPtr>();
        var pid = (uint)Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner == pid && IsWindowVisible(hwnd) && (!dialogsOnly || ClassOf(hwnd) == DialogClass))
                found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr WaitForDialog(int timeoutSec = 15)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            var dialogs = TopLevelWindows(dialogsOnly: true);
            if (dialogs.Count > 0)
                return dialogs[0];
            Thread.Sleep(50);
        }

        throw new TimeoutException("No dialog appeared.");
    }

    public static string MessageText(IntPtr dialog)
    {
        var text = new StringBuilder(4096);
        GetDlgItemText(dialog, MessageBoxTextId, text, text.Capacity);
        return text.ToString();
    }

    public static string Caption(IntPtr window)
    {
        var text = new StringBuilder(512);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    public static void Close(IntPtr dialog) => PostMessage(dialog, WmClose, IntPtr.Zero, IntPtr.Zero);

    public static void CloseAll()
    {
        foreach (var dialog in TopLevelWindows(dialogsOnly: true))
            Close(dialog);
    }

    /// <summary>Waits for one dialog on a background thread, closes it, and yields its message.</summary>
    public static Task<string> CloseNextAsync(int timeoutSec = 15) => Task.Run(() =>
    {
        var dialog = WaitForDialog(timeoutSec);
        var message = MessageText(dialog);
        Close(dialog);
        return message;
    });

    private static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDlgItemText(IntPtr dialog, int id, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}

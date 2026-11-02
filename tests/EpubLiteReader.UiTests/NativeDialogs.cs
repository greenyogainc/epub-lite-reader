using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

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

    /// <summary>Waits for a dialog and returns it. A message box is only taken once it has its text, because
    /// its window shows before the text is set; other dialogs (file dialogs) are taken as soon as they show.</summary>
    public static IntPtr WaitForDialog(int timeoutSec = 15, bool messageBox = false)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            foreach (var dialog in TopLevelWindows(dialogsOnly: true))
            {
                if (!messageBox || MessageText(dialog).Length > 0)
                    return dialog;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException(messageBox ? "No message box appeared." : "No dialog appeared.");
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

    /// <summary>Waits for one message box on a background thread, closes it, and yields its message.</summary>
    public static Task<string> CloseNextAsync(int timeoutSec = 15) => Task.Run(() =>
    {
        var dialog = WaitForDialog(timeoutSec, messageBox: true);
        var message = MessageText(dialog);
        Close(dialog);
        return message;
    });

    /// <summary>Waits for a file dialog on a background thread and closes it, which cancels it.</summary>
    public static Task CloseNextFileDialogAsync(int timeoutSec = 15) => Task.Run(() => Close(WaitForDialog(timeoutSec)));

    /// <summary>
    /// Waits for a file dialog, types <paramref name="path"/> into its file-name box through UI Automation
    /// (control id 1148) and clicks its Open button (control id 1, a plain button window with no automation
    /// pattern), so the choice does not depend on focus. Yields the dialog's caption.
    /// </summary>
    public static Task<string> AcceptFileDialogAsync(string path, int timeoutSec = 30) => Task.Run(() =>
    {
        var dialog = WaitForDialog(timeoutSec);
        var root = AutomationElement.FromHandle(dialog);
        var name = Find(root, "1148", ControlType.Edit, timeoutSec);
        ((ValuePattern)name.GetCurrentPattern(ValuePattern.Pattern)).SetValue(path);
        var caption = Caption(dialog);
        PostMessage(GetDlgItem(dialog, IdOk), BmClick, IntPtr.Zero, IntPtr.Zero);
        return caption;
    });

    private const int IdOk = 1;
    private const uint BmClick = 0x00F5;

    private static AutomationElement Find(AutomationElement root, string automationId, ControlType type, int timeoutSec)
    {
        var sw = Stopwatch.StartNew();
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.AutomationIdProperty, automationId),
            new PropertyCondition(AutomationElement.ControlTypeProperty, type));
        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            var found = root.FindFirst(TreeScope.Descendants, condition);
            if (found is not null)
                return found;
            Thread.Sleep(100);
        }

        throw new TimeoutException("The dialog has no control with id " + automationId + ".");
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDlgItem(IntPtr dialog, int id);

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

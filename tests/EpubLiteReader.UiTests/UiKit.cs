using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using Xunit;

namespace EpubLiteReader.UiTests;

/// <summary>Small helpers shared by the window tests: finding controls, raising routed input without
/// needing focus, and reading page state.</summary>
internal static class UiKit
{
    /// <summary>The error log of anything that runs outside a <see cref="StaReadingSession"/>, so no test
    /// ever writes the real log in the temp folder.</summary>
    internal static string DefaultLog { get; private set; } = "";

    [ModuleInitializer]
    internal static void RedirectLog()
    {
        DefaultLog = Path.Combine(Path.GetTempPath(), "elr-ui-tests-" + Environment.ProcessId + ".log");
        App.LogPathOverride = DefaultLog;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { File.Delete(DefaultLog); } catch (IOException) { }
        };
    }

    // ---------- Controls ----------

    public static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var nested in VisualDescendants<T>(child))
                yield return nested;
        }
    }

    /// <summary>The button whose content is exactly <paramref name="content"/>, searched below <paramref name="scope"/>.</summary>
    public static ButtonBase Button(DependencyObject scope, string content)
    {
        var found = VisualDescendants<ButtonBase>(scope).FirstOrDefault(b => b.Content as string == content);
        return found ?? throw new InvalidOperationException($"No button with content '{content}'.");
    }

    public static void Click(ButtonBase button)
    {
        Assert.True(button.IsEnabled);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    public const string Next = "▶";
    public const string Prev = "◀";
    public const string FontOut = "A−";
    public const string FontIn = "A+";
    public const string Settings = "⚙";
    public const string Fullscreen = "⛶";
    public const string FindPrev = "▲";
    public const string FindNext = "▼";
    public const string Close = "✕";

    // ---------- Focus ----------

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr child);

    /// <summary>Whether Win32 keyboard focus is inside the pane's browser window. WPF reports no focused
    /// element while a hosted native window holds focus, so <see cref="UIElement.IsKeyboardFocusWithin"/>
    /// cannot answer for a WebView2.</summary>
    public static bool HasNativeFocus(WebView2 view)
    {
        var focus = GetFocus();
        return focus != IntPtr.Zero && (focus == view.Handle || IsChild(view.Handle, focus));
    }

    // ---------- Keys ----------

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] keys);

    [DllImport("user32.dll")]
    private static extern bool SetKeyboardState(byte[] keys);

    /// <summary>Runs <paramref name="action"/> with the app thread's key state showing these modifiers, so
    /// <see cref="Keyboard.Modifiers"/> reads them without anyone pressing a real key.</summary>
    public static T WithModifiers<T>(ModifierKeys modifiers, Func<T> action)
    {
        var saved = new byte[256];
        GetKeyboardState(saved);
        var state = (byte[])saved.Clone();
        // Start from "no modifier down" whatever the thread's table says, so a key left down by real input
        // earlier in the run cannot turn a plain key into a shortcut.
        foreach (var vk in new[] { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
            state[vk] = 0;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            state[0x11] |= 0x80;
            state[0xA2] |= 0x80;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            state[0x10] |= 0x80;
            state[0xA0] |= 0x80;
        }

        SetKeyboardState(state);
        try
        {
            return action();
        }
        finally
        {
            SetKeyboardState(saved);
        }
    }

    /// <summary>Raises a key event on <paramref name="target"/> (the window when null) and returns its
    /// arguments; <c>Handled</c> may still change after an async handler awaits.</summary>
    public static KeyEventArgs RaiseKey(MainWindow window, Key key, ModifierKeys modifiers = ModifierKeys.None,
        UIElement? target = null, bool preview = true)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
        {
            RoutedEvent = preview ? Keyboard.PreviewKeyDownEvent : Keyboard.KeyDownEvent
        };
        WithModifiers(modifiers, () =>
        {
            (target ?? window).RaiseEvent(args);
            return 0;
        });
        return args;
    }

    public static MouseButtonEventArgs DoubleClick(RoutedEvent routedEvent) =>
        new(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routedEvent };

    /// <summary>DragEventArgs has no public constructor, so this fills the internal one by parameter type.</summary>
    public static DragEventArgs DragArgs(RoutedEvent routedEvent, IDataObject data, UIElement target)
    {
        var ctor = typeof(DragEventArgs)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c => c.GetParameters().Any(p => p.ParameterType == typeof(IDataObject)));
        var values = ctor.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(IDataObject)) return (object)data;
            if (p.ParameterType == typeof(DragDropKeyStates)) return DragDropKeyStates.None;
            if (p.ParameterType == typeof(DragDropEffects)) return DragDropEffects.Copy | DragDropEffects.Move;
            if (p.ParameterType == typeof(DependencyObject)) return target;
            if (p.ParameterType == typeof(Point)) return new Point(20, 20);
            throw new NotSupportedException("Unexpected DragEventArgs parameter " + p.ParameterType);
        }).ToArray();
        var args = (DragEventArgs)ctor.Invoke(values);
        args.RoutedEvent = routedEvent;
        return args;
    }

    // ---------- Page state ----------

    public static async Task<string> EvalStringAsync(WebView2 view, string script)
    {
        var json = await view.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<string>(json) ?? "";
    }

    public static async Task<double> EvalNumberAsync(WebView2 view, string script) =>
        double.Parse(await view.ExecuteScriptAsync(script), System.Globalization.CultureInfo.InvariantCulture);

    public static Task<string> SelectionAsync(WebView2 view) =>
        EvalStringAsync(view, "window.getSelection().toString()");

    /// <summary>Waits until the page's selection is <paramref name="text"/>.</summary>
    public static async Task WaitForSelectionAsync(WebView2 view, string text, int timeoutSec = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (await SelectionAsync(view) != text)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"The page never selected '{text}'.");
            await Task.Delay(25);
        }
    }

    /// <summary>Vertical scroll position of the page in pixels.</summary>
    public static Task<double> ScrollTopAsync(WebView2 view) =>
        EvalNumberAsync(view, "(document.scrollingElement || document.documentElement).scrollTop");

    /// <summary>Counts every navigation the pane starts from now on.</summary>
    public static Func<int> CountNavigations(WebView2 view)
    {
        var count = 0;
        view.CoreWebView2.NavigationStarting += (_, _) => count++;
        return () => count;
    }

    // ---------- The app thread ----------

    /// <summary>Runs an async step on the app thread, with no window of its own, and waits for it.</summary>
    public static void RunOnApp(Func<Task> action, int timeoutSec = 60) =>
        StaReadingSession.Wait(ReaderApp.Dispatcher.InvokeAsync(action).Task.Unwrap(), timeoutSec);

    /// <summary>Brings the session's window to the foreground and gives <paramref name="pick"/>'s element
    /// keyboard focus, failing if focus did not actually move there.</summary>
    public static void FocusElement(StaReadingSession session, Func<MainWindow, UIElement> pick)
    {
        UiDriver.RequireForeground(session.Handle);
        session.Invoke(w =>
        {
            var element = pick(w);
            element.Focus();
            Assert.True(element.IsKeyboardFocusWithin, "The element did not take keyboard focus.");
        });
    }
}

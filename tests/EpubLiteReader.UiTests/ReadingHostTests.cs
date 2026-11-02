using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Xunit;

namespace EpubLiteReader.UiTests;

/// <summary>
/// <see cref="ReadingHost"/> on its own small window and WebView2, for cases that would damage a reader
/// window (a crashed or disposed browser) or that need a page the reader never shows. The page here is
/// the initial about:blank, which carries the injected script like any chapter but has no policy of its own.
/// </summary>
public sealed class ReadingHostTests
{
    private sealed class Bench : IDisposable
    {
        private Bench(Window window, WebView2 view, ReadingHost host)
        {
            Window = window;
            View = view;
            Host = host;
        }

        public Window Window { get; }

        public WebView2 View { get; }

        public ReadingHost Host { get; }

        public string LogPath { get; } = Path.Combine(Path.GetTempPath(), "elr-host-" + Guid.NewGuid().ToString("N") + ".log");

        public static Bench Create(bool ready = true)
        {
            var bench = ReaderApp.Dispatcher.Invoke(() =>
            {
                var view = new WebView2();
                var window = new Window
                {
                    Width = 420,
                    Height = 320,
                    Left = 600,
                    Top = 60,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Content = view
                };
                window.Show();
                return new Bench(window, view, new ReadingHost(view, "test"));
            });
            ReaderApp.Dispatcher.Invoke(() => App.LogPathOverride = bench.LogPath);
            if (ready)
                UiKit.RunOnApp(() => bench.Host.EnsureReadyAsync());
            return bench;
        }

        public string Log => File.Exists(LogPath) ? File.ReadAllText(LogPath) : "";

        public async Task WaitForLogAsync(string text, int timeoutSec = 20)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
            while (!Log.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"The log never mentioned '{text}'. Log: {Log}");
                await Task.Delay(50);
            }
        }

        public void Dispose()
        {
            ReaderApp.Dispatcher.Invoke(() =>
            {
                Window.Close();
                App.LogPathOverride = UiKit.DefaultLog;
            });
            try { File.Delete(LogPath); } catch (IOException) { }
        }
    }

    private static Task Define(Bench bench, string script) => bench.View.ExecuteScriptAsync(script);

    [UiFact]
    public void BeforeItIsReady_EveryGuardReturnsItsDefaultAndNothingThrows()
    {
        using var bench = Bench.Create(ready: false);

        UiKit.RunOnApp(async () =>
        {
            var host = bench.Host;
            await host.ApplySettingsAsync();
            host.SetDisplaySettings(new DisplaySettings());
            await host.ContinuousGoToAsync(2, 0.5);
            await host.NavigateBlankAsync();
            await host.SetScrollFractionAsync(0.5);
            await host.PrintAsync();
            Assert.Equal("end", await host.PageTurnAsync(1));
            Assert.Equal(0, await host.GetScrollFractionAsync());
            Assert.Equal((0, 0d), await host.GetSpinePosAsync());
            Assert.False(await host.FindAsync("word"));
            Assert.False(await host.FindInSpineAsync(0, "word"));
        });
        Assert.Equal("", bench.Log);
    }

    [UiFact]
    public void EnsureReady_RunsOnce_AndAnEmptyQueryFindsNothing()
    {
        using var bench = Bench.Create();

        UiKit.RunOnApp(async () =>
        {
            var core = bench.View.CoreWebView2;
            await bench.Host.EnsureReadyAsync();
            Assert.Same(core, bench.View.CoreWebView2);
            Assert.False(await bench.Host.FindAsync(""));
            Assert.False(await bench.Host.FindInSpineAsync(0, ""));
            Assert.False(await bench.Host.FindAsync("anything", forward: false));
        });
    }

    [UiFact]
    public void GetSpinePos_ReadsWhatThePageReports_AndClampsIt()
    {
        using var bench = Bench.Create();

        UiKit.RunOnApp(async () =>
        {
            var host = bench.Host;
            // The injected script does not define it; only the continuous document does.
            Assert.Equal((0, 0d), await host.GetSpinePosAsync());

            await Define(bench, "window.__elrSpinePos = () => ({ spine: 3, fraction: 0.4 })");
            Assert.Equal((3, 0.4), await host.GetSpinePosAsync());

            await Define(bench, "window.__elrSpinePos = () => ({ spine: 5000000, fraction: 7 })");
            Assert.Equal((999_999, 1d), await host.GetSpinePosAsync());

            await Define(bench, "window.__elrSpinePos = () => ({ spine: -4, fraction: -2 })");
            Assert.Equal((0, 0d), await host.GetSpinePosAsync());

            await Define(bench, "window.__elrSpinePos = () => ({ spine: 2, fraction: 'high' })");
            Assert.Equal((2, 0d), await host.GetSpinePosAsync());

            // JSON cannot carry a non-finite number: it arrives as null.
            await Define(bench, "window.__elrSpinePos = () => ({ spine: 2, fraction: NaN })");
            Assert.Equal((2, 0d), await host.GetSpinePosAsync());
            await Define(bench, "window.__elrSpinePos = () => ({ spine: 2, fraction: 1e999 })");
            Assert.Equal((2, 0d), await host.GetSpinePosAsync());

            await Define(bench, "window.__elrSpinePos = () => ({ spine: 'two' })");
            Assert.Equal((0, 0d), await host.GetSpinePosAsync());

            // Not an object at all: the answer is unusable, so it is the origin.
            await Define(bench, "window.__elrSpinePos = () => 42");
            Assert.Equal((0, 0d), await host.GetSpinePosAsync());
        });
    }

    [UiFact]
    public void ScrollFractionAndPageTurn_ParseWhatThePageReturns()
    {
        using var bench = Bench.Create();

        UiKit.RunOnApp(async () =>
        {
            var host = bench.Host;
            await Define(bench, "window.__elrScrollFraction = () => 0.25");
            Assert.Equal(0.25, await host.GetScrollFractionAsync());
            await Define(bench, "window.__elrScrollFraction = () => 'abc'");
            Assert.Equal(0, await host.GetScrollFractionAsync());
            await Define(bench, "window.__elrScrollFraction = () => '0.75'");
            Assert.Equal(0.75, await host.GetScrollFractionAsync());

            await Define(bench, "window.__elrPage = () => 42");
            Assert.Equal("42", await host.PageTurnAsync(1));
            await Define(bench, "window.__elrPage = () => 'scrolled'");
            Assert.Equal("scrolled", await host.PageTurnAsync(1));
            await Define(bench, "window.__elrPage = (dir) => 'dir' + dir");
            Assert.Equal("dir-1", await host.PageTurnAsync(-1));
            await Define(bench, "window.__elrPage = () => ''");
            Assert.Equal("", await host.PageTurnAsync(1));
            // A page script that throws, or answers nothing, reaches the host as the JSON null.
            await Define(bench, "window.__elrPage = () => { throw new Error('page failed'); }");
            Assert.Equal("null", await host.PageTurnAsync(1));
            await Define(bench, "window.__elrPage = () => undefined");
            Assert.Equal("null", await host.PageTurnAsync(1));

            await host.SetScrollFractionAsync(0.5);
        });
    }

    [UiFact]
    public void FindInSpine_WaitsForTheChapterFrame_BeforeSearchingIt()
    {
        using var bench = Bench.Create();

        UiKit.RunOnApp(async () =>
        {
            await Define(bench, """
                window.__loadChecks = 0;
                window.__ensured = [];
                window.__elrEnsureSpineLoaded = (n) => { window.__ensured.push(n); };
                window.__elrIsSpineLoaded = (n) => ++window.__loadChecks > 1;
                window.__elrFindInSpine = (n, q, forward) => window.__loadChecks > 1 && q === 'word' && n === 3 && forward === false;
                """);

            Assert.True(await bench.Host.FindInSpineAsync(3, "word", forward: false));
            Assert.Equal(2d, await UiKit.EvalNumberAsync(bench.View, "window.__loadChecks"));
            Assert.Equal("[3]", await UiKit.EvalStringAsync(bench.View, "JSON.stringify(window.__ensured)"));
        });
    }

    [UiFact]
    public void ADisposedBrowser_IsLogged_AndReadsAsTheOrigin()
    {
        using var bench = Bench.Create();

        UiKit.RunOnApp(async () =>
        {
            bench.View.Dispose();

            await bench.Host.ApplySettingsAsync();
            Assert.Equal((0, 0d), await bench.Host.GetSpinePosAsync());

            // What a search or page turn still running as the window closes goes through.
            Assert.NotEqual("scrolled", await bench.Host.PageTurnAsync(1));
            Assert.Equal(0, await bench.Host.GetScrollFractionAsync());
            Assert.False(await bench.Host.FindAsync("anything"));
            await bench.Host.SetScrollFractionAsync(0.5);
            await bench.Host.ContinuousGoToAsync(0, 0);
        });
        Assert.Contains(nameof(InvalidOperationException), bench.Log);
    }

    [UiFact]
    public void ANavigationThatIsNotWaitedFor_IsLoggedAsATimeout_ThenTheScrollIsStillRestored()
    {
        using var bench = Bench.Create();
        var path = TestBook.Rich("Timeout " + Guid.NewGuid().ToString("N")).SaveTemp();

        try
        {
            UiKit.RunOnApp(async () =>
            {
                var (doc, _) = await EpubDoc.OpenWithChaptersAsync(path, "Untitled");
                using (doc)
                {
                    bench.Host.NavigationTimeout = TimeSpan.Zero;
                    await bench.Host.NavigateSpineAsync(doc, 1, null, 0.5);
                }
            });

            Assert.Contains("timed out (test)", bench.Log);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [UiFact]
    public void ACrashedBrowserProcess_IsLogged()
    {
        using var bench = Bench.Create();

        UiKit.RunOnApp(async () =>
        {
            // The call never completes for a page that has just crashed.
            _ = bench.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.crash", "{}")
                .ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            await bench.WaitForLogAsync("process failed (test)");
        });
    }

    [UiFact]
    public void NavigationToAnotherSite_AndAFrameOnAnotherSite_AreCancelled()
    {
        using var bench = Bench.Create();

        UiKit.RunOnApp(async () =>
        {
            var core = bench.View.CoreWebView2;
            bool? pageCancelled = null, frameCancelled = null;
            core.NavigationStarting += (_, e) =>
            {
                if (e.Uri.Contains("probe.invalid", StringComparison.Ordinal))
                    pageCancelled = e.Cancel;
            };
            core.FrameNavigationStarting += (_, e) =>
            {
                if (e.Uri.Contains("probe.invalid", StringComparison.Ordinal))
                    frameCancelled = e.Cancel;
            };

            core.Navigate("https://probe.invalid/");
            await StaReadingSession.PollAsync(() => pageCancelled is not null, "the page navigation");
            Assert.True(pageCancelled);

            // about:blank has no policy of its own, so the frame request reaches the host's handler.
            await bench.View.ExecuteScriptAsync(
                "(() => { const f = document.createElement('iframe'); f.src = 'https://probe.invalid/'; document.body.appendChild(f); })()");
            await StaReadingSession.PollAsync(() => frameCancelled is not null, "the frame navigation");
            Assert.True(frameCancelled);
        });
    }

    [UiFact]
    public void Print_ShowsTheBrowserPrintUi_WithoutPrinting()
    {
        using var bench = Bench.Create();

        UiKit.RunOnApp(async () =>
        {
            // The in-page print preview; nothing is sent to a printer until someone presses Print in it.
            await bench.Host.PrintAsync();
            await Task.Delay(500);
            Assert.Equal(2d, await UiKit.EvalNumberAsync(bench.View, "1 + 1"));
        });
        Assert.Equal("", bench.Log);
    }
}

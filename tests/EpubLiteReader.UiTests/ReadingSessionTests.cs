using System.IO;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace EpubLiteReader.UiTests;

public sealed class ReadingSessionTests
{
    [UiFact]
    public void SampleBook_CoversTheReadingWindow()
    {
        var root = RepoRoot();
        var epub = Path.Combine(root, "tools", "fixtures", "sample.epub");
        var dataDir = Path.Combine(Path.GetTempPath(), "elr-ui-" + Guid.NewGuid().ToString("N"));
        var stateFile = Path.Combine(Path.GetTempPath(), "elr-ui-state-" + Guid.NewGuid().ToString("N") + ".json");

        StaReadingSession? session = null;
        try
        {
            session = StaReadingSession.Start(epub, dataDir, stateFile);
            var ui = new UiDriver(session);

            ui.WaitUntil(s => !string.IsNullOrWhiteSpace(s.Book) && s.NavIdle && s.SpineCount == 2, "sample book", 60);

            ui.FocusDocument();
            ui.SendKeys("2");
            ui.WaitUntil(s => s.Mode == "Facing" && s.NavIdle, "facing mode");
            Assert.True(session.IsRightPaneVisible());

            ui.FocusDocument();
            ui.SendKeys("{RIGHT}");
            ui.WaitUntil(s => s.Mode == "Facing" && s.NavIdle && s.Spine >= 1, "facing next page");
            Assert.True(session.IsRightPaneVisible());

            ui.FocusDocument();
            ui.SendKeys("{F4}");
            ui.WaitUntil(s => s.ChapterPane, "chapter sidebar open");
            ui.SendKeys("{F4}");
            ui.WaitUntil(s => !s.ChapterPane, "chapter sidebar closed");

            ui.FocusDocument();
            ui.SendKeys("3");
            ui.WaitUntil(s => s.Mode == "Continuous" && s.NavIdle, "continuous mode");
            Assert.False(session.IsRightPaneVisible());

            ui.Invoke("Search");
            ui.SendKeys("rivers{ENTER}");
            ui.WaitUntil(s => !string.IsNullOrWhiteSpace(s.SearchStatus) && s.NavIdle, "search results");
            ui.SendKeys("{ESC}");
            WaitUntil(ui.IsSearchBoxOffscreen, "search closed");

            ui.Invoke("Increase text size (Ctrl++)");
            ui.WaitUntil(s => s.FontScale > 1.05, "larger text");
            ui.FocusChrome();
            ui.SendKeys("^0");
            ui.WaitUntil(s => Math.Abs(s.FontScale - 1) < 0.01, "text size restored");

            ui.Invoke("Theme");
            ui.WaitUntil(s => s.Theme != "Light", "theme changed");

            ui.FocusChrome();
            ui.SendKeys("b");
            ui.WaitUntil(s => s.BookmarkCount == 1, "bookmark added");

            ui.FocusDocument();
            ui.SendKeys("{F11}");
            ui.WaitUntil(s => s.Fullscreen, "fullscreen");
            ui.SendKeys("{ESC}");
            ui.WaitUntil(s => !s.Fullscreen, "left fullscreen");

            ui.InvokeAboutAndClose();
        }
        finally
        {
            session?.Dispose();
            TryDelete(stateFile);
            TryDelete(stateFile + ".about");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // The window may still be flushing the state file.
        }
    }

    private static void WaitUntil(Func<bool> predicate, string what, int timeoutSec = 10)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            if (predicate())
                return;
            Thread.Sleep(100);
        }

        throw new TimeoutException("Timed out waiting for " + what + ".");
    }

    private static string RepoRoot()
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

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Xunit;

namespace EpubLiteReader.UiTests;

/// <summary>Paging, jumping, modes, progress and persistence of the reading position.</summary>
public sealed class ReadingNavigationTests
{
    private static string Title() => "Nav " + Guid.NewGuid().ToString("N");

    private static string Percent(int value) => string.Format(Strings.Get("ProgressFormat"), value);

    [UiFact]
    public void Single_StepScrollsInsideAChapter_ThenAdvances_AndPreviousReturns()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            await w.GoToSpineAsync(1);
            Assert.Equal(1, w.CurrentSpine);

            await w.StepAsync(+1);
            Assert.Equal(1, w.CurrentSpine);
            Assert.True(w.CurrentFraction > 0, "A long chapter scrolls instead of advancing.");
            Assert.True(await UiKit.ScrollTopAsync(w.WebLeft) > 0);
            Assert.Equal(Percent((int)Math.Round((1 + w.CurrentFraction) / 5 * 100)), w.ProgressText.Text);

            for (var i = 0; i < 100 && w.CurrentSpine == 1; i++)
                await w.StepAsync(+1);
            Assert.Equal(2, w.CurrentSpine);
            Assert.EndsWith("/OEBPS/part1b.xhtml", w.WebLeft.CoreWebView2.Source);

            // part1b fits on one page, so Previous at its top returns to the chapter before.
            await w.StepAsync(-1);
            Assert.Equal(1, w.CurrentSpine);
            Assert.Equal("2", w.PageBox.Text);
        });
    }

    [UiFact]
    public void Single_EndOfBook_NextStartsNoNavigation_AndKeepsTheScrollPosition()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            await w.GoToSpineAsync(4);
            for (var i = 0; i < 100; i++)
            {
                var before = await UiKit.ScrollTopAsync(w.WebLeft);
                await w.StepAsync(+1);
                if (await UiKit.ScrollTopAsync(w.WebLeft) <= before + 1)
                    break;
            }

            Assert.Equal(4, w.CurrentSpine);
            var atEnd = await UiKit.ScrollTopAsync(w.WebLeft);
            Assert.True(atEnd > 0);
            var navigations = UiKit.CountNavigations(w.WebLeft);

            await w.StepAsync(+1);
            await w.StepAsync(+1);

            Assert.Equal(0, navigations());
            Assert.Equal(4, w.CurrentSpine);
            Assert.Equal(atEnd, await UiKit.ScrollTopAsync(w.WebLeft));
        });
    }

    [UiFact]
    public void Single_StartOfBook_PreviousStartsNoNavigation()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            var navigations = UiKit.CountNavigations(w.WebLeft);
            await w.StepAsync(-1);
            await w.StepAsync(-1);
            Assert.Equal(0, navigations());
            Assert.Equal(0, w.CurrentSpine);
        });
    }

    [UiFact]
    public void Facing_StepsByPairs_ShowsThePartnerOnTheRight_AndEndsAreNoOps()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            w.ModeFacing.IsChecked = true;
            await w.WhenNavIdleAsync();
            Assert.Equal(ViewMode.Facing, w.CurrentMode);
            Assert.Equal(Visibility.Visible, w.WebRight.Visibility);
            Assert.Equal(0, w.CurrentSpine);

            await w.StepAsync(+1);
            Assert.Equal(1, w.CurrentSpine);
            Assert.EndsWith("/OEBPS/part1.xhtml", w.WebLeft.CoreWebView2.Source);
            Assert.EndsWith("/OEBPS/part1b.xhtml", w.WebRight.CoreWebView2.Source);

            await w.StepAsync(+1);
            Assert.Equal(3, w.CurrentSpine);
            Assert.EndsWith("/OEBPS/part2.xhtml", w.WebLeft.CoreWebView2.Source);
            Assert.EndsWith("/OEBPS/part3.xhtml", w.WebRight.CoreWebView2.Source);

            var navigations = UiKit.CountNavigations(w.WebLeft);
            await w.StepAsync(+1);
            Assert.Equal(0, navigations());
            Assert.Equal(3, w.CurrentSpine);

            await w.StepAsync(-1);
            Assert.Equal(1, w.CurrentSpine);
            await w.StepAsync(-1);
            Assert.Equal(0, w.CurrentSpine);
            Assert.Equal("1", w.PageBox.Text);
            await StaReadingSession.PollAsync(() => w.WebRight.CoreWebView2.Source == "about:blank", "the right pane to empty");

            navigations = UiKit.CountNavigations(w.WebLeft);
            await w.StepAsync(-1);
            Assert.Equal(0, navigations());
            Assert.Equal(0, w.CurrentSpine);
            Assert.Equal("about:blank", w.WebRight.CoreWebView2.Source);

            // A spine inside a pair lands on the pair's first chapter, and the right pane empties at the start.
            await w.GoToSpineAsync(2);
            Assert.Equal(1, w.CurrentSpine);
            Assert.EndsWith("/OEBPS/part1b.xhtml", w.WebRight.CoreWebView2.Source);
            await w.GoToSpineAsync(0);
            await StaReadingSession.PollAsync(() => w.WebRight.CoreWebView2.Source == "about:blank", "the right pane to empty again");
        });
    }

    [UiFact]
    public void Facing_ATwoSpineBook_LeavesTheRightPaneBlank()
    {
        using var session = StaReadingSession.Open(StaReadingSession.SampleEpub);
        session.WaitUntilReady();

        session.Run(async w =>
        {
            w.ModeFacing.IsChecked = true;
            await w.WhenNavIdleAsync();
            await w.StepAsync(+1);
            Assert.Equal(1, w.CurrentSpine);
            Assert.Equal("about:blank", w.WebRight.CoreWebView2.Source);
            Assert.Equal("", await UiKit.EvalStringAsync(w.WebRight, "document.body ? document.body.innerText : ''"));
            Assert.EndsWith("chapter2.xhtml", w.WebLeft.CoreWebView2.Source);
        });
    }

    [UiFact]
    public void Continuous_StepScrolls_AndFallsBackToTheSpineWhenTheScrollStalls()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            w.ModeContinuous.IsChecked = true;
            await w.WhenNavIdleAsync();
            Assert.Equal(ViewMode.Continuous, w.CurrentMode);
            Assert.Equal(Visibility.Collapsed, w.WebRight.Visibility);

            await w.StepAsync(+1);
            Assert.True(await UiKit.ScrollTopAsync(w.WebLeft) > 100, "A step scrolls the continuous document.");
            Assert.Equal(0, w.CurrentSpine);

            // Pretend the scroll cannot move: the reader jumps the spine instead.
            await UiKit.EvalStringAsync(w.WebLeft, "window.__elrPage = () => 'end'; 'ok'");
            await w.StepAsync(+1);
            Assert.Equal(1, w.CurrentSpine);
            await w.StepAsync(-1);
            Assert.Equal(0, w.CurrentSpine);
        });
    }

    [UiFact]
    public void Continuous_AtTheEnds_FallbackMovesNowhere()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            w.ModeContinuous.IsChecked = true;
            await w.WhenNavIdleAsync();
            await UiKit.EvalStringAsync(w.WebLeft, """
                window.__elrPage = () => 'end';
                window.__jumps = 0;
                const original = window.__elrContinuousGoTo;
                window.__elrContinuousGoTo = (n, f) => { window.__jumps++; return original(n, f); };
                'ok'
                """);

            // At the first chapter Previous has nowhere to go.
            await w.StepAsync(-1);
            Assert.Equal(0, w.CurrentSpine);
            Assert.Equal(0d, await UiKit.EvalNumberAsync(w.WebLeft, "window.__jumps"));

            await w.GoToSpineAsync(4);
            await StaReadingSession.PollAsync(() => w.CurrentSpine == 4, "the reader to settle on the last chapter", 20);
            var jumps = await UiKit.EvalNumberAsync(w.WebLeft, "window.__jumps");
            await w.StepAsync(+1);
            Assert.Equal(4, w.CurrentSpine);
            Assert.Equal(jumps, await UiKit.EvalNumberAsync(w.WebLeft, "window.__jumps"));
        });
    }

    [UiFact]
    public void Continuous_ChapterJumps_DoNotWaitOutTheNavigationTimeout()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        var elapsed = session.Run(async w =>
        {
            w.ModeContinuous.IsChecked = true;
            await w.WhenNavIdleAsync();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await w.GoToSpineAsync(2);
            await w.GoToSpineAsync(3);
            Assert.Equal(3, w.CurrentSpine);
            return sw.Elapsed;
        });

        // A jump inside the one continuous document is a fragment navigation, which WebView2 never completes.
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"Two chapter jumps took {elapsed}.");
        Assert.DoesNotContain("timed out", session.ReadLog());
    }

    [UiFact]
    public void Single_AnAnchorInTheCurrentChapter_DoesNotWaitOutTheNavigationTimeout()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        var elapsed = session.Run(async w =>
        {
            await w.GoToSpineAsync(1);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await w.GoToSpineAsync(1, "again");
            Assert.True(await UiKit.ScrollTopAsync(w.WebLeft) > 500, "The page scrolled to the anchor.");
            return sw.Elapsed;
        });

        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"The anchor jump took {elapsed}.");
        Assert.DoesNotContain("timed out", session.ReadLog());
    }

    [UiFact]
    public void SwitchingBooksInContinuousMode_ShowsTheNewBooksChapters()
    {
        using var session = TestBook.Rich(Title()).Open(beforeOpen: dir =>
            File.WriteAllText(Path.Combine(dir, "settings.json"), """{ "defaults": { "viewMode": "continuous" } }"""));
        session.WaitUntilReady();
        var other = TestBook.EmptyToc(Title()).SaveTemp("other.epub");

        try
        {
            var frames = session.Run(async w =>
            {
                Assert.Equal(ViewMode.Continuous, w.CurrentMode);
                Assert.Equal(5d, await UiKit.EvalNumberAsync(w.WebLeft, "document.querySelectorAll('iframe.elr-spine').length"));
                await w.GoToSpineAsync(2);
                await w.OpenFileAsync(other);
                await w.WhenNavIdleAsync();
                return await UiKit.EvalNumberAsync(w.WebLeft, "document.querySelectorAll('iframe.elr-spine').length");
            });

            Assert.Equal(2d, frames);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(other)!, recursive: true);
        }
    }

    [UiFact]
    public void ModeRadios_ChangeTheLayout_AndKeepTheReadingPosition()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            await w.GoToSpineAsync(3);
            Assert.Equal(ViewMode.Single, w.CurrentMode);

            w.ModeFacing.IsChecked = true;
            await w.WhenNavIdleAsync();
            Assert.Equal(ViewMode.Facing, w.CurrentMode);
            Assert.Equal(ViewMode.Facing, w.CurrentDisplay.ViewMode);
            Assert.Equal(3, w.CurrentSpine);
            Assert.EndsWith("/OEBPS/part3.xhtml", w.WebRight.CoreWebView2.Source);

            w.ModeContinuous.IsChecked = true;
            await w.WhenNavIdleAsync();
            Assert.Equal(ViewMode.Continuous, w.CurrentMode);
            Assert.Equal(Visibility.Collapsed, w.WebRight.Visibility);
            Assert.Contains("/elr-continuous.html?book=", w.WebLeft.CoreWebView2.Source);
            Assert.EndsWith("#spine-3", w.WebLeft.CoreWebView2.Source);

            w.ModeSingle.IsChecked = true;
            await w.WhenNavIdleAsync();
            Assert.Equal(ViewMode.Single, w.CurrentMode);
            Assert.EndsWith("/OEBPS/part2.xhtml", w.WebLeft.CoreWebView2.Source);
            Assert.Equal("4", w.PageBox.Text);
        });
    }

    [UiFact]
    public void PageBox_EnterGoesToThePage_AndIgnoresBadInput()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            w.PageBox.Text = "4";
            var enter = UiKit.RaiseKey(w, Key.Enter, target: w.PageBox, preview: false);
            await StaReadingSession.PollAsync(() => enter.Handled, "Enter to be handled");
            await w.WhenNavIdleAsync();
            Assert.Equal(3, w.CurrentSpine);
            Assert.Equal("4", w.PageBox.Text);

            w.PageBox.Text = "not a number";
            var bad = UiKit.RaiseKey(w, Key.Enter, target: w.PageBox, preview: false);
            Assert.False(bad.Handled);
            Assert.Equal(3, w.CurrentSpine);

            w.PageBox.Text = "1";
            var other = UiKit.RaiseKey(w, Key.A, target: w.PageBox, preview: false);
            Assert.False(other.Handled);
            Assert.Equal(3, w.CurrentSpine);

            // A page past the end clamps to the last chapter.
            w.PageBox.Text = "99";
            var past = UiKit.RaiseKey(w, Key.Enter, target: w.PageBox, preview: false);
            await StaReadingSession.PollAsync(() => past.Handled, "Enter to be handled");
            await w.WhenNavIdleAsync();
            Assert.Equal(4, w.CurrentSpine);
            Assert.Equal("5", w.PageBox.Text);
        });
    }

    [UiFact]
    public void PreviousAndNextButtons_Step()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            await w.GoToSpineAsync(1);
            UiKit.Click(UiKit.Button(w, UiKit.Next));
            await StaReadingSession.PollAsync(() => w.CurrentFraction > 0, "Next to scroll the chapter");
            Assert.Equal(1, w.CurrentSpine);

            await w.GoToSpineAsync(2);
            UiKit.Click(UiKit.Button(w, UiKit.Prev));
            await StaReadingSession.PollAsync(() => w.CurrentSpine == 1, "Previous to return a chapter");
            await w.WhenNavIdleAsync();
            Assert.Equal("2", w.PageBox.Text);
        });
    }

    [UiFact]
    public void Progress_ShowsTheBookPosition_AndClearsWithoutABook()
    {
        using var session = StaReadingSession.Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            Assert.Equal(string.Format(Strings.Get("ChapterCountFormat"), 0), w.PageCountText.Text);
            w.ProgressText.Text = "stale";
            // A page without a book can still post a scroll position; there is nothing to measure it against.
            var arrived = new TaskCompletionSource();
            w.LeftHost!.MessageReceived += (_, m) => { if (m.Type == "scroll") arrived.TrySetResult(); };
            await w.WebLeft.ExecuteScriptAsync("window.chrome.webview.postMessage({ type: 'scroll', fraction: 0.4 })");
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("", w.ProgressText.Text);
        });
    }

    [UiFact]
    public void ProgressAndCounts_FollowTheOpenBook()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            Assert.Equal(string.Format(Strings.Get("ChapterCountFormat"), 5), w.PageCountText.Text);
            await w.GoToSpineAsync(2);
            Assert.Equal(Percent(40), w.ProgressText.Text);
            await w.GoToSpineAsync(4);
            Assert.Equal(Percent(80), w.ProgressText.Text);
        });
    }

    [UiFact]
    public void SavedWindowSize_IsClampedIntoTheScreenAndTheMinimum()
    {
        using var session = StaReadingSession.Open(beforeOpen: dir =>
            File.WriteAllText(Path.Combine(dir, "settings.json"), """{ "windowWidth": 100000, "windowHeight": 50 }"""));
        session.WaitUntilReady();

        var (width, height, minHeight, screenWidth) =
            session.Invoke(w => (w.Width, w.Height, w.MinHeight, SystemParameters.VirtualScreenWidth));
        Assert.Equal(screenWidth, width);
        Assert.Equal(minHeight, height);
    }

    [UiFact]
    public void SaveTimer_PersistsThePosition_AndDoesNotFireAfterTheWindowCloses()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();
        var bookId = session.Invoke(w => w.CurrentDoc!.BookId);
        var stateFile = Path.Combine(session.DataDir, "books", bookId + ".json");

        session.Run(w => w.GoToSpineAsync(2));
        // The tick is two seconds out; opening the book saved nothing yet.
        Assert.False(File.Exists(stateFile));

        WaitFor(() => File.Exists(stateFile), "the save timer to write the book state", 8);
        using (var json = JsonDocument.Parse(File.ReadAllText(stateFile)))
            Assert.Equal(2, json.RootElement.GetProperty("spineIndex").GetInt32());

        // Closing saves once and stops the timer; a change made just before must not tick afterwards.
        session.Run(w => w.GoToSpineAsync(3));
        session.Close();
        File.Delete(stateFile);
        Thread.Sleep(2800);
        Assert.False(File.Exists(stateFile), "The save timer fired after the window closed.");
    }

    [UiFact]
    public void ReopeningTheSameBook_RestoresTheSavedSpineAndScroll()
    {
        var book = TestBook.Rich(Title());
        double saved;
        string dataDir;
        using (var first = book.Open())
        {
            first.KeepData = true;
            dataDir = first.DataDir;
            first.WaitUntilReady();
            saved = first.Run(async w =>
            {
                await w.GoToSpineAsync(1);
                await w.StepAsync(+1);
                await w.StepAsync(+1);
                return w.CurrentFraction;
            });
            Assert.True(saved > 0);
            first.Close();
        }

        try
        {
            using var second = book.Open(reuseDataDir: dataDir);
            second.WaitUntilReady();
            Assert.Equal(1, second.Invoke(w => w.CurrentSpine));
            second.Run(async w =>
            {
                Assert.InRange(w.CurrentFraction, saved - 0.05, saved + 0.05);
                Assert.True(await UiKit.ScrollTopAsync(w.WebLeft) > 0, "The page is scrolled to the saved place.");
            });
        }
        finally
        {
            Directory.Delete(dataDir, recursive: true);
        }
    }

    [UiFact]
    public void OldContinuousState_ResumesAtTheTopOfItsChapter_ButOldSingleStateKeepsItsScroll()
    {
        var book = TestBook.Rich(Title());
        static void WriteState(string dir, string mode)
        {
            var (doc, _) = EpubDoc.OpenWithChaptersAsync(Path.Combine(dir, "book.epub"), "Untitled").GetAwaiter().GetResult();
            using (doc)
            {
                Directory.CreateDirectory(Path.Combine(dir, "books"));
                File.WriteAllText(Path.Combine(dir, "books", doc.BookId + ".json"),
                    $$"""{ "schemaVersion": 0, "bookId": "{{doc.BookId}}", "spineIndex": 2, "scrollFraction": 0.5, "display": { "viewMode": "{{mode}}" } }""");
            }
        }

        using (var continuous = book.Open(beforeOpen: dir => WriteState(dir, "continuous")))
        {
            continuous.WaitUntilReady();
            Assert.Equal(ViewMode.Continuous, continuous.Invoke(w => w.CurrentMode));
            Assert.Equal(2, continuous.Invoke(w => w.CurrentSpine));
            Assert.InRange(continuous.Invoke(w => w.CurrentFraction), 0, 0.05);
        }

        using var single = book.Open(beforeOpen: dir => WriteState(dir, "single"));
        single.WaitUntilReady();
        Assert.Equal(ViewMode.Single, single.Invoke(w => w.CurrentMode));
        Assert.Equal(2, single.Invoke(w => w.CurrentSpine));
    }

    private static void WaitFor(Func<bool> condition, string what, int timeoutSec)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed.TotalSeconds > timeoutSec)
                throw new TimeoutException("Timed out waiting for " + what + ".");
            Thread.Sleep(100);
        }
    }
}

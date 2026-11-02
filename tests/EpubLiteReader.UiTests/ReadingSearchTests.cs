using System.Windows;
using System.Windows.Input;
using Xunit;

namespace EpubLiteReader.UiTests;

/// <summary>Find in book: in-page find, jumping between chapters, facing panes, continuous mode and the search bar's controls.</summary>
public sealed class ReadingSearchTests
{
    private const string Shared = TestBook.SharedWord;

    private static string Title() => "Search " + Guid.NewGuid().ToString("N");

    private static string Matches(int count) => string.Format(Strings.Get("SearchResultsFormat"), count);

    private static async Task Search(MainWindow w, string query, bool forward = true)
    {
        w.SearchBox.Text = query;
        await w.RunSearchAsync(forward);
    }

    private static Task<string> FrameSelection(MainWindow w, int spine) => UiKit.EvalStringAsync(w.WebLeft,
        $"document.querySelectorAll('iframe.elr-spine')[{spine}].contentWindow.getSelection().toString()");

    [UiFact]
    public void Single_FindsInThePageFirst_ThenWalksTheOtherChaptersAndWraps()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            await w.GoToSpineAsync(1);

            await Search(w, Shared);
            Assert.Equal("", w.SearchStatus.Text);
            Assert.Equal(1, w.CurrentSpine);
            Assert.Equal(Shared, await UiKit.SelectionAsync(w.WebLeft));

            // The chapter has no further match, so Find Next moves on to the next chapter with one.
            await w.RunSearchAsync(true);
            Assert.Equal(Matches(3), w.SearchStatus.Text);
            Assert.Equal(3, w.CurrentSpine);
            Assert.EndsWith("/OEBPS/part2.xhtml", w.WebLeft.CoreWebView2.Source);
            Assert.Equal(Shared, await UiKit.SelectionAsync(w.WebLeft));

            await w.RunSearchAsync(true);
            Assert.Equal(4, w.CurrentSpine);
            await w.RunSearchAsync(true);
            Assert.Equal(1, w.CurrentSpine);

            // Backward from the first match wraps to the last.
            await w.RunSearchAsync(false);
            Assert.Equal(4, w.CurrentSpine);
            await w.RunSearchAsync(false);
            Assert.Equal(3, w.CurrentSpine);
        });
    }

    [UiFact]
    public void NoMatch_ShowsNoSearchResults_AndABlankQueryDoesNothing()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            await w.GoToSpineAsync(2);

            await Search(w, "nothinglikethisanywhere");
            Assert.Equal(Strings.Get("NoSearchResults"), w.SearchStatus.Text);
            Assert.Equal(2, w.CurrentSpine);

            w.SearchStatus.Text = "unchanged";
            await Search(w, "   ");
            Assert.Equal("unchanged", w.SearchStatus.Text);
            Assert.Equal(2, w.CurrentSpine);
        });
    }

    [UiFact]
    public void OverlappingSearches_AreSerialized()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            await w.GoToSpineAsync(1);
            await Search(w, Shared);

            var first = w.RunSearchAsync(true);
            var second = w.RunSearchAsync(true);
            await Task.WhenAll(first, second);

            // Run one after the other, the two would have moved on twice, to spine 4.
            Assert.Equal(3, w.CurrentSpine);
        });
    }

    [UiFact]
    public void RepeatedQuery_ReusesTheHits_AndANewQueryStartsOver()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            await w.GoToSpineAsync(1);
            await Search(w, Shared);
            await w.RunSearchAsync(true);
            Assert.Equal(3, w.CurrentSpine);

            // Take the word out of the last chapter's search index. A fresh search would no longer
            // find it there; the cached hit list still names that chapter.
            ((List<string>)w.CurrentDoc!.SpinePlainText)[4] = "";
            await w.RunSearchAsync(true);
            Assert.Equal(4, w.CurrentSpine);

            // A different query is searched for afresh: the word is not in the cached list.
            await Search(w, TestBook.RightPaneWord);
            Assert.Equal(2, w.CurrentSpine);
            Assert.Equal(Matches(1), w.SearchStatus.Text);

            // Returning to the earlier query indexes again, and now finds only two chapters.
            await Search(w, Shared);
            Assert.Equal(Matches(2), w.SearchStatus.Text);
        });
    }

    [UiFact]
    public void Facing_FindsInEitherPane_AndLandsAHitInThePairsRightPane()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            w.ModeFacing.IsChecked = true;
            await w.WhenNavIdleAsync();
            await w.GoToSpineAsync(1);

            // Found on the left page.
            await Search(w, Shared);
            Assert.Equal("", w.SearchStatus.Text);
            Assert.Equal(Shared, await UiKit.SelectionAsync(w.WebLeft));

            // Found on the right page of the pair.
            await Search(w, TestBook.RightPaneWord);
            Assert.Equal("", w.SearchStatus.Text);
            Assert.Equal(TestBook.RightPaneWord, await UiKit.SelectionAsync(w.WebRight));
            Assert.Equal(1, w.CurrentSpine);

            // From the next pair the only match is in the right page of the earlier one.
            await w.GoToSpineAsync(3);
            await Search(w, TestBook.RightPaneWord);
            Assert.Equal(1, w.CurrentSpine);
            Assert.Equal(Matches(1), w.SearchStatus.Text);
            Assert.EndsWith("/OEBPS/part1b.xhtml", w.WebRight.CoreWebView2.Source);
            Assert.Equal(TestBook.RightPaneWord, await UiKit.SelectionAsync(w.WebRight));
            Assert.Equal("", await UiKit.SelectionAsync(w.WebLeft));
        });
    }

    [UiFact]
    public void Continuous_WalksTheHitsForwardAndBackward_WithWrapAround()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            w.ModeContinuous.IsChecked = true;
            await w.WhenNavIdleAsync();

            await Search(w, Shared);
            Assert.Equal(Matches(3), w.SearchStatus.Text);
            Assert.Equal(1, w.CurrentSpine);
            Assert.Equal("2", w.PageBox.Text);
            Assert.Equal(Shared, await FrameSelection(w, 1));

            await w.RunSearchAsync(true);
            Assert.Equal(3, w.CurrentSpine);
            Assert.Equal(Shared, await FrameSelection(w, 3));
            await w.RunSearchAsync(true);
            Assert.Equal(4, w.CurrentSpine);
            await w.RunSearchAsync(true);
            Assert.Equal(1, w.CurrentSpine);

            await w.RunSearchAsync(false);
            Assert.Equal(4, w.CurrentSpine);
            await w.RunSearchAsync(false);
            Assert.Equal(3, w.CurrentSpine);
        });
    }

    [UiFact]
    public void Continuous_TextThePageCannotFind_StillJumpsToItsChapter()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            w.ModeContinuous.IsChecked = true;
            await w.WhenNavIdleAsync();
            await UiKit.EvalStringAsync(w.WebLeft, """
                window.__jumps = [];
                const original = window.__elrContinuousGoTo;
                window.__elrContinuousGoTo = (n, f) => { window.__jumps.push(n); return original(n, f); };
                'ok'
                """);

            // The word is in the search index but display:none, which find-in-page skips.
            await Search(w, TestBook.HiddenWord);
            Assert.Equal(Matches(1), w.SearchStatus.Text);
            Assert.Equal(4, w.CurrentSpine);
            Assert.Equal("[4]", await UiKit.EvalStringAsync(w.WebLeft, "JSON.stringify(window.__jumps)"));
            Assert.Equal("", await FrameSelection(w, 4));
        });
    }

    [UiFact]
    public void SearchBox_EnterSearchesForward_ShiftEnterBackward_EscapeCloses_OtherKeysAreIgnored()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            w.SearchToggle.IsChecked = true;
            Assert.Equal(Visibility.Visible, w.SearchBar.Visibility);
            await w.GoToSpineAsync(3);
            w.SearchBox.Text = Shared;

            var other = UiKit.RaiseKey(w, Key.A, target: w.SearchBox, preview: false);
            Assert.False(other.Handled);
            Assert.Equal(3, w.CurrentSpine);
            Assert.Equal("", w.SearchStatus.Text);

            // At the middle match, Enter goes on and Shift+Enter goes back.
            var enter = UiKit.RaiseKey(w, Key.Enter, target: w.SearchBox, preview: false);
            await StaReadingSession.PollAsync(() => enter.Handled, "Enter to finish searching");
            Assert.Equal(Shared, await UiKit.SelectionAsync(w.WebLeft));
            Assert.Equal("", w.SearchStatus.Text);
            Assert.Equal(3, w.CurrentSpine);

            var forward = UiKit.RaiseKey(w, Key.Enter, target: w.SearchBox, preview: false);
            await StaReadingSession.PollAsync(() => forward.Handled, "Enter to finish searching");
            Assert.Equal(4, w.CurrentSpine);

            // Going forward from the last match would have wrapped around to chapter 2.
            var back = UiKit.RaiseKey(w, Key.Enter, ModifierKeys.Shift, w.SearchBox, preview: false);
            await StaReadingSession.PollAsync(() => back.Handled, "Shift+Enter to finish searching");
            Assert.Equal(3, w.CurrentSpine);

            var escape = UiKit.RaiseKey(w, Key.Escape, target: w.SearchBox, preview: false);
            Assert.True(escape.Handled);
            Assert.False(w.SearchToggle.IsChecked);
            Assert.Equal(Visibility.Collapsed, w.SearchBar.Visibility);
        });
    }

    [UiFact]
    public void FindButtons_Search_AndTheCloseButtonHidesTheBar()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            w.SearchToggle.IsChecked = true;
            await w.GoToSpineAsync(3);
            w.SearchBox.Text = Shared;
            await w.RunSearchAsync(true);

            UiKit.Click(UiKit.Button(w.SearchBar, UiKit.FindNext));
            await StaReadingSession.PollAsync(() => w.CurrentSpine == 4, "Find Next to reach the next chapter");
            // The click handler cannot be awaited; the search is done once it has selected the match.
            await UiKit.WaitForSelectionAsync(w.WebLeft, Shared);
            Assert.Equal(Matches(3), w.SearchStatus.Text);

            UiKit.Click(UiKit.Button(w.SearchBar, UiKit.FindPrev));
            await StaReadingSession.PollAsync(() => w.CurrentSpine == 3, "Find Previous to return a chapter");
            await UiKit.WaitForSelectionAsync(w.WebLeft, Shared);

            UiKit.Click(UiKit.Button(w.SearchBar, UiKit.Close));
            Assert.False(w.SearchToggle.IsChecked);
            Assert.Equal(Visibility.Collapsed, w.SearchBar.Visibility);
        });
    }

    [UiFact]
    public void ClosingTheSearchBarWithFocusInIt_ReturnsFocusToTheBook()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();
        UiDriver.RequireForeground(session.Handle);

        session.Run(async w =>
        {
            w.SearchToggle.IsChecked = true;
            Assert.True(w.SearchBox.IsKeyboardFocused, "Opening the bar focuses the search box.");
            w.SearchToggle.IsChecked = false;
            Assert.False(w.SearchBar.IsKeyboardFocusWithin);

            // The reading pane takes focus asynchronously; a toolbar button must not be where it lands.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!UiKit.HasNativeFocus(w.WebLeft) && sw.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(25);
            Assert.True(UiKit.HasNativeFocus(w.WebLeft), "Focus went back to the reading pane.");
        });
    }
}

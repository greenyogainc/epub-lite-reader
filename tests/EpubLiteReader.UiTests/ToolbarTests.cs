using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace EpubLiteReader.UiTests;

/// <summary>The toolbar's text controls, the settings menu, bookmarks and fullscreen.</summary>
public sealed class ToolbarTests
{
    private static string Title() => "Toolbar " + Guid.NewGuid().ToString("N");

    [UiFact]
    public void TextSizeButtons_StepAndStopAtTheLimits_AndThemeCyclesThroughAllThree()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            for (var i = 0; i < 14; i++)
                UiKit.Click(UiKit.Button(w, UiKit.FontIn));
            Assert.Equal(2.5, w.CurrentDisplay.FontScale);
            for (var i = 0; i < 30; i++)
                UiKit.Click(UiKit.Button(w, UiKit.FontOut));
            Assert.Equal(0.7, w.CurrentDisplay.FontScale);

            Assert.Equal(ReadingTheme.Light, w.CurrentDisplay.Theme);
            var theme = UiKit.Button(w, Strings.Get("Theme"));
            UiKit.Click(theme);
            Assert.Equal(ReadingTheme.Sepia, w.CurrentDisplay.Theme);
            UiKit.Click(theme);
            Assert.Equal(ReadingTheme.Dark, w.CurrentDisplay.Theme);
            UiKit.Click(theme);
            Assert.Equal(ReadingTheme.Light, w.CurrentDisplay.Theme);

            // The page really shows the last theme change.
            UiKit.Click(theme);
            for (var i = 0; i < 200 && await UiKit.EvalStringAsync(w.WebLeft, "getComputedStyle(document.body).backgroundColor") != "rgb(244, 236, 216)"; i++)
                await Task.Delay(25);
            Assert.Equal("rgb(244, 236, 216)", await UiKit.EvalStringAsync(w.WebLeft, "getComputedStyle(document.body).backgroundColor"));
        });
    }

    [UiFact]
    public void SettingsMenu_EachItemChangesTheDisplay()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();
        var ui = new UiDriver(session);

        void Choose(string item, Func<DisplaySettings, bool> done)
        {
            session.Invoke(w => UiKit.Click(UiKit.Button(w, UiKit.Settings)));
            ui.Invoke(item);
            session.Run(w => StaReadingSession.PollAsync(() => done(w.CurrentDisplay), "'" + item + "' to take effect"));
        }

        const string Minus = "−";
        Choose(Strings.Get("FontSerif"), d => d.FontFamily == ReaderFontFamily.Serif);
        Choose(Strings.Get("FontSans"), d => d.FontFamily == ReaderFontFamily.Sans);
        Choose(Strings.Get("FontPublisher"), d => d.FontFamily == ReaderFontFamily.Publisher);
        Choose(Strings.Get("ThemeSepia"), d => d.Theme == ReadingTheme.Sepia);
        Choose(Strings.Get("ThemeDark"), d => d.Theme == ReadingTheme.Dark);
        Choose(Strings.Get("ThemeLight"), d => d.Theme == ReadingTheme.Light);
        Choose(Strings.Get("LineSpacing") + " +", d => Math.Abs(d.LineHeight - 1.6) < 0.001);
        Choose(Strings.Get("LineSpacing") + " " + Minus, d => Math.Abs(d.LineHeight - 1.5) < 0.001);
        Choose(Strings.Get("Margins") + " +", d => Math.Abs(d.MarginEm - 1.4) < 0.001);
        Choose(Strings.Get("Margins") + " " + Minus, d => Math.Abs(d.MarginEm - 1.2) < 0.001);

        session.Invoke(w =>
        {
            w.CurrentDisplay.FontScale = 1.6;
            w.CurrentDisplay.FontFamily = ReaderFontFamily.Sans;
        });
        Choose(Strings.Get("ResetText"), d => d.FontScale == 1.0 && d.FontFamily == ReaderFontFamily.Publisher);
        Assert.Equal(1.5, session.Invoke(w => w.CurrentDisplay.LineHeight));
    }

    [UiFact]
    public void SettingsMenu_LineSpacingAndMarginsStopAtTheirLimits()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();
        var ui = new UiDriver(session);

        void Choose(string item, Func<DisplaySettings, bool> done)
        {
            session.Invoke(w => UiKit.Click(UiKit.Button(w, UiKit.Settings)));
            ui.Invoke(item);
            session.Run(w => StaReadingSession.PollAsync(() => done(w.CurrentDisplay), "'" + item + "' to take effect"));
        }

        const string Minus = "−";
        session.Invoke(w =>
        {
            w.CurrentDisplay.LineHeight = 2.35;
            w.CurrentDisplay.MarginEm = 2.95;
        });
        Choose(Strings.Get("LineSpacing") + " +", d => d.LineHeight == 2.4);
        Choose(Strings.Get("Margins") + " +", d => d.MarginEm == 3.0);

        session.Invoke(w =>
        {
            w.CurrentDisplay.LineHeight = 1.15;
            w.CurrentDisplay.MarginEm = 0.45;
        });
        Choose(Strings.Get("LineSpacing") + " " + Minus, d => d.LineHeight == 1.1);
        Choose(Strings.Get("Margins") + " " + Minus, d => d.MarginEm == 0.4);
    }

    [UiFact]
    public void SettingsMenu_About_OpensTheAboutWindowOverTheReader()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();
        var ui = new UiDriver(session);

        session.Invoke(w => UiKit.Click(UiKit.Button(w, UiKit.Settings)));
        // The window is modal; the menu item's invoke returns before it shows.
        ui.Invoke(Strings.Get("About"));
        session.Run(async w =>
        {
            await StaReadingSession.PollAsync(() => w.OwnedWindows.OfType<AboutWindow>().Any(), "the About window");
            var about = w.OwnedWindows.OfType<AboutWindow>().Single();
            Assert.Same(w, about.Owner);
            Assert.Equal(string.Format(Strings.Get("AboutWindowTitleFormat"), Strings.Get("AppTitle")), about.Title);
            about.Close();
        });
    }

    [UiFact]
    public void Bookmarks_ToggleOnAndOff_ListInOrder_AndDoubleClickReturnsToOne()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            w.ChapterToggle.IsChecked = true;
            Assert.Equal(Strings.Get("NoBookmarks"), Assert.Single(w.BookmarkList.Items));

            // The same place twice is added, then removed.
            await w.GoToSpineAsync(3);
            await w.ToggleBookmarkAsync();
            Assert.Single(w.CurrentBookState!.Bookmarks);
            await w.ToggleBookmarkAsync();
            Assert.Empty(w.CurrentBookState!.Bookmarks);
            Assert.Equal(Strings.Get("NoBookmarks"), Assert.Single(w.BookmarkList.Items));

            await w.ToggleBookmarkAsync();
            await w.GoToSpineAsync(1);
            await w.ToggleBookmarkAsync();
            await w.StepAsync(+1);
            await w.StepAsync(+1);
            await w.ToggleBookmarkAsync();

            var entries = w.BookmarkList.Items.Cast<BookmarkEntry>().ToList();
            Assert.Equal(new[] { 1, 1, 3 }, entries.Select(e => e.SpineIndex));
            Assert.True(entries[0].ScrollFraction < entries[1].ScrollFraction);
            Assert.StartsWith("2 — ", entries[0].Label);

            // A row that is not a bookmark does nothing when double-clicked; a bookmark returns to its place.
            await w.GoToSpineAsync(4);
            w.BookmarkList.SelectedItem = null;
            w.BookmarkList.RaiseEvent(UiKit.DoubleClick(Control.MouseDoubleClickEvent));
            await Task.Delay(200);
            Assert.Equal(4, w.CurrentSpine);

            w.BookmarkList.SelectedItem = entries[1];
            w.BookmarkList.RaiseEvent(UiKit.DoubleClick(Control.MouseDoubleClickEvent));
            await StaReadingSession.PollAsync(() => w.CurrentSpine == 1, "the bookmark to open");
            await w.WhenNavIdleAsync();
            Assert.True(await UiKit.ScrollTopAsync(w.WebLeft) > 0, "The page is scrolled to the bookmark.");

            // With no bookmarks, the placeholder row is not a bookmark either.
            foreach (var entry in entries)
                w.CurrentBookState!.Bookmarks.Remove(entry);
            await w.GoToSpineAsync(2);
            await w.ToggleBookmarkAsync();
            await w.ToggleBookmarkAsync();
            w.BookmarkList.SelectedIndex = 0;
            w.BookmarkList.RaiseEvent(UiKit.DoubleClick(Control.MouseDoubleClickEvent));
            await Task.Delay(200);
            Assert.Equal(2, w.CurrentSpine);
        });
    }

    [UiFact]
    public void Bookmark_InContinuousMode_UsesThePositionTheDocumentReports()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            w.ModeContinuous.IsChecked = true;
            await w.WhenNavIdleAsync();
            await w.GoToSpineAsync(2);
            // Let the document settle on the chapter, then mark where it says it is.
            await Task.Delay(800);
            var (spine, _) = await w.LeftHost!.GetSpinePosAsync();

            await w.ToggleBookmarkAsync();
            var bookmark = Assert.Single(w.CurrentBookState!.Bookmarks);
            Assert.Equal(spine, bookmark.SpineIndex);
            Assert.Equal(spine, w.CurrentSpine);
        });
    }

    [UiFact]
    public void Fullscreen_HidesTheChrome_AndLeavingRestoresIt()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            var style = w.WindowStyle;
            var state = w.WindowState;
            w.ChapterToggle.IsChecked = true;
            w.SearchToggle.IsChecked = true;

            UiKit.Click(UiKit.Button(w, UiKit.Fullscreen));
            Assert.True(w.IsFullscreen);
            Assert.False(w.IsChapterPaneVisible);
            Assert.Equal(WindowStyle.None, w.WindowStyle);
            Assert.Equal(WindowState.Maximized, w.WindowState);
            Assert.Equal(Visibility.Collapsed, w.ToolbarHost.Visibility);
            Assert.Equal(Visibility.Collapsed, w.StatusBar.Visibility);
            Assert.Equal(Visibility.Collapsed, w.SearchBar.Visibility);

            UiKit.Click(UiKit.Button(w, UiKit.Fullscreen));
            Assert.False(w.IsFullscreen);
            Assert.True(w.IsChapterPaneVisible);
            Assert.Equal(style, w.WindowStyle);
            Assert.Equal(state, w.WindowState);
            Assert.Equal(Visibility.Visible, w.ToolbarHost.Visibility);
            Assert.Equal(Visibility.Visible, w.StatusBar.Visibility);
            Assert.Equal(Visibility.Visible, w.SearchBar.Visibility);

            // Without the pane or the search bar open, they stay closed afterwards.
            w.ChapterToggle.IsChecked = false;
            w.SearchToggle.IsChecked = false;
            UiKit.Click(UiKit.Button(w, UiKit.Fullscreen));
            UiKit.Click(UiKit.Button(w, UiKit.Fullscreen));
            Assert.False(w.IsChapterPaneVisible);
            Assert.Equal(Visibility.Collapsed, w.SearchBar.Visibility);
            await w.WhenNavIdleAsync();
        });
    }
}

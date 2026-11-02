using System.IO;
using System.Windows;
using Xunit;

namespace EpubLiteReader.UiTests;

/// <summary>The chapter sidebar: showing and hiding it, its states, selecting chapters, and following the reading position.</summary>
public sealed class ChapterPaneTests
{
    private static string Title() => "Chapters " + Guid.NewGuid().ToString("N");

    private sealed record Items(ChapterItem Header, ChapterItem PartOne, ChapterItem Section,
        ChapterItem PartTwo, ChapterItem Again, ChapterItem Untitled)
    {
        public static Items Of(MainWindow w)
        {
            var roots = (List<ChapterItem>)w.ChapterTree.ItemsSource;
            return new Items(roots[0], roots[0].Children[0], roots[0].Children[0].Children[0], roots[1], roots[2], roots[3]);
        }

        public IEnumerable<ChapterItem> All => new[] { Header, PartOne, Section, PartTwo, Again, Untitled };
    }

    private static void OpenPane(MainWindow w)
    {
        w.ChapterToggle.IsChecked = true;
        w.UpdateLayout();
    }

    [UiFact]
    public void TheNavigationTree_MapsNestedEntries_AndLabelsABlankOne()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Invoke(w =>
        {
            var items = Items.Of(w);
            Assert.Null(items.Header.SpineIndex);
            Assert.Equal(1, items.PartOne.SpineIndex);
            Assert.Equal(2, items.Section.SpineIndex);
            Assert.Equal("sec", items.Section.Anchor);
            Assert.Equal(1, items.Again.SpineIndex);
            Assert.Equal("again", items.Again.Anchor);
            Assert.Equal(Strings.Get("UntitledChapter"), items.Untitled.Title);
        });
    }

    [UiFact]
    public void TogglingThePane_ShowsAndHidesIt_RemembersItsWidth_AndKeepsFocusInTheBook()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Invoke(w =>
        {
            Assert.False(w.IsChapterPaneVisible);
            Assert.Equal(Visibility.Collapsed, w.ChapterPane.Visibility);

            OpenPane(w);
            Assert.True(w.IsChapterPaneVisible);
            Assert.Equal(Visibility.Visible, w.ChapterPane.Visibility);
            Assert.Equal(Visibility.Visible, w.ChapterSplitter.Visibility);
            Assert.Equal(Visibility.Visible, w.ChapterTree.Visibility);
            Assert.Equal(270, w.ChapterColumn.Width.Value);

            // The user drags the splitter, then closes the pane with its own close button.
            w.ChapterColumn.Width = new GridLength(333);
            w.UpdateLayout();
            UiKit.Click(UiKit.Button(w.ChapterPane, UiKit.Close));
            Assert.False(w.IsChapterPaneVisible);
            Assert.False(w.ChapterToggle.IsChecked);
            Assert.Equal(Visibility.Collapsed, w.ChapterPane.Visibility);
            Assert.Equal(0, w.ChapterColumn.Width.Value);

            w.ChapterToggle.IsChecked = true;
            Assert.Equal(333, w.ChapterColumn.Width.Value);
            w.ChapterToggle.IsChecked = false;
            Assert.False(w.IsChapterPaneVisible);

            // Hiding an already hidden pane changes nothing.
            w.ChapterToggle.IsChecked = false;
            Assert.False(w.IsChapterPaneVisible);
        });

        UiKit.FocusElement(session, w =>
        {
            OpenPane(w);
            return w.ChapterTree;
        });
        session.Run(async w =>
        {
            UiKit.Click(UiKit.Button(w.ChapterPane, UiKit.Close));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!UiKit.HasNativeFocus(w.WebLeft) && sw.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(25);
            Assert.True(UiKit.HasNativeFocus(w.WebLeft), "Closing the pane that held focus moves it to the book.");
        });
    }

    [UiFact]
    public void ABookWithoutEntries_ShowsTheEmptyMessage_AndALoadingBookShowsLoading()
    {
        using var session = StaReadingSession.Open();
        session.WaitUntilReady();
        var path = TestBook.EmptyToc(Title()).SaveTemp("empty.epub");

        try
        {
            session.Run(async w =>
            {
                OpenPane(w);
                var opening = w.OpenFileAsync(path);
                Assert.Equal(Visibility.Visible, w.ChapterLoadingText.Visibility);
                Assert.Equal(Visibility.Collapsed, w.ChapterTree.Visibility);
                await opening;

                Assert.Equal(Visibility.Visible, w.ChapterEmptyText.Visibility);
                Assert.Equal(Visibility.Collapsed, w.ChapterTree.Visibility);
                Assert.Equal(Visibility.Collapsed, w.ChapterLoadingText.Visibility);
                Assert.Empty((List<ChapterItem>)w.ChapterTree.ItemsSource);
            });
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [UiFact]
    public void ABookWithEntries_ShowsTheTree()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Invoke(w =>
        {
            OpenPane(w);
            Assert.Equal(Visibility.Visible, w.ChapterTree.Visibility);
            Assert.Equal(Visibility.Collapsed, w.ChapterEmptyText.Visibility);
            Assert.Equal(Visibility.Collapsed, w.ChapterLoadingText.Visibility);
            Assert.Equal(4, w.ChapterTree.Items.Count);
        });
    }

    [UiFact]
    public void SelectingAnEntry_GoesToItsChapterAndAnchor()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            OpenPane(w);
            var items = Items.Of(w);

            items.PartTwo.IsSelected = true;
            await StaReadingSession.PollAsync(() => w.CurrentSpine == 3, "the second part to open");
            await w.WhenNavIdleAsync();
            Assert.EndsWith("/OEBPS/part2.xhtml", w.WebLeft.CoreWebView2.Source);
            Assert.True(items.PartTwo.IsSelected);

            // A nested entry needs its parents open before the tree has a row for it.
            items.Header.IsExpanded = true;
            items.PartOne.IsExpanded = true;
            w.UpdateLayout();
            items.Section.IsSelected = true;
            await StaReadingSession.PollAsync(() => w.CurrentSpine == 2, "the section to open");
            await w.WhenNavIdleAsync();
            Assert.EndsWith("/OEBPS/part1b.xhtml#sec", w.WebLeft.CoreWebView2.Source);
            Assert.False(items.PartTwo.IsSelected);

            items.Again.IsSelected = true;
            await StaReadingSession.PollAsync(() => w.CurrentSpine == 1, "the anchored entry to open");
            await w.WhenNavIdleAsync();
            Assert.EndsWith("/OEBPS/part1.xhtml#again", w.WebLeft.CoreWebView2.Source);
            Assert.True(await UiKit.ScrollTopAsync(w.WebLeft) > 500, "The page is scrolled to the anchor.");
        });
    }

    [UiFact]
    public void SelectingAHeaderWithNoTarget_AndReselectingTheCurrentEntry_DoNotNavigate()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            OpenPane(w);
            var items = Items.Of(w);
            items.PartTwo.IsSelected = true;
            await StaReadingSession.PollAsync(() => w.CurrentSpine == 3, "the second part to open");
            await w.WhenNavIdleAsync();
            var navigations = UiKit.CountNavigations(w.WebLeft);

            // Deselecting and selecting the chapter that is already open must leave the page alone.
            items.PartTwo.IsSelected = false;
            items.PartTwo.IsSelected = true;
            await Task.Delay(300);
            Assert.Equal(0, navigations());
            Assert.Equal(3, w.CurrentSpine);

            items.Header.IsSelected = true;
            await Task.Delay(300);
            Assert.Equal(0, navigations());
            Assert.Equal(3, w.CurrentSpine);
            Assert.False(items.PartTwo.IsSelected, "Selecting another row clears the previous one.");
        });
    }

    [UiFact]
    public void TheSelection_FollowsNavigation_ExpandsParents_ClearsBeforeTheFirstEntry_AndIsRestored()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            OpenPane(w);
            var items = Items.Of(w);

            // intro is in no entry, so nothing is selected.
            Assert.DoesNotContain(items.All, i => i.IsSelected);

            await w.GoToSpineAsync(3);
            Assert.True(items.PartTwo.IsSelected);

            await w.GoToSpineAsync(2);
            Assert.True(items.Section.IsSelected);
            Assert.False(items.PartTwo.IsSelected);
            Assert.True(items.PartOne.IsExpanded);
            Assert.True(items.Header.IsExpanded);

            // Two entries point at part1; the deeper one, listed later, is the one that shows.
            await w.GoToSpineAsync(1);
            Assert.True(items.PartOne.IsSelected);
            Assert.False(items.Again.IsSelected);

            await w.GoToSpineAsync(0);
            Assert.DoesNotContain(items.All, i => i.IsSelected);

            await w.GoToSpineAsync(4);
            Assert.True(items.Untitled.IsSelected);

            // The row lost its highlight (the tree can drop it); the next sync puts it back.
            items.Untitled.IsSelected = false;
            await w.GoToSpineAsync(4);
            Assert.True(items.Untitled.IsSelected);
        });
    }

    [UiFact]
    public void Facing_FollowsTheRightPanesChapter()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            OpenPane(w);
            var items = Items.Of(w);
            w.ModeFacing.IsChecked = true;
            await w.WhenNavIdleAsync();

            await w.GoToSpineAsync(1);
            Assert.True(items.Section.IsSelected, "The pair [1,2] is led by the right page's chapter.");

            await w.GoToSpineAsync(3);
            Assert.True(items.Untitled.IsSelected, "The pair [3,4] shows chapter 4 on the right.");

            await w.GoToSpineAsync(0);
            Assert.DoesNotContain(items.All, i => i.IsSelected);
        });
    }
}

using System.Windows;
using System.Windows.Input;
using Xunit;

namespace EpubLiteReader.UiTests;

/// <summary>
/// The window's keyboard shortcuts, raised as routed events so the run does not depend on who holds the
/// foreground. Real keystrokes stay with <see cref="ReadingSessionTests"/>.
/// </summary>
public sealed class KeyboardShortcutTests
{
    private static string Title() => "Keys " + Guid.NewGuid().ToString("N");

    private static async Task<KeyEventArgs> Handled(KeyEventArgs args)
    {
        await StaReadingSession.PollAsync(() => args.Handled, "the key to be handled");
        return args;
    }

    private static KeyEventArgs Press(MainWindow w, Key key, ModifierKeys modifiers = ModifierKeys.None) =>
        UiKit.RaiseKey(w, key, modifiers);

    [UiFact]
    public void Chrome_Shortcuts_SearchPaneFullscreenAndModes()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            // Ctrl+F opens the search bar; Escape closes it.
            Assert.True(Press(w, Key.F, ModifierKeys.Control).Handled);
            Assert.True(w.SearchToggle.IsChecked);
            Assert.Equal(Visibility.Visible, w.SearchBar.Visibility);
            Assert.True(Press(w, Key.Escape).Handled);
            Assert.False(w.SearchToggle.IsChecked);

            // F4 toggles the chapter pane.
            Assert.True(Press(w, Key.F4).Handled);
            Assert.True(w.IsChapterPaneVisible);
            Assert.True(Press(w, Key.F4).Handled);
            Assert.False(w.IsChapterPaneVisible);

            // F11 enters fullscreen; Escape leaves it before it touches the search bar.
            Assert.True(Press(w, Key.F11).Handled);
            Assert.True(w.IsFullscreen);
            w.SearchToggle.IsChecked = true;
            Assert.True(Press(w, Key.Escape).Handled);
            Assert.False(w.IsFullscreen);
            Assert.True(w.SearchToggle.IsChecked);
            Assert.True(Press(w, Key.Escape).Handled);
            Assert.False(w.SearchToggle.IsChecked);

            // Escape with nothing to close is not handled.
            Assert.False(Press(w, Key.Escape).Handled);

            // 1, 2 and 3 pick the layout; with Ctrl they are left alone.
            Assert.True(Press(w, Key.D2).Handled);
            await w.WhenNavIdleAsync();
            Assert.Equal(ViewMode.Facing, w.CurrentMode);
            Assert.True(Press(w, Key.D3).Handled);
            await w.WhenNavIdleAsync();
            Assert.Equal(ViewMode.Continuous, w.CurrentMode);
            Assert.False(Press(w, Key.D1, ModifierKeys.Control).Handled);
            Assert.Equal(ViewMode.Continuous, w.CurrentMode);
            Assert.True(Press(w, Key.D1).Handled);
            await w.WhenNavIdleAsync();
            Assert.Equal(ViewMode.Single, w.CurrentMode);
        });
    }

    [UiFact]
    public void Chrome_Shortcuts_TextSizeAndReset()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Invoke(w =>
        {
            double Scale() => w.CurrentDisplay.FontScale;

            Assert.False(Press(w, Key.OemPlus).Handled, "Plus without Ctrl is not a shortcut.");
            Assert.Equal(1.0, Scale(), 3);

            Assert.True(Press(w, Key.OemPlus, ModifierKeys.Control).Handled);
            Assert.Equal(1.1, Scale(), 3);
            Assert.True(Press(w, Key.Add, ModifierKeys.Control).Handled);
            Assert.Equal(1.21, Scale(), 3);
            Assert.True(Press(w, Key.OemMinus, ModifierKeys.Control).Handled);
            Assert.Equal(1.1, Scale(), 3);
            Assert.True(Press(w, Key.Subtract, ModifierKeys.Control).Handled);
            Assert.Equal(1.0, Scale(), 3);

            Press(w, Key.Add, ModifierKeys.Control);
            Assert.True(Press(w, Key.D0, ModifierKeys.Control).Handled);
            Assert.Equal(1.0, Scale(), 3);
            Press(w, Key.Add, ModifierKeys.Control);
            Assert.True(Press(w, Key.NumPad0, ModifierKeys.Control).Handled);
            Assert.Equal(1.0, Scale(), 3);
        });
    }

    [UiFact]
    public void Chrome_Shortcuts_PagingBookmarksAndJumps()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            Keyboard.ClearFocus();

            async Task Turn(Key key, ModifierKeys modifiers, int expectedSpine)
            {
                await Handled(Press(w, key, modifiers));
                await w.WhenNavIdleAsync();
                Assert.Equal(expectedSpine, w.CurrentSpine);
            }

            // The first chapter fits one page, so each of these crosses a chapter boundary.
            await Turn(Key.PageDown, ModifierKeys.None, 1);
            await Turn(Key.PageUp, ModifierKeys.None, 0);
            await Turn(Key.Right, ModifierKeys.None, 1);
            await Turn(Key.Left, ModifierKeys.None, 0);
            await Turn(Key.Space, ModifierKeys.None, 1);
            await Turn(Key.Space, ModifierKeys.Shift, 0);

            await Turn(Key.End, ModifierKeys.None, 4);
            await Turn(Key.Home, ModifierKeys.None, 0);

            // B toggles a bookmark at the reading position.
            await Handled(Press(w, Key.B));
            Assert.Single(w.CurrentBookState!.Bookmarks);
            await Handled(Press(w, Key.B));
            Assert.Empty(w.CurrentBookState!.Bookmarks);

            Assert.False(Press(w, Key.Z).Handled, "A key with no meaning is left for the control that has focus.");
        });
    }

    [UiFact]
    public void Space_WithAToolbarButtonFocused_ActivatesTheButtonInsteadOfStepping()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        UiKit.FocusElement(session, w => UiKit.Button(w, UiKit.FontIn));
        session.Run(async w =>
        {
            Assert.IsAssignableFrom<ButtonBase>(Keyboard.FocusedElement);
            var space = Press(w, Key.Space);
            await Task.Delay(300);
            Assert.False(space.Handled);
            Assert.Equal(0, w.CurrentSpine);
        });
    }

    [UiFact]
    public void ShortcutsWithoutABook_DoNothingHarmful()
    {
        using var session = StaReadingSession.Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            Assert.True(Press(w, Key.End).Handled);
            Assert.True(Press(w, Key.Home).Handled);
            Assert.True(Press(w, Key.PageDown).Handled);
            Assert.True(Press(w, Key.B).Handled);
            Assert.True(Press(w, Key.P, ModifierKeys.Control).Handled);
            await w.WhenNavIdleAsync();
            Assert.Null(w.CurrentBookState);
            Assert.Equal(0, w.CurrentSpine);
            Assert.DoesNotContain("Exception", session.ReadLog());
        });
    }

    [UiFact]
    public void CtrlO_OpensTheFileDialog_AndCancelLeavesTheBookAlone()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();
        var bookBefore = session.Invoke(w => w.CurrentDoc);

        KeyEventArgs? args = null;
        var dialog = NativeDialogs.CloseNextFileDialogAsync(30);
        // The dialog is modal, so the key event cannot return until it closes.
        session.Invoke(w => w.Dispatcher.BeginInvoke(() => args = UiKit.RaiseKey(w, Key.O, ModifierKeys.Control)));
        dialog.GetAwaiter().GetResult();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (args?.Handled != true && sw.Elapsed < TimeSpan.FromSeconds(15))
            Thread.Sleep(50);
        Assert.True(args?.Handled);
        Assert.Same(bookBefore, session.Invoke(w => w.CurrentDoc));
    }

    [UiFact]
    public void FocusInTheSearchBox_LeavesTheTypingKeysToIt_ButEscapeStillCloses()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        UiKit.FocusElement(session, w =>
        {
            w.SearchToggle.IsChecked = true;
            return w.SearchBox;
        });
        session.Run(async w =>
        {
            Assert.False(Press(w, Key.F4).Handled);
            Assert.False(Press(w, Key.B).Handled);
            Assert.False(Press(w, Key.Space).Handled);
            Assert.False(Press(w, Key.Enter).Handled);
            Assert.False(w.IsChapterPaneVisible);
            Assert.Empty(w.CurrentBookState!.Bookmarks);

            await Handled(Press(w, Key.Escape));
            Assert.False(w.SearchToggle.IsChecked);
        });
    }

    [UiFact]
    public void FocusInThePageBox_LeavesNavigationKeysToIt()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            foreach (var key in new[] { Key.D1, Key.D2, Key.D3, Key.Left, Key.Right, Key.Home, Key.End, Key.B, Key.Space })
                Assert.False(UiKit.RaiseKey(w, key, target: w.PageBox).Handled, key + " belongs to the page box.");
            await Task.Delay(300);
            Assert.Equal(0, w.CurrentSpine);
            Assert.Equal(ViewMode.Single, w.CurrentMode);
            Assert.Empty(w.CurrentBookState!.Bookmarks);

            // Page keys are not typing keys, so they still turn the page from there.
            await Handled(UiKit.RaiseKey(w, Key.PageDown, target: w.PageBox));
            await w.WhenNavIdleAsync();
            Assert.Equal(1, w.CurrentSpine);
        });
    }

    [UiFact]
    public void FocusInTheChapterTree_LeavesNavigationKeysToIt_ButNotTheShortcuts()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        UiKit.FocusElement(session, w =>
        {
            w.ChapterToggle.IsChecked = true;
            w.UpdateLayout();
            return w.ChapterTree;
        });
        session.Run(async w =>
        {
            foreach (var key in new[] { Key.Left, Key.Right, Key.Home, Key.End, Key.PageUp, Key.PageDown, Key.B, Key.Space })
                Assert.False(UiKit.RaiseKey(w, key, target: w.ChapterTree).Handled, key + " belongs to the tree.");
            await Task.Delay(300);
            Assert.Equal(0, w.CurrentSpine);
            Assert.Empty(w.CurrentBookState!.Bookmarks);

            Assert.True(UiKit.RaiseKey(w, Key.F4, target: w.ChapterTree).Handled);
            Assert.False(w.IsChapterPaneVisible);
        });
    }
}

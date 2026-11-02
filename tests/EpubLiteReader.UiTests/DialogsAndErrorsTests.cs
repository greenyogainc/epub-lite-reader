using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using Xunit;

namespace EpubLiteReader.UiTests;

/// <summary>Opening books (dialog, drop, failures), the dialogs the window shows when something goes wrong,
/// and the places where the window must carry on after a failure.</summary>
public sealed class DialogsAndErrorsTests
{
    private static string Title() => "Dialogs " + Guid.NewGuid().ToString("N");

    private static string[] ExtractRoots() => Directory.Exists(EpubDoc.ExtractBaseDir)
        ? Directory.GetDirectories(EpubDoc.ExtractBaseDir)
        : Array.Empty<string>();

    private static void DeleteFolderOf(string path) => Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);

    // ---------- Failed opens ----------

    [UiFact]
    public void ACorruptFile_ShowsTheError_AndThePreviousBookStaysOpen()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();
        var corrupt = Path.Combine(session.DataDir, "corrupt.epub");
        File.WriteAllText(corrupt, "this is not a zip archive");

        var message = session.Run(async w =>
        {
            w.ChapterToggle.IsChecked = true;
            var before = w.CurrentDoc;
            var dialog = NativeDialogs.CloseNextAsync();
            await w.OpenFileAsync(corrupt);
            var text = await dialog;

            Assert.Same(before, w.CurrentDoc);
            Assert.Equal(Visibility.Visible, w.ChapterTree.Visibility);
            Assert.Equal(Visibility.Collapsed, w.ChapterLoadingText.Visibility);
            await w.GoToSpineAsync(1);
            Assert.EndsWith("/OEBPS/part1.xhtml", w.WebLeft.CoreWebView2.Source);
            return text;
        });

        Assert.Contains(corrupt, message);
        Assert.Contains("Exception", session.ReadLog());
    }

    [UiFact]
    public void ABookWithNothingToRead_SaysSo_AndAFirstEverFailureLeavesTheEmptyScreen()
    {
        using var session = StaReadingSession.Open();
        session.WaitUntilReady();
        var emptySpine = TestBook.EmptySpine(Title()).SaveTemp("empty-spine.epub");
        var corrupt = Path.Combine(session.DataDir, "corrupt.epub");
        File.WriteAllText(corrupt, "not a book");

        try
        {
            session.Run(async w =>
            {
                var dialog = NativeDialogs.CloseNextAsync();
                await w.OpenFileAsync(emptySpine);
                Assert.Equal(Strings.Get("NoReadableContent"), (await dialog).Trim());

                dialog = NativeDialogs.CloseNextAsync();
                await w.OpenFileAsync(corrupt);
                Assert.Contains(corrupt, await dialog);

                Assert.Null(w.CurrentDoc);
                Assert.Equal(Visibility.Visible, w.EmptyState.Visibility);
                Assert.Equal(Visibility.Collapsed, w.ReaderGrid.Visibility);
                Assert.Equal(Visibility.Collapsed, w.ChapterLoadingText.Visibility);
                Assert.Equal(Visibility.Collapsed, w.ChapterTree.Visibility);
            });
        }
        finally
        {
            DeleteFolderOf(emptySpine);
        }
    }

    [UiFact]
    public void AFailureAfterTheNewBookIsLive_KeepsTheNewBook_AndStillReleasesTheOldOne()
    {
        var newTitle = Title();
        var newer = TestBook.EmptyToc(newTitle).SaveTemp("newer.epub");
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        try
        {
            session.Run(async w =>
            {
                var oldDoc = w.CurrentDoc!;
                var oldRoot = oldDoc.ExtractRoot;
                // A row added by hand makes the tree refuse a bound list, which happens after the new book is committed.
                w.ChapterTree.ItemsSource = null;
                w.ChapterTree.Items.Add("a row added by hand");

                var dialog = NativeDialogs.CloseNextAsync();
                await w.OpenFileAsync(newer);
                Assert.Contains(newer, await dialog);

                Assert.NotSame(oldDoc, w.CurrentDoc);
                Assert.Equal(newTitle, w.CurrentDoc!.Title);
                Assert.False(Directory.Exists(oldRoot), "The old book's extract root is released.");
                Assert.True(Directory.Exists(w.CurrentDoc.ExtractRoot), "The new book stays live.");
            });
        }
        finally
        {
            DeleteFolderOf(newer);
        }
    }

    [UiFact]
    public void WhenTwoOpensOverlap_TheLaterOneWins_AndTheEarlierLeavesNoExtractRoot()
    {
        using var session = StaReadingSession.Open();
        session.WaitUntilReady();
        var first = TestBook.Rich(Title()).SaveTemp("first.epub");
        var second = TestBook.EmptyToc(Title()).SaveTemp("second.epub");

        try
        {
            session.Run(async w =>
            {
                var before = ExtractRoots();
                var opening = w.OpenFileAsync(first);
                var superseding = w.OpenFileAsync(second);
                await Task.WhenAll(opening, superseding);

                Assert.Equal(second, w.CurrentDoc!.FilePath);
                Assert.Equal(new[] { w.CurrentDoc.ExtractRoot }, ExtractRoots().Except(before).ToArray());
            });
        }
        finally
        {
            DeleteFolderOf(first);
            DeleteFolderOf(second);
        }
    }

    [UiFact]
    public void AnOpenSupersededAfterItsBookWasExtracted_DisposesThatBook()
    {
        using var session = StaReadingSession.Open();
        session.WaitUntilReady();
        var first = TestBook.Rich(Title()).SaveTemp("first.epub");
        var second = TestBook.EmptyToc(Title()).SaveTemp("second.epub");

        try
        {
            session.Run(async w =>
            {
                var before = ExtractRoots();
                var opening = w.OpenFileAsync(first);

                // Hold the UI thread until the first book is completely extracted on the pool, so its
                // finished result is already waiting when the second open cancels it.
                var sw = Stopwatch.StartNew();
                while (!ExtractRoots().Except(before).Any(r => File.Exists(Path.Combine(r, EpubDoc.ContinuousFileName))))
                {
                    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), "The first book was never extracted.");
                    Thread.Sleep(20);
                }

                Thread.Sleep(300);
                var superseding = w.OpenFileAsync(second);
                await Task.WhenAll(opening, superseding);

                Assert.Equal(second, w.CurrentDoc!.FilePath);
                Assert.Equal(new[] { w.CurrentDoc.ExtractRoot }, ExtractRoots().Except(before).ToArray());
            });
        }
        finally
        {
            DeleteFolderOf(first);
            DeleteFolderOf(second);
        }
    }

    [UiFact]
    public void OpeningABookBeforeTheReaderIsReady_ReturnsAtOnce()
    {
        var path = TestBook.EmptyToc(Title()).SaveTemp("early.epub");
        Task? opening = null;

        try
        {
            using var session = StaReadingSession.Open(beforeShow: w => opening = w.OpenFileAsync(path));
            Assert.True(opening!.IsCompletedSuccessfully);
            session.WaitUntilReady();
            Assert.Null(session.Invoke(w => w.CurrentDoc));
        }
        finally
        {
            DeleteFolderOf(path);
        }
    }

    [UiFact]
    public void EntriesThatCannotBeExtracted_AreReportedInTheLog_AndTheBookOpens()
    {
        using var session = StaReadingSession.Open();
        session.WaitUntilReady();
        var path = TestBook.Colliding(Title()).SaveTemp("colliding.epub");

        try
        {
            session.Run(async w =>
            {
                await w.OpenFileAsync(path);
                Assert.NotNull(w.CurrentDoc);
                Assert.Single(w.CurrentDoc!.SkippedEntries);
            });
            Assert.Contains("Skipped 1 unsafe, colliding, or oversized entries", session.ReadLog());
        }
        finally
        {
            DeleteFolderOf(path);
        }
    }

    // ---------- Choosing a book ----------

    [UiFact]
    public void TheOpenButton_ShowsTheFileDialog_CancelChangesNothing_AcceptOpensTheChoice()
    {
        var chosenTitle = Title();
        var chosen = TestBook.EmptyToc(chosenTitle).SaveTemp("chosen.epub");
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();
        var before = session.Invoke(w => w.CurrentDoc);

        try
        {
            // The dialog is modal, so the click cannot return until it closes: post it and drive the dialog here.
            void ClickOpen() => session.Invoke(w =>
                w.Dispatcher.BeginInvoke(() => UiKit.Click(UiKit.Button(w, Strings.Get("Open")))));

            var cancel = NativeDialogs.CloseNextFileDialogAsync(30);
            ClickOpen();
            cancel.GetAwaiter().GetResult();
            session.Run(w => w.WhenNavIdleAsync());
            Assert.Same(before, session.Invoke(w => w.CurrentDoc));

            var accept = NativeDialogs.AcceptFileDialogAsync(chosen, 30);
            ClickOpen();
            Assert.Contains(Strings.Get("OpenFileDialogTitle"), accept.GetAwaiter().GetResult());
            session.Run(async w =>
            {
                await StaReadingSession.PollAsync(() => w.CurrentDoc?.Title == chosenTitle, "the chosen book to open", 30);
                await w.WhenNavIdleAsync();
            });
        }
        finally
        {
            DeleteFolderOf(chosen);
        }
    }

    [UiFact]
    public void TheSampleBookLink_OpensTheBundledSample()
    {
        using var session = StaReadingSession.Open();
        session.WaitUntilReady();

        session.Invoke(w => UiKit.Click(UiKit.Button(w.EmptyState, Strings.Get("OpenSampleBook"))));
        session.Run(async w =>
        {
            await StaReadingSession.PollAsync(() => w.CurrentDoc is not null, "the sample book to open", 30);
            await w.WhenNavIdleAsync();
            Assert.EndsWith(Path.Combine("Assets", "sample.epub"), w.CurrentDoc!.FilePath);
            Assert.True(w.CurrentDoc.SpineCount > 1);
            Assert.Equal(Visibility.Visible, w.ReaderGrid.Visibility);
        });
    }

    [UiFact]
    public void DroppingFiles_OpensAnEpub_AndRefusesEverythingElse()
    {
        var droppedTitle = Title();
        var epub = TestBook.EmptyToc(droppedTitle).SaveTemp("dropped.epub");
        var notes = Path.Combine(Path.GetDirectoryName(epub)!, "notes.txt");
        File.WriteAllText(notes, "not a book");
        using var session = StaReadingSession.Open();
        session.WaitUntilReady();

        try
        {
            session.Run(async w =>
            {
                DragEventArgs Raise(RoutedEvent kind, IDataObject data)
                {
                    var args = UiKit.DragArgs(kind, data, w);
                    w.RaiseEvent(args);
                    return args;
                }

                var epubData = new DataObject(DataFormats.FileDrop, new[] { epub });
                var notesData = new DataObject(DataFormats.FileDrop, new[] { notes });
                var textData = new DataObject(DataFormats.Text, "just some text");

                var over = Raise(DragDrop.DragOverEvent, epubData);
                Assert.Equal(DragDropEffects.Copy, over.Effects);
                Assert.True(over.Handled);
                Assert.Equal(DragDropEffects.None, Raise(DragDrop.DragOverEvent, notesData).Effects);
                Assert.Equal(DragDropEffects.None, Raise(DragDrop.DragOverEvent, textData).Effects);

                Raise(DragDrop.DropEvent, notesData);
                Raise(DragDrop.DropEvent, textData);
                await Task.Delay(300);
                Assert.Null(w.CurrentDoc);

                Raise(DragDrop.DropEvent, epubData);
                await StaReadingSession.PollAsync(() => w.CurrentDoc is not null, "the dropped book to open", 30);
                await w.WhenNavIdleAsync();
                Assert.Equal(droppedTitle, w.CurrentDoc!.Title);
            });
        }
        finally
        {
            DeleteFolderOf(epub);
        }
    }

    // ---------- Failures the window must survive ----------

    [UiFact]
    public void WhenAPaneCannotStart_TheWindowShowsTheError()
    {
        var dialog = NativeDialogs.CloseNextAsync(60);
        // A pane that is already disposed cannot start its browser; this fails at once and every time,
        // where pointing at a missing runtime folder takes anywhere from a moment to many minutes.
        using var session = StaReadingSession.Open(beforeShow: w => w.WebLeft.Dispose());

        var text = dialog.GetAwaiter().GetResult();
        Assert.NotEmpty(text.Trim());
        Assert.False(session.Invoke(w => w.HostsReady));
        Assert.Contains(text.Split((char)10)[0].Trim(), session.ReadLog());
    }

    [UiFact]
    public void ADeadReadingPane_IsLoggedByTheNavigationPump_AndPrintShowsTheError()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            w.WebLeft.Dispose();
            await w.GoToSpineAsync(1);
            await w.WhenNavIdleAsync();
        });
        Assert.NotEqual("", session.ReadLog());

        var dialog = NativeDialogs.CloseNextAsync(30);
        session.Invoke(w => w.Dispatcher.BeginInvoke(() => UiKit.Click(UiKit.Button(w, Strings.Get("Print")))));
        Assert.NotEmpty(dialog.GetAwaiter().GetResult().Trim());
    }

    [UiFact]
    public void ASearchOrPageTurnOutlivingItsPanes_EndsQuietly()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        // Closing the window disposes the panes while these may still be awaiting a step. Each
        // used to throw out of its async void handler into the "unexpected error" dialog.
        session.Run(async w =>
        {
            w.SearchBox.Text = TestBook.SharedWord;
            w.WebLeft.Dispose();
            w.WebRight.Dispose();
            await w.RunSearchAsync(forward: true);
            await w.StepAsync(1);
            await w.ToggleBookmarkAsync();
            await w.WhenNavIdleAsync();
        });
        Assert.Contains(nameof(ObjectDisposedException), session.ReadLog());
    }

    [UiFact]
    public void AStateFileThatCannotBeWritten_NeverStopsTheReader()
    {
        var dir = Path.Combine(Path.GetTempPath(), "elr-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // Later arguments win, so this makes the automation file a path that is a folder.
            using var session = TestBook.Rich(Title()).Open(reuseDataDir: dir, extraArgs: new[] { "--statefile=" + dir });
            session.WaitUntilReady();
            session.Run(async w =>
            {
                await w.GoToSpineAsync(2);
                Assert.Equal(2, w.CurrentSpine);
                w.ChapterToggle.IsChecked = true;
                await w.ToggleBookmarkAsync();
                Assert.Single(w.CurrentBookState!.Bookmarks);
            });
            Assert.Null(session.DispatcherError);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [UiFact]
    public void Print_OpensThePreview_WithoutPrintingOrErrors()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            // The in-page preview; nothing reaches a printer until someone presses Print in it.
            UiKit.Click(UiKit.Button(w, Strings.Get("Print")));
            await Task.Delay(500);
            Assert.Equal(2d, await UiKit.EvalNumberAsync(w.WebLeft, "1 + 1"));
            await w.GoToSpineAsync(1);
            Assert.Equal(1, w.CurrentSpine);
        });
        Assert.Equal("", session.ReadLog());
    }

    [UiFact]
    public void AnUnhandledException_IsLogged_AndShownInADialogWithNoOwner()
    {
        var log = Path.Combine(Path.GetTempPath(), "elr-unhandled-" + Guid.NewGuid().ToString("N") + ".log");
        ReaderApp.Dispatcher.Invoke(() => App.LogPathOverride = log);
        ReaderApp.TakeUnhandled();

        try
        {
            var dialog = NativeDialogs.CloseNextAsync(30);
            ReaderApp.Dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("boom from the test")));
            var text = dialog.GetAwaiter().GetResult();

            Assert.Contains("boom from the test", text);
            Assert.Contains(log, text);
            Assert.Equal("boom from the test", ReaderApp.TakeUnhandled()?.Message);
            Assert.Contains("boom from the test", File.ReadAllText(log));
        }
        finally
        {
            ReaderApp.Dispatcher.Invoke(() => App.LogPathOverride = UiKit.DefaultLog);
            File.Delete(log);
        }
    }

    [UiFact]
    public void ErrorDialogs_ReadRightToLeft_UnderArabic_AndFlowDirectionFollowsTheCulture()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            var flows = ReaderApp.Dispatcher.Invoke(() =>
            {
                var window = new Window();
                CultureInfo.CurrentUICulture = new CultureInfo("ar");
                Strings.ApplyFlowDirection(window);
                var arabic = (window.FlowDirection, Strings.MessageBoxOptions);
                CultureInfo.CurrentUICulture = new CultureInfo("en");
                Strings.ApplyFlowDirection(window);
                return (arabic, english: (window.FlowDirection, Strings.MessageBoxOptions));
            });

            Assert.Equal(FlowDirection.RightToLeft, flows.arabic.FlowDirection);
            Assert.Equal(MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign, flows.arabic.Item2);
            Assert.Equal(FlowDirection.LeftToRight, flows.english.Item1);
            Assert.Equal(MessageBoxOptions.None, flows.english.Item2);

            // An ownerless message box under an RTL culture still opens and closes.
            var dialog = NativeDialogs.CloseNextAsync(30);
            ReaderApp.Dispatcher.BeginInvoke(new Action(() =>
            {
                CultureInfo.CurrentUICulture = new CultureInfo("ar");
                Strings.ShowError(null, "no owner");
            }));
            Assert.Equal("no owner", dialog.GetAwaiter().GetResult().Trim());
        }
        finally
        {
            ReaderApp.Dispatcher.Invoke(() => CultureInfo.CurrentUICulture = original);
        }
    }
}

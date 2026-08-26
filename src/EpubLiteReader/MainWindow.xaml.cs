using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace EpubLiteReader;

public partial class MainWindow : Window
{
    private EpubDoc? _doc;
    private ViewMode _mode = ViewMode.Single;
    private int _spineIndex;

    /// <summary>Scroll position as a fraction inside the current spine item.</summary>
    private double _scrollFraction;

    private DisplaySettings _display = new();
    private BookState? _bookState;
    private AppSettings _appSettings = new();

    private ReadingHost? _left;
    private ReadingHost? _right;
    private bool _hostsReady;

    private bool _fullscreen;
    private WindowState _preFsState;
    private WindowStyle _preFsStyle;

    private enum ChapterLoadState { NotLoaded, Loading, Loaded, Empty, Failed }

    private bool _chapterPaneVisible;
    private bool _preFsChapterVisible;
    private GridLength _chapterPaneWidth = new(270);
    private ChapterLoadState _chapterState = ChapterLoadState.NotLoaded;
    private List<ChapterItem>? _chapterRoots;
    private List<ChapterItem> _navigableChapters = new();
    private ChapterItem? _selectedChapter;
    private bool _suppressChapterNav;
    private bool _suppressModeEvents;

    private readonly DispatcherTimer _saveTimer;
    private List<(int SpineIndex, int Offset, string Snippet)> _searchHits = new();
    private int _searchHitIndex = -1;
    private string? _searchHitsQuery;

    private CancellationTokenSource? _openCts;

    public MainWindow()
    {
        InitializeComponent();
        Strings.ApplyFlowDirection(this);

        _appSettings = BookStateStore.LoadAppSettings();
        _display = _appSettings.Defaults.Clone();
        if (_appSettings.WindowWidth is double w && w >= MinWidth)
            Width = Math.Min(w, SystemParameters.VirtualScreenWidth);
        if (_appSettings.WindowHeight is double h && h >= MinHeight)
            Height = Math.Min(h, SystemParameters.VirtualScreenHeight);

        PageCountText.Text = string.Format(Strings.Get("ChapterCountFormat"), 0);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); PersistBookState(); };

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _left = new ReadingHost(WebLeft, "left");
            _right = new ReadingHost(WebRight, "right");
            await _left.EnsureReadyAsync();
            await _right.EnsureReadyAsync();
            _left.MessageReceived += OnHostMessage;
            _right.MessageReceived += OnHostMessage;
            _hostsReady = true;

            var startupFile = ((App)Application.Current).StartupFile;
            if (startupFile is not null)
                await OpenFileAsync(startupFile);
            WriteAutomationState();
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            Strings.ShowError(this, ex.Message);
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _openCts?.Cancel();
        PersistBookState();
        _appSettings.Defaults = _display.Clone();
        _appSettings.WindowWidth = Width;
        _appSettings.WindowHeight = Height;
        BookStateStore.SaveAppSettings(_appSettings);
        _doc?.Dispose();
    }

    // ---------- File ----------

    private void Open_Click(object sender, RoutedEventArgs e) => _ = ShowOpenDialogAsync();

    private void OpenSample_Click(object sender, RoutedEventArgs e) =>
        _ = OpenFileAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "sample.epub"));

    private async Task ShowOpenDialogAsync()
    {
        var dlg = new OpenFileDialog
        {
            Filter = Strings.Get("OpenFileDialogFilter"),
            Title = Strings.Get("OpenFileDialogTitle")
        };
        if (dlg.ShowDialog() == true)
            await OpenFileAsync(dlg.FileName);
    }

    private async Task OpenFileAsync(string path)
    {
        if (!_hostsReady) return;

        // A newer open supersedes any open still in flight.
        _openCts?.Cancel();
        var cts = new CancellationTokenSource();
        _openCts = cts;

        var prevChapterState = _chapterState;
        PersistBookState();

        _chapterState = ChapterLoadState.Loading;
        ShowChapterState(_chapterState);

        EpubDoc? doc = null;
        try
        {
            var untitled = Strings.Get("UntitledChapter");
            var sectionFormat = Strings.Get("ContinuousSectionTitleFormat");
            List<ChapterItem> chapters;
            (doc, chapters) = await EpubDoc.OpenWithChaptersAsync(path, untitled, sectionFormat, cts.Token);
            if (cts.Token.IsCancellationRequested)
            {
                doc.Dispose();
                return;
            }

            // Commit point: the replacement opened successfully, so the previous
            // document can now be released and every piece of UI switched over.
            var old = _doc;
            _doc = doc;
            old?.Dispose();
            if (doc.SkippedEntries.Count > 0)
                App.LogError(new InvalidOperationException(
                    $"Skipped {doc.SkippedEntries.Count} unsafe or colliding entries while extracting \"{path}\"."));

            _chapterRoots = chapters;
            _navigableChapters = FlattenNavigable(chapters);
            _chapterState = chapters.Count == 0 ? ChapterLoadState.Empty : ChapterLoadState.Loaded;
            ChapterTree.ItemsSource = chapters;
            ShowChapterState(_chapterState);
            _selectedChapter = null;

            _searchHits.Clear();
            _searchHitsQuery = null;
            _searchHitIndex = -1;
            SearchStatus.Text = "";

            _bookState = BookStateStore.LoadBook(doc.BookId) ?? new BookState
            {
                BookId = doc.BookId,
                FilePath = path,
                Display = _appSettings.Defaults.Clone()
            };
            _bookState.FilePath = path;
            _display = _bookState.Display.Clone();
            _spineIndex = Math.Clamp(_bookState.SpineIndex, 0, Math.Max(0, doc.SpineCount - 1));
            _scrollFraction = Math.Clamp(_bookState.ScrollFraction, 0, 1);
            _mode = _display.ViewMode;
            UpdateModeRadios(_mode);

            Title = string.Format(Strings.Get("MainWindowTitleFormat"), doc.Title, Strings.Get("AppTitle"));
            MetaText.Text = string.IsNullOrWhiteSpace(doc.Author)
                ? doc.Title
                : string.Format(Strings.Get("MetadataFormat"), doc.Title, doc.Author);
            EmptyState.Visibility = Visibility.Collapsed;
            ReaderGrid.Visibility = Visibility.Visible;
            PageCountText.Text = string.Format(Strings.Get("ChapterCountFormat"), doc.SpineCount);
            RefreshBookmarksUi();

            await RequestViewAsync(new ViewRequest(_mode, _spineIndex, null, _scrollFraction, RestoreScroll: true));
        }
        catch (OperationCanceledException)
        {
            doc?.Dispose();
            // Superseded by a newer open; that open owns the UI now.
        }
        catch (Exception ex)
        {
            doc?.Dispose();
            App.LogError(ex);
            // The previous book was never torn down, so simply restore its state.
            _chapterState = prevChapterState;
            ShowChapterState(_chapterState);
            Strings.ShowError(this, string.Format(Strings.Get("OpenFileErrorMessage"), path, ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_openCts, cts))
                _openCts = null;
            cts.Dispose();
            WriteAutomationState();
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasEpub(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var epub = files.FirstOrDefault(f => f.EndsWith(".epub", StringComparison.OrdinalIgnoreCase));
            if (epub is not null)
                await OpenFileAsync(epub);
        }
    }

    private static bool HasEpub(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files &&
        files.Any(f => f.EndsWith(".epub", StringComparison.OrdinalIgnoreCase));

    // ---------- View requests ----------
    //
    // All document navigation funnels through a latest-request-wins pump: requests
    // made while one is executing replace any still-pending request instead of
    // being dropped, so rapid mode/page changes always settle on the last one.

    private sealed record ViewRequest(ViewMode Mode, int Spine, string? Anchor, double Fraction, bool RestoreScroll);

    private ViewRequest? _pendingView;
    private Task? _viewPump;

    private Task RequestViewAsync(ViewRequest request)
    {
        _pendingView = request;
        if (_viewPump is null || _viewPump.IsCompleted)
            _viewPump = RunViewPumpAsync();
        return _viewPump;
    }

    private Task WhenNavIdleAsync() => _viewPump ?? Task.CompletedTask;

    private async Task RunViewPumpAsync()
    {
        try
        {
            while (_pendingView is { } request)
            {
                _pendingView = null;
                await ExecuteViewAsync(request);
            }
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
        finally
        {
            // This pump's own task is still marked running here, so idleness
            // must be stated explicitly: the queue is drained.
            WriteAutomationState(navIdleOverride: _pendingView is null);
        }
    }

    /// <summary>Facing pairs: [0], [1,2], [3,4], …</summary>
    private static int FacingGroupStart(int spine) => spine == 0 ? 0 : (spine % 2 == 1 ? spine : spine - 1);

    private async Task ExecuteViewAsync(ViewRequest request)
    {
        if (_doc is null || _left is null || _right is null) return;

        _mode = request.Mode;
        _spineIndex = Math.Clamp(request.Spine, 0, Math.Max(0, _doc.SpineCount - 1));
        _scrollFraction = request.RestoreScroll ? Math.Clamp(request.Fraction, 0, 1) : 0;
        UpdateModeRadios(_mode);

        _left.SetDisplaySettings(_display);
        _right.SetDisplaySettings(_display);

        if (_mode == ViewMode.Facing)
        {
            RightCol.Width = new GridLength(1, GridUnitType.Star);
            WebRight.Visibility = Visibility.Visible;
            int start = FacingGroupStart(_spineIndex);
            _spineIndex = start;
            await _left.NavigateSpineAsync(_doc, start, request.Anchor, _scrollFraction);
            if (start != 0 && start + 1 < _doc.SpineCount)
                await _right.NavigateSpineAsync(_doc, start + 1);
            else
                await _right.NavigateBlankAsync();
        }
        else
        {
            RightCol.Width = new GridLength(0);
            WebRight.Visibility = Visibility.Collapsed;
            if (_mode == ViewMode.Continuous)
                await _left.NavigateContinuousAsync(_doc, _spineIndex, _scrollFraction);
            else
                await _left.NavigateSpineAsync(_doc, _spineIndex, request.Anchor, _scrollFraction);
        }

        PageBox.Text = (_spineIndex + 1).ToString();
        UpdateProgress();
        SyncChapterSelection();
    }

    // ---------- Modes ----------

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _doc is null || _suppressModeEvents) return;
        var mode = ReferenceEquals(sender, ModeFacing) ? ViewMode.Facing
                 : ReferenceEquals(sender, ModeContinuous) ? ViewMode.Continuous
                 : ViewMode.Single;
        _display.ViewMode = mode;
        ScheduleSave();
        // Keep the reading position when flipping between layouts.
        _ = RequestViewAsync(new ViewRequest(mode, _spineIndex, null, _scrollFraction, RestoreScroll: true));
    }

    private void UpdateModeRadios(ViewMode mode)
    {
        _suppressModeEvents = true;
        try
        {
            switch (mode)
            {
                case ViewMode.Facing: ModeFacing.IsChecked = true; break;
                case ViewMode.Continuous: ModeContinuous.IsChecked = true; break;
                default: ModeSingle.IsChecked = true; break;
            }
        }
        finally
        {
            _suppressModeEvents = false;
        }
    }

    private void SetMode(ViewMode mode)
    {
        // Keyboard shortcut path: flips the radio, which raises Mode_Checked.
        switch (mode)
        {
            case ViewMode.Facing: ModeFacing.IsChecked = true; break;
            case ViewMode.Continuous: ModeContinuous.IsChecked = true; break;
            default: ModeSingle.IsChecked = true; break;
        }
    }

    // ---------- Navigation ----------

    private async void Prev_Click(object sender, RoutedEventArgs e) => await StepAsync(-1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await StepAsync(+1);

    private async Task StepAsync(int direction)
    {
        if (_doc is null || _left is null) return;

        if (_mode == ViewMode.Continuous)
        {
            var result = await _left.PageTurnAsync(direction);
            if (result is "scrolled") { ScheduleSave(); return; }
            // A frame that failed to load can stall the scroll; jump the spine instead.
            if (direction > 0 && _spineIndex < _doc.SpineCount - 1)
                await GoToSpineAsync(_spineIndex + 1);
            else if (direction < 0 && _spineIndex > 0)
                await GoToSpineAsync(_spineIndex - 1);
            return;
        }

        if (_mode == ViewMode.Single)
        {
            var result = await _left.PageTurnAsync(direction);
            if (result is "scrolled")
            {
                _scrollFraction = await _left.GetScrollFractionAsync();
                ScheduleSave();
                UpdateProgress();
                return;
            }
        }

        if (_mode == ViewMode.Facing)
        {
            int start = FacingGroupStart(_spineIndex);
            int target = direction > 0
                ? (start == 0 ? 1 : start + 2)
                : (start <= 1 ? 0 : start - 2);
            await GoToSpineAsync(target);
            return;
        }

        await GoToSpineAsync(_spineIndex + direction);
    }

    private async Task GoToSpineAsync(int spine, string? anchor = null, double scroll = 0, bool syncChapters = true)
    {
        if (_doc is null) return;
        spine = Math.Clamp(spine, 0, _doc.SpineCount - 1);
        bool restore = scroll > 0.001 || anchor is not null;
        await RequestViewAsync(new ViewRequest(_mode, spine, anchor, scroll, restore));
        if (syncChapters) SyncChapterSelection();
        ScheduleSave();
        UpdateProgress();
    }

    private async void PageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && int.TryParse(PageBox.Text, out int p))
        {
            await GoToSpineAsync(p - 1);
            e.Handled = true;
        }
    }

    private void UpdateProgress()
    {
        if (_doc is null || _doc.SpineCount == 0)
        {
            ProgressText.Text = "";
            return;
        }

        double book = (_spineIndex + _scrollFraction) / _doc.SpineCount * 100.0;
        ProgressText.Text = string.Format(Strings.Get("ProgressFormat"), Math.Clamp((int)Math.Round(book), 0, 100));
    }

    // ---------- Chapters ----------

    private void ChapterToggle_Changed(object sender, RoutedEventArgs e) =>
        SetChapterPaneVisible(ChapterToggle.IsChecked == true);

    private void ChapterClose_Click(object sender, RoutedEventArgs e) => SetChapterPaneVisible(false);

    private void SetChapterPaneVisible(bool visible)
    {
        if (_chapterPaneVisible == visible) return;
        _chapterPaneVisible = visible;
        if (ChapterToggle.IsChecked != visible)
            ChapterToggle.IsChecked = visible;

        if (visible)
        {
            ChapterColumn.MinWidth = 180;
            ChapterColumn.Width = _chapterPaneWidth;
            ChapterPane.Visibility = Visibility.Visible;
            ChapterSplitter.Visibility = Visibility.Visible;
            ShowChapterState(_chapterState);
        }
        else
        {
            bool hadFocus = ChapterPane.IsKeyboardFocusWithin;
            if (ChapterColumn.ActualWidth > 0)
                _chapterPaneWidth = new GridLength(ChapterColumn.ActualWidth);
            ChapterColumn.MinWidth = 0;
            ChapterColumn.Width = new GridLength(0);
            ChapterPane.Visibility = Visibility.Collapsed;
            ChapterSplitter.Visibility = Visibility.Collapsed;
            if (hadFocus)
                WebLeft.Focus();
        }

        WriteAutomationState();
    }

    private static List<ChapterItem> FlattenNavigable(List<ChapterItem> roots)
    {
        var list = new List<ChapterItem>();
        var stack = new Stack<ChapterItem>();
        for (int i = roots.Count - 1; i >= 0; i--)
            stack.Push(roots[i]);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.SpineIndex.HasValue)
                list.Add(node);
            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }

        list.Sort((a, b) =>
        {
            int bySpine = a.SpineIndex!.Value.CompareTo(b.SpineIndex!.Value);
            if (bySpine != 0) return bySpine;
            int byDepth = a.Depth.CompareTo(b.Depth);
            return byDepth != 0 ? byDepth : a.SourceOrder.CompareTo(b.SourceOrder);
        });
        return list;
    }

    private void ShowChapterState(ChapterLoadState state)
    {
        ChapterTree.Visibility = state == ChapterLoadState.Loaded ? Visibility.Visible : Visibility.Collapsed;
        ChapterLoadingText.Visibility = state == ChapterLoadState.Loading ? Visibility.Visible : Visibility.Collapsed;
        ChapterEmptyText.Visibility = state == ChapterLoadState.Empty ? Visibility.Visible : Visibility.Collapsed;
        ChapterFailedText.Visibility = state == ChapterLoadState.Failed ? Visibility.Visible : Visibility.Collapsed;
    }

    private int GetChapterSyncSpine()
    {
        if (_mode != ViewMode.Facing || _doc is null)
            return _spineIndex;
        int start = FacingGroupStart(_spineIndex);
        if (start != 0 && start + 1 < _doc.SpineCount)
            return start + 1;
        return start;
    }

    private void SyncChapterSelection()
    {
        if (_doc is null || _navigableChapters.Count == 0) return;
        int sync = GetChapterSyncSpine();

        int lo = 0, hi = _navigableChapters.Count - 1, best = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_navigableChapters[mid].SpineIndex!.Value <= sync) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        if (best < 0)
        {
            ClearChapterSelection();
            return;
        }

        int bestSpine = _navigableChapters[best].SpineIndex!.Value;
        if (_selectedChapter?.SpineIndex == bestSpine)
        {
            if (!_selectedChapter.IsSelected)
                _selectedChapter.IsSelected = true;
            return;
        }

        var active = _navigableChapters[best];
        if (ReferenceEquals(active, _selectedChapter)) return;

        _suppressChapterNav = true;
        try
        {
            if (_selectedChapter is not null)
                _selectedChapter.IsSelected = false;
            _selectedChapter = active;
            active.IsSelected = true;
            for (var p = active.Parent; p is not null; p = p.Parent)
                p.IsExpanded = true;
        }
        finally
        {
            _suppressChapterNav = false;
        }
    }

    private void ClearChapterSelection()
    {
        if (_selectedChapter is null) return;
        _suppressChapterNav = true;
        try
        {
            _selectedChapter.IsSelected = false;
            _selectedChapter = null;
        }
        finally
        {
            _suppressChapterNav = false;
        }
    }

    private async void ChapterTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressChapterNav) return;
        if (e.NewValue is not ChapterItem item) return;

        // The virtualized tree re-raises selection for the already-current
        // chapter when its container realizes (e.g. on opening the pane).
        // Navigating then would yank the reader away from its position.
        if (ReferenceEquals(item, _selectedChapter)) return;

        _suppressChapterNav = true;
        try
        {
            if (_selectedChapter is not null)
                _selectedChapter.IsSelected = false;
            _selectedChapter = item;
        }
        finally
        {
            _suppressChapterNav = false;
        }

        if (item.SpineIndex is int spine)
            await GoToSpineAsync(spine, item.Anchor, syncChapters: false);
    }

    // ---------- Typography ----------

    private void FontIn_Click(object sender, RoutedEventArgs e) => ChangeFont(1.1);
    private void FontOut_Click(object sender, RoutedEventArgs e) => ChangeFont(1 / 1.1);

    private void ChangeFont(double factor)
    {
        _display.FontScale = Math.Clamp(_display.FontScale * factor, 0.7, 2.5);
        PushDisplay();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _display.Theme = _display.Theme switch
        {
            ReadingTheme.Light => ReadingTheme.Sepia,
            ReadingTheme.Sepia => ReadingTheme.Dark,
            _ => ReadingTheme.Light
        };
        PushDisplay();
    }

    private void PushDisplay()
    {
        _left?.SetDisplaySettings(_display);
        _right?.SetDisplaySettings(_display);
        ScheduleSave();
        WriteAutomationState();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MakeMenu(Strings.Get("FontPublisher"), () => { _display.FontFamily = ReaderFontFamily.Publisher; PushDisplay(); }));
        menu.Items.Add(MakeMenu(Strings.Get("FontSerif"), () => { _display.FontFamily = ReaderFontFamily.Serif; PushDisplay(); }));
        menu.Items.Add(MakeMenu(Strings.Get("FontSans"), () => { _display.FontFamily = ReaderFontFamily.Sans; PushDisplay(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenu(Strings.Get("ThemeLight"), () => { _display.Theme = ReadingTheme.Light; PushDisplay(); }));
        menu.Items.Add(MakeMenu(Strings.Get("ThemeSepia"), () => { _display.Theme = ReadingTheme.Sepia; PushDisplay(); }));
        menu.Items.Add(MakeMenu(Strings.Get("ThemeDark"), () => { _display.Theme = ReadingTheme.Dark; PushDisplay(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenu(Strings.Get("LineSpacing") + " +", () => { _display.LineHeight = Math.Clamp(_display.LineHeight + 0.1, 1.1, 2.4); PushDisplay(); }));
        menu.Items.Add(MakeMenu(Strings.Get("LineSpacing") + " −", () => { _display.LineHeight = Math.Clamp(_display.LineHeight - 0.1, 1.1, 2.4); PushDisplay(); }));
        menu.Items.Add(MakeMenu(Strings.Get("Margins") + " +", () => { _display.MarginEm = Math.Clamp(_display.MarginEm + 0.2, 0.4, 3.0); PushDisplay(); }));
        menu.Items.Add(MakeMenu(Strings.Get("Margins") + " −", () => { _display.MarginEm = Math.Clamp(_display.MarginEm - 0.2, 0.4, 3.0); PushDisplay(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenu(Strings.Get("ResetText"), () => { _display.ResetTypography(); PushDisplay(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenu(Strings.Get("About"), ShowAboutWindow));
        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
    }

    private void ShowAboutWindow()
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private static MenuItem MakeMenu(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    // ---------- Search ----------

    private void SearchToggle_Changed(object sender, RoutedEventArgs e)
    {
        bool on = SearchToggle.IsChecked == true;
        bool hadFocus = SearchBar.IsKeyboardFocusWithin;
        SearchBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
        else if (hadFocus)
        {
            // Return focus to the content instead of letting WPF drop it on an
            // arbitrary toolbar button.
            WebLeft.Focus();
        }
    }

    private void CloseSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchToggle.IsChecked = false;
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await RunSearchAsync(forward: !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchToggle.IsChecked = false;
            e.Handled = true;
        }
    }

    private async void FindNext_Click(object sender, RoutedEventArgs e) => await RunSearchAsync(true);
    private async void FindPrev_Click(object sender, RoutedEventArgs e) => await RunSearchAsync(false);

    private async Task RunSearchAsync(bool forward)
    {
        if (_doc is null || _left is null || _right is null) return;
        var q = SearchBox.Text?.Trim() ?? "";
        if (q.Length == 0) return;

        await WhenNavIdleAsync();

        // Prefer in-page find first on the currently displayed chapter panes.
        // The continuous view keeps chapters in child frames, which in-page find
        // cannot reach, so it goes straight to the book index below.
        if (_mode != ViewMode.Continuous)
        {
            if (await _left.FindAsync(q, forward))
            {
                SearchStatus.Text = "";
                WriteAutomationState();
                return;
            }
            if (_mode == ViewMode.Facing && WebRight.Visibility == Visibility.Visible &&
                await _right.FindAsync(q, forward))
            {
                SearchStatus.Text = "";
                WriteAutomationState();
                return;
            }
        }

        if (_searchHits.Count == 0 || !string.Equals(_searchHitsQuery, q, StringComparison.Ordinal))
        {
            _searchHits = _doc.Search(q).ToList();
            _searchHitsQuery = q;
            _searchHitIndex = -1;
        }

        if (_searchHits.Count == 0)
        {
            SearchStatus.Text = Strings.Get("NoSearchResults");
            WriteAutomationState();
            return;
        }

        SearchStatus.Text = string.Format(Strings.Get("SearchResultsFormat"), _searchHits.Count);
        if (forward)
            _searchHitIndex = (_searchHitIndex + 1) % _searchHits.Count;
        else
            _searchHitIndex = _searchHitIndex <= 0 ? _searchHits.Count - 1 : _searchHitIndex - 1;

        var hit = _searchHits[_searchHitIndex];

        if (_mode == ViewMode.Continuous)
        {
            // Highlight inside the chapter frame; the frame is loaded on demand.
            var found = await _left.FindInSpineAsync(hit.SpineIndex, q);
            if (!found)
                await _left.ContinuousGoToAsync(hit.SpineIndex, 0);
            _spineIndex = hit.SpineIndex;
            PageBox.Text = (_spineIndex + 1).ToString();
            SyncChapterSelection();
            ScheduleSave();
            UpdateProgress();
        }
        else
        {
            await GoToSpineAsync(hit.SpineIndex);
            // In facing mode the hit may sit in the right-hand pane.
            var host = _mode == ViewMode.Facing && hit.SpineIndex != _spineIndex ? _right : _left;
            await host.FindAsync(q, true);
        }

        WriteAutomationState();
    }

    // ---------- Bookmarks ----------

    private async Task ToggleBookmarkAsync()
    {
        if (_doc is null || _bookState is null || _left is null) return;

        if (_mode == ViewMode.Continuous)
        {
            var (spine, fraction) = await _left.GetSpinePosAsync();
            _spineIndex = Math.Clamp(spine, 0, Math.Max(0, _doc.SpineCount - 1));
            _scrollFraction = fraction;
        }
        else
        {
            _scrollFraction = await _left.GetScrollFractionAsync();
        }

        var existing = _bookState.Bookmarks.FindIndex(b =>
            b.SpineIndex == _spineIndex && Math.Abs(b.ScrollFraction - _scrollFraction) < 0.05);
        if (existing >= 0)
        {
            _bookState.Bookmarks.RemoveAt(existing);
        }
        else
        {
            _bookState.Bookmarks.Add(new BookmarkEntry
            {
                SpineIndex = _spineIndex,
                ScrollFraction = _scrollFraction,
                Label = $"{_spineIndex + 1} — {Math.Round(_scrollFraction * 100)}%"
            });
        }

        RefreshBookmarksUi();
        PersistBookState();
    }

    private void RefreshBookmarksUi()
    {
        BookmarkList.Items.Clear();
        if (_bookState is null || _bookState.Bookmarks.Count == 0)
        {
            BookmarkList.Items.Add(Strings.Get("NoBookmarks"));
            return;
        }

        foreach (var b in _bookState.Bookmarks.OrderBy(x => x.SpineIndex).ThenBy(x => x.ScrollFraction))
            BookmarkList.Items.Add(b);
    }

    private async void BookmarkList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BookmarkList.SelectedItem is BookmarkEntry b)
            await GoToSpineAsync(b.SpineIndex, b.Anchor, b.ScrollFraction);
    }

    // ---------- Persist ----------

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void PersistBookState()
    {
        if (_doc is null) return;
        _bookState ??= new BookState { BookId = _doc.BookId };
        _bookState.BookId = _doc.BookId;
        _bookState.FilePath = _doc.FilePath;
        _bookState.SpineIndex = _spineIndex;
        _bookState.ScrollFraction = _scrollFraction;
        _bookState.Display = _display.Clone();
        BookStateStore.SaveBook(_bookState);
    }

    private void OnHostMessage(ReadingHost host, HostMessage msg)
    {
        switch (msg.Type)
        {
            case "scroll":
                // Only the primary pane's position is the reading position; the
                // facing right pane must not overwrite it, and the continuous
                // view reports its logical position through "spinepos" instead.
                if (ReferenceEquals(host, _left) && _mode != ViewMode.Continuous)
                {
                    _scrollFraction = msg.Fraction;
                    UpdateProgress();
                    ScheduleSave();
                }
                break;

            case "spinepos":
                if (ReferenceEquals(host, _left) && _mode == ViewMode.Continuous && _doc is not null)
                {
                    int spine = Math.Clamp(msg.Spine, 0, Math.Max(0, _doc.SpineCount - 1));
                    if (spine != _spineIndex)
                    {
                        _spineIndex = spine;
                        PageBox.Text = (spine + 1).ToString();
                        SyncChapterSelection();
                    }
                    _scrollFraction = msg.Fraction;
                    UpdateProgress();
                    ScheduleSave();
                }
                break;

            case "ready":
                host.SetDisplaySettings(_display);
                break;

            case "step":
                // Clicks and keys inside the WebView both arrive here, so they
                // share StepAsync's scroll-then-advance behaviour.
                _ = StepAsync(msg.Direction);
                break;

            case "key":
                // App shortcuts pressed while the reading pane holds focus.
                switch (msg.Key)
                {
                    case "1": SetMode(ViewMode.Single); break;
                    case "2": SetMode(ViewMode.Facing); break;
                    case "3": SetMode(ViewMode.Continuous); break;
                    case "F4": SetChapterPaneVisible(!_chapterPaneVisible); break;
                    case "F11": ToggleFullscreen(); break;
                    case "Escape":
                        if (_fullscreen) ToggleFullscreen();
                        else if (SearchToggle.IsChecked == true) SearchToggle.IsChecked = false;
                        break;
                }
                break;
        }
    }

    // ---------- Print / fullscreen ----------

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_left is null || _doc is null) return;
        try
        {
            await _left.PrintAsync();
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            Strings.ShowError(this, ex.Message);
        }
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _preFsChapterVisible = _chapterPaneVisible;
            if (_chapterPaneVisible)
                SetChapterPaneVisible(false);
            _preFsState = WindowState;
            _preFsStyle = WindowStyle;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
            ToolbarHost.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;
            SearchBar.Visibility = Visibility.Collapsed;
            _fullscreen = true;
        }
        else
        {
            WindowStyle = _preFsStyle;
            WindowState = _preFsState;
            ToolbarHost.Visibility = Visibility.Visible;
            StatusBar.Visibility = Visibility.Visible;
            if (SearchToggle.IsChecked == true)
                SearchBar.Visibility = Visibility.Visible;
            _fullscreen = false;
            if (_preFsChapterVisible)
                SetChapterPaneVisible(true);
        }

        WriteAutomationState();
    }

    // ---------- Automation ----------

    /// <summary>
    /// With --statefile=&lt;path&gt; on the command line, mirrors the UI state to a
    /// small JSON file so scripted captures can wait for real readiness instead
    /// of sleeping. Inert in normal use.
    /// </summary>
    private void WriteAutomationState(bool? navIdleOverride = null)
    {
        var file = App.AutomationStateFile;
        if (file is null) return;
        try
        {
            var state = new
            {
                book = _doc?.Title,
                mode = _mode.ToString(),
                spine = _spineIndex,
                spineCount = _doc?.SpineCount ?? 0,
                theme = _display.Theme.ToString(),
                fontScale = _display.FontScale,
                navIdle = navIdleOverride ?? (_viewPump is null || _viewPump.IsCompleted),
                searchStatus = SearchStatus.Text,
                fullscreen = _fullscreen,
                chapterPane = _chapterPaneVisible,
                timestamp = DateTime.UtcNow.ToString("O")
            };
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(state));
        }
        catch
        {
            // Automation mirroring must never affect the app.
        }
    }

    // ---------- Keys ----------

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (SearchBox.IsKeyboardFocusWithin && e.Key is not Key.Escape and not Key.Enter)
            return;

        if (ReferenceEquals(e.OriginalSource, PageBox) && e.Key is Key.D1 or Key.D2 or Key.D3
            or Key.Left or Key.Right or Key.Home or Key.End)
            return;

        if (ChapterTree.IsKeyboardFocusWithin && e.Key is Key.Left or Key.Right
            or Key.Home or Key.End or Key.PageUp or Key.PageDown)
            return;

        switch (e.Key)
        {
            case Key.O when ctrl:
                await ShowOpenDialogAsync();
                break;
            case Key.P when ctrl:
                Print_Click(this, new RoutedEventArgs());
                break;
            case Key.F when ctrl:
                SearchToggle.IsChecked = true;
                break;
            case Key.F4:
                SetChapterPaneVisible(!_chapterPaneVisible);
                break;
            case Key.F11:
                ToggleFullscreen();
                break;
            case Key.Escape when _fullscreen:
                ToggleFullscreen();
                break;
            case Key.Escape when SearchToggle.IsChecked == true:
                SearchToggle.IsChecked = false;
                break;

            case Key.D1 when !ctrl: SetMode(ViewMode.Single); break;
            case Key.D2 when !ctrl: SetMode(ViewMode.Facing); break;
            case Key.D3 when !ctrl: SetMode(ViewMode.Continuous); break;

            case Key.OemPlus when ctrl:
            case Key.Add when ctrl: ChangeFont(1.1); break;
            case Key.OemMinus when ctrl:
            case Key.Subtract when ctrl: ChangeFont(1 / 1.1); break;
            case Key.D0 when ctrl:
            case Key.NumPad0 when ctrl:
                _display.ResetTypography();
                PushDisplay();
                break;

            case Key.B when !ctrl && !SearchBox.IsKeyboardFocusWithin:
                await ToggleBookmarkAsync();
                break;

            case Key.PageDown:
            case Key.Right:
                await StepAsync(+1);
                break;
            case Key.PageUp:
            case Key.Left:
                await StepAsync(-1);
                break;
            // Space mirrors the in-page handler for when the toolbar, not the
            // reading pane, holds focus. Skipped while a button is focused so
            // it still activates that button instead.
            case Key.Space when !ctrl
                && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.ButtonBase:
                await StepAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : +1);
                break;
            case Key.Home:
                await GoToSpineAsync(0);
                break;
            case Key.End:
                await GoToSpineAsync(_doc?.SpineCount - 1 ?? 0);
                break;

            default:
                return;
        }

        e.Handled = true;
    }
}

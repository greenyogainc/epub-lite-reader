using System.IO;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>BookStateStore keeps its target directory in a static field (RootOverride),
/// so tests that redirect it must never run concurrently with each other.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BookStateStoreCollection
{
    public const string Name = "BookStateStore";
}

[Collection(BookStateStoreCollection.Name)]
public sealed class BookStateStoreTests : IDisposable
{
    private readonly string _tempRoot;

    public BookStateStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "elr-bookstate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        BookStateStore.RootOverride = _tempRoot;
    }

    public void Dispose()
    {
        BookStateStore.RootOverride = null;
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    private string BooksDir => Path.Combine(_tempRoot, "books");

    [Fact]
    public void SaveBook_ThenLoadBook_RoundTripsState()
    {
        var state = new BookState
        {
            BookId = "book-1",
            FilePath = @"C:\books\sample.epub",
            SpineIndex = 3,
            ScrollFraction = 0.42,
            Display = new DisplaySettings { Theme = ReadingTheme.Sepia, FontScale = 1.25, ViewMode = ViewMode.Facing },
            Bookmarks = new List<BookmarkEntry>
            {
                new() { SpineIndex = 1, ScrollFraction = 0.1, Anchor = "a1", Label = "Bookmark A" },
                new() { SpineIndex = 2, ScrollFraction = 0.2, Anchor = null, Label = "Bookmark B" },
            }
        };

        BookStateStore.SaveBook(state);
        var loaded = BookStateStore.LoadBook("book-1");

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.SpineIndex);
        Assert.Equal(0.42, loaded.ScrollFraction);
        Assert.Equal(ReadingTheme.Sepia, loaded.Display.Theme);
        Assert.Equal(1.25, loaded.Display.FontScale);
        Assert.Equal(ViewMode.Facing, loaded.Display.ViewMode);
        Assert.Equal(2, loaded.Bookmarks.Count);
        Assert.Equal("Bookmark A", loaded.Bookmarks[0].Label);
        Assert.Equal("a1", loaded.Bookmarks[0].Anchor);
        Assert.Null(loaded.Bookmarks[1].Anchor);
    }

    [Fact]
    public void SaveAppSettings_ThenLoadAppSettings_RoundTrips()
    {
        var settings = new AppSettings
        {
            Defaults = new DisplaySettings { Theme = ReadingTheme.Dark, ViewMode = ViewMode.Continuous, MarginEm = 2.0 },
            WindowWidth = 1024,
            WindowHeight = 768
        };

        BookStateStore.SaveAppSettings(settings);
        var loaded = BookStateStore.LoadAppSettings();

        Assert.Equal(ReadingTheme.Dark, loaded.Defaults.Theme);
        Assert.Equal(ViewMode.Continuous, loaded.Defaults.ViewMode);
        Assert.Equal(2.0, loaded.Defaults.MarginEm);
        Assert.Equal(1024, loaded.WindowWidth);
        Assert.Equal(768, loaded.WindowHeight);
    }

    [Fact]
    public void SaveBook_LeavesNoTmpFilesBehind()
    {
        BookStateStore.SaveBook(new BookState { BookId = "book-2" });

        var tmpFiles = Directory.Exists(BooksDir) ? Directory.GetFiles(BooksDir, "*.tmp") : Array.Empty<string>();
        Assert.Empty(tmpFiles);
    }

    [Fact]
    public void LoadBook_WithCorruptJson_ReturnsNullWithoutThrowing()
    {
        Directory.CreateDirectory(BooksDir);
        File.WriteAllText(Path.Combine(BooksDir, "corrupt.json"), "{ not valid json ][");

        var loaded = BookStateStore.LoadBook("corrupt");

        Assert.Null(loaded);
    }

    [Fact]
    public void LoadAppSettings_WithCorruptJson_ReturnsDefaults()
    {
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(Path.Combine(_tempRoot, "settings.json"), "{ not valid json ][");

        var loaded = BookStateStore.LoadAppSettings();

        Assert.NotNull(loaded);
        Assert.Equal(ReadingTheme.Light, loaded.Defaults.Theme);
        Assert.Null(loaded.WindowWidth);
        Assert.Null(loaded.WindowHeight);
    }

    [Fact]
    public void LoadBook_WithOrphanedInterruptedTmpFile_StillReturnsIntactState()
    {
        BookStateStore.SaveBook(new BookState { BookId = "book-3", SpineIndex = 5 });

        var orphan = Path.Combine(BooksDir, "book-3.json.deadbeef.tmp");
        File.WriteAllText(orphan, "{ garbage, not json");

        var loaded = BookStateStore.LoadBook("book-3");

        Assert.NotNull(loaded);
        Assert.Equal(5, loaded!.SpineIndex);
    }

    [Fact]
    public void SaveBook_DeletesStaleOrphanTmpFile_ButKeepsFreshOne()
    {
        Directory.CreateDirectory(BooksDir);

        var staleOrphan = Path.Combine(BooksDir, "other.json.staleorphan.tmp");
        File.WriteAllText(staleOrphan, "stale");
        File.SetLastWriteTimeUtc(staleOrphan, DateTime.UtcNow.AddHours(-1));

        var freshOrphan = Path.Combine(BooksDir, "other.json.freshorphan.tmp");
        File.WriteAllText(freshOrphan, "fresh");
        File.SetLastWriteTimeUtc(freshOrphan, DateTime.UtcNow);

        BookStateStore.SaveBook(new BookState { BookId = "book-4" });

        Assert.False(File.Exists(staleOrphan));
        Assert.True(File.Exists(freshOrphan));
    }

    [Fact]
    public void WriteAtomic_OverwritesExistingFileContentCompletely()
    {
        var path = Path.Combine(_tempRoot, "atomic-test.txt");
        BookStateStore.WriteAtomic(path, "this is a much longer initial payload than the next write");
        BookStateStore.WriteAtomic(path, "short");

        Assert.Equal("short", File.ReadAllText(path));
    }
}

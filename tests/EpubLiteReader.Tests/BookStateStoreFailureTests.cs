using System.IO;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>The failure paths of BookStateStore: every save and load is best effort and
/// must never throw into the reader, and must never leave temp litter behind. Failures are
/// logged, so the log is redirected to a throwaway file as well as the store itself.</summary>
[Collection(BookStateStoreCollection.Name)]
public sealed class BookStateStoreFailureTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _log;
    private readonly string? _priorRoot;
    private readonly string? _priorLog;

    public BookStateStoreFailureTests()
    {
        _priorRoot = BookStateStore.RootOverride;
        _priorLog = App.LogPathOverride;
        _tempRoot = Path.Combine(Path.GetTempPath(), "elr-bookstate-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _log = Path.Combine(_tempRoot, "test.log");
        BookStateStore.RootOverride = Path.Combine(_tempRoot, "store");
        App.LogPathOverride = _log;
    }

    public void Dispose()
    {
        BookStateStore.RootOverride = _priorRoot;
        App.LogPathOverride = _priorLog;
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    private string Store => BookStateStore.RootDir;
    private string BooksDir => Path.Combine(Store, "books");

    private string LoggedText => File.Exists(_log) ? File.ReadAllText(_log) : "";

    private static IEnumerable<string> TempFiles(string dir) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.tmp") : Enumerable.Empty<string>();

    // ---------- the write cannot land ----------

    [Fact]
    public void SaveAppSettings_WhenTheTargetIsADirectory_DoesNotThrowAndLeavesNoTempFile()
    {
        var target = Path.Combine(Store, "settings.json");
        Directory.CreateDirectory(target);

        BookStateStore.SaveAppSettings(new AppSettings { WindowWidth = 800 });

        Assert.True(Directory.Exists(target));
        Assert.Empty(TempFiles(Store));
        Assert.NotEqual("", LoggedText); // the failure was logged, not thrown
        Assert.Null(BookStateStore.LoadAppSettings().WindowWidth);
    }

    [Fact]
    public void SaveBook_WhenTheTargetIsADirectory_DoesNotThrowAndLeavesNoTempFile()
    {
        var target = Path.Combine(BooksDir, "book-dir.json");
        Directory.CreateDirectory(target);

        BookStateStore.SaveBook(new BookState { BookId = "book-dir" });

        Assert.True(Directory.Exists(target));
        Assert.Empty(TempFiles(BooksDir));
        Assert.NotEqual("", LoggedText);
        Assert.Null(BookStateStore.LoadBook("book-dir"));
    }

    // ---------- stale temp files ----------

    [Fact]
    public void SaveBook_RemovesTempFilesOlderThanTenMinutesAndKeepsNewerOnes()
    {
        Directory.CreateDirectory(BooksDir);
        var older = Path.Combine(BooksDir, "x.json.older.tmp");
        var newer = Path.Combine(BooksDir, "x.json.newer.tmp");
        File.WriteAllText(older, "stale");
        File.WriteAllText(newer, "in flight");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-11));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddMinutes(-9));

        BookStateStore.SaveBook(new BookState { BookId = "book-tmp" });

        Assert.False(File.Exists(older));
        Assert.True(File.Exists(newer));
        Assert.NotNull(BookStateStore.LoadBook("book-tmp"));
    }

    [Fact]
    public void SaveBook_SkipsAStaleTempFileThatAnotherWriterStillHolds()
    {
        Directory.CreateDirectory(BooksDir);
        var held = Path.Combine(BooksDir, "x.json.held.tmp");
        File.WriteAllText(held, "being written");
        File.SetLastWriteTimeUtc(held, DateTime.UtcNow.AddHours(-1));

        using (new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            BookStateStore.SaveBook(new BookState { BookId = "book-held" });
        }

        Assert.True(File.Exists(held));
        Assert.NotNull(BookStateStore.LoadBook("book-held"));
    }

    [Fact]
    public void SaveBook_InADirectoryThatCannotBeListed_StillSaves()
    {
        Directory.CreateDirectory(BooksDir);

        using (DenyAccess.ListingDirectory(BooksDir))
        {
            // Sweeping stale temp files needs to list the folder; the save must not depend on it.
            BookStateStore.SaveBook(new BookState { BookId = "book-acl", SpineIndex = 4 });
        }

        var loaded = BookStateStore.LoadBook("book-acl");
        Assert.NotNull(loaded);
        Assert.Equal(4, loaded!.SpineIndex);
    }

    // ---------- loads ----------

    [Fact]
    public void LoadAppSettings_WithoutASettingsFile_ReturnsDefaults()
    {
        var loaded = BookStateStore.LoadAppSettings();

        Assert.Equal(ReadingTheme.Light, loaded.Defaults.Theme);
        Assert.Null(loaded.WindowWidth);
        Assert.Equal("", LoggedText); // a missing file is normal, not a failure
    }

    [Fact]
    public void LoadAppSettings_WithInvalidJson_ReturnsDefaultsAndLogsIt()
    {
        Directory.CreateDirectory(Store);
        File.WriteAllText(Path.Combine(Store, "settings.json"), "{ not valid json ][");

        var loaded = BookStateStore.LoadAppSettings();

        Assert.Equal(ReadingTheme.Light, loaded.Defaults.Theme);
        Assert.Contains("JsonException", LoggedText);
    }

    [Fact]
    public void LoadAppSettings_WithAJsonNullDocument_ReturnsDefaults()
    {
        Directory.CreateDirectory(Store);
        File.WriteAllText(Path.Combine(Store, "settings.json"), "null");

        var loaded = BookStateStore.LoadAppSettings();

        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Defaults);
        Assert.Null(loaded.WindowHeight);
    }

    [Fact]
    public void LoadBook_WithAJsonNullDocument_ReturnsNull()
    {
        Directory.CreateDirectory(BooksDir);
        File.WriteAllText(Path.Combine(BooksDir, "nothing.json"), "null");

        Assert.Null(BookStateStore.LoadBook("nothing"));
    }

    [Fact]
    public void LoadBook_ThatWasNeverSaved_ReturnsNull() =>
        Assert.Null(BookStateStore.LoadBook("never-saved"));

    [Fact]
    public void BookmarkEntry_ToString_IsTheLabel() =>
        Assert.Equal("Chapter 3 start", new BookmarkEntry { Label = "Chapter 3 start" }.ToString());
}

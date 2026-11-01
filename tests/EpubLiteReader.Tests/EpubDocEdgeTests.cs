using System.IO;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>Edge behaviour of EpubDoc that the happy-path open tests do not reach: skipped
/// and colliding entries, failed opens, URL building, metadata fallbacks, nested navigation,
/// and how Dispose and the sweep cope with a root the OS will not let go of.
/// Serial (the BookStateStore collection) because several tests compare the shared
/// extract base directory before and after, and one sweeps it.</summary>
[Collection(BookStateStoreCollection.Name)]
public sealed class EpubDocEdgeTests : IDisposable
{
    private static readonly byte[] SomeBytes = { 1, 2, 3, 4 };

    private readonly string _dir;
    private readonly List<EpubDoc> _docs = new();

    public EpubDocEdgeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "elr-docedge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        foreach (var doc in _docs) doc.Dispose();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private async Task<(EpubDoc Doc, List<ChapterItem> Chapters)> OpenAsync(
        MiniEpub book, string fileName = "book.epub", string? sectionFormat = null)
    {
        var path = book.Save(Path.Combine(_dir, fileName));
        var opened = await EpubDoc.OpenWithChaptersAsync(path, "Untitled", sectionFormat);
        _docs.Add(opened.Doc);
        return opened;
    }

    private static string[] SnapshotOfExtractBase() =>
        Directory.Exists(EpubDoc.ExtractBaseDir)
            ? Directory.EnumerateFileSystemEntries(EpubDoc.ExtractBaseDir).OrderBy(p => p, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

    private static string LockOf(EpubDoc doc) => doc.ExtractRoot + ".lock";

    // ---------- skipped entries ----------

    [Fact]
    public async Task ManifestItemsThatCollideCaseInsensitively_OnlyTheFirstIsExtracted()
    {
        var book = new MiniEpub();
        book.Extras.Add(("Image.png", "image/png", SomeBytes));
        book.Extras.Add(("image.png", "image/png", SomeBytes));

        var (doc, _) = await OpenAsync(book);

        Assert.Equal(new[] { "OEBPS/image.png" }, doc.SkippedEntries);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(doc.ExtractRoot, "OEBPS"), "image.png", SearchOption.TopDirectoryOnly));
    }

    [Theory]
    [InlineData("aux.txt")]   // reserved device name
    [InlineData("con.png")]
    public async Task EntryWithAReservedDeviceName_IsSkipped(string name)
    {
        var book = new MiniEpub();
        book.Extras.Add((name, "application/octet-stream", SomeBytes));

        var (doc, _) = await OpenAsync(book);

        Assert.Equal(new[] { "OEBPS/" + name }, doc.SkippedEntries);
        Assert.False(File.Exists(Path.Combine(doc.ExtractRoot, "OEBPS", name)));
    }

    [Fact]
    public async Task EntryThatCollidesWithADirectory_IsRecordedAndTheBookStillOpens()
    {
        var book = new MiniEpub();
        book.Extras.Add(("a/b.txt", "application/octet-stream", SomeBytes));
        book.Extras.Add(("a", "application/octet-stream", SomeBytes)); // "a" is now a directory

        var (doc, _) = await OpenAsync(book);

        Assert.Equal(new[] { "OEBPS/a" }, doc.SkippedEntries);
        Assert.True(Directory.Exists(Path.Combine(doc.ExtractRoot, "OEBPS", "a")));
        Assert.Equal(2, doc.SpineCount);
    }

    // ---------- failed opens ----------

    [Fact]
    public async Task BadSectionTitleFormat_FailsTheOpenAndLeavesNoRootOrLockBehind()
    {
        // Chapter two has no navigation entry, so it needs the (invalid) numbered fallback.
        var book = new MiniEpub();
        var before = SnapshotOfExtractBase();

        await Assert.ThrowsAsync<FormatException>(() => OpenAsync(book, sectionFormat: "{x}"));

        Assert.Equal(before, SnapshotOfExtractBase());
    }

    // ---------- URLs ----------

    [Fact]
    public async Task GetSpineUrl_BuildsTheVirtualHostUrlWithAnOptionalAnchor()
    {
        var (doc, _) = await OpenAsync(new MiniEpub());

        Assert.Equal("https://epub.local/OEBPS/c2.xhtml", doc.GetSpineUrl(1));
        Assert.Equal("https://epub.local/OEBPS/c2.xhtml#sec-1", doc.GetSpineUrl(1, "sec-1"));
        Assert.Equal("https://epub.local/OEBPS/c2.xhtml", doc.GetSpineUrl(1, ""));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task GetSpineUrl_OutOfRange_Throws(int index)
    {
        var (doc, _) = await OpenAsync(new MiniEpub());

        Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetSpineUrl(index));
    }

    [Theory]
    [InlineData(0, "https://epub.local/elr-continuous.html#spine-0")]
    [InlineData(1, "https://epub.local/elr-continuous.html#spine-1")]
    [InlineData(-1, "https://epub.local/elr-continuous.html")]
    [InlineData(2, "https://epub.local/elr-continuous.html")]
    public async Task GetContinuousUrl_AddsASpineHashOnlyForARealSpineItem(int index, string expected)
    {
        var (doc, _) = await OpenAsync(new MiniEpub());

        Assert.Equal(expected, doc.GetContinuousUrl(index));
    }

    // ---------- search ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_WithABlankQuery_FindsNothing(string query)
    {
        var (doc, _) = await OpenAsync(new MiniEpub());

        Assert.Empty(doc.Search(query));
    }

    [Fact]
    public async Task Search_StopsAtMaxResults()
    {
        var (doc, _) = await OpenAsync(new MiniEpub());

        Assert.Equal(2, doc.Search("text").Count());
        Assert.Single(doc.Search("text", maxResults: 1));
    }

    // ---------- metadata ----------

    [Fact]
    public async Task Metadata_IsReadFromThePackage()
    {
        var book = new MiniEpub { Title = "A Real Title", Author = "Ada Lovelace", Description = "About the book." };

        var (doc, _) = await OpenAsync(book, "meta.epub");

        Assert.Equal("A Real Title", doc.Title);
        Assert.Equal("Ada Lovelace", doc.Author);
        Assert.Equal("About the book.", doc.Description);
        Assert.Equal(Path.Combine(_dir, "meta.epub"), doc.FilePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Metadata_BlankTitle_FallsBackToTheFileName(string title)
    {
        var (doc, _) = await OpenAsync(new MiniEpub { Title = title }, "untitled-book.epub");

        Assert.Equal("untitled-book.epub", doc.Title);
    }

    [Fact]
    public async Task Metadata_MissingAuthorAndDescription_AreEmptyAndNull()
    {
        var (doc, _) = await OpenAsync(new MiniEpub());

        Assert.Equal("", doc.Author);
        Assert.Null(doc.Description);
    }

    [Fact]
    public async Task BookId_IsStableForAPathAndDiffersBetweenPaths()
    {
        var book = new MiniEpub();
        var (first, _) = await OpenAsync(book, "one.epub");
        var (again, _) = await OpenAsync(book, "one.epub");
        var (elsewhere, _) = await OpenAsync(book, "two.epub");

        Assert.Matches("^[0-9a-f]{32}$", first.BookId);
        Assert.Equal(first.BookId, again.BookId);
        Assert.NotEqual(first.BookId, elsewhere.BookId);
    }

    // ---------- navigation ----------

    [Fact]
    public async Task NestedNavigation_PopulatesChildrenWithParentDepthAndAnchor()
    {
        var book = new MiniEpub
        {
            NavItems = "<li><a href=\"c1.xhtml\">Part</a><ol><li><a href=\"c2.xhtml#sec\">Child</a></li></ol></li>"
        };

        var (_, chapters) = await OpenAsync(book);

        var part = Assert.Single(chapters);
        var child = Assert.Single(part.Children);
        Assert.Equal("Child", child.Title);
        Assert.Same(part, child.Parent);
        Assert.Equal(1, child.Depth);
        Assert.Equal(1, child.SpineIndex);
        Assert.Equal("sec", child.Anchor);
        Assert.Equal(0, part.Depth);
        Assert.Null(part.Anchor);
    }

    // ---------- Dispose ----------

    [Fact]
    public async Task Dispose_RemovesTheRootAndItsLockFile()
    {
        var (doc, _) = await OpenAsync(new MiniEpub());
        var root = doc.ExtractRoot;
        Assert.True(File.Exists(LockOf(doc)));

        doc.Dispose();

        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(root + ".lock"));
    }

    [Fact]
    public async Task Dispose_Twice_IsANoOp()
    {
        var (doc, _) = await OpenAsync(new MiniEpub());
        var root = doc.ExtractRoot;
        doc.Dispose();

        doc.Dispose();

        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(root + ".lock"));
    }

    [Fact]
    public async Task Dispose_WhenTheLockFileCannotBeDeleted_StillSucceeds()
    {
        var (doc, _) = await OpenAsync(new MiniEpub());
        var lockPath = LockOf(doc);
        File.SetAttributes(lockPath, FileAttributes.ReadOnly);
        try
        {
            doc.Dispose(); // the delete of the read-only lock file fails; that is swallowed

            Assert.False(Directory.Exists(doc.ExtractRoot));
            Assert.True(File.Exists(lockPath));
        }
        finally
        {
            File.SetAttributes(lockPath, FileAttributes.Normal);
            File.Delete(lockPath);
        }
    }

    [Fact]
    public async Task Dispose_WithAFileInTheRootStillOpen_KeepsTheRootAndItsLockForTheNextSweep()
    {
        var (doc, _) = await OpenAsync(new MiniEpub());
        var root = doc.ExtractRoot;
        var lockPath = LockOf(doc);
        var held = Path.Combine(root, EpubDoc.ContinuousFileName);

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read)) // no FileShare.Delete
        {
            doc.Dispose();

            // The lock is released but its file stays, so the sweep recognizes the root as
            // abandoned at once instead of treating it as a legacy extract for a full day.
            Assert.True(Directory.Exists(root));
            Assert.True(File.Exists(lockPath));
        }

        // TimeSpan.MaxValue: only roots with an unheld lock file qualify, so nothing that
        // belongs to another running copy of the app (or a legacy root) is touched.
        var removed = EpubDoc.SweepOrphanedExtracts(EpubDoc.ExtractBaseDir, TimeSpan.MaxValue);

        Assert.True(removed >= 1);
        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(lockPath));
    }

    // ---------- TryMapToDisk ----------

    [Fact]
    public void TryMapToDisk_AcceptsAnOrdinaryEntry()
    {
        var root = Path.Combine(_dir, "root");

        Assert.True(EpubDoc.TryMapToDisk(root, "OEBPS\\ch1.xhtml", out var dest));
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "OEBPS", "ch1.xhtml"), dest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("C:/x.txt")]
    [InlineData("elr-continuous.html")]
    [InlineData("OEBPS/../x.txt")]
    [InlineData("OEBPS//x.txt")]
    [InlineData("OEBPS/x.txt.")]
    [InlineData("OEBPS/nul.txt")]
    [InlineData("a\0b.txt")] // a NUL gets past the segment checks and is rejected by the path APIs
    public void TryMapToDisk_RejectsUnsafeEntries(string entry)
    {
        Assert.False(EpubDoc.TryMapToDisk(Path.Combine(_dir, "root"), entry, out var dest));
        Assert.Equal("", dest);
    }

    [Fact]
    public void TryMapToDisk_RootWithATrailingSeparator_MapsNothing()
    {
        // Same quirk as BookResourceServer.TryResolvePath: containment appends its own separator.
        var root = Path.Combine(_dir, "root") + Path.DirectorySeparatorChar;

        Assert.False(EpubDoc.TryMapToDisk(root, "ch1.xhtml", out _));
    }

    // ---------- LooksLikeMarkup ----------

    [Theory]
    [InlineData(new byte[] { 0x3C, 0x61 }, true)]                                   // "<a"
    [InlineData(new byte[] { 0x20, 0x0A, 0x09, 0x3C }, true)]                       // whitespace then '<'
    [InlineData(new byte[] { 0x61, 0x3C }, false)]                                  // text first
    [InlineData(new byte[] { }, false)]
    [InlineData(new byte[] { 0x20, 0x0A, 0x0D }, false)]                            // whitespace only
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x3C, 0x61 }, true)]                 // UTF-8 BOM then '<'
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x20, 0x3C }, true)]                 // UTF-8 BOM, whitespace, '<'
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x61 }, false)]                      // UTF-8 BOM then text
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF }, false)]                            // UTF-8 BOM alone
    [InlineData(new byte[] { 0xFF, 0xFE, 0x3C, 0x00 }, true)]                       // UTF-16 LE '<'
    [InlineData(new byte[] { 0xFF, 0xFE, 0x20, 0x00, 0x3C, 0x00 }, true)]           // UTF-16 LE ' ' '<'
    [InlineData(new byte[] { 0xFF, 0xFE, 0x20, 0x00, 0x0A, 0x00 }, false)]          // UTF-16 LE whitespace only
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41, 0x01 }, false)]                      // UTF-16 LE non-ASCII unit
    [InlineData(new byte[] { 0xFF, 0xFE, 0x61, 0x00 }, false)]                      // UTF-16 LE 'a'
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, 0x3C }, true)]                       // UTF-16 BE '<'
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, 0x20, 0x00, 0x0A }, false)]          // UTF-16 BE whitespace only
    [InlineData(new byte[] { 0xFE, 0xFF, 0x01, 0x3C }, false)]                      // UTF-16 BE non-ASCII unit
    public void LooksLikeMarkup_SniffsTheFirstNonWhitespaceCodeUnit(byte[] content, bool expected) =>
        Assert.Equal(expected, EpubDoc.LooksLikeMarkup(content));
}

/// <summary>The failure paths of SweepOrphanedExtracts, on a private base directory.</summary>
public sealed class ExtractSweepFailureTests : IDisposable
{
    private readonly string _base;

    public ExtractSweepFailureTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "elr-sweepfail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_base);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_base)) Directory.Delete(_base, recursive: true); } catch { /* best effort */ }
    }

    private string MakeOrphan(string name)
    {
        var root = Path.Combine(_base, name);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "content.txt"), "x");
        File.WriteAllText(root + ".lock", "");
        return root;
    }

    [Fact]
    public void Sweep_OrphanRootWithAFileStillOpen_IsKeptWithItsLock()
    {
        var root = MakeOrphan("orphan");
        using (new FileStream(Path.Combine(root, "content.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var removed = EpubDoc.SweepOrphanedExtracts(_base, EpubDoc.LegacyExtractMaxAge);

            Assert.Equal(0, removed);
            Assert.True(Directory.Exists(root));
            Assert.True(File.Exists(root + ".lock")); // so a later sweep can retry
        }

        Assert.Equal(1, EpubDoc.SweepOrphanedExtracts(_base, EpubDoc.LegacyExtractMaxAge));
        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(root + ".lock"));
    }

    [Fact]
    public void Sweep_HeldLockFileWithNoRoot_IsLeftAlone()
    {
        var lockPath = Path.Combine(_base, "gone.lock");
        File.WriteAllText(lockPath, "");

        using (new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(0, EpubDoc.SweepOrphanedExtracts(_base, EpubDoc.LegacyExtractMaxAge));
            Assert.True(File.Exists(lockPath));
        }

        EpubDoc.SweepOrphanedExtracts(_base, EpubDoc.LegacyExtractMaxAge);
        Assert.False(File.Exists(lockPath)); // reclaimed once released
    }

    [Fact]
    public void Sweep_WhenTheBaseDirectoryCannotBeListed_RemovesNothingAndDoesNotThrow()
    {
        var root = MakeOrphan("orphan");

        using (DenyAccess.ListingDirectory(_base))
        {
            Assert.Equal(0, EpubDoc.SweepOrphanedExtracts(_base, EpubDoc.LegacyExtractMaxAge));
        }

        Assert.True(Directory.Exists(root));
        Assert.Equal(1, EpubDoc.SweepOrphanedExtracts(_base, EpubDoc.LegacyExtractMaxAge));
    }
}

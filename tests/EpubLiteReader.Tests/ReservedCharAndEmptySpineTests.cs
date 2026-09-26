using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>
/// F5: a chapter file whose name contains URL syntax ('#', '%', space) must extract,
/// build a valid epub.local URL, and resolve back to the file on disk.
/// F6: an EPUB with an empty spine must be rejected rather than opening into a
/// state where every navigation throws.
/// </summary>
public sealed class ReservedCharAndEmptySpineTests : IDisposable
{
    private readonly string _epubPath;

    public ReservedCharAndEmptySpineTests()
    {
        _epubPath = Path.Combine(Path.GetTempPath(), "elr-reserved-" + Guid.NewGuid().ToString("N") + ".epub");
    }

    public void Dispose()
    {
        try { if (File.Exists(_epubPath)) File.Delete(_epubPath); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData("C#.xhtml")]
    [InlineData("100%.xhtml")]
    [InlineData("a b.xhtml")]
    public async Task Open_ChapterWithReservedCharInName_ExtractsAndBuildsResolvableUrl(string chapterName)
    {
        EpubFixtureBuilder.BuildEpubWithReservedCharChapterName(_epubPath, chapterName);

        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled");
        try
        {
            // The chapter is a spine item and was written to disk under its real name.
            var spineIdx = doc.SpinePaths
                .Select((p, i) => (p, i))
                .First(t => Path.GetFileName(t.p) == chapterName).i;

            var onDisk = Path.Combine(doc.ExtractRoot, "OEBPS", chapterName);
            Assert.True(File.Exists(onDisk), $"chapter not extracted to {onDisk}");

            // The built URL is percent-encoded and resolves back to that same file.
            var url = doc.GetSpineUrl(spineIdx);
            Assert.DoesNotContain("#", url[(url.IndexOf("epub.local", StringComparison.Ordinal))..]); // '#' escaped, not a fragment
            Assert.True(BookResourceServer.TryResolvePath(doc.ExtractRoot, url, out var resolved, out _));
            Assert.Equal(Path.GetFullPath(onDisk), resolved);
        }
        finally
        {
            doc.Dispose();
        }
    }

    [Fact]
    public async Task Open_EmptySpine_ThrowsNoReadableContent()
    {
        EpubFixtureBuilder.BuildEmptySpineEpub(_epubPath);

        await Assert.ThrowsAsync<EpubNoReadableContentException>(
            () => EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled"));
    }

    [Fact]
    public async Task Open_EmptySpine_LeavesNoExtractRootBehind()
    {
        EpubFixtureBuilder.BuildEmptySpineEpub(_epubPath);

        var before = Directory.Exists(EpubDoc.ExtractBaseDir)
            ? Directory.GetDirectories(EpubDoc.ExtractBaseDir).Length
            : 0;

        await Assert.ThrowsAsync<EpubNoReadableContentException>(
            () => EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled"));

        // The rejection happens before extraction, so no new root (or lock) is left.
        var after = Directory.Exists(EpubDoc.ExtractBaseDir)
            ? Directory.GetDirectories(EpubDoc.ExtractBaseDir).Length
            : 0;
        Assert.True(after <= before);
    }
}

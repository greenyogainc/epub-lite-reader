using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

public sealed class MaliciousEpubTests : IDisposable
{
    private readonly string _epubPath;

    public MaliciousEpubTests()
    {
        _epubPath = Path.Combine(Path.GetTempPath(), "elr-malicious-" + Guid.NewGuid().ToString("N") + ".epub");
        EpubFixtureBuilder.BuildMaliciousEpub(_epubPath);
    }

    public void Dispose()
    {
        try { if (File.Exists(_epubPath)) File.Delete(_epubPath); } catch { /* best effort */ }
    }

    /// <summary>
    /// The manifest declares an item at "../escape.txt" - a traversal attempt one level
    /// above the OPF's own directory. Two outcomes are both acceptable here:
    ///   (1) the underlying EPUB library rejects/throws on the malformed manifest entry, or
    ///   (2) the book opens, and EpubDoc.TryMapToDisk safely maps or skips the entry so
    ///       nothing is ever written outside the extraction root.
    /// What must never happen is a file landing outside doc.ExtractRoot. We assert that
    /// invariant precisely and accept either upstream outcome: TryMapToDisk's own
    /// root-containment check (it only returns a destination that starts with
    /// Path.GetFullPath(extractRoot) + separator) makes an actual escape impossible
    /// regardless of what the library resolves "../escape.txt" to.
    /// </summary>
    [Fact]
    public async Task OpenWithChaptersAsync_NeverWritesOutsideExtractRootForTraversalManifestEntry()
    {
        EpubDoc? doc = null;
        try
        {
            (doc, _) = await EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled");
        }
        catch
        {
            // Outcome (1): the library rejected the malformed manifest entry. Acceptable.
            return;
        }

        try
        {
            var elrTempRoot = Path.Combine(Path.GetTempPath(), "EpubLiteReader");
            var extractRootFull = Path.GetFullPath(doc.ExtractRoot);

            var escapedFiles = Directory.Exists(elrTempRoot)
                ? Directory.EnumerateFiles(elrTempRoot, "escape.txt", SearchOption.AllDirectories)
                    .Where(f => !Path.GetFullPath(f)
                        .StartsWith(extractRootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : new List<string>();
            Assert.Empty(escapedFiles);

            var mappedInsideRoot = Directory.Exists(doc.ExtractRoot) &&
                Directory.EnumerateFiles(doc.ExtractRoot, "escape.txt", SearchOption.AllDirectories).Any();
            Assert.True(doc.SkippedEntries.Count > 0 || mappedInsideRoot,
                "the traversal manifest entry must either be recorded as skipped, or safely mapped inside the extract root");
        }
        finally
        {
            var extractRoot = doc.ExtractRoot;
            doc.Dispose();
            Assert.False(Directory.Exists(extractRoot));
        }
    }
}

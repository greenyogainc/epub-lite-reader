using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>
/// The per-resource extraction cap keeps a hostile or corrupt EPUB from
/// materializing an arbitrarily large binary resource on disk (P1-5).
/// </summary>
public sealed class ResourceCapTests : IDisposable
{
    private readonly string _epubPath;

    public ResourceCapTests()
    {
        _epubPath = Path.Combine(Path.GetTempPath(), "elr-cap-" + Guid.NewGuid().ToString("N") + ".epub");
    }

    public void Dispose()
    {
        try { if (File.Exists(_epubPath)) File.Delete(_epubPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Open_SkipsBinaryResourceOverCap()
    {
        EpubFixtureBuilder.BuildEpubWithBinaryResource(_epubPath, "OEBPS/big.png", new byte[1024]);

        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(
            _epubPath, "Untitled", null, default, maxResourceBytes: 64);
        try
        {
            Assert.Contains(doc.SkippedEntries, e => e.EndsWith("big.png", StringComparison.OrdinalIgnoreCase));
            Assert.False(File.Exists(Path.Combine(doc.ExtractRoot, "OEBPS", "big.png")),
                "an oversized binary resource must not be written to the extract root");
            // The rest of the book still opens normally.
            Assert.Equal(1, doc.SpineCount);
        }
        finally
        {
            doc.Dispose();
        }
    }

    [Fact]
    public async Task Open_WritesBinaryResourceUnderCap()
    {
        EpubFixtureBuilder.BuildEpubWithBinaryResource(_epubPath, "OEBPS/big.png", new byte[1024]);

        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled");
        try
        {
            Assert.DoesNotContain(doc.SkippedEntries, e => e.EndsWith("big.png", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(doc.ExtractRoot, "OEBPS", "big.png")),
                "a resource under the cap must be extracted to disk");
        }
        finally
        {
            var root = doc.ExtractRoot;
            doc.Dispose();
            Assert.False(Directory.Exists(root));
        }
    }
}

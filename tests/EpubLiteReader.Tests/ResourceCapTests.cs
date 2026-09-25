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
            var root = doc.ExtractRoot;
            doc.Dispose();
            Assert.False(Directory.Exists(root));
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

    [Fact]
    public async Task Open_SkipsTextResourceOverCap()
    {
        EpubFixtureBuilder.BuildEpubWithLargeChapter(_epubPath, paddingChars: 1024);

        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(
            _epubPath, "Untitled", null, default, maxResourceBytes: 64);
        try
        {
            Assert.Contains(doc.SkippedEntries, e => e.EndsWith("chapter1.xhtml", StringComparison.OrdinalIgnoreCase));
            Assert.False(File.Exists(Path.Combine(doc.ExtractRoot, "OEBPS", "chapter1.xhtml")),
                "an oversized text resource must not be written to the extract root");
            // The oversized chapter is still a spine item, but its plain-text
            // entry must stay empty instead of retaining the huge string.
            Assert.Equal(1, doc.SpineCount);
            Assert.Equal("", doc.SpinePlainText[0]);
        }
        finally
        {
            var root = doc.ExtractRoot;
            doc.Dispose();
            Assert.False(Directory.Exists(root));
        }
    }

    [Fact]
    public async Task Open_WritesTextResourceUnderCap()
    {
        EpubFixtureBuilder.BuildEpubWithLargeChapter(_epubPath, paddingChars: 16);

        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled");
        try
        {
            Assert.DoesNotContain(doc.SkippedEntries, e => e.EndsWith("chapter1.xhtml", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(doc.ExtractRoot, "OEBPS", "chapter1.xhtml")),
                "a text resource under the cap must be extracted to disk");
            Assert.NotEqual("", doc.SpinePlainText[0]);
        }
        finally
        {
            var root = doc.ExtractRoot;
            doc.Dispose();
            Assert.False(Directory.Exists(root));
        }
    }
}

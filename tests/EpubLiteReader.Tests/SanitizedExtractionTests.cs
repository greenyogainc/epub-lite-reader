using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>
/// End-to-end guard for the extraction pipeline: sanitization must be applied
/// to the files actually written into the extract root, not just available as a
/// string helper. A regression that bypassed the StripScripts call in
/// OpenWithChaptersCoreAsync would pass the unit-level SanitizerTests but fail
/// here.
/// </summary>
public sealed class SanitizedExtractionTests : IDisposable
{
    private readonly string _epubPath;

    public SanitizedExtractionTests()
    {
        _epubPath = Path.Combine(Path.GetTempPath(), "elr-san-" + Guid.NewGuid().ToString("N") + ".epub");
    }

    public void Dispose()
    {
        try { if (File.Exists(_epubPath)) File.Delete(_epubPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Open_StripsNetworkHintsAndScriptsFromExtractedChapter()
    {
        var headExtra =
            "<link rel=\"preconnect\" href=\"https://attacker.test\"/>" +
            "<link rel=\"dns-prefetch\" href=\"//attacker.test\"/>" +
            "<link rel=\"stylesheet\" href=\"style.css\"/>" +
            "<script>window.__elrApply=function(){document.title='pwned'};</script>";
        EpubFixtureBuilder.BuildEpubWithBinaryResource(
            _epubPath, "OEBPS/img.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }, headExtra);

        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled");
        try
        {
            var chapter = File.ReadAllText(Path.Combine(doc.ExtractRoot, "OEBPS", "chapter1.xhtml"));

            Assert.DoesNotContain("preconnect", chapter, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dns-prefetch", chapter, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("attacker.test", chapter, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<script", chapter, StringComparison.OrdinalIgnoreCase);
            // Benign content survives.
            Assert.Contains("style.css", chapter);
            Assert.Contains("Only chapter.", chapter);
        }
        finally
        {
            var root = doc.ExtractRoot;
            doc.Dispose();
            Assert.False(Directory.Exists(root));
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>
/// Guards C1: VersOne.Epub returns every non-text manifest item — including a genuine
/// image/svg+xml SVG, and anything mislabeled with an image media-type — as
/// EpubLocalByteContentFile, which a naive extractor writes raw. Whether a resource is
/// sanitized must be driven by how the virtual host will actually SERVE the file (its
/// on-disk extension) and, for anything not obviously safe either way, by sniffing its
/// content the same way Chromium sniffs for HTML/XML — never by the attacker-controlled
/// declared media-type alone.
/// </summary>
public sealed class MislabeledResourceSanitizationTests : IDisposable
{
    private const string HostileMarkup =
        "<html><body><script>alert(1)</script><p onclick=\"x()\">hi</p></body></html>";

    private readonly string _epubPath;

    public MislabeledResourceSanitizationTests()
    {
        _epubPath = Path.Combine(Path.GetTempPath(), "elr-mislabel-" + Guid.NewGuid().ToString("N") + ".epub");
    }

    public void Dispose()
    {
        try { if (File.Exists(_epubPath)) File.Delete(_epubPath); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData("OEBPS/evil.html")]
    [InlineData("OEBPS/evil.svg")]
    [InlineData("OEBPS/evil.xhtml")]
    [InlineData("OEBPS/evil.dat")]
    public async Task Open_SanitizesMarkupMislabeledAsImagePng(string entryName)
    {
        // Every one of these is declared media-type="image/png" (the fixture builder's
        // default) - exactly the mislabel case VersOne.Epub hands back as bytes.
        EpubFixtureBuilder.BuildEpubWithBinaryResource(_epubPath, entryName, Encoding.UTF8.GetBytes(HostileMarkup));

        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled");
        try
        {
            var extracted = File.ReadAllText(Path.Combine(doc.ExtractRoot, "OEBPS", Path.GetFileName(entryName)));

            Assert.DoesNotContain("<script", extracted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onclick", extracted, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hi", extracted);
        }
        finally
        {
            doc.Dispose();
        }
    }

    [Fact]
    public async Task Open_SanitizesSvgDeclaredWithCorrectMediaType()
    {
        EpubFixtureBuilder.BuildEpubWithBinaryResource(
            _epubPath, "OEBPS/real.svg", Encoding.UTF8.GetBytes(HostileMarkup), mediaType: "image/svg+xml");

        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled");
        try
        {
            var extracted = File.ReadAllText(Path.Combine(doc.ExtractRoot, "OEBPS", "real.svg"));

            Assert.DoesNotContain("<script", extracted, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hi", extracted);
        }
        finally
        {
            doc.Dispose();
        }
    }

    [Theory]
    [InlineData("OEBPS/evil.dat")]
    [InlineData("OEBPS/evil.svg")]
    public async Task Open_SanitizesUtf16LeMarkupWithBom(string entryName)
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(HostileMarkup)).ToArray();
        EpubFixtureBuilder.BuildEpubWithBinaryResource(_epubPath, entryName, bytes);

        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled");
        try
        {
            var extracted = File.ReadAllText(Path.Combine(doc.ExtractRoot, "OEBPS", Path.GetFileName(entryName)));

            Assert.DoesNotContain("<script", extracted, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hi", extracted);
        }
        finally
        {
            doc.Dispose();
        }
    }

    [Fact]
    public async Task Open_WritesRealPngByteIdentical()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03 };
        EpubFixtureBuilder.BuildEpubWithBinaryResource(_epubPath, "OEBPS/real.png", png);

        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled");
        try
        {
            var extracted = File.ReadAllBytes(Path.Combine(doc.ExtractRoot, "OEBPS", "real.png"));
            Assert.Equal(png, extracted);
        }
        finally
        {
            doc.Dispose();
        }
    }

    [Fact]
    public async Task Open_WritesUnknownExtensionNonMarkupBlobByteIdentical()
    {
        var blob = new byte[] { 0x00, 0x01, 0xFF, 0x02, 0x03, 0x04 };
        EpubFixtureBuilder.BuildEpubWithBinaryResource(_epubPath, "OEBPS/blob.bin", blob);

        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled");
        try
        {
            var extracted = File.ReadAllBytes(Path.Combine(doc.ExtractRoot, "OEBPS", "blob.bin"));
            Assert.Equal(blob, extracted);
        }
        finally
        {
            doc.Dispose();
        }
    }

    [Fact]
    public async Task Open_WritesPassiveExtensionHostileContentRaw()
    {
        // A .png is served by the virtual host with a non-document MIME type, so even
        // though this "image" is actually HTML, Chromium never renders or HTML-sniffs
        // it: it cannot execute. Passive extensions are intentionally written raw.
        EpubFixtureBuilder.BuildEpubWithBinaryResource(_epubPath, "OEBPS/evil.png", Encoding.UTF8.GetBytes(HostileMarkup));

        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled");
        try
        {
            var extracted = File.ReadAllText(Path.Combine(doc.ExtractRoot, "OEBPS", "evil.png"));
            Assert.Contains("<script", extracted, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            doc.Dispose();
        }
    }
}

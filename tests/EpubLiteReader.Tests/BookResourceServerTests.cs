using System.IO;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>
/// Guards F1: book resources are served through BookResourceServer, whose path
/// resolution must contain requests under the extract root (mirroring
/// EpubDoc.TryMapToDisk) and re-decode the percent-encoding that EpubDoc.ToUrlPath
/// adds, and whose Range parsing must be well-behaved for media seeking.
/// </summary>
public sealed class BookResourceServerTests
{
    private const string Root = "/tmp/elr-extract-root";

    [Theory]
    [InlineData("https://epub.local/OEBPS/ch1.xhtml", "OEBPS/ch1.xhtml")]
    [InlineData("https://epub.local/elr-continuous.html", "elr-continuous.html")]
    [InlineData("https://epub.local/OEBPS/C%23.xhtml", "OEBPS/C#.xhtml")]     // '#'
    [InlineData("https://epub.local/OEBPS/100%25.xhtml", "OEBPS/100%.xhtml")] // '%'
    [InlineData("https://epub.local/a/b/c.css", "a/b/c.css")]
    public void TryResolvePath_ResolvesAndDecodesBookPaths(string uri, string expectedRel)
    {
        var ok = BookResourceServer.TryResolvePath(Root, uri, out var filePath, out var rel);

        Assert.True(ok);
        Assert.Equal(expectedRel, rel);
        var expectedFull = Path.GetFullPath(Path.Combine(Path.GetFullPath(Root), expectedRel.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Equal(expectedFull, filePath);
    }

    [Theory]
    [InlineData("https://epub.local/../secret.txt")]
    [InlineData("https://epub.local/OEBPS/../../secret.txt")]
    [InlineData("https://epub.local/%2e%2e/secret.txt")]         // encoded ".."
    [InlineData("https://epub.local/OEBPS/%2e%2e/%2e%2e/x")]
    [InlineData("https://epub.local/")]                           // no path
    [InlineData("https://evil.example/OEBPS/ch1.xhtml")]          // wrong host
    [InlineData("http://epub.local/OEBPS/ch1.xhtml")]             // wrong scheme host still parses; host ok, but see IsBookUri
    public void TryResolvePath_RejectsUnsafeOrForeignPaths(string uri)
    {
        // For the http:// case the host still matches, so resolution can succeed; the
        // scheme gate lives in IsBookUri, asserted separately below. Everything else
        // must be rejected outright.
        var resolved = BookResourceServer.TryResolvePath(Root, uri, out _, out var rel);
        if (uri.StartsWith("http://"))
        {
            Assert.False(BookResourceServer.IsBookUri(uri));
            return;
        }
        Assert.False(resolved, $"expected rejection, got rel='{rel}'");
    }

    [Theory]
    [InlineData("https://epub.local/OEBPS/ch1.xhtml", true)]
    [InlineData("https://EPUB.LOCAL/OEBPS/ch1.xhtml", true)]
    [InlineData("http://epub.local/OEBPS/ch1.xhtml", false)]
    [InlineData("https://epub.local.evil.com/x", false)]
    [InlineData(null, false)]
    public void IsBookUri_MatchesOnlyHttpsVirtualHost(string? uri, bool expected) =>
        Assert.Equal(expected, BookResourceServer.IsBookUri(uri));

    [Theory]
    [InlineData("bytes=0-99", 1000, 0, 99)]
    [InlineData("bytes=100-", 1000, 100, 999)]
    [InlineData("bytes=-100", 1000, 900, 999)]
    [InlineData("bytes=0-100000", 1000, 0, 999)]   // clamp end to length-1
    public void ParseSingleRange_ParsesValidRanges(string header, long length, long from, long to)
    {
        Assert.True(BookResourceServer.ParseSingleRange(header, length, out var f, out var t));
        Assert.Equal(from, f);
        Assert.Equal(to, t);
    }

    [Theory]
    [InlineData("bytes=0-99,200-299", 1000)] // multi-range unsupported
    [InlineData("items=0-99", 1000)]          // wrong unit
    [InlineData("bytes=1000-1001", 1000)]     // start past end
    [InlineData("bytes=50-40", 1000)]         // end before start
    [InlineData("bytes=abc-def", 1000)]       // non-numeric
    [InlineData("bytes=0-99", 0)]             // empty resource
    public void ParseSingleRange_RejectsBadRanges(string header, long length) =>
        Assert.False(BookResourceServer.ParseSingleRange(header, length, out _, out _));
}

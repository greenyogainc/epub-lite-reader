using System.IO;
using System.Text;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>The serving rules of BookResourceServer, exercised through the WebView2-free
/// <see cref="BookResourceServer.Respond"/> against a real temp extract root: security
/// headers, Content-Type, status codes for every failure, Range handling, and the
/// FileShare.Delete requirement that lets a book be disposed while a response is open.</summary>
public sealed class BookResourceServerResponseTests : IDisposable
{
    private const string Origin = "https://epub.local/";
    private static readonly byte[] Digits = Encoding.ASCII.GetBytes("0123456789");

    private readonly string _root;
    private readonly BookResourceServer _server;
    private readonly List<Stream> _openStreams = new();

    public BookResourceServerResponseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "elr-brs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _server = new BookResourceServer(_root);
    }

    public void Dispose()
    {
        foreach (var s in _openStreams) s.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string Write(string rel, byte[]? bytes = null)
    {
        var path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes ?? Digits);
        return path;
    }

    private BookResourceServer.BookResponse Get(string rel, string? range = null) => GetUri(Origin + rel, range);

    [Fact]
    public void ContinuousDocument_OnItsPerBookUrl_StillGetsItsHashedPolicy()
    {
        Write(EpubDoc.ContinuousFileName);

        var response = GetUri($"{Origin}{EpubDoc.ContinuousFileName}?book=abc#spine-2");

        Assert.Equal(200, response.Status);
        Assert.Contains(EpubDoc.ContinuousScriptCspSource, response.Headers);
        Assert.DoesNotContain("script-src 'none'", response.Headers);
    }

    private BookResourceServer.BookResponse GetUri(string uri, string? range = null)
    {
        var response = _server.Respond(uri, range);
        if (response.Content is not null) _openStreams.Add(response.Content);
        return response;
    }

    private static Dictionary<string, string> Headers(BookResourceServer.BookResponse r) =>
        r.Headers.Split("\r\n").Select(line => line.Split(": ", 2)).ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

    private static byte[] ReadAll(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    // ---------- security headers ----------

    [Fact]
    public void BookDocument_IsServedWithAScriptBlockingCsp()
    {
        Write("OEBPS/ch1.xhtml");

        var r = Get("OEBPS/ch1.xhtml");

        Assert.Equal(200, r.Status);
        var csp = Headers(r)["Content-Security-Policy"];
        Assert.Contains("script-src 'none'", csp);
        Assert.Contains("default-src 'none'", csp);
        Assert.DoesNotContain(EpubDoc.ContinuousScriptCspSource, csp);
    }

    [Theory]
    [InlineData("elr-continuous.html")]
    [InlineData("ELR-Continuous.HTML")]
    public void ContinuousDocument_IsAllowedItsOneScriptByHash(string name)
    {
        Write(EpubDoc.ContinuousFileName);

        var r = Get(name);

        Assert.Equal(200, r.Status);
        var csp = Headers(r)["Content-Security-Policy"];
        Assert.Contains(EpubDoc.ContinuousScriptCspSource, csp);
        Assert.DoesNotContain("script-src 'none'", csp);
    }

    [Theory]
    [InlineData(null, 200)]
    [InlineData("bytes=0-3", 206)]
    public void Responses_CarryNosniffAndNoStore(string? range, int status)
    {
        Write("a.bin");

        var r = Get("a.bin", range);

        Assert.Equal(status, r.Status);
        var headers = Headers(r);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("no-store", headers["Cache-Control"]);
    }

    [Fact]
    public void Ok_ReportsLengthAndAdvertisesRanges()
    {
        Write("a.bin");

        var r = Get("a.bin");

        Assert.Equal("OK", r.Reason);
        var headers = Headers(r);
        Assert.Equal("10", headers["Content-Length"]);
        Assert.Equal("bytes", headers["Accept-Ranges"]);
        Assert.False(headers.ContainsKey("Content-Range"));
        Assert.Equal(Digits, ReadAll(r.Content!));
    }

    // ---------- content types ----------

    [Theory]
    [InlineData("a.xhtml", "application/xhtml+xml")]
    [InlineData("a.html", "text/html")]
    [InlineData("a.HTM", "text/html")]
    [InlineData("a.svg", "image/svg+xml")]
    [InlineData("a.xml", "text/xml")]
    [InlineData("a.ncx", "application/x-dtbncx+xml")]
    [InlineData("a.opf", "application/oebps-package+xml")]
    [InlineData("a.css", "text/css")]
    [InlineData("a.txt", "text/plain")]
    [InlineData("a.png", "image/png")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.woff2", "font/woff2")]
    [InlineData("a.ttf", "font/ttf")]
    [InlineData("a.eot", "application/vnd.ms-fontobject")]
    [InlineData("a.mp3", "audio/mpeg")]
    [InlineData("a.mp4", "video/mp4")]
    [InlineData("a.webm", "video/webm")]
    [InlineData("a.unknownext", "application/octet-stream")]
    [InlineData("noextension", "application/octet-stream")]
    public void ContentType_FollowsTheOnDiskExtension(string name, string expected)
    {
        Write(name);

        Assert.Equal(expected, Headers(Get(name))["Content-Type"]);
    }

    // ---------- failures ----------

    [Fact]
    public void MissingFile_Is404()
    {
        var r = Get("OEBPS/missing.xhtml");

        AssertTextFailure(r, 404, "Not Found");
    }

    [Fact]
    public void RequestForADirectory_Is404()
    {
        Directory.CreateDirectory(Path.Combine(_root, "OEBPS"));

        AssertTextFailure(Get("OEBPS"), 404, "Not Found");
    }

    [Theory]
    [InlineData("https://epub.local/%2e%2e/secret.txt")]
    [InlineData("https://epub.local/OEBPS/..%2f..%2fsecret.txt")]
    [InlineData("https://epub.local/C%3a/secret.txt")]
    [InlineData("https://epub.local/")]       // no path segments
    [InlineData("https://epub.local")]        // no path at all
    [InlineData("https://epub.local?x=1")]    // query only
    [InlineData("https://evil.example/a.xhtml")]
    public void UnsafeOrForeignPath_Is403(string uri)
    {
        AssertTextFailure(GetUri(uri), 403, "Forbidden");
    }

    [Fact]
    public void FileThatCannotBeOpenedBecauseItIsHeldExclusively_Is404()
    {
        var path = Write("held.xhtml");
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        AssertTextFailure(Get("held.xhtml"), 404, "Not Found");
    }

    [Fact]
    public void FileThatMayNotBeRead_Is403()
    {
        var path = Write("secret.xhtml");

        // File.Exists is still true, so the open itself must fail with UnauthorizedAccessException.
        using (DenyAccess.ReadingFile(path))
        {
            AssertTextFailure(Get("secret.xhtml"), 403, "Forbidden");
        }
    }

    private static void AssertTextFailure(BookResourceServer.BookResponse r, int status, string reason)
    {
        Assert.Equal(status, r.Status);
        Assert.Equal(reason, r.Reason);
        Assert.Null(r.Content);
        var headers = Headers(r);
        Assert.Equal("text/plain", headers["Content-Type"]);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("default-src 'none'", headers["Content-Security-Policy"]);
    }

    // ---------- ranges ----------

    [Theory]
    [InlineData("bytes=2-5", "2345", "bytes 2-5/10")]
    [InlineData("bytes=-3", "789", "bytes 7-9/10")]
    [InlineData("bytes=2-", "23456789", "bytes 2-9/10")]
    [InlineData("bytes=7-100", "789", "bytes 7-9/10")] // end past EOF is clamped
    [InlineData("bytes=0-0", "0", "bytes 0-0/10")]
    public void SingleRange_Returns206WithExactlyTheRequestedBytes(string range, string expected, string contentRange)
    {
        Write("a.bin");

        var r = Get("a.bin", range);

        Assert.Equal(206, r.Status);
        Assert.Equal("Partial Content", r.Reason);
        var headers = Headers(r);
        Assert.Equal(contentRange, headers["Content-Range"]);
        Assert.Equal(expected.Length.ToString(), headers["Content-Length"]);
        Assert.Equal("bytes", headers["Accept-Ranges"]);
        Assert.Equal(expected, Encoding.ASCII.GetString(ReadAll(r.Content!)));
    }

    [Theory]
    [InlineData("bytes=0-1,3-4")]  // multi-range is not supported
    [InlineData("garbage")]
    [InlineData("items=0-1")]
    [InlineData("bytes=50-60")]    // start past EOF
    [InlineData("bytes=4-2")]      // end before start
    [InlineData("")]
    public void RangeWeCannotHonor_FallsBackToTheWholeFile(string range)
    {
        Write("a.bin");

        var r = Get("a.bin", range);

        Assert.Equal(200, r.Status);
        Assert.False(Headers(r).ContainsKey("Content-Range"));
        Assert.Equal(Digits, ReadAll(r.Content!));
    }

    // ---------- LimitedStream (the 206 body) ----------

    [Fact]
    public void RangeBody_IsAForwardOnlyReadOnlyStream()
    {
        Write("a.bin");
        var s = Get("a.bin", "bytes=2-5").Content!;

        Assert.True(s.CanRead);
        Assert.False(s.CanSeek);
        Assert.False(s.CanWrite);
        Assert.Throws<NotSupportedException>(() => s.Length);
        Assert.Throws<NotSupportedException>(() => s.Position);
        Assert.Throws<NotSupportedException>(() => s.Position = 0);
        Assert.Throws<NotSupportedException>(() => s.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => s.SetLength(1));
        Assert.Throws<NotSupportedException>(() => s.Write(new byte[1], 0, 1));
        s.Flush(); // a no-op, not an error
    }

    [Fact]
    public void RangeBody_StopsAtTheLimitEvenWhenTheFileHasMore()
    {
        Write("a.bin");
        var s = Get("a.bin", "bytes=2-5").Content!;
        var buffer = new byte[100];

        var first = s.Read(buffer, 0, 2);
        var second = s.Read(buffer, first, 100 - first);
        var third = s.Read(buffer, 0, 100);

        Assert.Equal(2, first);
        Assert.Equal(2, second);
        Assert.Equal(0, third);
        Assert.Equal("2345", Encoding.ASCII.GetString(buffer, 0, 4));
    }

    [Fact]
    public void RangeBody_DisposeReleasesTheFile()
    {
        var path = Write("a.bin");
        var s = Get("a.bin", "bytes=2-5").Content!;

        s.Dispose();

        // An exclusive open only succeeds once the inner file stream has been closed too.
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(10, exclusive.Length);
    }

    // ---------- FileShare.Delete ----------

    [Fact]
    public void ExtractRootCanBeDeletedWhileAResponseStreamIsOpen()
    {
        Write("OEBPS/ch1.xhtml");
        var r = Get("OEBPS/ch1.xhtml");

        Directory.Delete(_root, recursive: true);

        Assert.False(Directory.Exists(_root));
        Assert.Equal(Digits, ReadAll(r.Content!)); // the open handle still reads
    }

    // ---------- TryResolvePath edge cases ----------

    [Fact]
    public void TryResolvePath_PercentZero_IsRejectedByThePathApis()
    {
        var ok = BookResourceServer.TryResolvePath(_root, Origin + "OEBPS/a%00b.xhtml", out var path, out var rel);

        Assert.False(ok);
        Assert.Equal("", path);
        Assert.Equal("", rel);
    }

    [Fact]
    public void TryResolvePath_MalformedEscape_IsTakenLiterally()
    {
        var ok = BookResourceServer.TryResolvePath(_root, Origin + "OEBPS/%zz.xhtml", out var path, out var rel);

        Assert.True(ok);
        Assert.Equal("OEBPS/%zz.xhtml", rel);
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "OEBPS", "%zz.xhtml"), path);
    }

    [Fact]
    public void TryResolvePath_RootWithTrailingSeparator_ResolvesNothing()
    {
        // The containment check appends its own separator, so a root that already has one
        // can never match. The server always passes a GetFullPath-normalized, separator-free root.
        var rootWithSeparator = _root + Path.DirectorySeparatorChar;

        Assert.False(BookResourceServer.TryResolvePath(rootWithSeparator, Origin + "a.xhtml", out _, out _));
    }

    [Theory]
    [InlineData("https://epub.local/a%2fb.xhtml")]         // decoded '/'
    [InlineData("https://epub.local/a%5cb.xhtml")]         // decoded '\'
    [InlineData("https://epub.local/C%3a.xhtml")]          // decoded ':'
    [InlineData("https:epub.local/a.xhtml")]               // parses as epub.local but has no "://"
    [InlineData("/OEBPS/a.xhtml")]                         // relative
    [InlineData("OEBPS/a.xhtml")]                          // relative
    [InlineData("not a uri")]
    [InlineData("https://epub.local")]                     // no path
    [InlineData("https://epub.local?x")]                   // query only
    [InlineData("https://epub.local//")]                   // only empty segments
    [InlineData("https://epub.local.evil.com/a.xhtml")]    // host mismatch
    public void TryResolvePath_RejectsWithoutThrowing(string uri)
    {
        Assert.False(BookResourceServer.TryResolvePath(_root, uri, out var path, out var rel));
        Assert.Equal("", path);
        Assert.Equal("", rel);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("https://epub.local", false)]  // the prefix needs the slash
    [InlineData("https://other.example/x", false)]
    [InlineData("https://epub.local/x", true)]
    public void IsBookUri_RequiresTheHttpsVirtualHostPrefix(string? uri, bool expected) =>
        Assert.Equal(expected, BookResourceServer.IsBookUri(uri));
}

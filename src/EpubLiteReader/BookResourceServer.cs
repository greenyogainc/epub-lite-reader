using System.IO;
using Microsoft.Web.WebView2.Core;

namespace EpubLiteReader;

/// <summary>
/// Serves <c>https://epub.local/*</c> from a book's extract root through
/// <see cref="CoreWebView2.WebResourceRequested"/>, so every book resource carries a
/// locked-down Content-Security-Policy, an explicit Content-Type, and
/// <c>X-Content-Type-Options: nosniff</c>.
///
/// The CSP is the primary control that a book cannot run script or reach the network:
/// <c>script-src 'none'</c> blocks inline handlers, <c>&lt;script&gt;</c>, and
/// <c>javascript:</c> navigations (a child frame's initial document inherits the policy,
/// so <c>&lt;iframe src="javascript:…"&gt;</c> is blocked too), and <c>default-src 'none'</c>
/// plus <c>form-action 'none'</c> stop every outbound request except same-origin loads of
/// the book's own resources. <see cref="EpubDoc.StripScripts"/> is defense-in-depth behind it.
/// </summary>
internal sealed class BookResourceServer
{
    private const string Origin = "https://" + EpubDoc.VirtualHost;
    private const string OriginPrefix = Origin + "/";

    private readonly string _extractRoot;
    private readonly string _bookCsp;
    private readonly string _continuousCsp;

    internal BookResourceServer(string extractRoot)
    {
        _extractRoot = Path.GetFullPath(extractRoot);

        // Same policy for every book document. The continuous document additionally needs
        // its one inline script allowed by hash and needs to frame its chapter documents.
        const string common =
            "default-src 'none'; " +
            "img-src " + Origin + " data: blob:; " +
            "media-src " + Origin + " data: blob:; " +
            "style-src " + Origin + " 'unsafe-inline'; " +
            "font-src " + Origin + " data:; " +
            "object-src 'none'; " +
            "base-uri 'none'; " +
            "form-action 'none'; " +
            "frame-src " + Origin + "; " +
            "child-src " + Origin + "; " +
            "frame-ancestors " + Origin;

        _bookCsp = common + "; script-src 'none'";
        _continuousCsp = common + "; script-src " + EpubDoc.ContinuousScriptCspSource;
    }

    /// <summary>Content-Type served per on-disk extension. Kept in step with
    /// <see cref="EpubDoc.PassiveExtensions"/>/<see cref="EpubDoc.MarkupExtensions"/> and
    /// with Chromium's own extension table. Anything not listed is served as
    /// <c>application/octet-stream</c> (never sniffed, thanks to nosniff).</summary>
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documents
        [".xhtml"] = "application/xhtml+xml", [".xht"] = "application/xhtml+xml", [".xhtm"] = "application/xhtml+xml",
        [".html"] = "text/html", [".htm"] = "text/html", [".shtml"] = "text/html", [".shtm"] = "text/html", [".ehtml"] = "text/html",
        [".xml"] = "text/xml", [".xsl"] = "text/xml", [".xslt"] = "text/xml", [".xbl"] = "text/xml",
        [".svg"] = "image/svg+xml",
        [".ncx"] = "application/x-dtbncx+xml", [".opf"] = "application/oebps-package+xml",
        // Styles / text
        [".css"] = "text/css", [".txt"] = "text/plain",
        // Images
        [".png"] = "image/png", [".apng"] = "image/apng", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".bmp"] = "image/bmp", [".ico"] = "image/x-icon",
        [".avif"] = "image/avif", [".tif"] = "image/tiff", [".tiff"] = "image/tiff",
        // Fonts
        [".ttf"] = "font/ttf", [".otf"] = "font/otf", [".woff"] = "font/woff", [".woff2"] = "font/woff2",
        [".eot"] = "application/vnd.ms-fontobject",
        // Audio / video
        [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4", [".aac"] = "audio/aac", [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg", [".opus"] = "audio/ogg", [".wav"] = "audio/wav", [".flac"] = "audio/flac",
        [".mp4"] = "video/mp4", [".m4v"] = "video/mp4", [".webm"] = "video/webm",
    };

    internal static bool IsBookUri(string? uri) =>
        uri is not null && uri.StartsWith(OriginPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Produces the response for a book request, or null when the URI is not a
    /// book URI (the caller then blocks it). A missing or unsafe path yields 404/403.</summary>
    internal CoreWebView2WebResourceResponse? CreateResponse(
        CoreWebView2Environment environment, CoreWebView2WebResourceRequest request)
    {
        var uri = request.Uri;
        if (!IsBookUri(uri)) return null;

        if (!TryResolve(uri!, out var filePath, out var relForCsp))
            return Text(environment, 403, "Forbidden");

        if (!File.Exists(filePath))
            return Text(environment, 404, "Not Found");

        var ext = Path.GetExtension(filePath);
        var contentType = ContentTypes.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";
        var isContinuous = string.Equals(relForCsp, EpubDoc.ContinuousFileName, StringComparison.OrdinalIgnoreCase);
        var csp = isContinuous ? _continuousCsp : _bookCsp;

        FileStream stream;
        try
        {
            stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (IOException)
        {
            return Text(environment, 404, "Not Found");
        }
        catch (UnauthorizedAccessException)
        {
            return Text(environment, 403, "Forbidden");
        }

        var length = stream.Length;

        // Honor a single-range request so audio/video seeking works; anything fancier
        // (multipart, suffix-only beyond what ParseSingleRange handles) falls back to 200.
        if (TryGetHeader(request, "Range", out var rangeHeader) &&
            ParseSingleRange(rangeHeader, length, out var from, out var to))
        {
            stream.Seek(from, SeekOrigin.Begin);
            var headers = BuildHeaders(contentType, csp,
                $"Content-Length: {to - from + 1}",
                $"Content-Range: bytes {from}-{to}/{length}",
                "Accept-Ranges: bytes");
            // A LimitedStream keeps WebView2 from reading past the requested range.
            return environment.CreateWebResourceResponse(
                new LimitedStream(stream, to - from + 1), 206, "Partial Content", headers);
        }

        var okHeaders = BuildHeaders(contentType, csp,
            $"Content-Length: {length}",
            "Accept-Ranges: bytes");
        return environment.CreateWebResourceResponse(stream, 200, "OK", okHeaders);
    }

    private static string BuildHeaders(string contentType, string csp, params string[] extra)
    {
        var lines = new List<string>
        {
            "Content-Type: " + contentType,
            "X-Content-Type-Options: nosniff",
            "Content-Security-Policy: " + csp,
            "Cache-Control: no-store",
        };
        lines.AddRange(extra);
        return string.Join("\r\n", lines);
    }

    private static CoreWebView2WebResourceResponse Text(CoreWebView2Environment environment, int code, string reason) =>
        environment.CreateWebResourceResponse(null, code, reason,
            "Content-Type: text/plain\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'none'");

    /// <summary>
    /// Resolves a book URI to an on-disk path guaranteed to sit under the extract root.
    /// Mirrors <see cref="EpubDoc.TryMapToDisk"/>: each path segment is URL-decoded
    /// (VersOne hands back decoded names and <see cref="EpubDoc.ToUrlPath"/> re-encodes
    /// them), then rejected for empties, dot segments, separators, or a normalized result
    /// that escapes the root.
    /// </summary>
    private bool TryResolve(string uri, out string filePath, out string relForCsp) =>
        TryResolvePath(_extractRoot, uri, out filePath, out relForCsp);

    internal static bool TryResolvePath(string extractRoot, string uri, out string filePath, out string relForCsp)
    {
        filePath = "";
        relForCsp = "";
        var rootFull = Path.GetFullPath(extractRoot);
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return false;
        if (!string.Equals(parsed.Host, EpubDoc.VirtualHost, StringComparison.OrdinalIgnoreCase)) return false;

        // AbsolutePath is percent-encoded and query/fragment are already split off.
        var rawSegments = parsed.AbsolutePath.Split('/');
        var decoded = new List<string>(rawSegments.Length);
        foreach (var raw in rawSegments)
        {
            if (raw.Length == 0) continue;
            string seg;
            try { seg = Uri.UnescapeDataString(raw); }
            catch { return false; }
            if (seg.Length == 0 || seg is "." or "..") return false;
            if (seg.Contains('/') || seg.Contains('\\') || seg.Contains(':')) return false;
            decoded.Add(seg);
        }
        if (decoded.Count == 0) return false;

        var rel = string.Join('/', decoded);
        try
        {
            var combined = Path.GetFullPath(Path.Combine(rootFull, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!combined.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;
            filePath = combined;
            relForCsp = rel;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetHeader(CoreWebView2WebResourceRequest request, string name, out string value)
    {
        value = "";
        try
        {
            if (request.Headers.Contains(name))
            {
                value = request.Headers.GetHeader(name) ?? "";
                return value.Length > 0;
            }
        }
        catch
        {
            // Header collection can throw on odd requests; treat as absent.
        }
        return false;
    }

    /// <summary>Parses a single "bytes=from-to" / "bytes=from-" / "bytes=-suffix" range
    /// against a known length. Returns false for anything else, leaving the caller on 200.</summary>
    internal static bool ParseSingleRange(string header, long length, out long from, out long to)
    {
        from = 0;
        to = length - 1;
        if (length <= 0) return false;
        const string prefix = "bytes=";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var spec = header[prefix.Length..].Trim();
        if (spec.Contains(',')) return false; // multi-range not supported
        var dash = spec.IndexOf('-');
        if (dash < 0) return false;

        var startText = spec[..dash].Trim();
        var endText = spec[(dash + 1)..].Trim();

        if (startText.Length == 0)
        {
            // Suffix range: last N bytes.
            if (!long.TryParse(endText, out var suffix) || suffix <= 0) return false;
            from = Math.Max(0, length - suffix);
            to = length - 1;
            return true;
        }

        if (!long.TryParse(startText, out from) || from < 0 || from >= length) return false;
        if (endText.Length == 0)
        {
            to = length - 1;
        }
        else
        {
            if (!long.TryParse(endText, out to) || to < from) return false;
            if (to > length - 1) to = length - 1;
        }
        return true;
    }

    /// <summary>Read-only view over the first <c>count</c> bytes from the stream's current
    /// position, so a 206 response never hands WebView2 bytes past the requested range.</summary>
    private sealed class LimitedStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;

        internal LimitedStream(Stream inner, long count)
        {
            _inner = inner;
            _remaining = count;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            var toRead = (int)Math.Min(count, _remaining);
            var read = _inner.Read(buffer, offset, toRead);
            _remaining -= read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

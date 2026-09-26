using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using VersOne.Epub;

namespace EpubLiteReader;

/// <summary>Thrown when an EPUB parses but has no spine items to display.</summary>
public sealed class EpubNoReadableContentException : Exception
{
    public EpubNoReadableContentException()
        : base("The EPUB contains no readable spine items.") { }
}

/// <summary>Opened EPUB: extracted local content, spine, TOC, and metadata.</summary>
public sealed class EpubDoc : IDisposable
{
    public const string VirtualHost = "epub.local";
    public const string ContinuousFileName = "elr-continuous.html";

    private readonly string _extractRoot;
    private FileStream? _extractLock;
    private bool _disposed;

    public string FilePath { get; }
    public string BookId { get; }
    public string Title { get; }
    public string Author { get; }
    public string? Description { get; }

    /// <summary>Spine entries as relative paths under the extract root (forward slashes).</summary>
    public IReadOnlyList<string> SpinePaths { get; }

    /// <summary>Plain text per spine item for search.</summary>
    public IReadOnlyList<string> SpinePlainText { get; }

    public int SpineCount => SpinePaths.Count;

    public string ExtractRoot => _extractRoot;

    private EpubDoc(
        string filePath,
        string bookId,
        string title,
        string author,
        string? description,
        string extractRoot,
        IReadOnlyList<string> spinePaths,
        IReadOnlyList<string> spinePlainText)
    {
        FilePath = filePath;
        BookId = bookId;
        Title = title;
        Author = author;
        Description = description;
        _extractRoot = extractRoot;
        SpinePaths = spinePaths;
        SpinePlainText = spinePlainText;
    }

    private List<ChapterItem>? _chapters;

    public List<ChapterItem> GetChapters() => _chapters ?? new List<ChapterItem>();

    /// <summary>Entries skipped during extraction because their paths were unsafe, collided, or exceeded the resource size cap.</summary>
    public IReadOnlyList<string> SkippedEntries { get; private set; } = Array.Empty<string>();

    /// <summary>Default cap for a single resource (text or binary - a chapter, image, font, ...) extracted from a book.</summary>
    public const long DefaultMaxResourceBytes = 64L * 1024 * 1024;

    public static Task<(EpubDoc Doc, List<ChapterItem> Chapters)> OpenWithChaptersAsync(
        string path, string untitledLabel, string? sectionTitleFormat = null, CancellationToken ct = default,
        long maxResourceBytes = DefaultMaxResourceBytes) =>
        // Parsing, extraction, and text conversion are CPU/IO heavy; keep all of it
        // off the caller's (dispatcher) thread so large books cannot freeze the UI.
        Task.Run(() => OpenWithChaptersCoreAsync(path, untitledLabel, sectionTitleFormat, ct, maxResourceBytes), ct);

    private static async Task<(EpubDoc Doc, List<ChapterItem> Chapters)> OpenWithChaptersCoreAsync(
        string path, string untitledLabel, string? sectionTitleFormat, CancellationToken ct, long maxResourceBytes)
    {
        path = Path.GetFullPath(path);
        var book = await EpubReader.ReadBookAsync(path);
        ct.ThrowIfCancellationRequested();

        // VersOne.Epub accepts an empty <spine/>; a book with nothing to display must be
        // rejected here, before anything is extracted or the UI commits to it.
        if (book.ReadingOrder.Count == 0)
            throw new EpubNoReadableContentException();

        var extractRoot = Path.Combine(ExtractBaseDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);
        FileStream? extractLock = null;

        try
        {
            // Held for the document's lifetime; a startup sweep treats an extract root
            // whose lock can be taken exclusively as orphaned (crash, kill, failed delete).
            extractLock = AcquireExtractLock(extractRoot);
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skipped = new List<string>();
            // Relative paths (NormalizePath-keyed, same as pathToSpine below) skipped for
            // being oversized, so a spine item among them never has HtmlToPlainText run on
            // its (already in-memory) huge content and never retains that text afterward.
            var oversizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in book.Content.AllFiles.Local)
            {
                ct.ThrowIfCancellationRequested();
                if (!TryMapToDisk(extractRoot, file.FilePath, out var dest) || !written.Add(dest))
                {
                    skipped.Add(file.FilePath);
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    var ext = Path.GetExtension(file.FilePath);
                    if (file is EpubLocalTextContentFile text)
                    {
                        if (Encoding.UTF8.GetByteCount(text.Content) > maxResourceBytes)
                        {
                            // A single hostile or corrupt oversized resource must not be
                            // materialized on disk; skip it and record the entry.
                            skipped.Add(file.FilePath);
                            oversizedPaths.Add(NormalizePath(file.FilePath));
                            continue;
                        }

                        // A passive extension served with a non-document MIME type is safe
                        // to leave unsanitized (this is how CSS skips StripScripts today -
                        // though File.WriteAllTextAsync with Encoding.UTF8 below still
                        // re-encodes it and prepends a UTF-8 BOM, so it is not written
                        // byte-for-byte); everything else is sanitized, including
                        // NCX/OPF text entries, where stripping is a harmless no-op.
                        var content = IsPassiveExtension(ext) && !IsDeclaredHtmlLike(text.ContentMimeType)
                            ? text.Content
                            : StripScripts(text.Content);
                        await File.WriteAllTextAsync(dest, content, Encoding.UTF8, ct);
                    }
                    else if (file is EpubLocalByteContentFile bytes)
                    {
                        if (bytes.Content.LongLength > maxResourceBytes)
                        {
                            // A single hostile or corrupt oversized resource must not be
                            // materialized on disk; skip it and record the entry.
                            skipped.Add(file.FilePath);
                            continue;
                        }

                        // The manifest media-type is attacker-controlled: VersOne.Epub
                        // returns every non-text manifest item as bytes, including a
                        // genuine image/svg+xml SVG and any item mislabeled as e.g.
                        // image/png, and the virtual host serves each file by its
                        // on-disk EXTENSION, not by the manifest's claim. So the
                        // sanitize-or-not decision below is driven by extension and, for
                        // anything not obviously safe either way, by sniffing the content
                        // the same way Chromium sniffs for HTML/XML.
                        var declaredHtmlLike = IsDeclaredHtmlLike(bytes.ContentMimeType);
                        if (IsPassiveExtension(ext) && !declaredHtmlLike && !LooksLikeMarkup(bytes.Content))
                        {
                            // No genuine image, font, or media file starts with '<'; one
                            // that does is treated as markup below, never trusted by name.
                            await File.WriteAllBytesAsync(dest, bytes.Content, ct);
                        }
                        else if (IsMarkupExtension(ext) || declaredHtmlLike || IsPassiveExtension(ext))
                        {
                            // Already known to need sanitizing; decode the full resource once.
                            var decoded = DecodeResourceText(bytes.Content);
                            await File.WriteAllTextAsync(dest, StripScripts(decoded), Encoding.UTF8, ct);
                        }
                        else
                        {
                            // Unknown extension with a media-type that isn't html/xml-like
                            // either: sniff at the byte level first, and only pay to
                            // decode (and sanitize) the whole resource when that sniff
                            // calls for it.
                            if (LooksLikeMarkup(bytes.Content))
                            {
                                var decoded = DecodeResourceText(bytes.Content);
                                await File.WriteAllTextAsync(dest, StripScripts(decoded), Encoding.UTF8, ct);
                            }
                            else
                            {
                                await File.WriteAllBytesAsync(dest, bytes.Content, ct);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // A single hostile or malformed entry (file/directory collision,
                    // reserved name the OS rejects) must not take the whole book down.
                    skipped.Add(file.FilePath);
                }
            }

            var spinePaths = new List<string>();
            var spineText = new List<string>();
            var pathToSpine = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in book.ReadingOrder)
            {
                ct.ThrowIfCancellationRequested();
                var rel = NormalizePath(item.FilePath);
                pathToSpine[rel] = spinePaths.Count;
                // Also index by filename-only for loose matches
                pathToSpine.TryAdd(Path.GetFileName(rel), spinePaths.Count);
                spinePaths.Add(rel);
                // A spine item skipped above for being oversized was never written to
                // disk; also skip running HtmlToPlainText on its (already in-memory)
                // huge content so the resulting huge string is not retained here either.
                spineText.Add(oversizedPaths.Contains(rel) ? "" : HtmlToPlainText(item.Content));
            }

            var order = 0;
            var chapters = BuildChapters(book.Navigation, pathToSpine, untitledLabel, depth: 0, parent: null, ref order);

            var spineTitles = BuildSpineTitles(spinePaths.Count, chapters, sectionTitleFormat ?? "Section {0}");
            WriteContinuousDocument(extractRoot, spinePaths, spineTitles);

            var bookId = ComputeBookId(path, book);
            var doc = new EpubDoc(
                path,
                bookId,
                string.IsNullOrWhiteSpace(book.Title) ? Path.GetFileName(path) : book.Title,
                book.Author ?? "",
                book.Description,
                extractRoot,
                spinePaths,
                spineText)
            {
                _chapters = chapters,
                SkippedEntries = skipped,
                _extractLock = extractLock
            };

            return (doc, chapters);
        }
        catch
        {
            ReleaseExtractLock(extractRoot, extractLock);
            TryDeleteDirectory(extractRoot);
            throw;
        }
    }

    /// <summary>Per-spine display titles: the first chapter that points at the spine item, else a numbered fallback.</summary>
    internal static string[] BuildSpineTitles(int spineCount, List<ChapterItem> chapters, string sectionTitleFormat)
    {
        var titles = new string[spineCount];
        var stack = new Stack<ChapterItem>();
        for (int i = chapters.Count - 1; i >= 0; i--)
            stack.Push(chapters[i]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.SpineIndex is int s && s >= 0 && s < spineCount)
                titles[s] ??= node.Title;
            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
        for (int i = 0; i < spineCount; i++)
            titles[i] ??= string.Format(sectionTitleFormat, i + 1);
        return titles;
    }

    public string GetSpineUrl(int spineIndex, string? anchor = null)
    {
        if (spineIndex < 0 || spineIndex >= SpinePaths.Count)
            throw new ArgumentOutOfRangeException(nameof(spineIndex));
        var path = SpinePaths[spineIndex];
        var url = $"https://{VirtualHost}/{ToUrlPath(path)}";
        if (!string.IsNullOrEmpty(anchor))
            url += "#" + anchor;
        return url;
    }

    public string GetContinuousUrl(int spineIndex = 0)
    {
        var url = $"https://{VirtualHost}/{ContinuousFileName}";
        if (spineIndex >= 0 && spineIndex < SpinePaths.Count)
            url += $"#spine-{spineIndex}";
        return url;
    }

    public int? FindSpineIndex(string contentFilePath)
    {
        var norm = NormalizePath(contentFilePath);
        for (int i = 0; i < SpinePaths.Count; i++)
        {
            if (string.Equals(SpinePaths[i], norm, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        var name = Path.GetFileName(norm);
        for (int i = 0; i < SpinePaths.Count; i++)
        {
            if (string.Equals(Path.GetFileName(SpinePaths[i]), name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return null;
    }

    public IEnumerable<(int SpineIndex, int Offset, string Snippet)> Search(string query, int maxResults = 200)
    {
        if (string.IsNullOrWhiteSpace(query))
            yield break;

        var q = query.Trim();
        int found = 0;
        for (int i = 0; i < SpinePlainText.Count && found < maxResults; i++)
        {
            var text = SpinePlainText[i];
            int start = 0;
            while (found < maxResults)
            {
                int idx = text.IndexOf(q, start, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                int snipStart = Math.Max(0, idx - 40);
                int snipLen = Math.Min(text.Length - snipStart, q.Length + 80);
                var snippet = text.Substring(snipStart, snipLen).Replace('\n', ' ').Replace('\r', ' ');
                yield return (i, idx, snippet);
                found++;
                start = idx + Math.Max(1, q.Length);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TryDeleteDirectory(_extractRoot);
        ReleaseExtractLock(_extractRoot, _extractLock);
        _extractLock = null;
    }

    /// <summary>Parent of every per-book extract root.</summary>
    internal static string ExtractBaseDir => Path.Combine(Path.GetTempPath(), "EpubLiteReader");

    private const string ExtractLockSuffix = ".lock";

    /// <summary>Extract roots without a lock file (written by 1.0.6 and earlier) are only
    /// swept once they are at least this old, so a still-running older build is not hit.</summary>
    internal static readonly TimeSpan LegacyExtractMaxAge = TimeSpan.FromDays(1);

    private static FileStream AcquireExtractLock(string extractRoot) =>
        new(extractRoot + ExtractLockSuffix, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

    private static void ReleaseExtractLock(string extractRoot, FileStream? extractLock)
    {
        if (extractLock is null) return;
        try
        {
            extractLock.Dispose();
            File.Delete(extractRoot + ExtractLockSuffix);
        }
        catch
        {
            // Best effort: an orphaned, unheld lock file is cleaned up by the next sweep.
        }
    }

    /// <summary>
    /// Deletes extract roots left behind by a crash, a kill, or a delete that failed
    /// while the WebView still held file handles. A root is orphaned when its sibling
    /// lock file exists but can be opened exclusively (no live instance holds it), or
    /// when it has no lock file and is older than <paramref name="legacyMaxAge"/>.
    /// Best effort throughout; returns the number of roots removed.
    /// </summary>
    internal static int SweepOrphanedExtracts(string baseDir, TimeSpan legacyMaxAge)
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(baseDir)) return 0;
            foreach (var dir in Directory.EnumerateDirectories(baseDir))
            {
                try
                {
                    var lockPath = dir + ExtractLockSuffix;
                    if (File.Exists(lockPath))
                    {
                        FileStream probe;
                        try
                        {
                            probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        }
                        catch (IOException)
                        {
                            continue; // held by a live instance
                        }
                        using (probe)
                        {
                            Directory.Delete(dir, recursive: true);
                        }
                        File.Delete(lockPath);
                        removed++;
                    }
                    else if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) > legacyMaxAge)
                    {
                        Directory.Delete(dir, recursive: true);
                        removed++;
                    }
                }
                catch
                {
                    // Skip anything we cannot inspect or remove right now.
                }
            }

            // Lock files whose extract root is already gone.
            foreach (var lockPath in Directory.EnumerateFiles(baseDir, "*" + ExtractLockSuffix))
            {
                try
                {
                    if (Directory.Exists(lockPath[..^ExtractLockSuffix.Length])) continue;
                    using (new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    File.Delete(lockPath);
                }
                catch
                {
                    // Held or already gone.
                }
            }
        }
        catch
        {
            // Enumeration is best effort.
        }
        return removed;
    }

    private static List<ChapterItem> BuildChapters(
        List<EpubNavigationItem>? navigation,
        Dictionary<string, int> pathToSpine,
        string untitledLabel,
        int depth,
        ChapterItem? parent,
        ref int order)
    {
        var list = new List<ChapterItem>();
        if (navigation is null) return list;

        foreach (var nav in navigation)
        {
            int? spine = null;
            string? anchor = null;
            if (nav.Link is not null)
            {
                var path = NormalizePath(nav.Link.ContentFilePath);
                if (pathToSpine.TryGetValue(path, out int idx) ||
                    pathToSpine.TryGetValue(Path.GetFileName(path), out idx))
                    spine = idx;
                anchor = string.IsNullOrEmpty(nav.Link.Anchor) ? null : nav.Link.Anchor;
            }

            var item = new ChapterItem
            {
                Title = string.IsNullOrWhiteSpace(nav.Title) ? untitledLabel : nav.Title.Trim(),
                SpineIndex = spine,
                Anchor = anchor,
                Depth = depth,
                SourceOrder = order++,
                Parent = parent
            };

            foreach (var child in BuildChapters(nav.NestedItems, pathToSpine, untitledLabel, depth + 1, item, ref order))
                item.Children.Add(child);

            list.Add(item);
        }

        return list;
    }

    /// <summary>
    /// Percent-encodes each segment of a book-relative path for use in an epub.local URL.
    /// VersOne.Epub hands back decoded paths, so a file named "C#.xhtml" or "100%.xhtml"
    /// must be re-escaped or the '#', '?' or '%' is read as URL syntax.
    /// </summary>
    internal static string ToUrlPath(string relativePath) =>
        string.Join('/', NormalizePath(relativePath).Split('/').Select(Uri.EscapeDataString));

    internal static string EscapeHtmlAttribute(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static void WriteContinuousDocument(string extractRoot, List<string> spinePaths, string[] spineTitles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\"/>");
        sb.AppendLine("<style>");
        sb.AppendLine("html,body{margin:0;padding:0;background:transparent;}");
        sb.AppendLine("iframe.elr-spine{width:100%;border:0;display:block;height:60vh;}");
        sb.AppendLine("section.elr-section{margin:0;padding:0;}");
        sb.AppendLine("</style></head><body>");

        for (int i = 0; i < spinePaths.Count; i++)
        {
            var src = EscapeHtmlAttribute(ToUrlPath(spinePaths[i]));
            var title = EscapeHtmlAttribute(spineTitles[i]);
            sb.AppendLine($"<section class=\"elr-section\" id=\"spine-{i}\" data-spine=\"{i}\">");
            // data-src, not src: frames load on demand as they approach the viewport.
            sb.AppendLine($"<iframe class=\"elr-spine\" title=\"{title}\" data-src=\"/{src}\" scrolling=\"no\"></iframe>");
            sb.AppendLine("</section>");
        }

        // No whitespace between the tags and the script text: the CSP hash below is
        // computed over exactly these characters.
        sb.Append("<script>").Append(ContinuousScript).AppendLine("</script></body></html>");

        File.WriteAllText(Path.Combine(extractRoot, ContinuousFileName), sb.ToString(), Encoding.UTF8);
    }

    /// <summary>The continuous document's only script. Book documents are served with
    /// script-src 'none'; the continuous document is allowed exactly this script by hash.
    /// Line endings are normalized to LF because the HTML parser normalizes CRLF before
    /// the browser hashes the script, and a Windows checkout (core.autocrlf) would
    /// otherwise compile this literal with CRLF and break the hash match.</summary>
    internal static readonly string ContinuousScript = ContinuousScriptSource.Replace("\r\n", "\n");

    private const string ContinuousScriptSource = """
            (function(){
              var spineFrames = Array.prototype.slice.call(document.querySelectorAll('iframe.elr-spine'));
              var sections = Array.prototype.slice.call(document.querySelectorAll('section.elr-section'));
              var pin = null; // {spine, fraction, expires} — hold position while nearby frames load

              function scroller(){ return document.scrollingElement || document.documentElement; }

              function resize(f){
                try {
                  var doc = f.contentDocument;
                  if (!doc || !doc.documentElement) return;
                  var h = Math.max(
                    doc.documentElement.scrollHeight,
                    doc.body ? doc.body.scrollHeight : 0,
                    200);
                  if (Math.abs((parseFloat(f.style.height) || 0) - h) > 1) {
                    f.style.height = h + 'px';
                    reapplyPin();
                  }
                } catch (e) {}
              }

              function reapplyPin(){
                if (!pin || performance.now() > pin.expires) { pin = null; return; }
                var sec = sections[pin.spine];
                if (!sec) return;
                scroller().scrollTop = sec.offsetTop + pin.fraction * Math.max(0, sec.offsetHeight);
              }

              function loadFrame(f){
                if (!f || f.getAttribute('src')) return;
                f.src = f.dataset.src;
              }

              spineFrames.forEach(function(f){
                f.addEventListener('load', function(){
                  resize(f);
                  try {
                    // Push the parent's current display settings into the new frame.
                    if (f.contentWindow && f.contentWindow.__elrApply)
                      f.contentWindow.__elrApply(window.__elrLastSettings || null);
                  } catch (e) {}
                  try {
                    var doc = f.contentDocument;
                    var RO = f.contentWindow ? f.contentWindow.ResizeObserver : null;
                    if (doc && typeof RO === 'function') {
                      var ro = new RO(function(){ resize(f); });
                      ro.observe(doc.documentElement);
                      if (doc.body) ro.observe(doc.body);
                    }
                    if (f.contentWindow) f.contentWindow.addEventListener('resize', function(){ resize(f); });
                  } catch (e) {}
                });
              });

              var io = new IntersectionObserver(function(entries){
                entries.forEach(function(en){
                  if (en.isIntersecting)
                    loadFrame(en.target.querySelector('iframe.elr-spine'));
                });
              }, { rootMargin: '150% 0px 150% 0px' });
              sections.forEach(function(s){ io.observe(s); });

              // Theme/typography changes applied to this parent document flow into
              // every loaded chapter frame as well.
              var baseApply = window.__elrApply;
              if (baseApply) {
                window.__elrApply = function(s){
                  baseApply(s);
                  var eff = window.__elrLastSettings || null;
                  spineFrames.forEach(function(f){
                    try {
                      if (f.getAttribute('src') && f.contentWindow && f.contentWindow.__elrApply)
                        f.contentWindow.__elrApply(eff);
                    } catch (e) {}
                  });
                  // Typography changes alter chapter heights; re-measure once
                  // the new styles have applied.
                  setTimeout(function(){ spineFrames.forEach(resize); }, 60);
                };
              }

              window.__elrEnsureSpineLoaded = function(n){ loadFrame(spineFrames[n]); };

              window.__elrIsSpineLoaded = function(n){
                var f = spineFrames[n];
                try {
                  return !!(f && f.getAttribute('src') && f.contentDocument &&
                            f.contentDocument.readyState === 'complete');
                } catch (e) { return false; }
              };

              window.__elrSpinePos = function(){
                var y = scroller().scrollTop + 8;
                var cur = 0;
                for (var i = 0; i < sections.length; i++) {
                  if (sections[i].offsetTop <= y) cur = i; else break;
                }
                var sec = sections[cur];
                var frac = Math.max(0, Math.min(1, (y - sec.offsetTop) / Math.max(1, sec.offsetHeight)));
                return { spine: cur, fraction: frac };
              };

              window.__elrContinuousGoTo = function(n, frac){
                if (n < 0 || n >= sections.length) return false;
                loadFrame(spineFrames[n]);
                frac = (typeof frac === 'number' && isFinite(frac)) ? Math.max(0, Math.min(1, frac)) : 0;
                pin = { spine: n, fraction: frac, expires: performance.now() + 2500 };
                reapplyPin();
                return true;
              };

              window.__elrFindInSpine = function(n, query, forward){
                var f = spineFrames[n];
                if (!f || !query) return false;
                try {
                  var w = f.contentWindow;
                  if (!w || !w.find) return false;
                  var found = w.find(query, false, !forward, true, false, false, false);
                  if (!found) {
                    try { w.getSelection().removeAllRanges(); } catch (e) {}
                    found = w.find(query, false, !forward, true, false, false, false);
                  }
                  if (found) {
                    var offset = 0;
                    try {
                      var sel = w.getSelection();
                      if (sel && sel.rangeCount) offset = sel.getRangeAt(0).getBoundingClientRect().top;
                    } catch (e) {}
                    var se = scroller();
                    pin = null;
                    se.scrollTop = Math.max(0, f.getBoundingClientRect().top + se.scrollTop + offset - se.clientHeight * 0.3);
                  }
                  return found;
                } catch (e) { return false; }
              };

              // Report the logical reading position (spine + fraction inside it)
              // so the host can persist something stable under lazy loading.
              var posTimer = null;
              window.addEventListener('scroll', function(){
                if (posTimer) clearTimeout(posTimer);
                posTimer = setTimeout(function(){
                  try {
                    var p = window.__elrSpinePos();
                    if (window.chrome && window.chrome.webview)
                      window.chrome.webview.postMessage({ type: 'spinepos', spine: p.spine, fraction: p.fraction });
                  } catch (e) {}
                }, 120);
              }, { passive: true });

              function onHash(){
                var m = /^#spine-(\d+)$/.exec(location.hash || '');
                if (m) window.__elrContinuousGoTo(parseInt(m[1], 10), 0);
              }
              window.addEventListener('hashchange', onHash);
              if (document.readyState !== 'loading') onHash();
              else document.addEventListener('DOMContentLoaded', onHash);
            })();
            """;

    /// <summary>CSP source expression ('sha256-…') for <see cref="ContinuousScript"/>.</summary>
    internal static readonly string ContinuousScriptCspSource =
        "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(ContinuousScript))) + "'";

    /// <summary>Stable per-book key for persisted state. It deliberately includes the full
    /// file path, so the same book at two locations keeps two independent reading
    /// positions; moving or renaming the file starts it fresh.</summary>
    private static string ComputeBookId(string path, EpubBook book)
    {
        var id = book.Schema?.Package?.Metadata?.Identifiers?
            .Select(i => i.Identifier)
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

        var raw = $"{path}|{id}|{book.Title}|{book.Author}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Maps an EPUB entry path to a destination that is guaranteed to resolve under
    /// the extraction root. Rejects rooted paths, parent escapes, empty/dot segments,
    /// drive or stream separators, reserved device names, our generated continuous
    /// file name, and anything the path APIs cannot normalize.
    /// </summary>
    internal static bool TryMapToDisk(string extractRoot, string entryPath, out string dest)
    {
        dest = "";
        if (string.IsNullOrWhiteSpace(entryPath)) return false;

        var norm = NormalizePath(entryPath);
        if (norm.Length == 0 || norm.Contains(':')) return false;
        if (string.Equals(norm, ContinuousFileName, StringComparison.OrdinalIgnoreCase)) return false;

        foreach (var segment in norm.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..") return false;
            if (segment[^1] is '.' or ' ') return false;
            var stem = segment.Split('.')[0];
            if (ReservedDeviceNames.Contains(stem)) return false;
        }

        try
        {
            if (Path.IsPathRooted(norm)) return false;
            var rootFull = Path.GetFullPath(extractRoot);
            var combined = Path.GetFullPath(Path.Combine(rootFull, norm.Replace('/', Path.DirectorySeparatorChar)));
            if (!combined.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;
            dest = combined;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    /// <summary>Extensions the virtual host serves with a MIME type Chromium never treats
    /// as a script-capable document and never HTML-sniffs: images, fonts, audio/video,
    /// and CSS. A resource under one of these is safe to leave unsanitized (StripScripts
    /// is skipped), as long as its declared media-type does not itself claim html/xml. A
    /// byte entry (image, font, audio/video) is then written byte-for-byte; a text entry
    /// (CSS) is written via File.WriteAllTextAsync with Encoding.UTF8, which re-encodes
    /// it and prepends a UTF-8 BOM, so that one is unsanitized but not byte-identical.
    /// </summary>
    private static readonly HashSet<string> PassiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".avif", ".tif", ".tiff",
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        ".mp3", ".mp4", ".m4a", ".m4v", ".aac", ".ogg", ".oga", ".opus", ".webm", ".wav", ".flac",
        ".css"
    };

    /// <summary>Extensions Chromium can render or parse as markup (HTML/XML/SVG and
    /// friends), regardless of what the manifest declares for the entry. Kept in step
    /// with Chromium's built-in extension table (net/base/mime_util.cc) and with
    /// <see cref="BookResourceServer"/>'s served Content-Type map.</summary>
    private static readonly HashSet<string> MarkupExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xhtml", ".xht", ".xhtm", ".html", ".htm", ".shtml", ".shtm", ".ehtml",
        ".xml", ".svg", ".xsl", ".xslt", ".xbl", ".xul", ".rdf", ".rss", ".mht", ".mhtml"
    };

    internal static bool IsPassiveExtension(string extension) => PassiveExtensions.Contains(extension);

    internal static bool IsMarkupExtension(string extension) => MarkupExtensions.Contains(extension);

    /// <summary>Whether the EPUB manifest declares this entry's media-type as HTML or
    /// XML-like (this includes image/svg+xml). The declared media-type is
    /// attacker-controlled, so this must never be the only signal used to decide
    /// whether content is sanitized — see <see cref="IsPassiveExtension"/> and
    /// <see cref="IsMarkupExtension"/>.</summary>
    private static bool IsDeclaredHtmlLike(string? mime) =>
        mime is not null &&
        (mime.Contains("html", StringComparison.OrdinalIgnoreCase) ||
         mime.Contains("xml", StringComparison.OrdinalIgnoreCase));

    /// <summary>Decodes a resource's raw bytes to text, honoring a UTF-8 or UTF-16
    /// (LE/BE) byte-order mark and defaulting to UTF-8 when none is present.</summary>
    internal static string DecodeResourceText(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// True when, after an optional UTF-8/UTF-16(LE/BE) byte-order mark and leading
    /// ASCII whitespace, the first code unit is '&lt;' — the same leading-byte sniff
    /// Chromium uses to recognize HTML/XML content no matter what Content-Type it was
    /// served with. Works directly on the raw bytes with no full decode and no fixed
    /// lookahead window (an earlier, 1024-byte-prefix version of this sniff could be
    /// pushed past with more leading whitespace than the window covered); this is
    /// O(leading whitespace), not O(resource size) or bounded by a window.
    /// </summary>
    internal static bool LooksLikeMarkup(byte[] content)
    {
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
            return LooksLikeMarkupSingleByte(content, 3);
        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
            return LooksLikeMarkupUtf16(content, 2, littleEndian: true);
        if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
            return LooksLikeMarkupUtf16(content, 2, littleEndian: false);
        // No BOM: same "default UTF-8" assumption as DecodeResourceText, and ASCII
        // whitespace/'<' are single bytes under UTF-8 regardless of what follows.
        return LooksLikeMarkupSingleByte(content, 0);
    }

    private static bool LooksLikeMarkupSingleByte(byte[] content, int start)
    {
        int i = start;
        while (i < content.Length && IsAsciiWhitespaceByte(content[i])) i++;
        return i < content.Length && content[i] == (byte)'<';
    }

    private static bool LooksLikeMarkupUtf16(byte[] content, int start, bool littleEndian)
    {
        int i = start;
        while (i + 1 < content.Length)
        {
            var low = littleEndian ? content[i] : content[i + 1];
            var high = littleEndian ? content[i + 1] : content[i];
            if (high != 0) return false; // not an ASCII code unit: not whitespace, not '<'
            if (!IsAsciiWhitespaceByte(low)) return low == (byte)'<';
            i += 2;
        }
        return false;
    }

    private static bool IsAsciiWhitespaceByte(byte b) =>
        b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f' or (byte)'\v';

    // A namespace-prefixed element ("svg:script") is a distinct tag name to an XML
    // parser but is still the real script element the namespace resolves to - allow
    // an optional prefix wherever "script" appears; the open and close tag's
    // prefixes need not match each other, since a sanitizer should err toward
    // stripping too much rather than missing a real script.
    private const string ScriptNamePattern = @"(?:[A-Za-z_][\w.-]*:)?script";

    private static readonly Regex ScriptTagRegex = new(
        $@"<{ScriptNamePattern}\b[^>]*>[\s\S]*?</{ScriptNamePattern}\s*>|<\s*{ScriptNamePattern}\b[^>]*/>",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    private static readonly Regex ResidualScriptOpenRegex = new(
        $@"<\s*{ScriptNamePattern}\b",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    // The HTML5 tokenizer starts a new attribute right after "/" or the closing
    // quote of the previous attribute's value, not only after whitespace (this is a
    // parse error, but the attribute is still created) - a lookbehind for any of
    // those separators, instead of consuming leading "\s+", catches an event handler
    // in "<img/onerror=...>" or "<img src=\"x\"onerror=...>" without requiring space.
    // The lookbehind means this one can't use RegexOptions.NonBacktracking (unlike
    // every other regex below); SanitizerPerformanceTests measures that it still
    // stays linear on adversarial input instead of just assuming so.
    private static readonly Regex EventAttrRegex = new(
        @"(?<=[\s/""'])on\w+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A start tag, quote-aware so a '>' inside an attribute value does not end it.
    // Event-handler removal is confined to these matches: run over the whole document it
    // also deleted prose and code ("int one = 1;") and, through the unquoted-value branch,
    // could swallow a following end tag and break XHTML well-formedness.
    private static readonly Regex StartTagRegex = new(
        @"<[A-Za-z](?:[^>""']|""[^""]*""|'[^']*')*>",
        RegexOptions.NonBacktracking);

    private static readonly Regex LinkTagRegex = new(
        @"<link\b(?:[^>""']|""[^""]*""|'[^']*')*>",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    private static readonly Regex RelAttrRegex = new(
        @"\brel\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    private static readonly Regex XmlStylesheetPiRegex = new(
        @"<\?xml-stylesheet\b[\s\S]*?\?>",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    private static readonly Regex PiTypeAttrRegex = new(
        @"\btype\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)')",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    /// <summary>
    /// Sanitizes EPUB-supplied HTML/XML: removes script elements — including a
    /// namespace-prefixed one (&lt;svg:script&gt; is a real, executing element to
    /// an XML parser) and an unclosed one, since an HTML5 parser treats everything
    /// after it as script data through EOF — event-handler attributes inside start
    /// tags (even one the HTML5 tokenizer starts without preceding whitespace, e.g.
    /// right after "/" or a closing attribute quote; text content is left alone), &lt;link&gt; network hints (preconnect /
    /// dns-prefetch), which Chromium acts on without ever raising a
    /// WebResourceRequested event, and a non-CSS &lt;?xml-stylesheet?&gt; processing
    /// instruction, since Chromium runs XSLT for one and an XSL stylesheet can emit
    /// a &lt;script&gt; element no regex over this document could ever see. Left
    /// unhandled, any of these would defeat the "book content cannot make any
    /// network request" and "scripts are stripped" guarantees.
    /// The tail truncation for a residual unclosed &lt;script&gt; is deliberate:
    /// a browser would not render content after such a tag either, so dropping
    /// it loses nothing a reader could see and guarantees no script survives.
    /// Pass order and replacement text both matter, and neither is arbitrary: a
    /// pass that removes a whole delimited token (a PI, a link, a script pair)
    /// replaces it with a single space rather than "", because a space can never
    /// be part of "&lt;script", "&lt;link", "&lt;?xml-stylesheet", or "on\w+=" — so
    /// text left on either side of a removal can never splice together into a
    /// token an EARLIER pass already scanned for (e.g. "&lt;scr" + a removed link +
    /// "ipt&gt;" must never re-form "&lt;script&gt;"). The event-handler pass runs
    /// last and keeps its "" replacement: its lookbehind only fires on an already
    /// genuine separator (whitespace, "/", or a closing quote — including a space
    /// left behind by an earlier pass), so it can't be spliced into by anything
    /// that still has to run after it. It also only rewrites the inside of a single
    /// start-tag match, so it can never reach across a '&gt;' into text or another tag.
    /// This sanitizer is defense-in-depth: <see cref="BookResourceServer"/> serves every
    /// book document with a script-src 'none' Content-Security-Policy.
    /// </summary>
    internal static string StripScripts(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        var cleaned = XmlStylesheetPiRegex.Replace(html, m => IsNonCssStylesheetPi(m.Value) ? " " : m.Value);
        cleaned = LinkTagRegex.Replace(cleaned, m => IsNetworkHintLink(m.Value) ? " " : m.Value);
        cleaned = ScriptTagRegex.Replace(cleaned, " ");
        // An unclosed <script> start tag flips the HTML5 parser into script-data
        // state until EOF: drop everything from a residual start tag onward.
        var residual = ResidualScriptOpenRegex.Match(cleaned);
        if (residual.Success)
            cleaned = cleaned[..residual.Index];
        cleaned = StartTagRegex.Replace(cleaned, m => EventAttrRegex.Replace(m.Value, ""));
        return cleaned;
    }

    private static bool IsNetworkHintLink(string tag)
    {
        var rel = RelAttrRegex.Match(tag);
        if (!rel.Success) return false;
        // HTML5 rel is a space-separated token list where "space" includes
        // tab/LF/FF/CR; split on all whitespace so "stylesheet\tpreconnect"
        // cannot slip through as a single unknown token.
        foreach (var token in rel.Groups["v"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("preconnect", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("dns-prefetch", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Chromium treats an &lt;?xml-stylesheet?&gt; PI as CSS - and otherwise ignores
    /// it - only when its "type" pseudo-attribute is absent, empty, or "text/css";
    /// any other type (text/xsl, application/xslt+xml, application/xml, ...) makes
    /// it run XSLT, which can synthesize a script element.
    /// </summary>
    private static bool IsNonCssStylesheetPi(string pi)
    {
        var type = PiTypeAttrRegex.Match(pi);
        if (!type.Success) return false;
        var value = type.Groups["v"].Value.Trim();
        return value.Length > 0 && !value.Equals("text/css", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.NonBacktracking);
    private static readonly Regex WsRegex = new(@"\s+", RegexOptions.NonBacktracking);

    internal static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var noScript = StripScripts(html);
        var text = TagRegex.Replace(noScript, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return WsRegex.Replace(text, " ").Trim();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of temp extract.
        }
    }
}

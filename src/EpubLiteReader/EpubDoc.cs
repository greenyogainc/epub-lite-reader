using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using VersOne.Epub;

namespace EpubLiteReader;

/// <summary>Opened EPUB: extracted local content, spine, TOC, and metadata.</summary>
public sealed class EpubDoc : IDisposable
{
    public const string VirtualHost = "epub.local";
    public const string ContinuousFileName = "elr-continuous.html";

    private readonly string _extractRoot;
    private bool _disposed;

    public string FilePath { get; }
    public string BookId { get; }
    public string Title { get; }
    public string Author { get; }
    public string? Description { get; }
    public byte[]? CoverImage { get; }

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
        byte[]? coverImage,
        string extractRoot,
        IReadOnlyList<string> spinePaths,
        IReadOnlyList<string> spinePlainText)
    {
        FilePath = filePath;
        BookId = bookId;
        Title = title;
        Author = author;
        Description = description;
        CoverImage = coverImage;
        _extractRoot = extractRoot;
        SpinePaths = spinePaths;
        SpinePlainText = spinePlainText;
    }

    private List<ChapterItem>? _chapters;

    public List<ChapterItem> GetChapters() => _chapters ?? new List<ChapterItem>();

    /// <summary>Entries skipped during extraction because their paths were unsafe or collided.</summary>
    public IReadOnlyList<string> SkippedEntries { get; private set; } = Array.Empty<string>();

    public static Task<(EpubDoc Doc, List<ChapterItem> Chapters)> OpenWithChaptersAsync(
        string path, string untitledLabel, string? sectionTitleFormat = null, CancellationToken ct = default) =>
        // Parsing, extraction, and text conversion are CPU/IO heavy; keep all of it
        // off the caller's (dispatcher) thread so large books cannot freeze the UI.
        Task.Run(() => OpenWithChaptersCoreAsync(path, untitledLabel, sectionTitleFormat, ct), ct);

    private static async Task<(EpubDoc Doc, List<ChapterItem> Chapters)> OpenWithChaptersCoreAsync(
        string path, string untitledLabel, string? sectionTitleFormat, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        var book = await EpubReader.ReadBookAsync(path);
        ct.ThrowIfCancellationRequested();

        var extractRoot = Path.Combine(Path.GetTempPath(), "EpubLiteReader", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);

        try
        {
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skipped = new List<string>();
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
                    if (file is EpubLocalTextContentFile text)
                    {
                        var content = IsHtmlLike(file.FilePath, text.ContentMimeType)
                            ? StripScripts(text.Content)
                            : text.Content;
                        await File.WriteAllTextAsync(dest, content, Encoding.UTF8, ct);
                    }
                    else if (file is EpubLocalByteContentFile bytes)
                    {
                        await File.WriteAllBytesAsync(dest, bytes.Content, ct);
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
                spineText.Add(HtmlToPlainText(item.Content));
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
                book.CoverImage,
                extractRoot,
                spinePaths,
                spineText)
            {
                _chapters = chapters,
                SkippedEntries = skipped
            };

            return (doc, chapters);
        }
        catch
        {
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
        var url = $"https://{VirtualHost}/{path}";
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
            var src = EscapeHtmlAttribute(spinePaths[i]);
            var title = EscapeHtmlAttribute(spineTitles[i]);
            sb.AppendLine($"<section class=\"elr-section\" id=\"spine-{i}\" data-spine=\"{i}\">");
            // data-src, not src: frames load on demand as they approach the viewport.
            sb.AppendLine($"<iframe class=\"elr-spine\" title=\"{title}\" data-src=\"/{src}\" scrolling=\"no\"></iframe>");
            sb.AppendLine("</section>");
        }

        sb.AppendLine("<script>");
        sb.AppendLine("""
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
                    if (doc && typeof ResizeObserver === 'function') {
                      var ro = new ResizeObserver(function(){ resize(f); });
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
            """);
        sb.AppendLine("</script></body></html>");

        File.WriteAllText(Path.Combine(extractRoot, ContinuousFileName), sb.ToString(), Encoding.UTF8);
    }

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

    private static bool IsHtmlLike(string filePath, string? mime)
    {
        if (mime is not null &&
            (mime.Contains("html", StringComparison.OrdinalIgnoreCase) ||
             mime.Contains("xml", StringComparison.OrdinalIgnoreCase)))
            return true;
        var ext = Path.GetExtension(filePath);
        return ext is ".xhtml" or ".html" or ".htm" or ".xml" or ".svg";
    }

    private static readonly Regex ScriptTagRegex = new(
        @"<script\b[^>]*>[\s\S]*?</script\s*>|<\s*script\b[^>]*/>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EventAttrRegex = new(
        @"\s+on\w+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string StripScripts(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        var cleaned = ScriptTagRegex.Replace(html, "");
        cleaned = EventAttrRegex.Replace(cleaned, "");
        return cleaned;
    }

    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WsRegex = new(@"\s+", RegexOptions.Compiled);

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

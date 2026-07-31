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

    public static async Task<(EpubDoc Doc, List<ChapterItem> Chapters)> OpenWithChaptersAsync(
        string path, string untitledLabel, CancellationToken ct = default)
    {
        path = Path.GetFullPath(path);
        var book = await EpubReader.ReadBookAsync(path);
        ct.ThrowIfCancellationRequested();

        var extractRoot = Path.Combine(Path.GetTempPath(), "EpubLiteReader", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);

        try
        {
            foreach (var file in book.Content.AllFiles.Local)
            {
                ct.ThrowIfCancellationRequested();
                var dest = MapToDisk(extractRoot, file.FilePath);
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

            var spinePaths = new List<string>();
            var spineText = new List<string>();
            var pathToSpine = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in book.ReadingOrder)
            {
                var rel = NormalizePath(item.FilePath);
                pathToSpine[rel] = spinePaths.Count;
                // Also index by filename-only for loose matches
                pathToSpine.TryAdd(Path.GetFileName(rel), spinePaths.Count);
                spinePaths.Add(rel);
                spineText.Add(HtmlToPlainText(item.Content));
            }

            WriteContinuousDocument(extractRoot, spinePaths);

            var order = 0;
            var chapters = BuildChapters(book.Navigation, pathToSpine, untitledLabel, depth: 0, parent: null, ref order);

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
                _chapters = chapters
            };

            return (doc, chapters);
        }
        catch
        {
            TryDeleteDirectory(extractRoot);
            throw;
        }
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

    private static void WriteContinuousDocument(string extractRoot, List<string> spinePaths)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\"/>");
        sb.AppendLine("<style>");
        sb.AppendLine("html,body{margin:0;padding:0;background:transparent;}");
        sb.AppendLine("iframe.elr-spine{width:100%;border:0;display:block;min-height:40vh;}");
        sb.AppendLine("section.elr-section{margin:0;padding:0;}");
        sb.AppendLine("</style></head><body>");

        for (int i = 0; i < spinePaths.Count; i++)
        {
            var src = spinePaths[i].Replace("\"", "&quot;");
            sb.AppendLine($"<section class=\"elr-section\" id=\"spine-{i}\" data-spine=\"{i}\">");
            sb.AppendLine($"<iframe class=\"elr-spine\" title=\"spine-{i}\" src=\"/{src}\" scrolling=\"no\"></iframe>");
            sb.AppendLine("</section>");
        }

        sb.AppendLine("<script>");
        sb.AppendLine("""
            (function(){
              function resize(f){
                try {
                  var doc = f.contentDocument || f.contentWindow.document;
                  if (!doc) return;
                  var h = Math.max(
                    doc.documentElement.scrollHeight,
                    doc.body ? doc.body.scrollHeight : 0,
                    200);
                  f.style.height = h + 'px';
                } catch (e) {}
              }
              function resizeAll(){
                document.querySelectorAll('iframe.elr-spine').forEach(resize);
              }
              document.querySelectorAll('iframe.elr-spine').forEach(function(f){
                f.addEventListener('load', function(){ resize(f); });
              });
              window.addEventListener('load', resizeAll);
              setInterval(resizeAll, 1000);
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

    private static string MapToDisk(string extractRoot, string filePath)
    {
        var norm = NormalizePath(filePath).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(extractRoot, norm);
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

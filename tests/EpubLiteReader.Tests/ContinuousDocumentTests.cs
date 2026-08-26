using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>Exercises the lazily-loaded "continuous scroll" document EpubDoc generates
/// for every book (elr-continuous.html), using a programmatically-built 150-spine
/// fixture that the checked-in sample.epub is too small to exercise.</summary>
public sealed class ContinuousDocumentTests : IDisposable
{
    private const int SpineCount = 150;
    private readonly string _epubPath;

    public ContinuousDocumentTests()
    {
        _epubPath = Path.Combine(Path.GetTempPath(), "elr-large-" + Guid.NewGuid().ToString("N") + ".epub");
        EpubFixtureBuilder.BuildLargeSpineEpub(_epubPath, SpineCount);
    }

    public void Dispose()
    {
        try { if (File.Exists(_epubPath)) File.Delete(_epubPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task OpenWithChaptersAsync_GeneratesLazyContinuousDocumentForLargeBook()
    {
        var sw = Stopwatch.StartNew();
        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled", sectionTitleFormat: "Sect {0}");
        try
        {
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60),
                $"Opening the {SpineCount}-spine book took too long: {sw.Elapsed}");

            var html = File.ReadAllText(Path.Combine(doc.ExtractRoot, EpubDoc.ContinuousFileName));

            Assert.Equal(SpineCount, CountOccurrences(html, "data-src="));
            // "data-src=" itself contains "src=", so check for the real attribute form
            // (preceded by whitespace) to make sure no iframe eagerly loads its content.
            Assert.DoesNotContain(" src=\"", html);

            Assert.DoesNotContain("setInterval", html);
            Assert.Contains("IntersectionObserver", html);
            Assert.Contains("ResizeObserver", html);
            Assert.Contains("__elrContinuousGoTo", html);
            Assert.Contains("__elrFindInSpine", html);
            Assert.Contains("__elrSpinePos", html);
            Assert.Contains("spinepos", html);

            // ch005 is nav-titled with characters that must round-trip through
            // HTML-attribute escaping in the generated iframe title.
            Assert.Contains("&lt;5&gt;", html);
            Assert.Contains("&amp;", html);
            Assert.Contains("&quot;", html);

            // ch001 has no nav entry (only every 10th chapter is listed) -> numbered fallback.
            Assert.Contains("title=\"Sect 2\"", html);
        }
        finally
        {
            doc.Dispose();
        }
    }

    [Fact]
    public async Task Search_RespectsMaxResultsCap()
    {
        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(_epubPath, "Untitled");
        try
        {
            var capped = doc.Search(EpubFixtureBuilder.SearchWord, maxResults: 37).ToList();
            var uncapped = doc.Search(EpubFixtureBuilder.SearchWord, maxResults: 10_000).ToList();

            Assert.Equal(37, capped.Count);
            Assert.Equal(SpineCount, uncapped.Count);
        }
        finally
        {
            doc.Dispose();
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}

using System.IO;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>Exercises EpubDoc.TryMapToDisk, the guard that keeps EPUB zip entries from
/// escaping the per-book extraction directory.</summary>
public sealed class EpubPathSafetyTests : IDisposable
{
    private readonly string _root;

    public EpubPathSafetyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "elr-pathsafety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData("OEBPS/ch1.xhtml")]
    [InlineData("a/b/c.css")]
    [InlineData("images/图片.png")]
    public void TryMapToDisk_AcceptsSafeRelativePaths(string entryPath)
    {
        var ok = EpubDoc.TryMapToDisk(_root, entryPath, out var dest);

        Assert.True(ok);
        var rootFull = Path.GetFullPath(_root);
        Assert.StartsWith(rootFull + Path.DirectorySeparatorChar, dest, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a/../../evil.txt")]
    [InlineData("..")]
    [InlineData("C:\\evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("a:b.txt")]
    [InlineData("con.css")]
    [InlineData("COM1")]
    [InlineData("nul.txt")]
    [InlineData("aux/x.css")]
    [InlineData("elr-continuous.html")]
    [InlineData("ELR-CONTINUOUS.HTML")]
    [InlineData("a//b.txt")]
    [InlineData("a/./b.txt")]
    [InlineData("dir./f.txt")]
    [InlineData("dir /f.txt")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryMapToDisk_RejectsUnsafePaths(string entryPath)
    {
        var ok = EpubDoc.TryMapToDisk(_root, entryPath, out _);

        Assert.False(ok);
    }

    // Deliberate design decision, asserted explicitly: EPUB hrefs are sometimes
    // written container-absolute ("/OEBPS/ch1.xhtml"). NormalizePath strips the
    // leading slashes so such entries map INSIDE the extract root rather than
    // being rejected — real books keep working while nothing can escape the
    // root (Windows-rooted forms like "C:\evil" are rejected by the ':' check,
    // and the resolved destination is verified to stay under the root).

    [Theory]
    [InlineData("/rooted.txt")]
    [InlineData("\\rooted.txt")]
    [InlineData("//OEBPS/ch1.xhtml")]
    public void TryMapToDisk_ContainsContainerAbsolutePathsInsideRoot(string entry)
    {
        var ok = EpubDoc.TryMapToDisk(_root, entry, out var dest);
        Assert.True(ok);
        Assert.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, dest,
            StringComparison.OrdinalIgnoreCase);
    }
}

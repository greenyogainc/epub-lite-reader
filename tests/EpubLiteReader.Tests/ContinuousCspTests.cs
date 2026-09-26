using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>
/// F1: book documents are served with a script-src 'none' CSP, so the continuous
/// document — the one page that legitimately needs an inline script — must carry a CSP
/// hash that matches the script it actually emits. A drift here would silently break
/// continuous mode once the CSP is enforced.
/// </summary>
public sealed class ContinuousCspTests
{
    [Fact]
    public void ContinuousScriptCspSource_IsSha256OfTheScript()
    {
        var expected = "'sha256-" +
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(EpubDoc.ContinuousScript))) + "'";

        Assert.Equal(expected, EpubDoc.ContinuousScriptCspSource);
    }

    [Fact]
    public async Task ContinuousDocument_InlineScript_MatchesTheCspHash()
    {
        var (doc, _) = await EpubDoc.OpenWithChaptersAsync(TestPaths.SampleEpubPath, "Untitled");
        try
        {
            var html = await File.ReadAllTextAsync(
                Path.Combine(doc.ExtractRoot, EpubDoc.ContinuousFileName));

            // Extract the exact text the browser would hash: everything between the
            // single <script> and </script> the document generator writes.
            const string open = "<script>";
            const string close = "</script>";
            var start = html.IndexOf(open, StringComparison.Ordinal);
            var end = html.IndexOf(close, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start, "continuous document has no <script> block");
            var scriptText = html.Substring(start + open.Length, end - (start + open.Length));

            // The browser normalizes newlines before hashing; the file is LF already.
            var hash = "'sha256-" +
                Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(scriptText))) + "'";

            Assert.Equal(EpubDoc.ContinuousScriptCspSource, hash);
        }
        finally
        {
            doc.Dispose();
        }
    }
}

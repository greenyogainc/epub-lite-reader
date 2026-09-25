using System;
using System.Diagnostics;
using System.Text;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>
/// Regression guard for a ReDoS class of bug found in StripScripts/HtmlToPlainText: a
/// backtracking regex whose repeated-alternation loop scans to EOF looking for a
/// delimiter that never appears does O(remaining length) work per candidate start
/// position, and a hostile EPUB can supply up to DefaultMaxResourceBytes (64 MB) of
/// exactly that shape - unclosed &lt;script&gt;, &lt;link&gt;, or &lt;?xml-stylesheet?&gt;
/// tokens repeated end to end. Every regex in EpubDoc.cs now runs on
/// RegexOptions.NonBacktracking except EventAttrRegex, which keeps a lookbehind
/// NonBacktracking does not support. These tests exercise each one at roughly 4 MB and
/// assert a generously loose time bound - there is no CI here, so the point is to catch
/// a return to quadratic behavior, not to pin exact timings.
/// </summary>
public sealed class SanitizerPerformanceTests
{
    private const int TargetBytes = 4_000_000;
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private static string Repeat(string unit, int targetBytes)
    {
        var count = targetBytes / unit.Length + 1;
        var sb = new StringBuilder(unit.Length * count);
        for (int i = 0; i < count; i++) sb.Append(unit);
        return sb.ToString();
    }

    private static void AssertFast(Action action, string label)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        Assert.True(sw.Elapsed < Bound,
            $"{label} took {sw.Elapsed.TotalSeconds:F2}s, expected under {Bound.TotalSeconds:F0}s");
    }

    [Fact]
    public void StripScripts_UnclosedScriptTags_StaysLinear()
    {
        var html = Repeat("<script>", TargetBytes);
        AssertFast(() => EpubDoc.StripScripts(html), nameof(StripScripts_UnclosedScriptTags_StaysLinear));
    }

    [Fact]
    public void StripScripts_UnclosedNamespacePrefixedScriptTags_StaysLinear()
    {
        var html = Repeat("<svg:script>", TargetBytes);
        AssertFast(() => EpubDoc.StripScripts(html), nameof(StripScripts_UnclosedNamespacePrefixedScriptTags_StaysLinear));
    }

    [Fact]
    public void StripScripts_UnclosedLinkTags_StaysLinear()
    {
        var html = Repeat("<link a ", TargetBytes);
        AssertFast(() => EpubDoc.StripScripts(html), nameof(StripScripts_UnclosedLinkTags_StaysLinear));
    }

    [Fact]
    public void StripScripts_UnclosedXmlStylesheetPis_StaysLinear()
    {
        var html = Repeat("<?xml-stylesheet type=\"text/xsl\" ", TargetBytes);
        AssertFast(() => EpubDoc.StripScripts(html), nameof(StripScripts_UnclosedXmlStylesheetPis_StaysLinear));
    }

    [Fact]
    public void HtmlToPlainText_UnclosedAngleBrackets_StaysLinear()
    {
        var html = new string('<', TargetBytes);
        AssertFast(() => EpubDoc.HtmlToPlainText(html), nameof(HtmlToPlainText_UnclosedAngleBrackets_StaysLinear));
    }

    // EventAttrRegex keeps its lookbehind (unsupported by NonBacktracking); the next
    // three cases measure that it is still linear on adversarial input rather than
    // just assuming so.

    [Fact]
    public void StripScripts_QuotedEventAttributeRepeats_StaysLinear()
    {
        var html = Repeat("\"onx=\"", TargetBytes);
        AssertFast(() => EpubDoc.StripScripts(html), nameof(StripScripts_QuotedEventAttributeRepeats_StaysLinear));
    }

    [Fact]
    public void StripScripts_UnterminatedEventAttributeName_StaysLinear()
    {
        var html = "/onx" + new string('a', TargetBytes);
        AssertFast(() => EpubDoc.StripScripts(html), nameof(StripScripts_UnterminatedEventAttributeName_StaysLinear));
    }

    [Fact]
    public void StripScripts_SingleQuotedEventAttributeRepeats_StaysLinear()
    {
        var html = Repeat(" onx='", TargetBytes);
        AssertFast(() => EpubDoc.StripScripts(html), nameof(StripScripts_SingleQuotedEventAttributeRepeats_StaysLinear));
    }
}

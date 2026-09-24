using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

public class SanitizerTests
{
    [Fact]
    public void StripScripts_RemovesScriptBlockAndItsContent()
    {
        var html = "<p>before</p><script>alert(1)</script><p>after</p>";

        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(1)", result);
        Assert.Contains("<p>before</p>", result);
        Assert.Contains("<p>after</p>", result);
    }

    [Fact]
    public void StripScripts_RemovesSelfClosingScriptTagWithSrc()
    {
        var html = "<p>a</p><script src=\"evil.js\"/><p>b</p>";

        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.js", result);
    }

    [Fact]
    public void StripScripts_RemovesMixedCaseMultilineScriptBlock()
    {
        var html = "<p>a</p><SCRIPT type=\"text/javascript\">\n  var x = 1;\n  alert(x);\n</SCRIPT><p>b</p>";

        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain("<SCRIPT", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(x)", result);
        Assert.Contains("<p>a</p>", result);
        Assert.Contains("<p>b</p>", result);
    }

    [Theory]
    [InlineData("<p onload=\"evil()\">x</p>", "onload=")]
    [InlineData("<p onclick='evil()'>x</p>", "onclick=")]
    [InlineData("<p onmouseover=evil()>x</p>", "onmouseover=")]
    public void StripScripts_RemovesOnEventAttributesRegardlessOfQuoting(string html, string attrPrefix)
    {
        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain(attrPrefix, result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil()", result);
    }

    [Fact]
    public void StripScripts_RemovesUnclosedScriptTagAndEverythingAfterIt()
    {
        // HTML5 parsing: an unclosed <script> consumes everything to EOF as script
        // data, so the sanitizer must drop the residual tail, not just paired tags.
        var html = "<p>keep</p><script>window.__elrApply=function(){document.body.innerHTML='pwned'};/*";

        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pwned", result);
        Assert.DoesNotContain("__elrApply", result);
        Assert.Contains("<p>keep</p>", result);
    }

    [Fact]
    public void StripScripts_RemovesUnclosedScriptTagWithAttributesAtEof()
    {
        var html = "<html><body><p>a</p><SCRIPT type=\"text/javascript\">alert(1)";

        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain("<SCRIPT", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(1)", result);
        Assert.Contains("<p>a</p>", result);
    }

    [Theory]
    [InlineData("<link rel=\"preconnect\" href=\"https://attacker.test\"/>")]
    [InlineData("<link rel='dns-prefetch' href='//attacker.test'>")]
    [InlineData("<link rel=\"stylesheet preconnect\" href=\"https://attacker.test/x.css\">")]
    [InlineData("<LINK REL=\"PRECONNECT\" HREF=\"https://attacker.test\"/>")]
    [InlineData("<link foo=\">\" rel=\"preconnect\" href=\"https://attacker.test\">")]
    [InlineData("<link rel=\"stylesheet\tpreconnect\" href=\"https://attacker.test\">")]
    [InlineData("<link rel=\"stylesheet\npreconnect\" href=\"https://attacker.test\">")]
    [InlineData("<link rel=\"stylesheet\rdns-prefetch\" href=\"https://attacker.test\">")]
    public void StripScripts_RemovesNetworkHintLinkTags(string linkTag)
    {
        // Chromium acts on preconnect/dns-prefetch without raising a
        // WebResourceRequested event, so they must be stripped at the source.
        var html = $"<head>{linkTag}</head><body><p>x</p></body>";

        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain("attacker.test", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preconnect", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dns-prefetch", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>x</p>", result);
    }

    [Fact]
    public void StripScripts_KeepsNonHintLinkTags()
    {
        var html = "<head><link rel=\"stylesheet\" href=\"style.css\"/><link rel=\"preload\" as=\"font\" href=\"f.woff2\"/></head>";

        var result = EpubDoc.StripScripts(html);

        Assert.Contains("style.css", result);
        Assert.Contains("f.woff2", result);
    }

    [Fact]
    public void HtmlToPlainText_StripsTagsAndDecodesEntities()
    {
        var html = "<p>Hello &amp; welcome</p>\n\n<p>Long dash &#8212; end</p>";

        var text = EpubDoc.HtmlToPlainText(html);

        Assert.DoesNotContain("<", text);
        Assert.DoesNotContain(">", text);
        Assert.Contains("Hello & welcome", text);
        Assert.Contains("—", text); // &#8212; decodes to an em dash
    }

    [Fact]
    public void HtmlToPlainText_CollapsesWhitespaceRuns()
    {
        var html = "<p>Long   dash   here</p>\n\n\n<p>next   line</p>";

        var text = EpubDoc.HtmlToPlainText(html);

        Assert.DoesNotContain("  ", text);
        Assert.Equal(text.Trim(), text);
    }

    [Fact]
    public void HtmlToPlainText_AlsoStripsScriptContent()
    {
        var html = "<p>keep</p><script>document.write('bad')</script>";

        var text = EpubDoc.HtmlToPlainText(html);

        Assert.DoesNotContain("document.write", text);
        Assert.Contains("keep", text);
    }
}

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

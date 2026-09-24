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

    [Theory]
    [InlineData("<p>keep</p><svg:script>alert(1)</svg:script><p>after</p>")]
    [InlineData("<p>keep</p><h:script xmlns:h=\"http://www.w3.org/1999/xhtml\">alert(1)</h:script><p>after</p>")]
    public void StripScripts_RemovesNamespacePrefixedScriptElement(string html)
    {
        // Element identity in an XML-parsed document (every .xhtml chapter, every
        // .svg) is by namespace, not prefix: a namespace-prefixed "script" element
        // is a real, executing script element, not just plain "<script>".
        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain(":script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(1)", result);
        Assert.Contains("<p>keep</p>", result);
        Assert.Contains("<p>after</p>", result);
    }

    [Fact]
    public void StripScripts_RemovesSelfClosingNamespacePrefixedScriptTag()
    {
        var html = "<p>a</p><svg:script href=\"x.js\"/><p>b</p>";

        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain(":script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("x.js", result);
        Assert.Contains("<p>a</p>", result);
        Assert.Contains("<p>b</p>", result);
    }

    [Fact]
    public void StripScripts_RemovesUnclosedNamespacePrefixedScriptTagAndEverythingAfterIt()
    {
        var html = "<p>keep</p><svg:script>alert('pwned')";

        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain(":script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pwned", result);
        Assert.Contains("<p>keep</p>", result);
    }

    [Theory]
    [InlineData("<img src=\"x\"onerror=\"alert(1)\">", "onerror", "src=\"x\"")]
    [InlineData("<img src='x'onerror='a()'>", "onerror", "src='x'")]
    [InlineData("<img/onerror=alert(1) src=x>", "onerror", "src=x")]
    [InlineData("<img src=\"x\"/onerror=\"a()\">", "onerror", "src=\"x\"")]
    [InlineData("<svg/onload=a()>", "onload", "<svg")]
    public void StripScripts_RemovesEventAttributesWithoutPrecedingWhitespace(
        string html, string attrName, string survivingSubstring)
    {
        // The HTML5 tokenizer starts a new attribute right after "/" or the closing
        // quote of the previous attribute's value, not only after whitespace - a
        // missing-whitespace parse error still leaves the attribute created.
        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain(attrName + "=", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(survivingSubstring, result);
    }

    [Theory]
    [InlineData("<?xml-stylesheet type=\"text/xsl\" href=\"t.xsl\"?>")]
    [InlineData("<?xml-stylesheet type='application/xml' href='t.xsl'?>")]
    [InlineData("<?xml-stylesheet type=\"TEXT/XSL\" href=\"t.xsl\"?>")]
    public void StripScripts_RemovesNonCssXmlStylesheetPi(string pi)
    {
        // Chromium runs XSLT for a stylesheet PI whose type is neither absent/empty
        // nor text/css, and an XSL stylesheet can emit a <script> element no regex
        // over this document could ever see - the PI itself must go.
        var html = $"<?xml version=\"1.0\"?>{pi}<html><body><p>x</p></body></html>";

        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain("xml-stylesheet", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>x</p>", result);
    }

    [Theory]
    [InlineData("<?xml-stylesheet type=\"text/css\" href=\"s.css\"?>")]
    [InlineData("<?xml-stylesheet href=\"s.css\"?>")]
    public void StripScripts_KeepsCssXmlStylesheetPi(string pi)
    {
        var html = $"<?xml version=\"1.0\"?>{pi}<html><body><p>x</p></body></html>";

        var result = EpubDoc.StripScripts(html);

        Assert.Contains(pi, result);
    }

    [Theory]
    [InlineData("<scr<link rel=\"preconnect\" href=\"x\">ipt>alert(1)</script>")]
    [InlineData("<scr<?xml-stylesheet type=\"text/xsl\" href=\"t.xsl\"?>ipt>alert(1)</script>")]
    public void StripScripts_DoesNotLetARemovedTokenSpliceIntoAScriptTag(string html)
    {
        // This guards ORDERING, not the space substitution by itself: with ""
        // instead of a space, the leftover "<scr" and "ipt>" fragments would
        // still rejoin into "<script>" here too, but because script-pair removal
        // is the very next pass after link/PI removal, that reformed tag would
        // still be caught immediately, before anything else could see it. See
        // StripScripts_DoesNotLetAScriptRemovalSpliceIntoALiveStylesheetPi below
        // for a splice that genuinely depends on the space, not just ordering.
        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripScripts_DoesNotLetAStylesheetPiSpliceIntoAnEventHandler()
    {
        // This guards ORDERING (the PI pass must run before the event-handler
        // pass): this crafted input already has a real space between "src=x"
        // and the PI, so removing the PI leaves that pre-existing space right
        // in front of onerror whether the removal replaces with "" or " ". What
        // actually matters is that the PI pass completes first - if it ran
        // after EventAttrRegex, onerror would be preceded by ">" (not a
        // separator) and EventAttrRegex would never see it at all.
        var html = "<img src=x <?xml-stylesheet type=\"text/xsl\"?>onerror=alert(1)>";

        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripScripts_DoesNotLetAScriptRemovalSpliceIntoALiveLinkTag()
    {
        // Removing the <script></script> pair with "" instead of a space would
        // rejoin "<li" and "nk rel=..." into a genuine "<link rel=\"preconnect\">"
        // network hint; the space keeps it a harmless (if odd) <li> element
        // instead. The word "preconnect" can still appear as inert attribute text
        // on that <li> - what must never happen is a live <link> element being
        // reformed, since the link-removal pass has already run by this point.
        var html = "<li<script></script>nk rel=\"preconnect\" href=\"//evil\">";

        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain("<link", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripScripts_DoesNotLetAScriptRemovalSpliceIntoALiveStylesheetPi()
    {
        // This one genuinely depends on the space, not just ordering: the PI
        // pass has ALREADY completed by the time script-pair removal runs, so
        // if script-pair removal replaced its match with "" instead of a space,
        // "<?xml-sty" + "" + "lesheet ...?>" would rejoin into a live, unremoved
        // "<?xml-stylesheet type=\"text/xsl\" ...?>" PI that nothing downstream
        // ever scans for again - the mirror image of
        // StripScripts_DoesNotLetAScriptRemovalSpliceIntoALiveLinkTag above.
        var html = "<?xml-sty<script></script>lesheet type=\"text/xsl\" href=\"t.xsl\"?>";

        var result = EpubDoc.StripScripts(html);

        Assert.DoesNotContain("<?xml-stylesheet", result, StringComparison.OrdinalIgnoreCase);
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

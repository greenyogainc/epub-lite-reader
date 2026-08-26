using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

public class ReaderUriTests
{
    [Theory]
    [InlineData("https://epub.local/x.html")]
    [InlineData("HTTPS://EPUB.LOCAL/x")]
    [InlineData("about:blank")]
    public void IsAllowedReaderUri_AllowsBookHostAndBlankPage(string uri)
    {
        Assert.True(ReadingHost.IsAllowedReaderUri(uri));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("about:srcdoc")]
    [InlineData("data:text/html,hi")]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:x")]
    [InlineData("http://epub.local/x")]
    [InlineData("https://epub.local.evil.com/x")]
    [InlineData("https://evil.com/")]
    [InlineData("file:///C:/x")]
    public void IsAllowedReaderUri_RejectsEverythingElse(string? uri)
    {
        Assert.False(ReadingHost.IsAllowedReaderUri(uri));
    }
}

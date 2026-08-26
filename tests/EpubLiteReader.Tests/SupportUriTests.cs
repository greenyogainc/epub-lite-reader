using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>Exercises AboutWindow.IsAllowedSupportUri directly as a static call - the
/// window itself is never instantiated here, only its allowlist logic.</summary>
public class SupportUriTests
{
    [Theory]
    [InlineData("https://greenyogainc.com/")]
    [InlineData("https://greenyogainc.com/contact/")]
    [InlineData("https://api.greenyogainc.com/anything")]
    public void IsAllowedSupportUri_AllowsExactAllowedHosts(string uri)
    {
        Assert.True(AboutWindow.IsAllowedSupportUri(uri));
    }

    [Theory]
    [InlineData("http://greenyogainc.com/")]
    [InlineData("https://www.greenyogainc.com/")]
    [InlineData("https://greenyogainc.com.evil.com/")]
    [InlineData("https://evilgreenyogainc.com/")]
    [InlineData("https://clarity.ms/")]
    [InlineData("https://www.googletagmanager.com/gtm.js")]
    [InlineData("mailto:x@greenyogainc.com")]
    [InlineData(null)]
    [InlineData("not a uri")]
    public void IsAllowedSupportUri_RejectsEverythingElse(string? uri)
    {
        Assert.False(AboutWindow.IsAllowedSupportUri(uri));
    }
}

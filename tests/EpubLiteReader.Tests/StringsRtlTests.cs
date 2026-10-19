using System.Globalization;
using System.Threading;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

public sealed class StringsRtlTests
{
    [Theory]
    [InlineData("ar", true)]
    [InlineData("en", false)]
    public void IsRightToLeft_IsArabicOnly(string cultureName, bool expected)
    {
        var prior = CultureInfo.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            Assert.Equal(expected, Strings.IsRightToLeft);
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = prior;
        }
    }
}

using System.Globalization;
using System.Threading;
using System.Windows;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>The dialog-free parts of the Strings lookup and the XAML markup extension.
/// ApplyFlowDirection and ShowError need a window, so the window suite covers them.</summary>
public sealed class StringsTests
{
    private static void UnderUiCulture(string cultureName, Action body)
    {
        var prior = CultureInfo.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            body();
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = prior;
        }
    }

    [Fact]
    public void Get_WithAnUnknownKey_ReturnsTheKeyItself() =>
        UnderUiCulture("en", () => Assert.Equal("NoSuchKey_ForTests", Strings.Get("NoSuchKey_ForTests")));

    [Fact]
    public void Get_WithAKnownKey_ReturnsTheLocalizedText()
    {
        UnderUiCulture("en", () => Assert.Equal("Open EPUB", Strings.Get("OpenFileDialogTitle")));
        UnderUiCulture("de", () => Assert.Equal("EPUB \u00f6ffnen", Strings.Get("OpenFileDialogTitle")));
    }

    [Theory]
    [InlineData("ar", MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign)]
    [InlineData("en", MessageBoxOptions.None)]
    [InlineData("he", MessageBoxOptions.None)] // right-to-left script, but no Hebrew translation ships
    public void MessageBoxOptions_MirrorOnlyForALanguageWeShipRightToLeft(string cultureName, MessageBoxOptions expected) =>
        UnderUiCulture(cultureName, () => Assert.Equal(expected, Strings.MessageBoxOptions));

    [Fact]
    public void LocExtension_ProvidesTheLocalizedStringForItsKey()
    {
        var extension = new LocExtension("AppTitle");

        Assert.Equal("AppTitle", extension.Key);
        UnderUiCulture("en", () => Assert.Equal(Strings.Get("AppTitle"), extension.ProvideValue(null!)));
    }

    [Fact]
    public void LocExtension_WithAnUnknownKey_ProvidesTheKey() =>
        UnderUiCulture("en", () => Assert.Equal("Missing_Key", new LocExtension("Missing_Key").ProvideValue(null!)));
}

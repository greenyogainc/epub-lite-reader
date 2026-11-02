using System.Globalization;
using System.Threading;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>DisplaySettings value semantics and the scripts ReaderInject builds from them.</summary>
public sealed class DisplaySettingsTests
{
    private static DisplaySettings NonDefault() => new()
    {
        Theme = ReadingTheme.Sepia,
        FontFamily = ReaderFontFamily.Serif,
        FontScale = 1.25,
        LineHeight = 1.8,
        MarginEm = 2.5,
        ViewMode = ViewMode.Continuous
    };

    [Fact]
    public void Clone_CopiesEveryFieldIntoANewInstance()
    {
        var original = NonDefault();

        var copy = original.Clone();

        Assert.NotSame(original, copy);
        Assert.Equal(ReadingTheme.Sepia, copy.Theme);
        Assert.Equal(ReaderFontFamily.Serif, copy.FontFamily);
        Assert.Equal(1.25, copy.FontScale);
        Assert.Equal(1.8, copy.LineHeight);
        Assert.Equal(2.5, copy.MarginEm);
        Assert.Equal(ViewMode.Continuous, copy.ViewMode);

        copy.Theme = ReadingTheme.Dark;
        Assert.Equal(ReadingTheme.Sepia, original.Theme); // independent after the copy
    }

    [Fact]
    public void ResetTypography_RestoresTypographyAndLeavesThemeAndViewModeAlone()
    {
        var settings = NonDefault();

        settings.ResetTypography();

        Assert.Equal(1.0, settings.FontScale);
        Assert.Equal(1.5, settings.LineHeight);
        Assert.Equal(1.2, settings.MarginEm);
        Assert.Equal(ReaderFontFamily.Publisher, settings.FontFamily);
        Assert.Equal(ReadingTheme.Sepia, settings.Theme);
        Assert.Equal(ViewMode.Continuous, settings.ViewMode);
    }

    [Fact]
    public void ApplySettingsScript_EmitsLowercaseNamesAndInvariantNumbers()
    {
        var priorCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            // A comma-decimal culture must not leak into the JSON the page evaluates.
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de");

            var script = ReaderInject.ApplySettingsScript(NonDefault());

            Assert.Equal(
                "if (window.__elrApply) window.__elrApply(" +
                "{\"theme\":\"sepia\",\"fontFamily\":\"serif\",\"fontScale\":1.25,\"lineHeight\":1.8,\"marginEm\":2.5});",
                script);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = priorCulture;
        }
    }

    [Fact]
    public void BuildDocumentCreatedScript_DefinesEveryEntryPointTheHostCalls()
    {
        var script = ReaderInject.BuildDocumentCreatedScript();

        // ReadingHost evaluates these by name; a rename in one place only would silently
        // turn the host's guarded calls into no-ops.
        foreach (var entryPoint in new[]
                 {
                     "window.__elrApply = ", "window.__elrPage = ", "window.__elrFind = ",
                     "window.__elrScrollFraction = ", "window.__elrSetScrollFraction = "
                 })
        {
            Assert.Contains(entryPoint, script);
        }
    }
}

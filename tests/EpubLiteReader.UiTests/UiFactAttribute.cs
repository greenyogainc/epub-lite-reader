using Xunit;

namespace EpubLiteReader.UiTests;

/// <summary>Skipped unless ELR_UI=1, so a normal test run does not open a window.</summary>
public sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ELR_UI"), "1", StringComparison.Ordinal))
            Skip = "Set ELR_UI=1 to run the window suite. It needs an interactive desktop and the WebView2 runtime.";
    }
}

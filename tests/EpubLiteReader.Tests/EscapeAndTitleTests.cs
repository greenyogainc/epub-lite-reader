using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

public class EscapeAndTitleTests
{
    [Fact]
    public void EscapeHtmlAttribute_EscapesAmpersandBeforeOtherCharacters()
    {
        // Order matters: if '<'/'"' were escaped before '&', the '&' introduced by their
        // own replacement text (e.g. "&lt;") would itself get re-escaped into "&amp;lt;".
        var result = EpubDoc.EscapeHtmlAttribute("<a&\"b\">");

        Assert.Equal("&lt;a&amp;&quot;b&quot;&gt;", result);
    }

    [Fact]
    public void BuildSpineTitles_PicksFirstChapterForASpineInPreOrder()
    {
        var chapters = new List<ChapterItem>
        {
            new() { Title = "Chapter 1", SpineIndex = 0 },
            new()
            {
                Title = "Part A",
                SpineIndex = 1,
                Children = { new ChapterItem { Title = "Part A - Sub", SpineIndex = 1 } }
            },
        };

        var titles = EpubDoc.BuildSpineTitles(2, chapters, "Section {0}");

        Assert.Equal("Chapter 1", titles[0]);
        // Parent ("Part A") is visited before its child pointing at the same spine index.
        Assert.Equal("Part A", titles[1]);
    }

    [Fact]
    public void BuildSpineTitles_UnmappedSpineGetsFormattedFallback()
    {
        var chapters = new List<ChapterItem>
        {
            new() { Title = "Chapter 1", SpineIndex = 0 },
        };

        var titles = EpubDoc.BuildSpineTitles(3, chapters, "Section {0}");

        Assert.Equal("Chapter 1", titles[0]);
        Assert.Equal("Section 2", titles[1]);
        Assert.Equal("Section 3", titles[2]);
    }

    [Fact]
    public void BuildSpineTitles_IgnoresOutOfRangeSpineIndexValues()
    {
        var chapters = new List<ChapterItem>
        {
            new() { Title = "TooHigh", SpineIndex = 99 },
            new() { Title = "Negative", SpineIndex = -1 },
        };

        var titles = EpubDoc.BuildSpineTitles(2, chapters, "Section {0}");

        Assert.Equal("Section 1", titles[0]);
        Assert.Equal("Section 2", titles[1]);
    }
}

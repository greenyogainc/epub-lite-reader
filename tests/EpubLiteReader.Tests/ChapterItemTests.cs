using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>ChapterItem is bound to the chapter TreeView, so the expand and select state
/// must raise PropertyChanged for the bindings to follow it.</summary>
public sealed class ChapterItemTests
{
    private static List<string> Watch(ChapterItem item)
    {
        var raised = new List<string>();
        item.PropertyChanged += (sender, e) =>
        {
            Assert.Same(item, sender);
            raised.Add(e.PropertyName!);
        };
        return raised;
    }

    [Fact]
    public void IsExpanded_StoresTheValueAndRaisesPropertyChanged()
    {
        var item = new ChapterItem();
        var raised = Watch(item);

        item.IsExpanded = true;

        Assert.True(item.IsExpanded);
        Assert.Equal(new[] { nameof(ChapterItem.IsExpanded) }, raised);

        item.IsExpanded = false;

        Assert.False(item.IsExpanded);
        Assert.Equal(2, raised.Count);
    }

    [Fact]
    public void IsSelected_StoresTheValueAndRaisesPropertyChanged()
    {
        var item = new ChapterItem();
        var raised = Watch(item);

        item.IsSelected = true;

        Assert.True(item.IsSelected);
        Assert.Equal(new[] { nameof(ChapterItem.IsSelected) }, raised);

        item.IsSelected = false;

        Assert.False(item.IsSelected);
        Assert.Equal(2, raised.Count);
    }

    [Fact]
    public void ChangingState_WithNoSubscriber_DoesNotThrow()
    {
        var item = new ChapterItem();

        item.IsExpanded = true;
        item.IsSelected = true;

        Assert.True(item.IsExpanded);
        Assert.True(item.IsSelected);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(null, false)]
    public void IsNavigable_MeansTheNodeHasASpineDestination(int? spineIndex, bool expected)
    {
        var item = new ChapterItem { SpineIndex = spineIndex };

        Assert.Equal(expected, item.IsNavigable);
    }

    [Fact]
    public void ToString_IsTheTitle()
    {
        Assert.Equal("Chapter One", new ChapterItem { Title = "Chapter One" }.ToString());
        Assert.Equal("", new ChapterItem().ToString());
    }
}

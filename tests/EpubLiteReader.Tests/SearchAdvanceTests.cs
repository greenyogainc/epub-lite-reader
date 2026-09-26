using System.Collections.Generic;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>F4: after in-page find exhausts the current chapter, Find Next/Prev must move
/// to a hit in a DIFFERENT spine rather than cycling inside one chapter, wrapping at the
/// book's ends.</summary>
public sealed class SearchAdvanceTests
{
    private static IReadOnlyList<(int SpineIndex, int Offset, string Snippet)> Hits(params int[] spines)
    {
        var list = new List<(int, int, string)>();
        foreach (var s in spines) list.Add((s, 0, ""));
        return list;
    }

    [Fact]
    public void Forward_FromSpine0_GoesToFirstHitInLaterSpine()
    {
        var hits = Hits(0, 0, 2, 5); // two hits in spine 0
        Assert.Equal(2, MainWindow.NextHitIndexInOtherSpine(hits, currentSpine: 0, forward: true));
    }

    [Fact]
    public void Forward_FromLastSpine_WrapsToFirstHit()
    {
        var hits = Hits(0, 2, 5);
        Assert.Equal(0, MainWindow.NextHitIndexInOtherSpine(hits, currentSpine: 5, forward: true));
    }

    [Fact]
    public void Backward_FromSpine5_GoesToLastEarlierHit()
    {
        var hits = Hits(0, 2, 2, 5);
        Assert.Equal(2, MainWindow.NextHitIndexInOtherSpine(hits, currentSpine: 5, forward: false));
    }

    [Fact]
    public void Backward_FromFirstSpine_WrapsToLastHit()
    {
        var hits = Hits(0, 2, 5);
        Assert.Equal(2, MainWindow.NextHitIndexInOtherSpine(hits, currentSpine: 0, forward: false));
    }

    [Fact]
    public void Forward_SkipsAllHitsInCurrentSpine()
    {
        var hits = Hits(3, 3, 3, 7);
        Assert.Equal(3, MainWindow.NextHitIndexInOtherSpine(hits, currentSpine: 3, forward: true));
    }
}

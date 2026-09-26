using System.IO;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>F7: SweepOrphanedExtracts reclaims extract roots left by a crash or a delete
/// that lost a race with the WebView's file handles, without touching a root a live
/// instance still holds (a held lock file) or a recent lock-less legacy root.</summary>
public sealed class ExtractSweepTests : IDisposable
{
    private readonly string _base;

    public ExtractSweepTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "elr-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_base);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_base)) Directory.Delete(_base, recursive: true); } catch { /* best effort */ }
    }

    private string MakeRoot(string name)
    {
        var dir = Path.Combine(_base, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "content.txt"), "x");
        return dir;
    }

    [Fact]
    public void Sweep_RemovesRootWithUnheldLockFile()
    {
        var root = MakeRoot("orphan");
        File.WriteAllText(root + ".lock", ""); // lock file present, not held

        var removed = EpubDoc.SweepOrphanedExtracts(_base, EpubDoc.LegacyExtractMaxAge);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(root + ".lock"));
    }

    [Fact]
    public void Sweep_KeepsRootWhoseLockIsHeld()
    {
        var root = MakeRoot("live");
        var lockPath = root + ".lock";
        using var held = new FileStream(lockPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var removed = EpubDoc.SweepOrphanedExtracts(_base, EpubDoc.LegacyExtractMaxAge);

        Assert.Equal(0, removed);
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void Sweep_RemovesLegacyRootOlderThanMaxAge()
    {
        var root = MakeRoot("legacy-old"); // no lock file (written by 1.0.6 or earlier)
        Directory.SetLastWriteTimeUtc(root, DateTime.UtcNow - TimeSpan.FromDays(2));

        var removed = EpubDoc.SweepOrphanedExtracts(_base, TimeSpan.FromDays(1));

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Sweep_KeepsRecentLegacyRoot()
    {
        var root = MakeRoot("legacy-fresh"); // no lock file, but recent
        Directory.SetLastWriteTimeUtc(root, DateTime.UtcNow);

        var removed = EpubDoc.SweepOrphanedExtracts(_base, TimeSpan.FromDays(1));

        Assert.Equal(0, removed);
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void Sweep_RemovesStrandedLockFileWhoseRootIsGone()
    {
        var lockPath = Path.Combine(_base, "gone.lock");
        File.WriteAllText(lockPath, "");

        EpubDoc.SweepOrphanedExtracts(_base, EpubDoc.LegacyExtractMaxAge);

        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public void Sweep_MissingBaseDir_IsNoOp()
    {
        var removed = EpubDoc.SweepOrphanedExtracts(
            Path.Combine(_base, "does-not-exist"), EpubDoc.LegacyExtractMaxAge);
        Assert.Equal(0, removed);
    }
}

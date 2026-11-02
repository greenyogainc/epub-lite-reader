using System.IO;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>App.LogError appends to a log that must never take the app down and restarts
/// at <see cref="App.MaxLogBytes"/>. Only <see cref="App.LogPathOverride"/> is used, so
/// the real %TEMP% log is never touched; the override is a static, hence the collection.</summary>
[Collection(BookStateStoreCollection.Name)]
public sealed class AppLogTests : IDisposable
{
    private readonly string _dir;
    private readonly string _log;
    private readonly string? _priorOverride;

    public AppLogTests()
    {
        _priorOverride = App.LogPathOverride;
        _dir = Path.Combine(Path.GetTempPath(), "elr-applog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _log = Path.Combine(_dir, "test.log");
        App.LogPathOverride = _log;
    }

    public void Dispose()
    {
        App.LogPathOverride = _priorOverride;
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static Exception Failure(string message)
    {
        try { throw new InvalidOperationException(message); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void LogError_AppendsATimestampedEntryWithTheException()
    {
        App.LogError(Failure("first failure"));
        App.LogError(Failure("second failure"));

        var text = File.ReadAllText(_log);
        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}\D\d{2}\D\d{2}\] ", text);
        Assert.Contains("InvalidOperationException: first failure", text);
        Assert.True(text.IndexOf("first failure", StringComparison.Ordinal) < text.IndexOf("second failure", StringComparison.Ordinal));
    }

    [Fact]
    public void LogError_AFileUnderTheCap_KeepsItsContent()
    {
        File.WriteAllText(_log, new string('x', (int)App.MaxLogBytes - 1));

        App.LogError(Failure("late failure"));

        var text = File.ReadAllText(_log);
        Assert.StartsWith(new string('x', 100), text);
        Assert.Contains("late failure", text);
    }

    [Theory]
    [InlineData(0)]   // exactly at the cap
    [InlineData(500)] // over it
    public void LogError_AFileAtOrOverTheCap_RestartsTheLog(int extraBytes)
    {
        File.WriteAllText(_log, new string('x', (int)App.MaxLogBytes + extraBytes));

        App.LogError(Failure("fresh start"));

        var text = File.ReadAllText(_log);
        Assert.DoesNotContain("xxxxxxxxxx", text);
        Assert.Contains("fresh start", text);
        Assert.True(text.Length < 10_000);
    }

    [Fact]
    public void LogError_WhenTheFileIsHeldElsewhere_DoesNotThrowOrDamageIt()
    {
        File.WriteAllText(_log, "existing entry\n");
        using (new FileStream(_log, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            App.LogError(Failure("cannot be written")); // must swallow the sharing violation
        }

        Assert.Equal("existing entry\n", File.ReadAllText(_log));
    }

    [Fact]
    public void LogError_WhenTheLogIsAtTheCapAndHeld_DoesNotThrowOrDamageIt()
    {
        var full = new string('x', (int)App.MaxLogBytes);
        File.WriteAllText(_log, full);
        using (new FileStream(_log, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            App.LogError(Failure("cannot restart")); // the restart fails too
        }

        Assert.Equal(full, File.ReadAllText(_log));
    }

    [Fact]
    public void LogError_AtTheCapWithSomeoneTailingTheLog_StillRecordsTheEntry()
    {
        File.WriteAllText(_log, new string('x', (int)App.MaxLogBytes));
        // No FileShare.Delete, as a tail holds it: deleting the file here would fail.
        using (new FileStream(_log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            App.LogError(Failure("still recorded"));
        }

        var text = File.ReadAllText(_log);
        Assert.Contains("still recorded", text);
        Assert.DoesNotContain("xxxxxxxxxx", text);
    }

    [Fact]
    public void LogError_WhenTheDirectoryDoesNotExist_DoesNotThrow()
    {
        var missing = Path.Combine(_dir, "missing");
        App.LogPathOverride = Path.Combine(missing, "test.log");

        App.LogError(Failure("nowhere to write"));

        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void MaxLogBytes_IsOneMebibyte() =>
        Assert.Equal(1024 * 1024, App.MaxLogBytes);
}

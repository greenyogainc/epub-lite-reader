using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

public class HostMessageTests
{
    [Fact]
    public void TryParseMessage_Ready_Succeeds()
    {
        Assert.True(ReadingHost.TryParseMessage("{\"type\":\"ready\"}", out var msg));
        Assert.Equal("ready", msg.Type);
    }

    [Theory]
    [InlineData("{\"type\":\"scroll\",\"fraction\":0.5}", 0.5)]
    [InlineData("{\"type\":\"scroll\",\"fraction\":7}", 1.0)]
    [InlineData("{\"type\":\"scroll\",\"fraction\":-3}", 0.0)]
    public void TryParseMessage_Scroll_ClampsFractionToUnitRange(string json, double expected)
    {
        Assert.True(ReadingHost.TryParseMessage(json, out var msg));
        Assert.Equal("scroll", msg.Type);
        Assert.Equal(expected, msg.Fraction);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void TryParseMessage_Step_AcceptsBothDirections(int direction)
    {
        var json = $"{{\"type\":\"step\",\"direction\":{direction}}}";

        Assert.True(ReadingHost.TryParseMessage(json, out var msg));
        Assert.Equal("step", msg.Type);
        Assert.Equal(direction, msg.Direction);
    }

    [Fact]
    public void TryParseMessage_Spinepos_Succeeds()
    {
        Assert.True(ReadingHost.TryParseMessage("{\"type\":\"spinepos\",\"spine\":3,\"fraction\":0.25}", out var msg));
        Assert.Equal("spinepos", msg.Type);
        Assert.Equal(3, msg.Spine);
        Assert.Equal(0.25, msg.Fraction);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("F4")]
    [InlineData("F11")]
    [InlineData("Escape")]
    public void TryParseMessage_Key_AcceptsForwardedShortcuts(string key)
    {
        var json = $"{{\"type\":\"key\",\"key\":\"{key}\"}}";

        Assert.True(ReadingHost.TryParseMessage(json, out var msg));
        Assert.Equal("key", msg.Type);
        Assert.Equal(key, msg.Key);
    }

    [Theory]
    [InlineData("{\"type\":\"key\"}")]
    [InlineData("{\"type\":\"key\",\"key\":\"4\"}")]
    [InlineData("{\"type\":\"key\",\"key\":\"a\"}")]
    [InlineData("{\"type\":\"key\",\"key\":\"F12\"}")]
    [InlineData("{\"type\":\"key\",\"key\":\"Enter\"}")]
    [InlineData("{\"type\":\"key\",\"key\":3}")]
    public void TryParseMessage_Key_RejectsUnknownKeys(string json)
    {
        Assert.False(ReadingHost.TryParseMessage(json, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("{\"type\":\"unknown\"}")]
    [InlineData("{\"type\":\"scroll\"}")]
    [InlineData("{\"type\":\"scroll\",\"fraction\":\"0.5\"}")]
    [InlineData("{\"type\":\"step\",\"direction\":0}")]
    [InlineData("{\"type\":\"step\",\"direction\":2}")]
    [InlineData("{\"type\":\"step\",\"direction\":-2}")]
    [InlineData("{\"type\":\"step\",\"direction\":1.5}")]
    [InlineData("{\"type\":\"spinepos\",\"spine\":-1,\"fraction\":0}")]
    [InlineData("{\"type\":\"spinepos\",\"spine\":2000000,\"fraction\":0}")]
    [InlineData("{\"type\":\"blocked-nav\",\"href\":\"x\"}")]
    public void TryParseMessage_RejectsInvalidOrDisallowedMessages(string? json)
    {
        Assert.False(ReadingHost.TryParseMessage(json, out _));
    }

    [Fact]
    public void TryParseMessage_NonFiniteFractionLiteral_IsRejectedWithoutThrowing()
    {
        // 1e309 overflows double range. JsonDocument.Parse tolerates the literal and
        // JsonElement.GetDouble() converts it to +Infinity (verified empirically) rather
        // than throwing, so TryParseMessage's IsFinite guard rejects it; but even if a
        // future runtime made GetDouble throw instead, TryParseMessage must not propagate
        // that - it should still just report the message as unparseable.
        var ok = ReadingHost.TryParseMessage("{\"type\":\"scroll\",\"fraction\":1e309}", out _);

        Assert.False(ok);
    }
}

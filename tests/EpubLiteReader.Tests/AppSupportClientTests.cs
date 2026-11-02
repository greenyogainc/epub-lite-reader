using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using GreenYoga.Support;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>The support transport: request validation, idempotency keys, acknowledgment
/// parsing and error mapping. Every send goes through an inline handler; nothing here
/// reaches the real network.</summary>
public sealed class AppSupportClientTests
{
    private static AppSupportRequest Valid(string message = "Help please") =>
        new AppSupportDraft().GetRequest("epub-lite-reader", "1.0.7", "Windows 11", "en",
            " Ada ", "ada@example.com", "Question", message);

    private sealed class ReplyHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply)
        : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            cancellationToken.ThrowIfCancellationRequested();
            return reply(request, cancellationToken);
        }
    }

    private static (AppSupportClient Client, ReplyHandler Handler) ClientReplying(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply)
    {
        var handler = new ReplyHandler(reply);
        return (new AppSupportClient(new HttpClient(handler)), handler);
    }

    private static (AppSupportClient Client, ReplyHandler Handler) ClientReplying(HttpStatusCode status, string body) =>
        ClientReplying((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));

    private static string Ack(AppSupportRequest request) =>
        JsonSerializer.Serialize(new { success = true, requestId = request.RequestId });

    // ---------- draft ----------

    [Fact]
    public void Draft_UnchangedFields_RetainTheRequestId()
    {
        var draft = new AppSupportDraft();
        var first = draft.GetRequest("epub-lite-reader", "1.0.7", "Windows 11", "en", "Ada", "ada@example.com", "Hi", "Body");
        var second = draft.GetRequest("epub-lite-reader", "1.0.7", "Windows 11", "en", "Ada", "ada@example.com", "Hi", "Body");

        Assert.Equal(first.RequestId, second.RequestId);
        Assert.True(Guid.TryParse(first.RequestId, out _));
    }

    [Fact]
    public void Draft_ChangedField_ReplacesTheRequestId()
    {
        var draft = new AppSupportDraft();
        var first = draft.GetRequest("epub-lite-reader", "1.0.7", "Windows 11", "en", "Ada", "ada@example.com", "Hi", "Body");
        var edited = draft.GetRequest("epub-lite-reader", "1.0.7", "Windows 11", "en", "Ada", "ada@example.com", "Hi", "Body, edited");
        var editedAgain = draft.GetRequest("epub-lite-reader", "1.0.7", "Windows 11", "en", "Ada", "ada@example.com", "Hi", "Body, edited");

        Assert.NotEqual(first.RequestId, edited.RequestId);
        Assert.Equal(edited.RequestId, editedAgain.RequestId);
    }

    [Fact]
    public void Draft_TrimsEveryUserField()
    {
        var request = new AppSupportDraft().GetRequest("epub-lite-reader", "1.0.7", "Windows 11", "en",
            "  Ada ", " ada@example.com ", " Subject ", "\n Message \t");

        Assert.Equal("Ada", request.Name);
        Assert.Equal("ada@example.com", request.Email);
        Assert.Equal("Subject", request.Subject);
        Assert.Equal("Message", request.Message);
    }

    // ---------- validation ----------

    [Fact]
    public void Validate_AcceptsAWellFormedRequest() =>
        Assert.Null(AppSupportClient.Validate(Valid()));

    [Theory]
    [InlineData("epub-lite-reader")]
    [InlineData("pdf-lite-viewer")]
    public void Validate_AcceptsBothKnownAppIds(string appId) =>
        Assert.Null(AppSupportClient.Validate(Valid() with { AppId = appId }));

    [Fact]
    public void Validate_RejectsAnUnknownAppId() =>
        Assert.Equal(AppSupportStatus.InvalidRequest, AppSupportClient.Validate(Valid() with { AppId = "some-other-app" }));

    [Fact]
    public void Validate_RejectsARequestIdThatIsNotAGuid() =>
        Assert.Equal(AppSupportStatus.InvalidRequest, AppSupportClient.Validate(Valid() with { RequestId = "not-a-guid" }));

    [Theory]
    [InlineData("Name")]
    [InlineData("Subject")]
    [InlineData("Message")]
    [InlineData("Email")]
    [InlineData("AppVersion")]
    [InlineData("OsVersion")]
    [InlineData("Locale")]
    public void Validate_RejectsABlankRequiredField(string field)
    {
        var blank = With(Valid(), field, "   ");
        Assert.Equal(AppSupportStatus.InvalidRequest, AppSupportClient.Validate(blank));
    }

    [Fact]
    public void Validate_RejectsAFieldWithSurroundingWhitespace() =>
        Assert.Equal(AppSupportStatus.InvalidRequest, AppSupportClient.Validate(Valid() with { Name = " Ada" }));

    [Theory]
    [InlineData("AppVersion", 64)]
    [InlineData("OsVersion", 128)]
    [InlineData("Locale", 35)]
    [InlineData("Name", 200)]
    [InlineData("Subject", 200)]
    [InlineData("Message", 5000)]
    public void Validate_EnforcesEachFieldLengthLimit(string field, int limit)
    {
        Assert.Null(AppSupportClient.Validate(With(Valid(), field, new string('x', limit))));
        Assert.Equal(AppSupportStatus.InvalidRequest,
            AppSupportClient.Validate(With(Valid(), field, new string('x', limit + 1))));
    }

    [Fact]
    public void Validate_RejectsAnEmailOverTheLengthLimit()
    {
        var tooLong = new string('a', 321 - "@example.com".Length) + "@example.com";
        Assert.Equal(321, tooLong.Length);
        Assert.Equal(AppSupportStatus.InvalidRequest, AppSupportClient.Validate(Valid() with { Email = tooLong }));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("ada@")]
    [InlineData("Ada Lovelace <ada@example.com>")] // parses, but is not just the address
    [InlineData("ada@localhost")]                    // no dot in the domain
    [InlineData("ada@exa\rmple.com")]                // CR inside
    [InlineData("ada@exa\nmple.com")]                // LF inside
    public void Validate_RejectsABadEmail(string email) =>
        Assert.Equal(AppSupportStatus.InvalidEmail, AppSupportClient.Validate(Valid() with { Email = email }));

    private static AppSupportRequest With(AppSupportRequest r, string field, string value) => field switch
    {
        "Name" => r with { Name = value },
        "Subject" => r with { Subject = value },
        "Message" => r with { Message = value },
        "Email" => r with { Email = value },
        "AppVersion" => r with { AppVersion = value },
        "OsVersion" => r with { OsVersion = value },
        "Locale" => r with { Locale = value },
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    // ---------- sending ----------

    [Fact]
    public async Task Send_PostsCamelCaseJsonToTheDedicatedEndpoint()
    {
        var request = Valid();
        HttpMethod? method = null;
        string? uri = null;
        string? mediaType = null;
        string? body = null;
        var (client, _) = ClientReplying(async (outgoing, ct) =>
        {
            method = outgoing.Method;
            uri = outgoing.RequestUri!.AbsoluteUri;
            mediaType = outgoing.Content!.Headers.ContentType!.MediaType;
            body = await outgoing.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Ack(request)) };
        });

        var result = await client.SendAsync(request);

        Assert.Equal(AppSupportStatus.Success, result.Status);
        Assert.Equal(request.RequestId, result.RequestId);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("https://api.greenyogainc.com/app-support", uri);
        Assert.Equal(AppSupportClient.Endpoint, uri);
        Assert.Equal("application/json", mediaType);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal(request.RequestId, json.RootElement.GetProperty("requestId").GetString());
        Assert.Equal("epub-lite-reader", json.RootElement.GetProperty("appId").GetString());
        Assert.Equal("Ada", json.RootElement.GetProperty("name").GetString());
        Assert.Equal("ada@example.com", json.RootElement.GetProperty("email").GetString());
        Assert.Equal("Help please", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Send_InvalidForm_NeverPosts()
    {
        var (client, handler) = ClientReplying(HttpStatusCode.OK, "{}");

        var result = await client.SendAsync(Valid() with { Email = "bad" });

        Assert.Equal(AppSupportStatus.InvalidEmail, result.Status);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Send_InvalidRequest_ReportsItWithoutPosting()
    {
        var (client, handler) = ClientReplying(HttpStatusCode.OK, "{}");

        var result = await client.SendAsync(Valid() with { Name = "" });

        Assert.Equal(AppSupportStatus.InvalidRequest, result.Status);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"success\":true}")]
    [InlineData("{\"success\":true,\"requestId\":\"wrong\"}")]
    [InlineData("{\"success\":false,\"requestId\":\"%ID%\"}")]
    [InlineData("{\"success\":true,\"requestId\":5}")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public async Task Send_MalformedOrUnmatchedAcknowledgment_IsRejected(string body)
    {
        var request = Valid();
        var (client, _) = ClientReplying(HttpStatusCode.OK, body.Replace("%ID%", request.RequestId));

        Assert.Equal(AppSupportStatus.Failed, (await client.SendAsync(request)).Status);
    }

    [Fact]
    public async Task Send_AcknowledgmentWithANonOkStatus_IsNotSuccess()
    {
        var request = Valid();
        var (client, _) = ClientReplying(HttpStatusCode.Accepted, Ack(request));

        Assert.Equal(AppSupportStatus.Failed, (await client.SendAsync(request)).Status);
    }

    [Fact]
    public async Task Send_AcknowledgmentLargerThanOneChunk_IsStillRead()
    {
        // Three 1024-byte reads' worth of padding after a valid acknowledgment.
        var request = Valid();
        var (client, _) = ClientReplying(HttpStatusCode.OK, Ack(request) + new string(' ', 3000));

        Assert.Equal(AppSupportStatus.Success, (await client.SendAsync(request)).Status);
    }

    [Fact]
    public async Task Send_AcknowledgmentCap_AllowsExactlyEightKilobytes()
    {
        var request = Valid();
        var ack = Ack(request);

        var (atCap, _) = ClientReplying(HttpStatusCode.OK, ack + new string(' ', 8192 - ack.Length));
        var (overCap, _) = ClientReplying(HttpStatusCode.OK, ack + new string(' ', 8193 - ack.Length));

        Assert.Equal(AppSupportStatus.Success, (await atCap.SendAsync(request)).Status);
        Assert.Equal(AppSupportStatus.Failed, (await overCap.SendAsync(request)).Status);
    }

    [Fact]
    public async Task Send_OversizedAcknowledgment_IsRejected()
    {
        var (client, _) = ClientReplying(HttpStatusCode.OK, new string('x', 9000));

        Assert.Equal(AppSupportStatus.Failed, (await client.SendAsync(Valid())).Status);
    }

    [Theory]
    [InlineData(400, "invalid-email", AppSupportStatus.InvalidEmail)]
    [InlineData(400, "invalid-request", AppSupportStatus.InvalidRequest)]
    [InlineData(409, "request-in-progress", AppSupportStatus.InProgress)]
    [InlineData(409, "request-conflict", AppSupportStatus.Conflict)]
    [InlineData(429, "rate-limited", AppSupportStatus.RateLimited)]
    [InlineData(503, "delivery-failed", AppSupportStatus.Failed)]
    [InlineData(400, "rate-limited", AppSupportStatus.Failed)] // the code only counts with its own status
    public async Task Send_MapsServerErrorsWithoutRetrying(int status, string error, AppSupportStatus expected)
    {
        var (client, handler) = ClientReplying((HttpStatusCode)status, JsonSerializer.Serialize(new { error }));

        var result = await client.SendAsync(Valid());

        Assert.Equal(expected, result.Status);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Send_ErrorFieldThatIsNotAString_IsAGenericFailure()
    {
        var (client, _) = ClientReplying(HttpStatusCode.BadRequest, "{\"error\":5}");

        Assert.Equal(AppSupportStatus.Failed, (await client.SendAsync(Valid())).Status);
    }

    [Fact]
    public async Task Send_NetworkFailure_IsReportedAsFailed()
    {
        var (client, _) = ClientReplying((_, _) => throw new HttpRequestException("offline"));

        Assert.Equal(AppSupportStatus.Failed, (await client.SendAsync(Valid())).Status);
    }

    [Fact]
    public async Task Send_TimeoutNotCausedByTheCaller_IsReportedAsFailed()
    {
        // The client's own 20 s timeout surfaces as a cancellation the caller never asked for.
        var (client, _) = ClientReplying((_, _) => throw new TaskCanceledException("timed out"));

        Assert.Equal(AppSupportStatus.Failed, (await client.SendAsync(Valid())).Status);
    }

    [Fact]
    public async Task Send_IoErrorWhileReadingTheBody_IsReportedAsFailed()
    {
        var (client, _) = ClientReplying((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream()) }));

        Assert.Equal(AppSupportStatus.Failed, (await client.SendAsync(Valid())).Status);
    }

    [Fact]
    public async Task Send_CancelledBeforeSending_Throws()
    {
        var (client, handler) = ClientReplying(HttpStatusCode.OK, "{}");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(Valid(), cts.Token));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Send_CancelledDuringTheSend_Throws()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (client, _) = ClientReplying(async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var pending = client.SendAsync(Valid(), cts.Token);
        await started.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("connection reset");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("connection reset");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

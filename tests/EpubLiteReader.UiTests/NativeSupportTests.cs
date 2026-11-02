using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using Button = System.Windows.Controls.Button;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using GreenYoga.Support;
using Xunit;

namespace EpubLiteReader.UiTests;

public sealed class NativeSupportTests
{
    [UiFact]
    public void NativeForm_ValidatesPreservesDraftAndConfirmsDelivery()
    {
        RunOnDispatcher(async () =>
        {
            var handler = new SupportHandler();
            using var http = new HttpClient(handler);
            var window = new AboutWindow(new AppSupportClient(http));
            window.Show();
            try
            {
                window.ContactSupportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, window.SupportView.Visibility);
                Assert.False(HasWebView(window));
                Assert.True(window.SupportNameBox.IsKeyboardFocused);
                window.SupportSendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, handler.Calls);
                Assert.NotEmpty(window.SupportStatusText.Text);
                window.SupportNameBox.Text = "Reader";
                window.SupportEmailBox.Text = "reader@example.com";
                window.SupportSubjectBox.Text = "Reading help";
                window.SupportMessageBox.Text = "Please help with a book.";
                CaptureSupportForm(window);
                window.SupportBackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.ContactSupportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("Please help with a book.", window.SupportMessageBox.Text);
                window.SupportSendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.SupportSendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, handler.Calls);
                Assert.Equal("epub-lite-reader", handler.AppId);
                handler.Complete();
                for (var i = 0; i < 100 && window.SupportSendAnotherButton.Visibility != Visibility.Visible; i++)
                    await Task.Delay(20);
                Assert.Equal(Visibility.Visible, window.SupportSendAnotherButton.Visibility);
                Assert.Contains(handler.RequestId!, window.SupportStatusText.Text);
                window.SupportSendAnotherButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Empty(window.SupportMessageBox.Text);
                Assert.Equal(Visibility.Visible, window.SupportSendButton.Visibility);
            }
            finally { window.Close(); }
        });
    }

    [UiFact]
    public void NativeForm_RetainsFailedDraftAndRetryIdAndCancelsOnClose()
    {
        RunOnDispatcher(async () =>
        {
            var handler = new RetryHandler();
            using var http = new HttpClient(handler);
            var window = new AboutWindow(new AppSupportClient(http));
            window.Show();
            try
            {
                window.ContactSupportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.SupportNameBox.Text = "Reader";
                window.SupportEmailBox.Text = "reader@example.com";
                window.SupportSubjectBox.Text = "Question";
                window.SupportMessageBox.Text = "Retain this draft.";
                window.SupportSendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < 100 && !window.SupportSendButton.IsEnabled; i++) await Task.Delay(20);
                Assert.Equal("Retain this draft.", window.SupportMessageBox.Text);
                Assert.NotEmpty(window.SupportStatusText.Text);
                window.SupportSendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(2, handler.RequestIds.Count);
                Assert.Equal(handler.RequestIds[0], handler.RequestIds[1]);
                window.Close();
                await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally { window.Close(); }
        });
    }

    private static void CaptureSupportForm(AboutWindow window)
    {
        var path = Environment.GetEnvironmentVariable("ELR_SUPPORT_SCREENSHOT");
        if (string.IsNullOrEmpty(path)) return;
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    // ---------- Answers, closing, files and links, on the shared app thread ----------

    private static AboutWindow NewAbout(HttpMessageHandler handler)
    {
        var window = new AboutWindow(new AppSupportClient(new HttpClient(handler)));
        window.Show();
        return window;
    }

    private static void FillForm(AboutWindow window)
    {
        window.ContactSupportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.SupportNameBox.Text = "Reader";
        window.SupportEmailBox.Text = "reader@example.com";
        window.SupportSubjectBox.Text = "Question";
        window.SupportMessageBox.Text = "A question about reading.";
    }

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task WaitAsync(Func<bool> condition, string what)
    {
        for (var i = 0; i < 300 && !condition(); i++)
            await Task.Delay(20);
        Assert.True(condition(), "Timed out waiting for " + what + ".");
    }

    private sealed class AnswerHandler : HttpMessageHandler
    {
        public Queue<(int Status, string Body)> Answers { get; } = new();
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var (status, body) = Answers.Dequeue();
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [UiFact]
    public void EveryServerAnswer_ShowsItsOwnMessage()
    {
        UiKit.RunOnApp(async () =>
        {
            var handler = new AnswerHandler();
            var window = NewAbout(handler);
            try
            {
                FillForm(window);
                var cases = new[]
                {
                    (400, "invalid-email", "SupportInvalidEmail"),
                    (400, "invalid-request", "SupportRequired"),
                    (429, "rate-limited", "SupportRateLimited"),
                    (409, "request-in-progress", "SupportInProgress"),
                    (409, "request-conflict", "SupportConflict"),
                    (503, "delivery-failed", "SupportSendFailed"),
                };
                var expectedCalls = 0;
                foreach (var (status, error, key) in cases)
                {
                    handler.Answers.Enqueue((status, "{\"error\":\"" + error + "\"}"));
                    Press(window.SupportSendButton);
                    expectedCalls++;
                    await WaitAsync(() => handler.Calls == expectedCalls && window.SupportSendButton.IsEnabled, error);
                    Assert.Equal(Strings.Get(key), window.SupportStatusText.Text);
                }
            }
            finally { window.Close(); }
        });
    }

    /// <summary>Acknowledges the request from a stream that closes the window as its last read finishes
    /// and, like a transport that does not watch the token, ignores the cancellation that causes.</summary>
    private sealed class ClosingAckHandler : HttpMessageHandler
    {
        public AboutWindow? Window { get; set; }
        public TaskCompletionSource Finished { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = "{\"success\":true,\"requestId\":\"" + document.RootElement.GetProperty("requestId").GetString() + "\"}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ClosingStream(Window!, Encoding.UTF8.GetBytes(body), Finished)) };
        }
    }

    private sealed class ClosingStream(AboutWindow window, byte[] data, TaskCompletionSource finished) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = Math.Min(buffer.Length, data.Length - _position);
            if (count > 0)
            {
                data.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                return ValueTask.FromResult(count);
            }

            // The last read: the window closes now, and this read still returns normally.
            window.Dispatcher.Invoke(window.Close);
            finished.TrySetResult();
            return ValueTask.FromResult(0);
        }
    }

    [UiFact]
    public void ClosingWhileTheAnswerIsRead_DropsTheAnswer()
    {
        UiKit.RunOnApp(async () =>
        {
            var handler = new ClosingAckHandler();
            var window = NewAbout(handler);
            handler.Window = window;
            FillForm(window);

            Press(window.SupportSendButton);
            await handler.Finished.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Task.Delay(200);

            Assert.False(window.IsLoaded);
            // Had the success been applied, the form would have swapped Send for Send Another.
            Assert.Equal(Visibility.Collapsed, window.SupportSendAnotherButton.Visibility);
            Assert.Equal(Visibility.Visible, window.SupportSendButton.Visibility);
        });
    }

    private sealed class HoldingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> _response = new();
        public int Calls { get; private set; }
        public string? RequestId { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            using var document = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            RequestId = document.RootElement.GetProperty("requestId").GetString();
            return await _response.Task.WaitAsync(cancellationToken);
        }
        public void Acknowledge() => _response.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"success\":true,\"requestId\":\"" + RequestId + "\"}", Encoding.UTF8, "application/json")
        });
    }

    [UiFact]
    public void SendAnother_IsIgnoredWhileSending_AndReturningAfterASendFocusesIt()
    {
        UiKit.RunOnApp(async () =>
        {
            var handler = new HoldingHandler();
            var window = NewAbout(handler);
            try
            {
                FillForm(window);
                Press(window.SupportSendButton);
                await WaitAsync(() => handler.Calls == 1, "the request");

                // Still sending: the click changes nothing and the draft stays.
                Press(window.SupportSendAnotherButton);
                Assert.Equal("A question about reading.", window.SupportMessageBox.Text);
                Assert.False(window.SupportSendButton.IsEnabled);

                handler.Acknowledge();
                await WaitAsync(() => window.SupportSendAnotherButton.Visibility == Visibility.Visible, "the acknowledgment");

                // Back to the About page and into the form again: the focus goes to the action that is left.
                UiDriver.RequireForeground(new WindowInteropHelper(window).Handle);
                Press(window.SupportBackButton);
                Press(window.ContactSupportButton);
                Assert.True(window.SupportSendAnotherButton.IsKeyboardFocused);
            }
            finally { window.Close(); }
        });
    }

    private static T WithLog<T>(Func<string, T> body)
    {
        var log = Path.Combine(Path.GetTempPath(), "elr-about-" + Guid.NewGuid().ToString("N") + ".log");
        var previous = App.LogPathOverride;
        App.LogPathOverride = log;
        try { return body(log); }
        finally
        {
            App.LogPathOverride = previous;
            File.Delete(log);
        }
    }

    [UiFact]
    public void TheLicenseText_IsReplacedByANotice_WhenTheFileIsMissingOrLocked()
    {
        var license = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
        var moved = license + ".moved";
        Assert.True(File.Exists(license), "The test project copies LICENSE.txt next to the app.");

        UiKit.RunOnApp(() =>
        {
            WithLog(log =>
            {
                var unavailable = Strings.Get("AboutLicenseUnavailable");

                var normal = NewAbout(new AnswerHandler());
                Assert.NotEqual(unavailable, normal.LicenseText.Text);
                normal.Close();

                File.Move(license, moved);
                try
                {
                    var missing = NewAbout(new AnswerHandler());
                    Assert.Equal(unavailable, missing.LicenseText.Text);
                    missing.Close();
                }
                finally { File.Move(moved, license); }

                using (new FileStream(license, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var locked = NewAbout(new AnswerHandler());
                    Assert.Equal(unavailable, locked.LicenseText.Text);
                    locked.Close();
                }

                Assert.Contains("IOException", File.ReadAllText(log));
                return 0;
            });
            return Task.CompletedTask;
        });
    }

    [UiFact]
    public void ALinkThatCannotBeOpened_IsLogged_AndMarkedHandled()
    {
        UiKit.RunOnApp(() =>
        {
            WithLog(log =>
            {
                var window = NewAbout(new AnswerHandler());
                try
                {
                    var link = FindLinks(window).First();
                    var args = new RequestNavigateEventArgs(new Uri("file:///C:/elr-test-nonexistent/none.txt"), null)
                    {
                        RoutedEvent = Hyperlink.RequestNavigateEvent
                    };
                    link.RaiseEvent(args);

                    Assert.True(args.Handled);
                    Assert.NotEqual("", File.Exists(log) ? File.ReadAllText(log) : "");
                    Assert.Empty(NativeDialogs.TopLevelWindows(dialogsOnly: true));
                }
                finally { window.Close(); }
                return 0;
            });
            return Task.CompletedTask;
        });
    }

    private static IEnumerable<Hyperlink> FindLinks(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is Hyperlink link)
                yield return link;
            if (child is DependencyObject next)
                foreach (var nested in FindLinks(next))
                    yield return nested;
        }
    }

    [UiFact]
    public void AStateFileThatCannotBeWritten_DoesNotStopAboutFromWorking()
    {
        var state = Path.Combine(Path.GetTempPath(), "elr-about-state-" + Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(state + ".about");

        try
        {
            UiKit.RunOnApp(() =>
            {
                App.ApplyArgs(new[] { "--statefile=" + state });
                try
                {
                    var window = NewAbout(new AnswerHandler());
                    try
                    {
                        Press(window.ContactSupportButton);
                        Assert.Equal(Visibility.Visible, window.SupportView.Visibility);
                    }
                    finally { window.Close(); }
                }
                finally { App.ApplyArgs(Array.Empty<string>()); }
                return Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(state + ".about");
        }
    }

    [UiFact]
    public void TheAboutState_IsMirroredToTheStateFile_WhenOneIsGiven()
    {
        var state = Path.Combine(Path.GetTempPath(), "elr-about-state-" + Guid.NewGuid().ToString("N") + ".json");
        var mirror = state + ".about";

        try
        {
            UiKit.RunOnApp(() =>
            {
                App.ApplyArgs(new[] { "--statefile=" + state });
                try
                {
                    var window = NewAbout(new AnswerHandler());
                    using (var opened = System.Text.Json.JsonDocument.Parse(File.ReadAllText(mirror)))
                    {
                        Assert.True(opened.RootElement.GetProperty("aboutOpen").GetBoolean());
                        Assert.False(opened.RootElement.GetProperty("supportView").GetBoolean());
                    }

                    Press(window.ContactSupportButton);
                    using (var support = System.Text.Json.JsonDocument.Parse(File.ReadAllText(mirror)))
                        Assert.True(support.RootElement.GetProperty("supportView").GetBoolean());

                    window.Close();
                    using var closed = System.Text.Json.JsonDocument.Parse(File.ReadAllText(mirror));
                    Assert.False(closed.RootElement.GetProperty("aboutOpen").GetBoolean());
                    Assert.False(closed.RootElement.GetProperty("supportView").GetBoolean());
                }
                finally { App.ApplyArgs(Array.Empty<string>()); }
                return Task.CompletedTask;
            });
        }
        finally
        {
            File.Delete(mirror);
        }
    }

    [UiFact]
    public void TheAppMark_LoadsFromThePackUri_AndABadUriIsLoggedWithoutStoppingAbout()
    {
        UiKit.RunOnApp(() =>
        {
            WithLog(log =>
            {
                var window = NewAbout(new AnswerHandler());
                try
                {
                    // With the application running the icon resource resolves.
                    Assert.NotNull(window.AppMark.Source);
                    var mark = window.AppMark.Source;

                    window.LoadAppMark("this is not a pack uri");
                    Assert.Same(mark, window.AppMark.Source);
                    Assert.Contains("UriFormatException", File.ReadAllText(log));

                    Press(window.ContactSupportButton);
                    Assert.Equal(Visibility.Visible, window.SupportView.Visibility);
                }
                finally { window.Close(); }
                return 0;
            });
            return Task.CompletedTask;
        });
    }

    private sealed class RetryHandler : HttpMessageHandler
    {
        public List<string> RequestIds { get; } = new();
        public TaskCompletionSource Cancelled { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            RequestIds.Add(document.RootElement.GetProperty("requestId").GetString()!);
            if (RequestIds.Count == 1)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                { Content = new StringContent("{\"error\":\"delivery-failed\"}") };
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { Cancelled.SetResult(); throw; }
            throw new InvalidOperationException("The blocked request must be cancelled.");
        }
    }

    private static bool HasWebView(DependencyObject parent)
    {
        if (parent.GetType().Name == "WebView2") return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (HasWebView(VisualTreeHelper.GetChild(parent, i))) return true;
        return false;
    }

    private static void RunOnDispatcher(Func<Task> action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); }
                catch (Exception ex) { error = ex; }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Native support test timed out.");
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class SupportHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> _response = new();
        public int Calls { get; private set; }
        public string? RequestId { get; private set; }
        public string? AppId { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            using var document = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            RequestId = document.RootElement.GetProperty("requestId").GetString();
            AppId = document.RootElement.GetProperty("appId").GetString();
            return await _response.Task.WaitAsync(cancellationToken);
        }
        public void Complete() => _response.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"success\":true,\"requestId\":\"" + RequestId + "\"}", Encoding.UTF8, "application/json")
        });
    }
}

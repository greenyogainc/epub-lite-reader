using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using Button = System.Windows.Controls.Button;
using System.Windows.Media;
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

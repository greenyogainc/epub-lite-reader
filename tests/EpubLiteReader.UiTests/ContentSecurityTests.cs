using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Xunit;

namespace EpubLiteReader.UiTests;

/// <summary>
/// The reader's promise that a book cannot run script or reach the network, checked in real WebView2.
/// A virtual-host folder mapping once bypassed all of it: WebView2 served the mapped host without
/// raising WebResourceRequested, so no CSP was sent and an inserted script simply ran.
/// </summary>
public sealed class ContentSecurityTests
{
    private const string InsertScript = """
        window.__ran = {};
        window.__violations = 0;
        document.addEventListener('securitypolicyviolation', () => { window.__violations++; });
        (() => {
          const s = document.createElement('script');
          s.textContent = 'window.__ran.inline = 1';
          document.documentElement.appendChild(s);
        })();
        """;

    [UiFact]
    public void BookDocuments_AreAnsweredByTheLockedDownHandler()
    {
        using var session = StaReadingSession.Open(StaReadingSession.SampleEpub);
        session.WaitUntilReady();

        var responses = new Dictionary<string, (int Status, string? Csp, string? NoSniff, string? Type)>();
        session.Invoke(w =>
        {
            // Runs after the reader's own handler, so it sees the response that handler set.
            w.WebLeft.CoreWebView2.WebResourceRequested += (_, e) =>
            {
                var headers = e.Response?.Headers;
                responses[new Uri(e.Request.Uri).AbsolutePath] = (e.Response?.StatusCode ?? 0,
                    Header(headers, "Content-Security-Policy"),
                    Header(headers, "X-Content-Type-Options"),
                    Header(headers, "Content-Type"));
            };
        });

        session.Run(w => w.GoToSpineAsync(1));

        var chapter = responses["/OEBPS/chapter2.xhtml"];
        Assert.Equal(200, chapter.Status);
        Assert.Contains("script-src 'none'", chapter.Csp);
        Assert.Contains("default-src 'none'", chapter.Csp);
        Assert.Equal("nosniff", chapter.NoSniff);
        Assert.Equal("application/xhtml+xml", chapter.Type);
    }

    [UiFact]
    public void InsertedScript_JavascriptUrls_AndInlineHandlers_DoNotRun()
    {
        using var session = StaReadingSession.Open(StaReadingSession.SampleEpub);
        session.WaitUntilReady();

        var result = session.Run(async w =>
        {
            await StaReadingSession.EvalAsync(w.WebLeft, InsertScript);
            await StaReadingSession.EvalAsync(w.WebLeft, """
                (() => {
                  const a = document.createElement('a');
                  a.href = 'javascript:window.__ran.href = 1';
                  document.body.appendChild(a);
                  a.click();
                  const f = document.createElement('iframe');
                  f.src = 'javascript:parent.__ran.frame = 1';
                  document.body.appendChild(f);
                  const d = document.createElement('div');
                  d.setAttribute('onclick', 'window.__ran.handler = 1');
                  document.body.appendChild(d);
                  // Not bubbling: a click reaching the document would turn the page.
                  d.dispatchEvent(new MouseEvent('click', { bubbles: false }));
                })();
                """);
            await Task.Delay(500);
            return await StaReadingSession.EvalLaterAsync(w.WebLeft, "__out", """
                window.__out = JSON.stringify({ ran: window.__ran, violations: window.__violations, page: location.href });
                """);
        });

        using var json = JsonDocument.Parse(result);
        Assert.True(json.RootElement.TryGetProperty("ran", out _), result);
        Assert.Equal("{}", json.RootElement.GetProperty("ran").GetRawText());
        // A blocked script alone could mean the sanitizer did it; a violation means the CSP did.
        Assert.True(json.RootElement.GetProperty("violations").GetInt32() > 0);
    }

    [UiFact]
    public void ContinuousDocument_RunsItsHashedScript_ButNoOther()
    {
        using var session = StaReadingSession.Open(StaReadingSession.SampleEpub);
        session.WaitUntilReady();

        var result = session.Run(async w =>
        {
            w.ModeContinuous.IsChecked = true;
            await w.WhenNavIdleAsync();
            await StaReadingSession.EvalAsync(w.WebLeft, InsertScript);
            return await StaReadingSession.EvalLaterAsync(w.WebLeft, "__out", """
                setTimeout(() => {
                  const frame = document.querySelector('iframe.elr-spine');
                  window.__out = JSON.stringify({
                    ran: window.__ran,
                    hashedScriptRan: typeof window.__elrSpinePos === 'function',
                    chapterLoaded: !!(frame && frame.contentDocument && frame.contentDocument.querySelector('h1'))
                  });
                }, 300);
                """);
        });

        using var json = JsonDocument.Parse(result);
        Assert.Equal("{}", json.RootElement.GetProperty("ran").GetRawText());
        Assert.True(json.RootElement.GetProperty("hashedScriptRan").GetBoolean());
        Assert.True(json.RootElement.GetProperty("chapterLoaded").GetBoolean());
    }

    [UiFact]
    public void Popups_Downloads_AndPermissions_AreDenied()
    {
        using var session = StaReadingSession.Open(StaReadingSession.SampleEpub);
        session.WaitUntilReady();

        bool? popupHandled = null, downloadCancelled = null;
        CoreWebView2PermissionState? permission = null;
        session.Invoke(w =>
        {
            var core = w.WebLeft.CoreWebView2;
            core.NewWindowRequested += (_, e) => popupHandled = e.Handled;
            core.DownloadStarting += (_, e) => downloadCancelled = e.Cancel;
            core.PermissionRequested += (_, e) => permission = e.State;
        });

        session.Run(async w =>
        {
            await StaReadingSession.EvalAsync(w.WebLeft, "void window.open('https://epub.local/OEBPS/chapter2.xhtml')");
            await StaReadingSession.PollAsync(() => popupHandled is not null, "the popup request");

            await StaReadingSession.EvalAsync(w.WebLeft, "void Notification.requestPermission()");
            await StaReadingSession.PollAsync(() => permission is not null, "the permission request");

            // An unknown extension is served as application/octet-stream, which downloads.
            await File.WriteAllBytesAsync(Path.Combine(w.CurrentDoc!.ExtractRoot, "payload.bin"), new byte[64]);
            w.WebLeft.CoreWebView2.Navigate("https://epub.local/payload.bin");
            await StaReadingSession.PollAsync(() => downloadCancelled is not null, "the download");
        });

        Assert.True(popupHandled);
        Assert.Equal(CoreWebView2PermissionState.Deny, permission);
        Assert.True(downloadCancelled);
    }

    [UiFact]
    public void ExternalNavigation_IsCancelled_AndExternalResources_Get403()
    {
        using var session = StaReadingSession.Open(StaReadingSession.SampleEpub);
        session.WaitUntilReady();

        bool? cancelled = null;
        int? imageStatus = null;
        session.Invoke(w =>
        {
            var core = w.WebLeft.CoreWebView2;
            core.NavigationStarting += (_, e) =>
            {
                if (e.Uri.Contains("probe.invalid", StringComparison.Ordinal))
                    cancelled = e.Cancel;
            };
            core.WebResourceRequested += (_, e) =>
            {
                if (e.Request.Uri.Contains("probe.invalid", StringComparison.Ordinal))
                    imageStatus = e.Response?.StatusCode;
            };
        });

        session.Run(async w =>
        {
            var core = w.WebLeft.CoreWebView2;
            core.Navigate("https://probe.invalid/");
            await StaReadingSession.PollAsync(() => cancelled is not null, "the external navigation");

            // A book document's CSP stops this before the host sees it, so use the one
            // non-book page allowed: about:blank carries no CSP and reaches the handler.
            var blank = new TaskCompletionSource();
            void Done(object? s, CoreWebView2NavigationCompletedEventArgs e) => blank.TrySetResult();
            core.NavigationCompleted += Done;
            core.Navigate("about:blank");
            await blank.Task.WaitAsync(TimeSpan.FromSeconds(15));
            core.NavigationCompleted -= Done;
            await StaReadingSession.EvalAsync(w.WebLeft, "void (new Image().src = 'https://probe.invalid/x.png')");
            await StaReadingSession.PollAsync(() => imageStatus is not null, "the external image");
        });

        Assert.True(cancelled);
        Assert.Equal(403, imageStatus);
    }

    [UiFact]
    public void BeforeABookIsOpen_BookRequests_Get403()
    {
        using var session = StaReadingSession.Open();
        session.WaitUntilReady();

        int? status = null;
        session.Run(async w =>
        {
            var core = w.WebLeft.CoreWebView2;
            core.WebResourceRequested += (_, e) => status ??= e.Response?.StatusCode;
            core.Navigate("https://epub.local/OEBPS/chapter1.xhtml");
            await StaReadingSession.PollAsync(() => status is not null, "the book request");
        });

        Assert.Equal(403, status);
    }

    [UiFact]
    public void SwitchingBooks_RemovesThePreviousExtractRoot()
    {
        using var session = StaReadingSession.Open(StaReadingSession.SampleEpub);
        session.WaitUntilReady();

        var first = session.Invoke(w => w.CurrentDoc!.ExtractRoot);
        var second = Path.Combine(session.DataDir, "second.epub");
        File.Copy(StaReadingSession.SampleEpub, second);

        session.Run(w => w.OpenFileAsync(second));

        Assert.NotEqual(first, session.Invoke(w => w.CurrentDoc!.ExtractRoot));
        Assert.False(Directory.Exists(first));
        Assert.False(File.Exists(first + ".lock"));
    }

    private static string? Header(CoreWebView2HttpResponseHeaders? headers, string name) =>
        headers is not null && headers.Contains(name) ? headers.GetHeader(name) : null;
}

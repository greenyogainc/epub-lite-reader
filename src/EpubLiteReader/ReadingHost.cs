using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EpubLiteReader;

/// <summary>A validated message posted by the injected reader script.</summary>
public sealed record HostMessage(string Type, double Fraction = 0, int Direction = 0, int Spine = 0, string? Href = null, string? Key = null);

/// <summary>Configures a WebView2 for safe local EPUB rendering.</summary>
public sealed class ReadingHost
{
    private const int NavigationTimeoutSeconds = 30;
    private const int MaxSpineIndex = 999_999;

    private readonly WebView2 _webView;
    private readonly string _role;
    private string? _mappedFolder;
    private bool _initialized;
    private DisplaySettings _settings = new();

    public event Action<ReadingHost, HostMessage>? MessageReceived;

    public WebView2 Control => _webView;
    public bool IsReady => _initialized;
    public string Role => _role;

    public ReadingHost(WebView2 webView, string role)
    {
        _webView = webView;
        _role = role;
    }

    public async Task EnsureReadyAsync()
    {
        if (_initialized) return;
        _webView.AllowExternalDrop = false;
        await _webView.EnsureCoreWebView2Async();
        var core = _webView.CoreWebView2;

        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;

        core.NavigationStarting += OnNavigationStarting;
        core.FrameNavigationStarting += OnFrameNavigationStarting;
        core.WebMessageReceived += OnWebMessage;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.ProcessFailed += (_, e) =>
            App.LogError(new InvalidOperationException($"Reader WebView process failed ({_role}): {e.ProcessFailedKind}"));

        // Book content must never reach the network: only the local virtual host
        // may serve resources into the reader, no matter the resource context.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;

        await core.AddScriptToExecuteOnDocumentCreatedAsync(ReaderInject.BuildDocumentCreatedScript());
        _initialized = true;
    }

    public async Task MapBookAsync(EpubDoc doc)
    {
        await EnsureReadyAsync();
        var core = _webView.CoreWebView2!;
        if (string.Equals(_mappedFolder, doc.ExtractRoot, StringComparison.OrdinalIgnoreCase))
            return;
        if (_mappedFolder is not null)
            core.ClearVirtualHostNameToFolderMapping(EpubDoc.VirtualHost);

        // DenyCors: the reader origin can load its own book resources, but no
        // other origin can read them through cross-origin requests.
        core.SetVirtualHostNameToFolderMapping(
            EpubDoc.VirtualHost,
            doc.ExtractRoot,
            CoreWebView2HostResourceAccessKind.DenyCors);
        _mappedFolder = doc.ExtractRoot;
    }

    public void SetDisplaySettings(DisplaySettings settings)
    {
        _settings = settings.Clone();
        _ = ApplySettingsAsync();
    }

    public async Task ApplySettingsAsync()
    {
        if (!_initialized) return;
        try
        {
            await _webView.ExecuteScriptAsync(ReaderInject.ApplySettingsScript(_settings));
        }
        catch (Exception ex)
        {
            // View may be mid-navigation; still surface anything unexpected for diagnosis.
            App.LogError(ex);
        }
    }

    public async Task NavigateSpineAsync(EpubDoc doc, int spineIndex, string? anchor = null, double scrollFraction = 0)
    {
        await MapBookAsync(doc);
        var url = doc.GetSpineUrl(spineIndex, anchor);
        await NavigateAndRestoreAsync(url, scrollFraction);
    }

    public async Task NavigateContinuousAsync(EpubDoc doc, int spineIndex, double spineFraction = 0)
    {
        await MapBookAsync(doc);
        // Encode the real target in the URL hash so the document's own onHash
        // handler loads and pins that spine immediately — no flash to spine 0
        // and no needless eager load of chapter 0. ContinuousGoToAsync then
        // refines to the sub-chapter fraction.
        var url = doc.GetContinuousUrl(spineIndex);
        await NavigateAndRestoreAsync(url, 0);
        await ContinuousGoToAsync(spineIndex, spineFraction);
    }

    public async Task ContinuousGoToAsync(int spineIndex, double spineFraction)
    {
        if (!_initialized) return;
        var frac = spineFraction.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.ExecuteScriptAsync(
            $"if (window.__elrContinuousGoTo) window.__elrContinuousGoTo({spineIndex}, {frac});");
    }

    public async Task NavigateBlankAsync()
    {
        if (!_initialized) return;
        _webView.CoreWebView2!.Navigate("about:blank");
        await Task.CompletedTask;
    }

    public async Task<string> PageTurnAsync(int direction)
    {
        if (!_initialized) return "end";
        var result = await _webView.ExecuteScriptAsync($"window.__elrPage ? window.__elrPage({direction}) : 'end'");
        return Unquote(result);
    }

    public async Task SetScrollFractionAsync(double fraction)
    {
        if (!_initialized) return;
        await _webView.ExecuteScriptAsync(
            $"if (window.__elrSetScrollFraction) window.__elrSetScrollFraction({fraction.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
    }

    public async Task<double> GetScrollFractionAsync()
    {
        if (!_initialized) return 0;
        var result = await _webView.ExecuteScriptAsync(
            "window.__elrScrollFraction ? window.__elrScrollFraction() : 0");
        return double.TryParse(Unquote(result), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var f)
            ? f
            : 0;
    }

    /// <summary>Continuous mode: logical position as (spine, fraction inside that spine).</summary>
    public async Task<(int Spine, double Fraction)> GetSpinePosAsync()
    {
        if (!_initialized) return (0, 0);
        try
        {
            var result = await _webView.ExecuteScriptAsync(
                "window.__elrSpinePos ? window.__elrSpinePos() : null");
            if (string.IsNullOrEmpty(result) || result == "null") return (0, 0);
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            int spine = root.TryGetProperty("spine", out var s) && s.TryGetInt32(out var si)
                ? Math.Clamp(si, 0, MaxSpineIndex) : 0;
            double frac = root.TryGetProperty("fraction", out var f) && f.ValueKind == JsonValueKind.Number
                ? f.GetDouble() : 0;
            if (!double.IsFinite(frac)) frac = 0;
            return (spine, Math.Clamp(frac, 0, 1));
        }
        catch
        {
            return (0, 0);
        }
    }

    public async Task<bool> FindAsync(string query, bool forward = true)
    {
        if (!_initialized || string.IsNullOrEmpty(query)) return false;
        var q = JsonSerializer.Serialize(query);
        var result = await _webView.ExecuteScriptAsync(
            $"window.__elrFind ? window.__elrFind({q}, {(forward ? "true" : "false")}) : false");
        return result.Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Continuous mode: loads the frame for a spine item if needed, then finds and highlights inside it.</summary>
    public async Task<bool> FindInSpineAsync(int spineIndex, string query, bool forward = true)
    {
        if (!_initialized || string.IsNullOrEmpty(query)) return false;

        await _webView.ExecuteScriptAsync(
            $"if (window.__elrEnsureSpineLoaded) window.__elrEnsureSpineLoaded({spineIndex});");
        for (int i = 0; i < 40; i++)
        {
            var loaded = await _webView.ExecuteScriptAsync(
                $"window.__elrIsSpineLoaded ? window.__elrIsSpineLoaded({spineIndex}) : false");
            if (loaded.Contains("true", StringComparison.OrdinalIgnoreCase)) break;
            await Task.Delay(100);
        }

        var q = JsonSerializer.Serialize(query);
        var result = await _webView.ExecuteScriptAsync(
            $"window.__elrFindInSpine ? window.__elrFindInSpine({spineIndex}, {q}, {(forward ? "true" : "false")}) : false");
        return result.Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    public Task PrintAsync()
    {
        if (!_initialized) return Task.CompletedTask;
        _webView.CoreWebView2!.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        return Task.CompletedTask;
    }

    private async Task NavigateAndRestoreAsync(string url, double scrollFraction)
    {
        var core = _webView.CoreWebView2!;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult();

        core.NavigationCompleted += Handler;
        try
        {
            core.Navigate(url);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(NavigationTimeoutSeconds)));
            if (done != tcs.Task)
                App.LogError(new TimeoutException($"Navigation to reader content timed out ({_role})."));
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }

        await ApplySettingsAsync();
        if (scrollFraction > 0.001)
            await SetScrollFractionAsync(scrollFraction);
    }

    internal static bool IsAllowedReaderUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return false;
        if (uri.StartsWith($"https://{EpubDoc.VirtualHost}/", StringComparison.OrdinalIgnoreCase)) return true;
        // The only non-book document allowed is the harmless internal blank page.
        return string.Equals(uri, "about:blank", StringComparison.OrdinalIgnoreCase);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsAllowedReaderUri(e.Uri)) return;
        e.Cancel = true;
        RaiseBlockedNavigation(e.Uri ?? "");
    }

    private void OnFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsAllowedReaderUri(e.Uri)) return;
        e.Cancel = true;
        RaiseBlockedNavigation(e.Uri ?? "");
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = e.Request.Uri ?? "";
        if (uri.StartsWith($"https://{EpubDoc.VirtualHost}/", StringComparison.OrdinalIgnoreCase))
            return;
        e.Response = _webView.CoreWebView2!.Environment.CreateWebResourceResponse(
            null, 403, "Forbidden", "Content-Type: text/plain");
    }

    private void RaiseBlockedNavigation(string uri)
    {
        var href = uri.Length > 2048 ? uri[..2048] : uri;
        MessageReceived?.Invoke(this, new HostMessage("blocked-nav", Href: href));
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            // Only documents we ourselves loaded may talk to the host.
            if (!IsAllowedReaderUri(e.Source)) return;
            if (TryParseMessage(e.WebMessageAsJson, out var msg))
                MessageReceived?.Invoke(this, msg);
        }
        catch
        {
            // ignore malformed messages
        }
    }

    /// <summary>
    /// Parses and validates a raw web message. Unknown types, missing fields,
    /// non-finite or out-of-range numbers, and unexpected directions are rejected.
    /// </summary>
    internal static bool TryParseMessage(string? json, out HostMessage message)
    {
        message = null!;
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String) return false;

            switch (t.GetString())
            {
                case "ready":
                    message = new HostMessage("ready");
                    return true;

                case "scroll":
                {
                    if (!root.TryGetProperty("fraction", out var f) || f.ValueKind != JsonValueKind.Number) return false;
                    var frac = f.GetDouble();
                    if (!double.IsFinite(frac)) return false;
                    message = new HostMessage("scroll", Fraction: Math.Clamp(frac, 0, 1));
                    return true;
                }

                case "spinepos":
                {
                    if (!root.TryGetProperty("spine", out var s) || !s.TryGetInt32(out var spine)) return false;
                    if (spine < 0 || spine > MaxSpineIndex) return false;
                    if (!root.TryGetProperty("fraction", out var f) || f.ValueKind != JsonValueKind.Number) return false;
                    var frac = f.GetDouble();
                    if (!double.IsFinite(frac)) return false;
                    message = new HostMessage("spinepos", Fraction: Math.Clamp(frac, 0, 1), Spine: spine);
                    return true;
                }

                case "step":
                {
                    if (!root.TryGetProperty("direction", out var d) || !d.TryGetInt32(out var dir)) return false;
                    if (dir is not (-1 or 1)) return false;
                    message = new HostMessage("step", Direction: dir);
                    return true;
                }

                case "key":
                {
                    if (!root.TryGetProperty("key", out var k) || k.ValueKind != JsonValueKind.String) return false;
                    var key = k.GetString();
                    if (key is not ("1" or "2" or "3" or "F4" or "F11" or "Escape")) return false;
                    message = new HostMessage("key", Key: key);
                    return true;
                }

                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Unquote(string jsResult)
    {
        if (string.IsNullOrEmpty(jsResult)) return "";
        if (jsResult.Length >= 2 && jsResult[0] == '"' && jsResult[^1] == '"')
            return JsonSerializer.Deserialize<string>(jsResult) ?? "";
        return jsResult.Trim('"');
    }
}

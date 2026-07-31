using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EpubLiteReader;

/// <summary>Configures a WebView2 for safe local EPUB rendering.</summary>
public sealed class ReadingHost
{
    private readonly WebView2 _webView;
    private readonly string _role;
    private string? _mappedFolder;
    private bool _initialized;
    private DisplaySettings _settings = new();

    public event Action<string>? MessageReceived;
    public event Action<int>? SpineNavigated;

    public WebView2 Control => _webView;
    public bool IsReady => _initialized;

    public ReadingHost(WebView2 webView, string role)
    {
        _webView = webView;
        _role = role;
    }

    public async Task EnsureReadyAsync()
    {
        if (_initialized) return;
        await _webView.EnsureCoreWebView2Async();
        var core = _webView.CoreWebView2;

        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

        core.NavigationStarting += OnNavigationStarting;
        core.WebMessageReceived += OnWebMessage;
        core.NewWindowRequested += (_, e) => e.Handled = true;

        await core.AddScriptToExecuteOnDocumentCreatedAsync(ReaderInject.BuildDocumentCreatedScript());
        _initialized = true;
    }

    public async Task MapBookAsync(EpubDoc doc)
    {
        await EnsureReadyAsync();
        var core = _webView.CoreWebView2!;
        if (_mappedFolder is not null)
            core.ClearVirtualHostNameToFolderMapping(EpubDoc.VirtualHost);

        core.SetVirtualHostNameToFolderMapping(
            EpubDoc.VirtualHost,
            doc.ExtractRoot,
            CoreWebView2HostResourceAccessKind.Allow);
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
        SpineNavigated?.Invoke(spineIndex);
    }

    public async Task NavigateContinuousAsync(EpubDoc doc, int spineIndex, double scrollFraction = 0)
    {
        await MapBookAsync(doc);
        var url = doc.GetContinuousUrl(spineIndex);
        await NavigateAndRestoreAsync(url, scrollFraction);
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

    public async Task<bool> FindAsync(string query, bool forward = true)
    {
        if (!_initialized || string.IsNullOrEmpty(query)) return false;
        var q = JsonSerializer.Serialize(query);
        var result = await _webView.ExecuteScriptAsync(
            $"window.__elrFind ? window.__elrFind({q}, {(forward ? "true" : "false")}) : false");
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
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            _webView.CoreWebView2!.NavigationCompleted -= Handler;
            tcs.TrySetResult();
        }

        _webView.CoreWebView2!.NavigationCompleted += Handler;
        _webView.CoreWebView2.Navigate(url);
        await tcs.Task;
        await ApplySettingsAsync();
        if (scrollFraction > 0.001)
            await SetScrollFractionAsync(scrollFraction);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var uri = e.Uri ?? "";
        if (uri.StartsWith($"https://{EpubDoc.VirtualHost}/", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return;

        e.Cancel = true;
        MessageReceived?.Invoke($"{{\"type\":\"blocked-nav\",\"href\":{JsonSerializer.Serialize(uri)}}}");
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            MessageReceived?.Invoke(e.WebMessageAsJson);
        }
        catch
        {
            // ignore malformed messages
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

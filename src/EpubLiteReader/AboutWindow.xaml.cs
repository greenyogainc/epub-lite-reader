using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EpubLiteReader;

/// <summary>
/// About and Contact Support. The reader stays offline: the only network-capable
/// surface in the app is the dedicated support WebView2 created here, and only
/// after the user explicitly asks for the support page.
/// </summary>
public partial class AboutWindow : Window
{
    internal const string SupportUrl = "https://greenyogainc.com/contact/";
    internal const string SupportEmailAddress = "andreab@greenyogainc.com";

    private static readonly string[] AllowedSupportHosts = { "greenyogainc.com", "api.greenyogainc.com" };

    private WebView2? _supportView;
    private bool _supportViewInitializing;

    public AboutWindow()
    {
        InitializeComponent();
        Strings.ApplyFlowDirection(this);

        Title = string.Format(Strings.Get("AboutWindowTitleFormat"), Strings.Get("AppTitle"));

        var asm = typeof(AboutWindow).Assembly;
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                            ?? asm.GetName().Version?.ToString() ?? "?";
        var displayVersion = informational.Split('+')[0];
        VersionText.Text = string.Format(Strings.Get("AboutVersionFormat"), displayVersion);
        VersionText.ToolTip = informational;

        CopyrightText.Text = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
                             ?? "© Green Yoga Inc";

        try
        {
            AppMark.Source = new BitmapImage(new Uri("pack://application:,,,/app.ico"));
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }

        LoadLicenseText();
        Closed += (_, _) => TearDownSupportView();
        WriteAboutState();
    }

    private void LoadLicenseText()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
            LicenseText.Text = File.Exists(path)
                ? File.ReadAllText(path)
                : Strings.Get("AboutLicenseUnavailable");
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            LicenseText.Text = Strings.Get("AboutLicenseUnavailable");
        }
    }

    // ---------- Links ----------

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenExternal(e.Uri.ToString());
        e.Handled = true;
    }

    private static void OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
    }

    // ---------- Support view ----------

    private void ContactSupport_Click(object sender, RoutedEventArgs e)
    {
        AboutView.Visibility = Visibility.Collapsed;
        SupportView.Visibility = Visibility.Visible;
        SupportLoadButton.Focus();
        WriteAboutState();
    }

    private void SupportBack_Click(object sender, RoutedEventArgs e)
    {
        SupportView.Visibility = Visibility.Collapsed;
        AboutView.Visibility = Visibility.Visible;
        ContactSupportButton.Focus();
        WriteAboutState();
    }

    private async void SupportLoad_Click(object sender, RoutedEventArgs e) => await LoadSupportPageAsync();

    private async void SupportRetry_Click(object sender, RoutedEventArgs e) => await LoadSupportPageAsync();

    private void SupportOpenBrowser_Click(object sender, RoutedEventArgs e) => OpenExternal(SupportUrl);

    private void SupportEmail_Click(object sender, RoutedEventArgs e) =>
        OpenExternal($"mailto:{SupportEmailAddress}?subject={Uri.EscapeDataString(Strings.Get("AppTitle"))}");

    private async Task LoadSupportPageAsync()
    {
        if (_supportViewInitializing) return;

        SupportDisclosurePanel.Visibility = Visibility.Collapsed;
        SupportFailurePanel.Visibility = Visibility.Collapsed;
        SupportLoadingText.Visibility = Visibility.Visible;

        try
        {
            if (_supportView is null)
            {
                _supportViewInitializing = true;
                try
                {
                    var view = new WebView2 { AllowExternalDrop = false };
                    SupportWebHost.Child = view;
                    await view.EnsureCoreWebView2Async();
                    ConfigureSupportView(view.CoreWebView2);
                    _supportView = view;
                }
                finally
                {
                    _supportViewInitializing = false;
                }
            }

            _supportView.CoreWebView2!.Navigate(SupportUrl);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            ShowSupportFailure();
        }
    }

    private void ConfigureSupportView(CoreWebView2 core)
    {
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;

        core.NavigationStarting += SupportNavigationStarting;
        core.FrameNavigationStarting += SupportFrameNavigationStarting;
        core.NavigationCompleted += SupportNavigationCompleted;
        core.NewWindowRequested += SupportNewWindowRequested;
        core.DownloadStarting += SupportDownloadStarting;
        core.PermissionRequested += SupportPermissionRequested;
        core.ProcessFailed += SupportProcessFailed;

        // Only the exact hosts needed by the contact form may be reached from
        // this view. That cuts off Google Tag Manager, Microsoft Clarity, and
        // every other third party the public page would normally load.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += SupportWebResourceRequested;
    }

    /// <summary>Exact-host HTTPS allowlist for the embedded support experience.</summary>
    internal static bool IsAllowedSupportUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return false;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return false;
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var host in AllowedSupportHosts)
        {
            if (string.Equals(parsed.Host, host, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void SupportNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsAllowedSupportUri(e.Uri)) return;
        e.Cancel = true;
        // A link the user actually clicked may continue in the system browser;
        // automatic redirects and scripted navigation are simply dropped.
        if (e.IsUserInitiated && Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
            (uri.Scheme is "https" or "http" or "mailto"))
        {
            OpenExternal(e.Uri!);
        }
    }

    private void SupportFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!IsAllowedSupportUri(e.Uri))
            e.Cancel = true;
    }

    private void SupportNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess && e.HttpStatusCode < 400)
        {
            SupportLoadingText.Visibility = Visibility.Collapsed;
            SupportFailurePanel.Visibility = Visibility.Collapsed;
            SupportWebHost.Visibility = Visibility.Visible;
        }
        else
        {
            ShowSupportFailure();
        }
        WriteAboutState();
    }

    private void SupportNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!e.IsUserInitiated) return;
        if (IsAllowedSupportUri(e.Uri))
            _supportView?.CoreWebView2?.Navigate(e.Uri);
        else if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            OpenExternal(e.Uri);
    }

    private void SupportDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
        e.Handled = true;
    }

    private void SupportPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e) =>
        e.State = CoreWebView2PermissionState.Deny;

    private void SupportProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        App.LogError(new InvalidOperationException($"Support WebView process failed: {e.ProcessFailedKind}"));
        ShowSupportFailure();
    }

    private void SupportWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (IsAllowedSupportUri(e.Request.Uri)) return;
        var core = _supportView?.CoreWebView2;
        if (core is not null)
            e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "Content-Type: text/plain");
    }

    private void ShowSupportFailure()
    {
        SupportLoadingText.Visibility = Visibility.Collapsed;
        SupportWebHost.Visibility = Visibility.Collapsed;
        SupportFailurePanel.Visibility = Visibility.Visible;
        WriteAboutState();
    }

    private void TearDownSupportView()
    {
        try
        {
            if (_supportView?.CoreWebView2 is CoreWebView2 core)
            {
                core.NavigationStarting -= SupportNavigationStarting;
                core.FrameNavigationStarting -= SupportFrameNavigationStarting;
                core.NavigationCompleted -= SupportNavigationCompleted;
                core.NewWindowRequested -= SupportNewWindowRequested;
                core.DownloadStarting -= SupportDownloadStarting;
                core.PermissionRequested -= SupportPermissionRequested;
                core.ProcessFailed -= SupportProcessFailed;
                core.WebResourceRequested -= SupportWebResourceRequested;
            }
            _supportView?.Dispose();
            _supportView = null;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
        WriteAboutState(open: false);
    }

    /// <summary>Mirrors About/Support state next to the main automation state file.</summary>
    private void WriteAboutState(bool open = true)
    {
        var file = App.AutomationStateFile;
        if (file is null) return;
        try
        {
            var state = new
            {
                aboutOpen = open,
                supportView = open && SupportView.Visibility == Visibility.Visible,
                supportLoaded = open && SupportWebHost.Visibility == Visibility.Visible,
                supportFailed = open && SupportFailurePanel.Visibility == Visibility.Visible,
                timestamp = DateTime.UtcNow.ToString("O")
            };
            File.WriteAllText(file + ".about", System.Text.Json.JsonSerializer.Serialize(state));
        }
        catch
        {
            // Automation mirroring must never affect the app.
        }
    }
}

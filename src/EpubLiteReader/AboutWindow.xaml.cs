using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using GreenYoga.Support;

namespace EpubLiteReader;

/// <summary>About and native support. Reading stays offline; support sends only on request.</summary>
public partial class AboutWindow : Window
{
    private static readonly HttpClient SupportHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    private readonly AppSupportClient _supportClient;
    private AppSupportDraft _draft = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _appVersion;
    private readonly string _osVersion = Environment.OSVersion.VersionString;
    private readonly string _locale = string.IsNullOrEmpty(CultureInfo.CurrentUICulture.Name) ? "en" : CultureInfo.CurrentUICulture.Name;
    private bool _sending;
    private bool _sent;
    private bool _failed;
    private bool _closed;

    public AboutWindow() : this(new AppSupportClient(SupportHttp)) { }

    internal AboutWindow(AppSupportClient supportClient)
    {
        _supportClient = supportClient;
        InitializeComponent();
        Strings.ApplyFlowDirection(this);
        Title = string.Format(Strings.Get("AboutWindowTitleFormat"), Strings.Get("AppTitle"));

        var asm = typeof(AboutWindow).Assembly;
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                            ?? asm.GetName().Version?.ToString() ?? "?";
        _appVersion = informational.Split('+')[0];
        VersionText.Text = string.Format(Strings.Get("AboutVersionFormat"), _appVersion);
        VersionText.ToolTip = informational;
        SupportDetailsText.Text = string.Format(Strings.Get("SupportDetailsFormat"),
            Strings.Get("AppTitle"), _appVersion, _osVersion, _locale);

        CopyrightText.Text = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
                             ?? "© Green Yoga Inc";
        try
        {
            AppMark.Source = new BitmapImage(new Uri("pack://application:,,,/app.ico"));
        }
        catch (Exception ex) { App.LogError(ex); }

        LoadLicenseText();
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            WriteAboutState(open: false);
        };
        WriteAboutState();
    }

    private void LoadLicenseText()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
            LicenseText.Text = File.Exists(path) ? File.ReadAllText(path) : Strings.Get("AboutLicenseUnavailable");
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            LicenseText.Text = Strings.Get("AboutLicenseUnavailable");
        }
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true }); }
        catch (Exception ex) { App.LogError(ex); }
        e.Handled = true;
    }

    private void ContactSupport_Click(object sender, RoutedEventArgs e)
    {
        AboutView.Visibility = Visibility.Collapsed;
        SupportView.Visibility = Visibility.Visible;
        SupportView.UpdateLayout();
        if (_sent) SupportSendAnotherButton.Focus();
        else SupportNameBox.Focus();
        WriteAboutState();
    }

    private void SupportBack_Click(object sender, RoutedEventArgs e)
    {
        SupportView.Visibility = Visibility.Collapsed;
        AboutView.Visibility = Visibility.Visible;
        ContactSupportButton.Focus();
        WriteAboutState();
    }

    private async void SupportSend_Click(object sender, RoutedEventArgs e)
    {
        if (_sending || _sent || _closed) return;
        var request = _draft.GetRequest("epub-lite-reader", _appVersion, _osVersion, _locale,
            SupportNameBox.Text, SupportEmailBox.Text, SupportSubjectBox.Text, SupportMessageBox.Text);
        var invalid = AppSupportClient.Validate(request);
        if (invalid is not null)
        {
            ShowStatus(Strings.Get(invalid == AppSupportStatus.InvalidEmail ? "SupportInvalidEmail" : "SupportRequired"), failed: true);
            return;
        }

        _sending = true;
        _failed = false;
        SetFormEnabled(false);
        ShowStatus(Strings.Get("SupportSending"));
        try
        {
            var result = await _supportClient.SendAsync(request, _lifetime.Token);
            if (_closed) return;
            if (result.Status == AppSupportStatus.Success)
            {
                _sent = true;
                SupportSendButton.Visibility = Visibility.Collapsed;
                SupportSendAnotherButton.Visibility = Visibility.Visible;
                ShowStatus(Strings.Get("SupportSent") + "\n" +
                    string.Format(Strings.Get("SupportReferenceFormat"), result.RequestId), success: true);
            }
            else
            {
                var key = result.Status switch
                {
                    AppSupportStatus.InvalidEmail => "SupportInvalidEmail",
                    AppSupportStatus.InvalidRequest => "SupportRequired",
                    AppSupportStatus.RateLimited => "SupportRateLimited",
                    AppSupportStatus.InProgress => "SupportInProgress",
                    AppSupportStatus.Conflict => "SupportConflict",
                    _ => "SupportSendFailed"
                };
                ShowStatus(Strings.Get(key), failed: true);
            }
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (!_closed) ShowStatus(Strings.Get("SupportSendFailed"), failed: true);
        }
        finally
        {
            _sending = false;
            if (!_closed)
            {
                SetFormEnabled(!_sent);
                WriteAboutState();
            }
        }
    }

    private void SupportSendAnother_Click(object sender, RoutedEventArgs e)
    {
        if (_sending) return;
        _sent = false;
        _failed = false;
        _draft = new();
        SupportSubjectBox.Clear();
        SupportMessageBox.Clear();
        SupportStatusText.Text = "";
        SupportStatusText.Visibility = Visibility.Collapsed;
        SupportSendAnotherButton.Visibility = Visibility.Collapsed;
        SupportSendButton.Visibility = Visibility.Visible;
        SetFormEnabled(true);
        SupportSubjectBox.Focus();
        WriteAboutState();
    }

    private void SetFormEnabled(bool enabled)
    {
        SupportFields.IsEnabled = enabled;
        SupportSendButton.IsEnabled = enabled;
    }

    private void ShowStatus(string message, bool failed = false, bool success = false)
    {
        _failed = failed;
        SupportStatusText.Text = message;
        SupportStatusText.Foreground = new SolidColorBrush(
            success ? Color.FromRgb(139, 218, 171) : failed ? Color.FromRgb(255, 180, 171) : Color.FromRgb(221, 221, 221));
        SupportStatusText.Visibility = Visibility.Visible;
        if (!_sending || success || failed)
        {
            SupportStatusText.Focus();
            SupportStatusText.BringIntoView();
        }
        WriteAboutState();
    }

    /// <summary>Mirrors native form state without contact details or message content.</summary>
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
                supportLoaded = open && SupportView.Visibility == Visibility.Visible,
                supportFailed = open && _failed,
                supportSending = open && _sending,
                supportSent = open && _sent,
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

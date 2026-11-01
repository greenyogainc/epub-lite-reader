using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using EpubLiteReader;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>ApplyArgs writes the same statics BookStateStore tests redirect, so the two must not overlap.</summary>
[Collection(BookStateStoreCollection.Name)]
public sealed class AppArgsTests : IDisposable
{
    private readonly CultureInfo _uiCulture;
    private readonly CultureInfo? _defaultUiCulture;
    private readonly string? _rootOverride;

    public AppArgsTests()
    {
        _uiCulture = CultureInfo.CurrentUICulture;
        _defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        _rootOverride = BookStateStore.RootOverride;
    }

    public void Dispose()
    {
        App.ApplyArgs(Array.Empty<string>());
        BookStateStore.RootOverride = _rootOverride;
        Thread.CurrentThread.CurrentUICulture = _uiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUiCulture;
    }

    [Fact]
    public void Lang_SetsUiCulture()
    {
        App.ApplyArgs(new[] { "--lang=fr" });

        Assert.Equal("fr", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        Assert.Equal("fr", CultureInfo.DefaultThreadCurrentUICulture!.TwoLetterISOLanguageName);
    }

    [Fact]
    public void Lang_UnknownCulture_IsIgnored()
    {
        var german = CultureInfo.GetCultureInfo("de");
        Thread.CurrentThread.CurrentUICulture = german;
        CultureInfo.DefaultThreadCurrentUICulture = german;

        App.ApplyArgs(new[] { "--lang=bad culture" });

        Assert.Equal("de", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        Assert.Equal("de", CultureInfo.DefaultThreadCurrentUICulture!.TwoLetterISOLanguageName);
    }

    [Fact]
    public void StateFile_BecomesAFullPath()
    {
        var file = Path.Combine(Path.GetTempPath(), "elr-state-" + Guid.NewGuid().ToString("N") + ".json");

        App.ApplyArgs(new[] { "--statefile=" + file });

        Assert.Equal(Path.GetFullPath(file), App.AutomationStateFile);
    }

    [Fact]
    public void DataDir_RedirectsTheStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "elr-data-" + Guid.NewGuid().ToString("N"));

        App.ApplyArgs(new[] { "--datadir=" + dir });

        Assert.Equal(Path.GetFullPath(dir), BookStateStore.RootOverride);
    }

    [Fact]
    public void StartupFile_RequiresAnExistingPath()
    {
        var missing = Path.Combine(Path.GetTempPath(), "elr-missing-" + Guid.NewGuid().ToString("N") + ".epub");
        App.ApplyArgs(new[] { missing });
        Assert.Null(App.ParsedStartupFile);

        var existing = Path.Combine(Path.GetTempPath(), "elr-existing-" + Guid.NewGuid().ToString("N") + ".epub");
        File.WriteAllBytes(existing, Array.Empty<byte>());
        try
        {
            App.ApplyArgs(new[] { "--datadir=" + Path.GetTempPath(), existing });
            Assert.Equal(existing, App.ParsedStartupFile);
        }
        finally
        {
            File.Delete(existing);
        }
    }

    [Fact]
    public void StartupFile_IsPublishedOnTheRunningApplication()
    {
        var existing = Path.Combine(Path.GetTempPath(), "elr-existing-" + Guid.NewGuid().ToString("N") + ".epub");
        File.WriteAllBytes(existing, Array.Empty<byte>());
        try
        {
            // WPF allows one Application per process, and it must be made on an STA thread.
            // Application.Current is only cleared when Run() unwinds, so run the message
            // loop just long enough to check and shut down. No window is created: App.xaml's
            // StartupUri is applied by InitializeComponent, which is not called here. Anything
            // thrown on the dispatcher would reach App's unhandled-exception dialog, so it
            // is captured instead.
            Exception? failure = null;
            RunOnStaThread(() =>
            {
                var app = new App();
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        App.ApplyArgs(new[] { "--datadir=" + Path.GetTempPath(), existing });
                        Assert.Equal(existing, app.StartupFile);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        app.Shutdown();
                    }
                }));
                app.Run();
            });
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            Assert.Null(System.Windows.Application.Current);
        }
        finally
        {
            File.Delete(existing);
        }
    }

    private static void RunOnStaThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true; // a hung message loop must not keep the test host alive
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("The STA thread did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

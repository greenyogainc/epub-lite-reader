using Microsoft.Web.WebView2.Wpf;
using Xunit;

namespace EpubLiteReader.UiTests;

/// <summary>
/// Messages posted by the page itself (window.chrome.webview.postMessage), from each pane, through the
/// real WebView2 channel to the reader's handler.
/// </summary>
public sealed class HostMessageRoutingTests
{
    private static string Title() => "Messages " + Guid.NewGuid().ToString("N");

    /// <summary>Records what a pane's host passes on. Added after the reader's own handler, so when it
    /// sees a message the reader has already acted on it.</summary>
    private sealed class Probe
    {
        private readonly List<HostMessage> _seen = new();
        private (string Type, TaskCompletionSource Done)? _waiting;

        public Probe(ReadingHost host) => host.MessageReceived += (_, message) =>
        {
            _seen.Add(message);
            if (_waiting is { } waiting && waiting.Type == message.Type)
            {
                _waiting = null;
                waiting.Done.TrySetResult();
            }
        };

        public IReadOnlyList<string> Types => _seen.Select(m => m.Type).ToList();

        /// <summary>Posts <paramref name="json"/> from the page and waits until a message of
        /// <paramref name="type"/> has been delivered.</summary>
        public async Task PostAsync(WebView2 view, string json, string type)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiting = (type, done);
            await view.ExecuteScriptAsync("window.chrome.webview.postMessage(" + json + ")");
            await done.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }

        /// <summary>Posts messages the reader must drop, then a valid one to know they have all been handled.</summary>
        public async Task PostIgnoredAsync(WebView2 view, params string[] messages)
        {
            foreach (var message in messages)
                await view.ExecuteScriptAsync("window.chrome.webview.postMessage(" + message + ")");
            await PostAsync(view, "{ type: 'ready' }", "ready");
        }
    }

    [UiFact]
    public void Scroll_FromTheLeftPane_SetsThePosition_ButNotFromTheRightPaneOrInContinuousMode()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            var left = new Probe(w.LeftHost!);
            var right = new Probe(w.RightHost!);

            await left.PostAsync(w.WebLeft, "{ type: 'scroll', fraction: 0.25 }", "scroll");
            Assert.Equal(0.25, w.CurrentFraction);
            Assert.Equal(string.Format(Strings.Get("ProgressFormat"), 5), w.ProgressText.Text);

            await left.PostAsync(w.WebLeft, "{ type: 'scroll', fraction: 7 }", "scroll");
            Assert.Equal(1.0, w.CurrentFraction);

            // The right pane reports its own scroll all the time in facing mode; it is not the reading position.
            w.ModeFacing.IsChecked = true;
            await w.WhenNavIdleAsync();
            await w.GoToSpineAsync(1);
            await left.PostAsync(w.WebLeft, "{ type: 'scroll', fraction: 0.5 }", "scroll");
            await right.PostAsync(w.WebRight, "{ type: 'scroll', fraction: 0.9 }", "scroll");
            Assert.Equal(0.5, w.CurrentFraction);

            // The continuous document reports a logical position instead (spinepos).
            w.ModeContinuous.IsChecked = true;
            await w.WhenNavIdleAsync();
            await left.PostAsync(w.WebLeft, "{ type: 'scroll', fraction: 0.9 }", "scroll");
            Assert.NotEqual(0.9, w.CurrentFraction);
        });
    }

    [UiFact]
    public void SpinePos_InContinuousMode_MovesTheSpine_AndOtherwiseIsIgnored()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            var left = new Probe(w.LeftHost!);
            var right = new Probe(w.RightHost!);

            // Outside continuous mode the position is reported through scroll, so spinepos means nothing.
            await left.PostAsync(w.WebLeft, "{ type: 'spinepos', spine: 3, fraction: 0.5 }", "spinepos");
            Assert.Equal(0, w.CurrentSpine);

            w.ModeContinuous.IsChecked = true;
            await w.WhenNavIdleAsync();
            // Let the document's own position reports finish, so they cannot land after ours.
            await Task.Delay(500);

            await left.PostAsync(w.WebLeft, "{ type: 'spinepos', spine: 3, fraction: 0.5 }", "spinepos");
            Assert.Equal(3, w.CurrentSpine);
            Assert.Equal("4", w.PageBox.Text);
            Assert.Equal(0.5, w.CurrentFraction);

            // Same chapter, new place.
            await left.PostAsync(w.WebLeft, "{ type: 'spinepos', spine: 3, fraction: 0.2 }", "spinepos");
            Assert.Equal(3, w.CurrentSpine);
            Assert.Equal(0.2, w.CurrentFraction);

            await right.PostAsync(w.WebRight, "{ type: 'spinepos', spine: 1, fraction: 0.7 }", "spinepos");
            Assert.Equal(3, w.CurrentSpine);
            Assert.Equal(0.2, w.CurrentFraction);

            await left.PostAsync(w.WebLeft, "{ type: 'spinepos', spine: 999999, fraction: 0.1 }", "spinepos");
            Assert.Equal(4, w.CurrentSpine);
            Assert.Equal("5", w.PageBox.Text);
        });
    }

    [UiFact]
    public void Ready_PushesTheCurrentDisplaySettingsToThePage()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            const string Background = "getComputedStyle(document.body).backgroundColor";
            Assert.Equal("rgb(255, 255, 255)", await UiKit.EvalStringAsync(w.WebLeft, Background));

            // Changed behind the page's back: only a 'ready' makes the host send it again.
            w.CurrentDisplay.Theme = ReadingTheme.Dark;
            await new Probe(w.LeftHost!).PostAsync(w.WebLeft, "{ type: 'ready' }", "ready");

            // The settings are applied by a call the handler does not wait for.
            for (var i = 0; i < 200 && await UiKit.EvalStringAsync(w.WebLeft, Background) != "rgb(30, 30, 30)"; i++)
                await Task.Delay(25);
            Assert.Equal("rgb(30, 30, 30)", await UiKit.EvalStringAsync(w.WebLeft, Background));
        });
    }

    [UiFact]
    public void Step_FromThePage_ScrollsThenAdvancesLikeTheKeys()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            await w.GoToSpineAsync(1);
            var left = new Probe(w.LeftHost!);

            await left.PostAsync(w.WebLeft, "{ type: 'step', direction: 1 }", "step");
            await StaReadingSession.PollAsync(() => w.CurrentFraction > 0, "the chapter to scroll");
            Assert.Equal(1, w.CurrentSpine);

            await left.PostAsync(w.WebLeft, "{ type: 'step', direction: -1 }", "step");
            await StaReadingSession.PollAsync(() => w.CurrentSpine == 0 || w.CurrentFraction < 0.001, "the page to go back");
        });
    }

    [UiFact]
    public void Key_FromThePage_RunsTheAppShortcut()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            var left = new Probe(w.LeftHost!);
            Task Key(string key) => left.PostAsync(w.WebLeft, "{ type: 'key', key: '" + key + "' }", "key");

            await Key("2");
            await w.WhenNavIdleAsync();
            Assert.Equal(ViewMode.Facing, w.CurrentMode);
            await Key("3");
            await w.WhenNavIdleAsync();
            Assert.Equal(ViewMode.Continuous, w.CurrentMode);
            await Key("1");
            await w.WhenNavIdleAsync();
            Assert.Equal(ViewMode.Single, w.CurrentMode);

            await Key("F4");
            Assert.True(w.IsChapterPaneVisible);
            await Key("F4");
            Assert.False(w.IsChapterPaneVisible);

            // Escape leaves fullscreen first, then closes the search bar, and does nothing otherwise.
            await Key("F11");
            Assert.True(w.IsFullscreen);
            w.SearchToggle.IsChecked = true;
            await Key("Escape");
            Assert.False(w.IsFullscreen);
            Assert.True(w.SearchToggle.IsChecked);
            await Key("Escape");
            Assert.False(w.SearchToggle.IsChecked);
            await Key("Escape");
            Assert.False(w.IsFullscreen);
            Assert.False(w.SearchToggle.IsChecked);
        });
    }

    [UiFact]
    public void MalformedMessages_AreDropped_AndAThrowingHandlerDoesNotStopLaterOnes()
    {
        using var session = TestBook.Rich(Title()).Open();
        session.WaitUntilReady();

        session.Run(async w =>
        {
            var probe = new Probe(w.LeftHost!);
            await probe.PostIgnoredAsync(w.WebLeft,
                "{ type: 'nonsense' }",
                "{ type: 'scroll' }",
                "{ type: 'scroll', fraction: 'x' }",
                "{ type: 'spinepos', spine: -1, fraction: 0 }",
                "{ type: 'step', direction: 5 }",
                "{ type: 'key', key: 'Q' }",
                "'just a string'",
                "[1, 2]",
                "null");
            // Nothing but the sentinel reached the reader's handler, and nothing moved.
            Assert.Equal(new[] { "ready" }, probe.Types);
            Assert.Equal(0, w.CurrentSpine);
            Assert.Equal(0, w.CurrentFraction);

            var thrown = 0;
            w.LeftHost!.MessageReceived += (_, _) =>
            {
                if (thrown++ == 0)
                    throw new InvalidOperationException("a handler failed");
            };
            // The reader's own handler runs first, so this message is processed; the failing handler
            // after it must not stop the next message from arriving.
            await w.WebLeft.ExecuteScriptAsync("window.chrome.webview.postMessage({ type: 'ready' })");
            await probe.PostAsync(w.WebLeft, "{ type: 'scroll', fraction: 0.3 }", "scroll");
            Assert.Equal(0.3, w.CurrentFraction);
            Assert.True(thrown >= 2, "The failing handler ran for the first message and again for the next.");
        });
    }
}

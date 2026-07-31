namespace EpubLiteReader;

/// <summary>CSS/JS injected into every chapter WebView for themes, typography, messaging.</summary>
internal static class ReaderInject
{
    public static string BuildDocumentCreatedScript() => """
        (() => {
          if (window.__elrInstalled) return;
          window.__elrInstalled = true;

          // Resolved lazily: this script runs at document-creation time, when
          // documentElement/head may not exist yet. Appending eagerly there
          // throws and silently kills every window.__elr* definition below,
          // which reads downstream as "themes and typography do nothing".
          function styleEl() {
            const root = document.head || document.documentElement;
            if (!root) return null;
            let el = document.getElementById('elr-chrome');
            if (!el) {
              el = document.createElement('style');
              el.id = 'elr-chrome';
              root.appendChild(el);
            }
            return el;
          }

          window.__elrApply = function(settings) {
            const s = settings || (window.__elrLastSettings || {});
            window.__elrLastSettings = s;
            const style = styleEl();
            if (!style) return;
            const theme = s.theme || 'light';
            const font = s.fontFamily || 'publisher';
            const size = typeof s.fontScale === 'number' ? s.fontScale : 1;
            const line = typeof s.lineHeight === 'number' ? s.lineHeight : 1.5;
            const margin = typeof s.marginEm === 'number' ? s.marginEm : 1.2;

            let bg = '#ffffff', fg = '#1a1a1a', link = '#1F5B94';
            if (theme === 'sepia') { bg = '#f4ecd8'; fg = '#5b4636'; link = '#8b4513'; }
            if (theme === 'dark')  { bg = '#1e1e1e'; fg = '#e0e0e0'; link = '#6cb6ff'; }

            let ff = 'inherit';
            if (font === 'serif') ff = 'Georgia, "Times New Roman", serif';
            if (font === 'sans')  ff = 'Segoe UI, Tahoma, sans-serif';

            style.textContent = `
              html, body {
                background: ${bg} !important;
                color: ${fg} !important;
                font-size: ${size}em !important;
                line-height: ${line} !important;
                max-width: none !important;
                margin: 0 !important;
                padding: ${margin}em ${margin * 1.4}em !important;
              }
              body, p, div, span, li, td, th, blockquote, h1, h2, h3, h4, h5, h6 {
                ${font === 'publisher' ? '' : `font-family: ${ff} !important;`}
                color: inherit;
              }
              a { color: ${link} !important; }
              img, svg { max-width: 100%; height: auto; }
            `;

            try {
              document.documentElement.style.colorScheme = theme === 'dark' ? 'dark' : 'light';
            } catch (e) {}
          };

          // The style element lives in the document, so a fresh parse drops it.
          // Re-apply the last known settings once the DOM exists.
          document.addEventListener('DOMContentLoaded', () => window.__elrApply(null));

          window.__elrScrollFraction = function() {
            const se = document.scrollingElement || document.documentElement;
            const max = Math.max(1, se.scrollHeight - se.clientHeight);
            return se.scrollTop / max;
          };

          window.__elrSetScrollFraction = function(f) {
            const se = document.scrollingElement || document.documentElement;
            const max = Math.max(0, se.scrollHeight - se.clientHeight);
            se.scrollTop = Math.max(0, Math.min(1, f)) * max;
          };

          window.__elrPage = function(dir) {
            const se = document.scrollingElement || document.documentElement;
            const page = Math.max(120, se.clientHeight * 0.9);
            const before = se.scrollTop;
            se.scrollTop = before + dir * page;
            const after = se.scrollTop;
            if (dir > 0 && after <= before + 1) return 'end';
            if (dir < 0 && after >= before - 1) return 'start';
            return 'scrolled';
          };

          window.__elrFind = function(query, forward) {
            if (!query) return false;
            try {
              if (window.find) {
                return window.find(query, false, !forward, true, false, false, false);
              }
            } catch (e) {}
            return false;
          };

          function post(msg) {
            try {
              if (window.chrome && window.chrome.webview)
                window.chrome.webview.postMessage(msg);
            } catch (e) {}
          }

          let scrollTimer = null;
          window.addEventListener('scroll', () => {
            if (scrollTimer) clearTimeout(scrollTimer);
            scrollTimer = setTimeout(() => {
              post({ type: 'scroll', fraction: window.__elrScrollFraction() });
            }, 120);
          }, { passive: true });

          document.addEventListener('click', (ev) => {
            const a = ev.target && ev.target.closest ? ev.target.closest('a[href]') : null;
            if (!a) return;
            const href = a.getAttribute('href') || '';
            if (href.startsWith('#') || href.startsWith('https://epub.local/') || href.startsWith('/'))
              return;
            if (/^https?:/i.test(href) || /^mailto:/i.test(href)) {
              ev.preventDefault();
              post({ type: 'blocked-nav', href });
            }
          }, true);

          post({ type: 'ready' });
        })();
        """;

    public static string ApplySettingsScript(DisplaySettings settings)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            theme = settings.Theme.ToString().ToLowerInvariant(),
            fontFamily = settings.FontFamily.ToString().ToLowerInvariant(),
            fontScale = settings.FontScale,
            lineHeight = settings.LineHeight,
            marginEm = settings.MarginEm
        });
        return $"if (window.__elrApply) window.__elrApply({json});";
    }
}

public enum ReadingTheme { Light, Sepia, Dark }
public enum ReaderFontFamily { Publisher, Serif, Sans }

public sealed class DisplaySettings
{
    public ReadingTheme Theme { get; set; } = ReadingTheme.Light;
    public ReaderFontFamily FontFamily { get; set; } = ReaderFontFamily.Publisher;
    public double FontScale { get; set; } = 1.0;
    public double LineHeight { get; set; } = 1.5;
    public double MarginEm { get; set; } = 1.2;
    public ViewMode ViewMode { get; set; } = ViewMode.Single;

    public DisplaySettings Clone() => new()
    {
        Theme = Theme,
        FontFamily = FontFamily,
        FontScale = FontScale,
        LineHeight = LineHeight,
        MarginEm = MarginEm,
        ViewMode = ViewMode
    };

    public void ResetTypography()
    {
        FontScale = 1.0;
        LineHeight = 1.5;
        MarginEm = 1.2;
        FontFamily = ReaderFontFamily.Publisher;
    }
}

public enum ViewMode { Single, Facing, Continuous }

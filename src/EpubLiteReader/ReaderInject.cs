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
            // Allowlist, not blocklist: only fragments, book-relative paths, and
            // the book's own virtual host may navigate. Everything else —
            // http(s), mailto, javascript:, vbscript:, data:, file:, custom
            // schemes — is inert inside the reader.
            if (href.startsWith('#') || href.startsWith('/') ||
                href.toLowerCase().startsWith('https://epub.local/'))
              return;
            if (/^[a-z][a-z0-9+.-]*:/i.test(href) === false)
              return; // scheme-less relative path within the book
            ev.preventDefault();
            ev.stopPropagation();
          }, true);

          // ----- Click / key page turning -----
          // Both funnel into the same host 'step' message, so a tap and a key
          // press advance identically: scroll within the chapter, then move to
          // the next one at the end.

          function isTypingTarget(el) {
            if (!el) return false;
            const tag = (el.tagName || '').toUpperCase();
            return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable;
          }

          // A click that ends a drag is a text selection, not a page turn.
          let downX = 0, downY = 0;
          document.addEventListener('mousedown', (ev) => {
            downX = ev.clientX; downY = ev.clientY;
          }, true);

          document.addEventListener('click', (ev) => {
            if (ev.button !== 0 || ev.detail > 1) return;
            if (ev.defaultPrevented) return;
            if (isTypingTarget(ev.target)) return;
            if (ev.target && ev.target.closest && ev.target.closest('a[href]')) return;
            if (Math.abs(ev.clientX - downX) > 6 || Math.abs(ev.clientY - downY) > 6) return;
            try {
              const sel = window.getSelection();
              if (sel && !sel.isCollapsed && String(sel).trim()) return;
            } catch (e) {}

            // Left quarter goes back, the rest goes forward -- the usual
            // e-reader tap zones.
            const w = window.innerWidth || 1;
            post({ type: 'step', direction: ev.clientX < w * 0.25 ? -1 : 1 });
          });

          document.addEventListener('keydown', (ev) => {
            if (ev.ctrlKey || ev.altKey || ev.metaKey) return;
            if (isTypingTarget(ev.target)) return;

            // App-level shortcuts must keep working while the reading pane has
            // focus, so forward them to the host instead of letting the
            // browser swallow them.
            if (!ev.shiftKey &&
                (ev.key === '1' || ev.key === '2' || ev.key === '3' ||
                 ev.key === 'F4' || ev.key === 'F11' || ev.key === 'Escape')) {
              ev.preventDefault();
              post({ type: 'key', key: ev.key });
              return;
            }

            let dir = 0;
            if (ev.key === ' ' || ev.key === 'Spacebar') dir = ev.shiftKey ? -1 : 1;
            else if (ev.key === 'PageDown') dir = 1;
            else if (ev.key === 'PageUp') dir = -1;
            else if (ev.key === 'ArrowRight') dir = 1;
            else if (ev.key === 'ArrowLeft') dir = -1;
            if (!dir) return;

            // The host scrolls for us; letting the page also scroll would
            // advance twice per press.
            ev.preventDefault();
            post({ type: 'step', direction: dir });
          });

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

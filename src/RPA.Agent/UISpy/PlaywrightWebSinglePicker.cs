namespace RPA.Agent.UISpy;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using RPA.Domain.ValueObjects;

/// <summary>
/// <see cref="IWebSinglePicker"/>'ın Playwright implementasyonu — WebSpy tek-seçim kipi
/// (Spec Bölüm 5, Paket D). Başlıklı (headed) bir Chromium açar; kullanıcı hedef siteye
/// gezinip elemente <c>CTRL+Tık</c> yaparak seçer, <c>Esc</c> ile iptal eder. Sayfaya enjekte
/// edilen script imleç altındaki elementi vurgular ve tıklanınca kararlı bir CSS selector üretir.
/// </summary>
public sealed class PlaywrightWebSinglePicker : IWebSinglePicker
{
    private const string StartUrl = "about:blank";
    private readonly ILogger<PlaywrightWebSinglePicker> _logger;

    public PlaywrightWebSinglePicker(ILogger<PlaywrightWebSinglePicker> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<WebUiElement?> DetectOnceAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("WebSpy: tarayıcı açılıyor — hedefe CTRL+Tık, Esc iptal.");
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        IPlaywright? playwright = null;
        IBrowser? browser = null;
        try
        {
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = false });
            var page = await browser.NewPageAsync();

            await page.ExposeFunctionAsync<string>("__rpaPick", json => tcs.TrySetResult(json));
            await page.AddInitScriptAsync(PickerScript);
            await page.GotoAsync(StartUrl);

            await using var _ = cancellationToken.Register(() => tcs.TrySetResult(null));
            var payload = await tcs.Task;
            if (string.IsNullOrEmpty(payload))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("cancelled", out var cancelled)
                && cancelled.ValueKind == JsonValueKind.True)
            {
                _logger.LogDebug("WebSpy: kullanıcı Esc ile iptal etti.");
                return null;
            }

            var selector = GetString(root, "selector");
            if (string.IsNullOrWhiteSpace(selector))
            {
                return null;
            }

            var element = new WebUiElement
            {
                Selector = selector,
                TagName = GetString(root, "tagName"),
                InnerTextPreview = GetString(root, "text"),
                PageUrl = GetString(root, "url"),
            };
            _logger.LogInformation("WebSpy: element seçildi {Selector}", element.Selector);
            return element;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WebSpy: seçim başarısız.");
            return null;
        }
        finally
        {
            if (browser is not null)
            {
                try { await browser.CloseAsync(); } catch { /* kapatma hatası yok sayılır */ }
            }
            playwright?.Dispose();
        }
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // Her navigasyonda çalışan seçim scripti: banner + hover vurgu + CTRL+Tık yakala + Esc iptal.
    private const string PickerScript = @"
(() => {
  if (window.__rpaPickerInstalled) return;
  window.__rpaPickerInstalled = true;

  const banner = document.createElement('div');
  banner.textContent = 'RPA Seçici: hedefe CTRL+TIK · iptal için ESC';
  Object.assign(banner.style, { position:'fixed', top:'0', left:'0', right:'0', zIndex:2147483647,
    background:'#1e293b', color:'#fff', font:'13px sans-serif', padding:'6px 10px',
    textAlign:'center', pointerEvents:'none' });
  const attachBanner = () => { if (document.body && !banner.isConnected) document.body.appendChild(banner); };
  document.addEventListener('DOMContentLoaded', attachBanner);
  attachBanner();

  let last = null;
  const outline = (el) => {
    if (last && last !== el) last.style.outline = last.__rpaPrev || '';
    if (el && el !== last) { el.__rpaPrev = el.style.outline; el.style.outline = '2px solid #f97316'; }
    last = el;
  };
  document.addEventListener('mousemove', (e) => outline(e.target), true);

  const esc = (s) => (window.CSS && CSS.escape) ? CSS.escape(s) : s.replace(/[^a-zA-Z0-9_-]/g, '\\$&');
  const selectorFor = (el) => {
    if (!el || el.nodeType !== 1 || el === document.documentElement) return 'html';
    if (el.id && document.querySelectorAll('#' + esc(el.id)).length === 1) return '#' + esc(el.id);
    const tid = el.getAttribute && el.getAttribute('data-testid');
    if (tid) return el.tagName.toLowerCase() + '[data-testid=' + JSON.stringify(tid) + ']';
    const parts = [];
    let node = el;
    while (node && node.nodeType === 1 && node !== document.body && parts.length < 6) {
      let part = node.tagName.toLowerCase();
      const parent = node.parentElement;
      if (parent) {
        const same = Array.prototype.filter.call(parent.children, (c) => c.tagName === node.tagName);
        if (same.length > 1) part += ':nth-of-type(' + (same.indexOf(node) + 1) + ')';
      }
      parts.unshift(part);
      node = node.parentElement;
    }
    return parts.join(' > ');
  };

  document.addEventListener('click', (e) => {
    if (!e.ctrlKey) return;
    e.preventDefault(); e.stopPropagation();
    const el = e.target;
    const info = { selector: selectorFor(el),
      tagName: el.tagName ? el.tagName.toLowerCase() : null,
      text: ((el.innerText || el.value || '') + '').trim().slice(0, 80),
      url: location.href };
    if (window.__rpaPick) window.__rpaPick(JSON.stringify(info));
  }, true);

  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && window.__rpaPick) window.__rpaPick(JSON.stringify({ cancelled: true }));
  }, true);
})();
";
}

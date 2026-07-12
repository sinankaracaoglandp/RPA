namespace RPA.Domain.Interfaces;

/// <summary>
/// Herhangi bir Windows masaüstü uygulaması üzerinde UI Automation (UIA/FlaUI) tabanlı
/// otomasyon kanalı. İnsan hareketlerini taklit eder: tıklama, metin yazma, öğe seçme,
/// tuş gönderme (Spec Bölüm 5 — Paket E, Masaüstü Otomasyonu).
///
/// <para>Selector formatı UIA yoludur: <c>AutomationId</c> öncelikli, yoksa
/// <c>ControlType+Name</c> zinciri (örn.
/// <c>Window[Title~'Hesap.*']/Pane/Edit[AutomationId='amount']</c>).</para>
///
/// <para>SAP GUI (<see cref="ISapGuiChannel"/>) ve Web (Playwright) kanallarından bağımsızdır;
/// SAP ekranları için SAP kanalı, tarayıcı için Web kanalı kullanılmalıdır.</para>
///
/// <para>Exception sınıflandırması: element bulunamadı / timeout → <c>SystemException</c>
/// (teknik hata, retry edilebilir); bu kanalda iş kuralı reddi (Business) yoktur.</para>
/// </summary>
public interface IDesktopAutomationChannel
{
    /// <summary>
    /// Çalışan bir uygulamanın penceresine bağlanır (attach) ve aktif hedef yapar.
    /// </summary>
    /// <param name="processName">Süreç adı (örn. "notepad"); null ise <paramref name="windowTitle"/> kullanılır.</param>
    /// <param name="windowTitle">Pencere başlığı (regex desteklenir); null olabilir.</param>
    /// <returns>Pencere tanıtıcısı (opak string — sonraki hedefleme için).</returns>
    Task<string> AttachAsync(string? processName, string? windowTitle);

    /// <summary>
    /// Bir uygulamayı başlatır ve ana penceresine bağlanır.
    /// </summary>
    /// <param name="path">Çalıştırılabilir yol.</param>
    /// <param name="arguments">Komut satırı argümanları (opsiyonel).</param>
    /// <param name="waitForIdle">Pencere girişe hazır olana kadar bekle.</param>
    /// <returns>Pencere tanıtıcısı.</returns>
    Task<string> LaunchAsync(string path, string? arguments, bool waitForIdle);

    /// <summary>Bir elemente tıklar (butona basma, menü açma).</summary>
    /// <param name="selector">UIA selector yolu.</param>
    /// <param name="clickType">"left" (varsayılan), "right", "double".</param>
    Task ClickAsync(string selector, string? clickType);

    /// <summary>Bir alanı (textbox) metinle doldurur.</summary>
    Task SetTextAsync(string selector, string text);

    /// <summary>Bir elementin metnini okur.</summary>
    Task<string> GetTextAsync(string selector);

    /// <summary>Combobox / liste / menüde bir öğe seçer.</summary>
    Task SelectItemAsync(string selector, string item);

    /// <summary>
    /// Klavye tuşları gönderir (tarih yazma, kısayol). <paramref name="selector"/> null ise
    /// aktif odağa gönderilir; doluysa önce o element odaklanır.
    /// </summary>
    Task SendKeysAsync(string? selector, string keys);

    /// <summary>
    /// Bir element görünür/etkin olana kadar bekler. Süre aşımında <c>SystemException</c> fırlatır.
    /// </summary>
    /// <param name="timeoutMs">Zaman aşımı (ms); 0/negatif ise kanal varsayılanı.</param>
    Task WaitForAsync(string selector, int timeoutMs);

    /// <summary>
    /// Ekran görüntüsü alır. <paramref name="selector"/> doluysa yalnız o elementi,
    /// null ise aktif pencereyi yakalar.
    /// </summary>
    /// <returns>Kaydedilen PNG dosya yolu.</returns>
    Task<string> ScreenshotAsync(string? selector);
}

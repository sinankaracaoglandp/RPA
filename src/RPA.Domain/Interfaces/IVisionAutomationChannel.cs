namespace RPA.Domain.Interfaces;

/// <summary>
/// Erişilebilirlik ağacı (UIA/DOM) olmayan uygulamalar için piksel + metin tabanlı
/// otomasyon kanalı (Spec Bölüm — Paket F, Görüntü/OCR Fallback). Template matching
/// (OpenCvSharp) ve OCR (Tesseract) ile ekrandaki nesneyi bulur; gerçek fare/klavye
/// ile etkileşir. Etkileşimli masaüstü oturumu gerektirir.
///
/// <para>Exception sınıflandırması: görüntü/metin bulunamadı / timeout →
/// <c>SystemException</c> (teknik, retry edilebilir). Var-mı sorguları (<see cref="ImageExistsAsync"/>,
/// <see cref="TextExistsAsync"/>) fırlatmaz, false döner.</para>
/// </summary>
public interface IVisionAutomationChannel
{
    /// <summary>Base64 PNG template'i ekranda bulur, merkezine tıklar. timeoutMs içinde bulunmazsa SystemException.</summary>
    Task ClickImageAsync(string imageBase64, double confidence, string? clickType, int timeoutMs);

    /// <summary>Template ekranda görünene kadar bekler. Süre aşımı → SystemException.</summary>
    Task WaitForImageAsync(string imageBase64, double confidence, int timeoutMs);

    /// <summary>Template ekranda var mı? Fırlatmaz. timeoutMs 0 ise tek bakış.</summary>
    Task<bool> ImageExistsAsync(string imageBase64, double confidence, int timeoutMs);

    /// <summary>Bölgeden (null ise tam ekran) OCR ile metin okur. language örn. "tur+eng".</summary>
    Task<string> GetTextAsync(int? x, int? y, int? width, int? height, string language);

    /// <summary>OCR ile metni bulur, merkezine tıklar. matchMode "contains" (vars.) / "exact". Bulunmazsa SystemException.</summary>
    Task ClickTextAsync(string text, string language, string matchMode, string? clickType, int timeoutMs);

    /// <summary>
    /// OCR ile anchorText'i bulur, kelime kutusunun merkezinden (dx,dy) piksel ofsetle tıklar
    /// (etiketin yanındaki boş alan gibi kendi başına ayırt edilemeyen hedefler için).
    /// Çapa bulunamazsa/timeout → SystemException.
    /// </summary>
    Task ClickTextOffsetAsync(string anchorText, int dx, int dy,
        string language, string matchMode, string? clickType, int timeoutMs);

    /// <summary>Metin ekranda var mı? Fırlatmaz.</summary>
    Task<bool> TextExistsAsync(string text, string language, string matchMode, int timeoutMs);
}

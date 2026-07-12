namespace RPA.Domain.ValueObjects;

/// <summary>
/// Bir web (DOM) elementinin değişmez tanımı. WebSpy, kullanıcının tarayıcıda CTRL+Tık ile
/// seçtiği elementi tespit eder; <see cref="Selector"/> aktivitelerin (Web.*) hedefleme için
/// kullandığı CSS selector'dır (Spec Bölüm 5 — Paket D, Web Otomasyonu).
/// </summary>
public sealed record WebUiElement
{
    /// <summary>CSS selector (örn. <c>#login</c> veya <c>form > button:nth-of-type(2)</c>).</summary>
    public required string Selector { get; init; }

    /// <summary>HTML etiket adı (örn. "button", "input", "a").</summary>
    public string? TagName { get; init; }

    /// <summary>Elementin görünen metin önizlemesi (kısaltılmış).</summary>
    public string? InnerTextPreview { get; init; }

    /// <summary>Seçim anında sayfanın URL'si.</summary>
    public string? PageUrl { get; init; }

    public WebUiElement() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public WebUiElement(string selector, string? tagName = null, string? innerTextPreview = null, string? pageUrl = null)
    {
        Selector = selector;
        TagName = tagName;
        InnerTextPreview = innerTextPreview;
        PageUrl = pageUrl;
    }
}

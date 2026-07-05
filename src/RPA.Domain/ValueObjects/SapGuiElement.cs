namespace RPA.Domain.ValueObjects;

/// <summary>
/// Bir SAP GUI ekran elementinin değişmez (immutable) tanımı. UI Spy modülü bir elementin
/// hiyerarşik ID'sini (<c>wnd[0]/usr/...</c>) çıkarır; aktiviteler bu ID ile elementi hedefler.
/// Spec Bölüm 6 (UI Spy).
/// </summary>
public sealed record SapGuiElement
{
    /// <summary>Hiyerarşik element ID (örn. "wnd[0]/usr/ctxtRMMG1-MATNR").</summary>
    public required string Id { get; init; }

    /// <summary>Element tipi (örn. "GuiTextField", "GuiButton", "GuiGridView", "GuiTab").</summary>
    public string? Type { get; init; }

    /// <summary>Görsel metin / etiket (varsa).</summary>
    public string? Text { get; init; }

    /// <summary>Element etkin/tıklanabilir mi?</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Element değiştirilebilir mi (input alanı)?</summary>
    public bool Changeable { get; init; } = true;

    /// <summary>İmleç/ekran konumu — X koordinatı (piksel). UI Spy tespit anındaki konum.</summary>
    public int X { get; init; }

    /// <summary>İmleç/ekran konumu — Y koordinatı (piksel). UI Spy tespit anındaki konum.</summary>
    public int Y { get; init; }

    public SapGuiElement() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SapGuiElement(string id, string? type = null, string? text = null)
    {
        Id = id;
        Type = type;
        Text = text;
    }
}

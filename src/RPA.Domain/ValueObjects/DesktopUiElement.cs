namespace RPA.Domain.ValueObjects;

/// <summary>
/// Bir Windows masaüstü (UIA) elementinin değişmez tanımı. DesktopSpy imleç altındaki elementi
/// tespit eder; <see cref="UiaPath"/> aktivitelerin hedefleme için kullandığı selector'dır
/// (Spec Bölüm 5 — Paket E, Masaüstü Otomasyonu).
/// </summary>
public sealed record DesktopUiElement
{
    /// <summary>
    /// UIA selector yolu (örn. <c>Window[Title~'Hesap.*']/Edit[AutomationId='amount']</c>).
    /// Aktivitelerin <c>selector</c> alanına yazılan değer.
    /// </summary>
    public required string UiaPath { get; init; }

    /// <summary>UIA AutomationId (varsa).</summary>
    public string? AutomationId { get; init; }

    /// <summary>UIA ControlType (örn. "Button", "Edit", "ComboBox").</summary>
    public string? ControlType { get; init; }

    /// <summary>Görsel ad / etiket (Name).</summary>
    public string? Name { get; init; }

    /// <summary>Sahibi sürecin adı (örn. "notepad").</summary>
    public string? ProcessName { get; init; }

    /// <summary>İmleç/ekran konumu — X (piksel).</summary>
    public int X { get; init; }

    /// <summary>İmleç/ekran konumu — Y (piksel).</summary>
    public int Y { get; init; }

    public DesktopUiElement() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public DesktopUiElement(string uiaPath, string? controlType = null, string? name = null)
    {
        UiaPath = uiaPath;
        ControlType = controlType;
        Name = name;
    }
}

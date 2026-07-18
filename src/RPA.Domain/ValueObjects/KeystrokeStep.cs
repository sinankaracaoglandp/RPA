namespace RPA.Domain.ValueObjects;

/// <summary>Bir <c>Desktop.SendKeys</c> tuş dizisi adımının türü.</summary>
public enum KeystrokeStepType
{
    /// <summary>Opsiyonel modifier(ler) + tek ana tuş (örn. Ctrl+A, F4, Home).</summary>
    Chord,

    /// <summary>Düz metin yazar (örn. "09.07.2026").</summary>
    Text,
}

/// <summary>
/// <c>Desktop.SendKeys</c> yapısal tuş dizisinin tek adımı. Bir node içinde sırayla çalışan
/// adımlar; her adım ya bir tuş vuruşu (chord) ya da düz metindir (Spec — Paket E, yapısal tuş
/// editörü). Değişmezdir; ayrıştırma ve doğrulama <see cref="KeystrokeSequenceParser"/>'dadır.
/// </summary>
public sealed record KeystrokeStep
{
    /// <summary>Adım türü.</summary>
    public required KeystrokeStepType Type { get; init; }

    /// <summary>Chord için modifier'lar (küçük harf: ctrl, shift, alt, altgr, win). Text için boş.</summary>
    public IReadOnlyList<string> Modifiers { get; init; } = Array.Empty<string>();

    /// <summary>Chord için ana tuş (kanonik ad, örn. "A", "F4", "Enter"). Text için null.</summary>
    public string? Key { get; init; }

    /// <summary>Text adımı için yazılacak metin. Chord için null.</summary>
    public string? Text { get; init; }

    /// <summary>Adımdan sonra beklenecek süre (ms); negatifler 0'a sabitlenir.</summary>
    public int WaitMs { get; init; }
}

namespace RPA.Infrastructure.Retry;

/// <summary>
/// Retry gecikmesi büyüme stratejisi (Spec Bölüm 5.2, 6 — "üstel geri çekilme").
/// </summary>
public enum BackoffStrategy
{
    /// <summary>Sabit artış: gecikme = initial * denemeSayısı.</summary>
    Linear,

    /// <summary>Üstel geri çekilme: gecikme = initial * çarpan^(denemeSayısı-1).</summary>
    Exponential,
}

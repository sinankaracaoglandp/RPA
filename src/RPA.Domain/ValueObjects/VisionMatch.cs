namespace RPA.Domain.ValueObjects;

/// <summary>
/// Ekranda bulunan bir görüntü/metin eşleşmesinin sınır kutusu ve güven skoru.
/// Anchor (Faz 2) için matcher/OCR birden çok VisionMatch döndürür; aktiviteler
/// şimdilik en yüksek skorlu tekini kullanır.
/// </summary>
public sealed record VisionMatch(int X, int Y, int Width, int Height, double Score)
{
    /// <summary>Eşleşmenin yatay merkezi (tıklama noktası).</summary>
    public int CenterX => X + (Width / 2);

    /// <summary>Eşleşmenin dikey merkezi (tıklama noktası).</summary>
    public int CenterY => Y + (Height / 2);
}

namespace RPA.Agent.Vision;

using RPA.Domain.ValueObjects;

/// <summary>Çapa kelime kutusunun merkezinden piksel ofsetle tıklama noktasını hesaplar.
/// Picker-zamanı ve runtime aynı referansı (tight OCR kutusu merkezi) kullanır.</summary>
public static class VisionOffset
{
    public static (int X, int Y) ClickPoint(VisionMatch anchorBox, int dx, int dy)
        => (anchorBox.CenterX + dx, anchorBox.CenterY + dy);
}

namespace RPA.Agent.Vision;

using System.Runtime.Versioning;
using OpenCvSharp;
using RPA.Domain.ValueObjects;
using Tesseract;

/// <summary>Tesseract OCR — bir görüntüden tam metni ve kelime kutularını çıkarır.
/// Kanal ve text-offset picker tarafından paylaşılır.</summary>
[SupportedOSPlatform("windows")]
public static class OcrEngine
{
    public sealed record OcrWord(string Text, VisionMatch Box);

    public static (string Text, List<OcrWord> Words) Read(Mat image, string tessdataPath, string language)
    {
        using var engine = new TesseractEngine(tessdataPath, language, EngineMode.Default);
        var bytes = image.ImEncode(".png");
        using var pix = Pix.LoadFromMemory(bytes);
        using var page = engine.Process(pix);
        var full = page.GetText() ?? string.Empty;

        var words = new List<OcrWord>();
        using var iter = page.GetIterator();
        iter.Begin();
        do
        {
            if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var r))
            {
                var w = iter.GetText(PageIteratorLevel.Word);
                words.Add(new OcrWord(w ?? string.Empty, new VisionMatch(r.X1, r.Y1, r.Width, r.Height, 1.0)));
            }
        }
        while (iter.Next(PageIteratorLevel.Word));
        return (full, words);
    }
}

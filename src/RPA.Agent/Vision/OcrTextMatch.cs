namespace RPA.Agent.Vision;

using System.Globalization;

/// <summary>OCR kelime kutuları ile aranan metni normalize ederek (boşluk/case, TR-duyarlı) eşleştirir.</summary>
public static class OcrTextMatch
{
    public static bool Matches(string? ocrWord, string query, string matchMode)
    {
        var a = Normalize(ocrWord);
        var b = Normalize(query);
        if (b.Length == 0)
        {
            return false;
        }
        return string.Equals(matchMode, "exact", StringComparison.Ordinal)
            ? string.Equals(a, b, StringComparison.Ordinal)
            : a.Contains(b, StringComparison.Ordinal);
    }

    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }
        var collapsed = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.ToLower(new CultureInfo("tr-TR")).Trim();
    }
}

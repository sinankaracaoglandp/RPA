namespace RPA.Domain.ValueObjects;

using System.Text.Json;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// <c>Desktop.SendKeys</c> ham <c>keys</c> alanını tipli <see cref="KeystrokeStep"/> listesine
/// çevirir. Alan JSON adım dizisi taşır; geçerli JSON dizi değilse tek bir metin adımı olarak
/// yorumlanır (geriye uyumluluk: eski düz-metin <c>keys</c> değerleri korunur).
///
/// <para>Doğrulama hataları (tanınmayan tuş/modifier, boş chord, boş metin, boş girdi)
/// <see cref="BusinessException"/> fırlatır — kullanıcı girdi hatası (Business).</para>
/// </summary>
public static class KeystrokeSequenceParser
{
    /// <summary>Geçerli modifier'lar (küçük harf).</summary>
    public static readonly IReadOnlyList<string> Modifiers = new[] { "ctrl", "shift", "alt", "altgr", "win" };

    /// <summary>Geçerli ana tuşlar (kanonik ad). Studio dropdown paleti ile aynı küme.</summary>
    public static readonly IReadOnlyList<string> Keys = BuildKeys();

    private static readonly HashSet<string> ModifierSet =
        new(Modifiers, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> KeyLookup =
        Keys.ToDictionary(k => k, StringComparer.OrdinalIgnoreCase);

    /// <summary>Ham <c>keys</c> değerini ayrıştırır ve doğrular.</summary>
    public static IReadOnlyList<KeystrokeStep> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new BusinessException("'keys' parametresi boş olamaz.");
        }

        var trimmed = raw.TrimStart();
        if (!trimmed.StartsWith('['))
        {
            // JSON dizi değil → geriye uyumlu tek metin adımı.
            return new[] { new KeystrokeStep { Type = KeystrokeStepType.Text, Text = raw } };
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new[] { new KeystrokeStep { Type = KeystrokeStepType.Text, Text = raw } };
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return new[] { new KeystrokeStep { Type = KeystrokeStepType.Text, Text = raw } };
        }

        var steps = new List<KeystrokeStep>();
        foreach (var element in root.EnumerateArray())
        {
            steps.Add(ParseStep(element));
        }

        if (steps.Count == 0)
        {
            throw new BusinessException("Tuş dizisi en az bir adım içermelidir.");
        }

        return steps;
    }

    private static KeystrokeStep ParseStep(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new BusinessException("Tuş adımı bir nesne olmalıdır.");
        }

        var type = GetString(element, "type")?.Trim().ToLowerInvariant();
        var waitMs = GetWaitMs(element);

        switch (type)
        {
            case "chord":
                return ParseChord(element, waitMs);
            case "text":
                return ParseText(element, waitMs);
            default:
                throw new BusinessException($"Tanınmayan tuş adımı türü: '{type}'. 'chord' veya 'text' olmalıdır.");
        }
    }

    private static KeystrokeStep ParseChord(JsonElement element, int waitMs)
    {
        var modifiers = new List<string>();
        if (element.TryGetProperty("modifiers", out var mods) && mods.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in mods.EnumerateArray())
            {
                var name = m.GetString()?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                if (!ModifierSet.Contains(name))
                {
                    throw new BusinessException($"Geçersiz modifier: '{name}'.");
                }
                modifiers.Add(name.ToLowerInvariant());
            }
        }

        var key = GetString(element, "key")?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            throw new BusinessException("Tuş vuruşu adımında ana tuş ('key') zorunludur.");
        }
        if (!KeyLookup.TryGetValue(key, out var canonicalKey))
        {
            throw new BusinessException($"Tanınmayan tuş: '{key}'.");
        }

        return new KeystrokeStep
        {
            Type = KeystrokeStepType.Chord,
            Modifiers = modifiers,
            Key = canonicalKey,
            WaitMs = waitMs,
        };
    }

    private static KeystrokeStep ParseText(JsonElement element, int waitMs)
    {
        var text = GetString(element, "text");
        if (string.IsNullOrEmpty(text))
        {
            throw new BusinessException("Metin adımında 'text' boş olamaz.");
        }

        return new KeystrokeStep
        {
            Type = KeystrokeStepType.Text,
            Text = text,
            WaitMs = waitMs,
        };
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetWaitMs(JsonElement element)
    {
        if (element.TryGetProperty("waitMs", out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var ms))
        {
            return ms > 0 ? ms : 0;
        }
        return 0;
    }

    private static string[] BuildKeys()
    {
        var keys = new List<string>();
        for (var c = 'A'; c <= 'Z'; c++)
        {
            keys.Add(c.ToString());
        }
        for (var d = 0; d <= 9; d++)
        {
            keys.Add(d.ToString());
        }
        for (var f = 1; f <= 12; f++)
        {
            keys.Add($"F{f}");
        }
        keys.AddRange(new[]
        {
            "Home", "End", "PageUp", "PageDown",
            "Up", "Down", "Left", "Right",
            "Tab", "Enter", "Esc", "Space", "Backspace", "Delete", "Insert",
        });
        return keys.ToArray();
    }
}

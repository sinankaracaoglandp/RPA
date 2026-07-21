namespace RPA.Infrastructure.SAP;

using RPA.Domain.Exceptions;

/// <summary>
/// SAP GUI Scripting sanal tuş (VKey) numaralarının kullanıcı-dostu adlarla eşlenmesi.
///
/// <para>SAP'ın standart VKey tablosu: <c>0</c> = Enter, <c>1–12</c> = F1–F12,
/// <c>13–24</c> = Shift+F1–F12, <c>25–36</c> = Ctrl+F1–F12, <c>37–48</c> = Ctrl+Shift+F1–F12.
/// Bu numaralar SAP sürümünden bağımsızdır.</para>
///
/// <para>Kullanıcı Studio'da "F8" seçer; numarayı bilmesi gerekmez.</para>
/// </summary>
public static class SapVirtualKey
{
    /// <summary>SAP'ın kabul ettiği en büyük VKey (Ctrl+Shift+F12).</summary>
    public const int MaxVKey = 48;

    /// <summary>
    /// "F8", "Enter", "Shift+F4", "Ctrl+S" gibi bir adı ya da düz numarayı ("8") SAP VKey
    /// numarasına çevirir. Tanınmayan girdi → <see cref="BusinessException"/> (tasarım hatasıdır,
    /// teknik arıza değil).
    /// </summary>
    public static int Parse(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new BusinessException("SAP tuşu boş olamaz (örn. 'F8', 'Enter', 'Ctrl+S').");
        }

        var text = key.Trim();

        // Düz numara ("8") — ileri düzey kullanım.
        if (int.TryParse(text, out var numeric))
        {
            if (numeric is < 0 or > MaxVKey)
            {
                throw new BusinessException(
                    $"SAP VKey numarası 0–{MaxVKey} aralığında olmalıdır (verilen: {numeric}).");
            }

            return numeric;
        }

        var normalized = text.Replace(" ", string.Empty).ToUpperInvariant();

        if (normalized is "ENTER" or "F0")
        {
            return 0;
        }

        // Yaygın SAP kısayolları — kullanıcı bunları F-numarası olarak değil isimle bilir.
        switch (normalized)
        {
            case "CTRL+S":
            case "SAVE":
                return 11; // SAP'ta Kaydet = F11 = Ctrl+S
            case "BACK":
                return 3;  // Geri
            case "EXIT":
                return 15; // Shift+F3 — Çıkış
            case "CANCEL":
                return 12; // F12 — İptal
            case "EXECUTE":
                return 8;  // F8 — Çalıştır
        }

        var ctrl = false;
        var shift = false;
        while (true)
        {
            if (normalized.StartsWith("CTRL+", StringComparison.Ordinal))
            {
                ctrl = true;
                normalized = normalized["CTRL+".Length..];
                continue;
            }

            if (normalized.StartsWith("SHIFT+", StringComparison.Ordinal))
            {
                shift = true;
                normalized = normalized["SHIFT+".Length..];
                continue;
            }

            break;
        }

        if (normalized.Length < 2 ||
            normalized[0] != 'F' ||
            !int.TryParse(normalized.AsSpan(1), out var fNumber) ||
            fNumber is < 1 or > 12)
        {
            throw new BusinessException(
                $"Tanınmayan SAP tuşu: '{key}'. Beklenen: Enter, F1–F12, Shift+F1–F12, " +
                "Ctrl+F1–F12, Ctrl+Shift+F1–F12 (veya 0–48 arası VKey numarası).");
        }

        var offset = (ctrl, shift) switch
        {
            (false, false) => 0,
            (false, true) => 12,
            (true, false) => 24,
            (true, true) => 36,
        };

        return fNumber + offset;
    }
}

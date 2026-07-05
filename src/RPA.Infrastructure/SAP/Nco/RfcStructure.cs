namespace RPA.Infrastructure.SAP.Nco;

using System.Runtime.Versioning;
using RPA.Domain.Interfaces;

/// <summary>
/// RFC yapı yardımcısı (Task 4.2) — iş akışı girdisini RFC/BAPI yapısına ve BAPI çıktısını
/// iş akışı çıktısına eşler. Ayrıca standart SAP RFC_READ_TABLE ve BAPI RETURN yapısı için
/// yardımcı ayrıştırma/oluşturma metotları sağlar.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RfcStructure
{
    private readonly Dictionary<string, object?> _fields;

    public RfcStructure()
        : this(new Dictionary<string, object?>())
    {
    }

    public RfcStructure(IReadOnlyDictionary<string, object?> fields)
    {
        _fields = new Dictionary<string, object?>(
            fields ?? throw new ArgumentNullException(nameof(fields)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Alan sayısı.</summary>
    public int Count => _fields.Count;

    /// <summary>Alan oku/yaz (büyük/küçük harf duyarsız).</summary>
    public object? this[string field]
    {
        get => _fields.TryGetValue(field, out var v) ? v : null;
        set => _fields[field] = value;
    }

    /// <summary>Alanı zincirlenebilir şekilde set et.</summary>
    public RfcStructure Set(string field, object? value)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("Alan adı boş olamaz.", nameof(field));
        }

        _fields[field] = value;
        return this;
    }

    /// <summary>Alanı string olarak oku (yoksa boş).</summary>
    public string GetString(string field) => this[field]?.ToString() ?? string.Empty;

    /// <summary>Import parametresi olarak dışa ver (kopya).</summary>
    public IReadOnlyDictionary<string, object?> ToImporting() =>
        new Dictionary<string, object?>(_fields, StringComparer.OrdinalIgnoreCase);

    // =============================================================== RFC_READ_TABLE yardımcıları

    /// <summary>SAP tarafında bir satırın (WA) genişlik sınırı — OPTIONS/klasik RFC_READ_TABLE 72 karakter.</summary>
    public const int ReadTableOptionLineLength = 72;

    /// <summary>
    /// WHERE koşulunu RFC_READ_TABLE OPTIONS tablosuna böl (satır başına en çok 72 karakter,
    /// kelime sınırlarını koruyarak).
    /// </summary>
    public static List<Dictionary<string, object?>> BuildOptions(string? where)
    {
        var options = new List<Dictionary<string, object?>>();
        if (string.IsNullOrWhiteSpace(where))
        {
            return options;
        }

        foreach (var chunk in ChunkPreservingWords(where.Trim(), ReadTableOptionLineLength))
        {
            options.Add(new Dictionary<string, object?> { ["TEXT"] = chunk });
        }

        return options;
    }

    /// <summary>İstenen alan adlarını RFC_READ_TABLE FIELDS tablosuna dönüştür.</summary>
    public static List<Dictionary<string, object?>> BuildFields(IEnumerable<string>? fields)
    {
        var rows = new List<Dictionary<string, object?>>();
        if (fields is null)
        {
            return rows;
        }

        foreach (var f in fields)
        {
            if (!string.IsNullOrWhiteSpace(f))
            {
                rows.Add(new Dictionary<string, object?> { ["FIELDNAME"] = f.Trim().ToUpperInvariant() });
            }
        }

        return rows;
    }

    /// <summary>
    /// RFC_READ_TABLE sonucunu (FIELDS metadata + DATA satırları) ad-değer satırlarına çöz.
    /// FIELDS: FIELDNAME/OFFSET/LENGTH; DATA: WA (sabit genişlikli satır).
    /// </summary>
    public static List<Dictionary<string, object?>> ParseReadTableResult(RfcInvokeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var rows = new List<Dictionary<string, object?>>();
        if (!result.Tables.TryGetValue("FIELDS", out var fieldMeta) || fieldMeta.Count == 0)
        {
            return rows;
        }

        if (!result.Tables.TryGetValue("DATA", out var data))
        {
            return rows;
        }

        // Alan yerleşimini hazırla.
        var columns = new List<(string Name, int Offset, int Length)>();
        foreach (var fm in fieldMeta)
        {
            var name = fm.TryGetValue("FIELDNAME", out var n) ? n?.ToString() ?? "" : "";
            var offset = ParseInt(fm, "OFFSET");
            var length = ParseInt(fm, "LENGTH");
            columns.Add((name, offset, length));
        }

        foreach (var dataRow in data)
        {
            var wa = dataRow.TryGetValue("WA", out var w) ? w?.ToString() ?? "" : "";
            var parsed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, offset, length) in columns)
            {
                string value;
                if (offset >= wa.Length)
                {
                    value = "";
                }
                else
                {
                    var take = Math.Min(length, wa.Length - offset);
                    value = take <= 0 ? "" : wa.Substring(offset, take);
                }

                parsed[name] = value.TrimEnd();
            }

            rows.Add(parsed);
        }

        return rows;
    }

    // =============================================================== BAPI RETURN yardımcıları

    /// <summary>
    /// Bir RFC sonucundan SAP dönüş bilgisini çıkar. RETURN hem tablo (BAPI listesi) hem de
    /// tek export yapısı olabilir. En kötü tür kazanır (A &gt; E &gt; W &gt; I/S).
    /// </summary>
    public static SapReturn ExtractReturn(RfcInvokeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        SapReturn worst = SapReturn.Empty;

        if (result.Tables.TryGetValue("RETURN", out var returnRows))
        {
            foreach (var row in returnRows)
            {
                worst = Worse(worst, FromRow(row));
            }
        }

        if (result.Exporting.TryGetValue("RETURN", out var exportReturn)
            && exportReturn is IReadOnlyDictionary<string, object?> exportRow)
        {
            worst = Worse(worst, FromRow(exportRow));
        }

        return worst;
    }

    /// <summary>
    /// SAP dönüşünü <see cref="SapCallResult"/>'a uygula. E/A ise Success=false.
    /// </summary>
    public static SapCallResult ToCallResult(RfcInvokeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var ret = ExtractReturn(result);
        return new SapCallResult
        {
            Success = !ret.IsError,
            ReturnType = ret.Type,
            Message = ret.Message,
            Outputs = new Dictionary<string, object?>(result.Exporting),
            TableOutputs = result.Tables.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(r => new Dictionary<string, object?>(r)).ToList()),
        };
    }

    // =============================================================== iç yardımcılar

    private static SapReturn FromRow(IReadOnlyDictionary<string, object?> row)
    {
        char? type = null;
        if (row.TryGetValue("TYPE", out var t) && t is not null)
        {
            var s = t.ToString();
            if (!string.IsNullOrEmpty(s))
            {
                type = char.ToUpperInvariant(s[0]);
            }
        }

        var message = row.TryGetValue("MESSAGE", out var m) ? m?.ToString() : null;
        return new SapReturn(type, message);
    }

    private static SapReturn Worse(SapReturn a, SapReturn b) =>
        Severity(b.Type) > Severity(a.Type) ? b : a;

    private static int Severity(char? type) => type switch
    {
        'A' => 4,
        'E' => 3,
        'W' => 2,
        'I' => 1,
        'S' => 1,
        _ => 0,
    };

    private static int ParseInt(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out var v) && v is not null
            && int.TryParse(v.ToString(), out var i))
        {
            return i;
        }

        return 0;
    }

    private static IEnumerable<string> ChunkPreservingWords(string text, int max)
    {
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length >= max)
            {
                // Tek kelime çizgiden uzunsa sabit boyda böl.
                if (current.Length > 0)
                {
                    yield return current;
                    current = "";
                }

                for (var i = 0; i < word.Length; i += max)
                {
                    yield return word.Substring(i, Math.Min(max, word.Length - i));
                }

                continue;
            }

            var candidate = current.Length == 0 ? word : current + " " + word;
            if (candidate.Length > max)
            {
                yield return current;
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            yield return current;
        }
    }
}

/// <summary>
/// SAP dönüş mesajı özeti (TYPE + MESSAGE).
/// </summary>
[SupportedOSPlatform("windows")]
public readonly record struct SapReturn(char? Type, string? Message)
{
    public static SapReturn Empty => new(null, null);

    /// <summary>SAP hata türü E (Error) veya A (Abort) mı?</summary>
    public bool IsError => Type is 'E' or 'A';
}

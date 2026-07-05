namespace RPA.Infrastructure.Retry;

using System.Globalization;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// İstisna sınıflandırıcı (Spec Bölüm 5.2, 6). İki görev:
/// 1) Fırlatılmış bir istisnayı Business/System olarak sınıflandırır ve retry edilebilirliğini belirler.
/// 2) Aktivite metadata'sındaki <see cref="ExceptionClassificationRule"/> koşulunu, aktivite
///    sonuç olguları (örn. SAP ReturnType='E', HTTP StatusCode=503) üzerinde değerlendirir.
///
/// Kural: Business → Action Center (retry yok); System → retry politikası; sınıflandırılmamış → System.
/// </summary>
public sealed class ExceptionClassifier
{
    private static readonly string[] Operators = { "==", "!=", ">=", "<=", ">", "<" };

    // HIGH FIX: Whitelist of allowed field names in rule evaluation to prevent information disclosure.
    // Only these fields may be referenced in rule conditions.
    private static readonly HashSet<string> AllowedFields = new(StringComparer.Ordinal)
    {
        "ReturnType",      // SAP return type (E, W, S, I)
        "StatusCode",      // HTTP status code
        "ErrorMessage",    // Exception error message
    };

    /// <summary>İstisnayı Business/System olarak sınıflandırır (bilinmeyen tip → System).</summary>
    public ExceptionType Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            BusinessException => ExceptionType.Business,
            SystemException => ExceptionType.System,
            OperationCanceledException => ExceptionType.System,
            _ => ExceptionType.System, // sınıflandırılmamış teknik hata → retry edilebilir
        };
    }

    /// <summary>İstisna retry edilebilir mi? Yalnızca System istisnaları retry edilir.</summary>
    public bool IsRetryable(Exception exception)
        => Classify(exception) == ExceptionType.System;

    /// <summary>
    /// Metadata sınıflandırma kuralını olgular sözlüğüne karşı değerlendirir.
    /// Koşul sağlanırsa kuralın <see cref="ExceptionClassificationRule.Classification"/> değerini,
    /// aksi halde <c>null</c> (eşleşme yok — çağıran varsayılana düşer) döner.
    /// </summary>
    /// <param name="rule">Aktivite metadata kuralı (null → null döner).</param>
    /// <param name="facts">Aktivite sonuç olguları (örn. {"ReturnType","E"}, {"StatusCode",503}).</param>
    public ExceptionType? Evaluate(
        ExceptionClassificationRule? rule,
        IReadOnlyDictionary<string, object?> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (rule is null || string.IsNullOrWhiteSpace(rule.Condition))
        {
            return null;
        }

        return EvaluateCondition(rule.Condition, facts) ? rule.Classification : null;
    }

    /// <summary>
    /// <c>&lt;alan&gt; &lt;operatör&gt; &lt;literal&gt;</c> biçimli tekil koşulu değerlendirir.
    /// Alan adı olgulardan çözülür; sağ taraf literaldir. Operatör yoksa alanın truthy değeri.
    /// </summary>
    public bool EvaluateCondition(string? condition, IReadOnlyDictionary<string, object?> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (string.IsNullOrWhiteSpace(condition))
        {
            return false;
        }

        foreach (var op in Operators)
        {
            var idx = condition.IndexOf(op, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var leftRaw = condition[..idx].Trim();
                var rightRaw = condition[(idx + op.Length)..].Trim();
                var left = ResolveOperand(leftRaw, facts);
                var right = ResolveOperand(rightRaw, facts);
                return Compare(left, right, op);
            }
        }

        return IsTruthy(ResolveOperand(condition.Trim(), facts));
    }

    private static object? ResolveOperand(string raw, IReadOnlyDictionary<string, object?> facts)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        // Tırnaklı string literali.
        if ((raw.StartsWith('\'') && raw.EndsWith('\'') && raw.Length >= 2) ||
            (raw.StartsWith('"') && raw.EndsWith('"') && raw.Length >= 2))
        {
            return raw[1..^1];
        }

        // Alan referansı (olgularda var).
        // HIGH FIX: Enforce whitelist to prevent information disclosure via unallowed field access.
        if (facts.TryGetValue(raw, out var value))
        {
            if (!AllowedFields.Contains(raw))
            {
                throw new ArgumentException(
                    $"Field '{raw}' is not allowed in rule conditions. Allowed fields: {string.Join(", ", AllowedFields)}");
            }
            return value;
        }

        // Literal (sayı/bool) — aksi halde çıplak string.
        if (bool.TryParse(raw, out var b))
        {
            return b;
        }
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }

        return raw;
    }

    private static bool Compare(object? left, object? right, string op)
    {
        if (TryToDouble(left, out var dl) && TryToDouble(right, out var dr))
        {
            return op switch
            {
                "==" => dl == dr,
                "!=" => dl != dr,
                ">=" => dl >= dr,
                "<=" => dl <= dr,
                ">" => dl > dr,
                "<" => dl < dr,
                _ => false,
            };
        }

        var sl = left?.ToString() ?? "";
        var sr = right?.ToString() ?? "";
        var cmp = string.Compare(sl, sr, StringComparison.Ordinal);
        return op switch
        {
            "==" => cmp == 0,
            "!=" => cmp != 0,
            ">=" => cmp >= 0,
            "<=" => cmp <= 0,
            ">" => cmp > 0,
            "<" => cmp < 0,
            _ => false,
        };
    }

    private static bool TryToDouble(object? value, out double result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case double d:
                result = d;
                return true;
            case long l:
                result = l;
                return true;
            case int i:
                result = i;
                return true;
            case decimal m:
                result = (double)m;
                return true;
            default:
                return double.TryParse(
                    value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }
    }

    private static bool IsTruthy(object? value)
        => value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrEmpty(s) &&
                        !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase),
            long l => l != 0,
            int i => i != 0,
            double d => d != 0,
            _ => true,
        };
}

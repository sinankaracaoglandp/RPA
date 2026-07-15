namespace RPA.Infrastructure.Workflow;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// MVP ifade değerlendiricisi (Spec Bölüm 5.2 — tam ifade dili S2'ye ertelendi).
///
/// Desteklenen:
/// - Şablon: <c>${degisken}</c> → değişken değeriyle değiştirilir.
/// - JSON yolu: <c>${data.alan}</c> → iç içe özellik erişimi.
/// - Karşılaştırma (If/While koşulu): <c>${a} == ${b}</c>, <c>!=</c>, <c>&gt;</c>, <c>&lt;</c>, <c>&gt;=</c>, <c>&lt;=</c>.
/// - Literaller: sayılar, <c>true/false</c>, tırnaklı stringler.
/// </summary>
public sealed class ExpressionEvaluator
{
    private static readonly Regex TokenPattern =
        new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    private static readonly Regex SingleTokenPattern =
        new(@"^\s*\$\{([^}]+)\}\s*$", RegexOptions.Compiled);

    private static readonly Regex MustacheTokenPattern =
        new(@"\{\{\s*([^}]+?)\s*\}\}", RegexOptions.Compiled);

    private static readonly string[] Operators = { "==", "!=", ">=", "<=", ">", "<" };

    private readonly VariableScope _scope;
    private readonly Expressions.ExpressionEngine _engine;

    public ExpressionEvaluator(VariableScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _engine = new Expressions.ExpressionEngine(scope);
    }

    /// <summary>
    /// İfadeyi tipini koruyarak değerlendirir. Tümüyle tek bir <c>${...}</c> ise ham değer;
    /// aksi halde şablon değiştirmesiyle string döner.
    /// </summary>
    public object? EvaluateValue(string? expression)
    {
        if (expression is null)
        {
            return null;
        }

        expression = NormalizeExpression(expression);

        var single = SingleTokenPattern.Match(expression);
        if (single.Success)
        {
            return _engine.Evaluate(single.Groups[1].Value.Trim());
        }

        if (!TokenPattern.IsMatch(expression))
        {
            // Düz literal — sayı/bool/string olarak yorumla.
            return ParseLiteral(expression);
        }

        return EvaluateString(expression);
    }

    /// <summary>İfadeyi şablon değiştirerek string'e çevirir.</summary>
    public string EvaluateString(string? expression)
    {
        if (string.IsNullOrEmpty(expression))
        {
            return expression ?? "";
        }

        expression = NormalizeExpression(expression);

        return TokenPattern.Replace(expression, m =>
        {
            var value = _engine.Evaluate(m.Groups[1].Value.Trim());
            return value?.ToString() ?? "";
        });
    }

    /// <summary>Boolean koşulu değerlendirir (If/While).</summary>
    public bool EvaluateCondition(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return false;
        }

        condition = NormalizeExpression(condition);

        // Operator precedence: split on lowest-precedence operator (== and !=).
        // For each precedence level, find the rightmost occurrence to ensure correct associativity.

        // Level 1 (lowest precedence): equality operators
        foreach (var op in new[] { "==", "!=" })
        {
            var idx = FindOperatorRightmost(condition, op);
            if (idx >= 0)
            {
                var leftRaw = condition[..idx].Trim();
                var rightRaw = condition[(idx + op.Length)..].Trim();
                var left = ResolveOperand(leftRaw);
                var right = ResolveOperand(rightRaw);
                return Compare(left, right, op);
            }
        }

        // Level 2 (higher precedence): comparison operators
        foreach (var op in new[] { ">=", "<=", ">", "<" })
        {
            var idx = FindOperatorRightmost(condition, op);
            if (idx >= 0)
            {
                var leftRaw = condition[..idx].Trim();
                var rightRaw = condition[(idx + op.Length)..].Trim();
                var left = ResolveOperand(leftRaw);
                var right = ResolveOperand(rightRaw);
                return Compare(left, right, op);
            }
        }

        // Operatör yok → tekil operandın "truthy" değeri.
        return IsTruthy(ResolveOperand(condition.Trim()));
    }

    private static int FindOperator(string text, string op)
    {
        // ">=" gibi çok karakterli operatörlerin ">" ile karışmasını önlemek için
        // Operators dizisi uzundan kısaya sıralı verilir.
        return text.IndexOf(op, StringComparison.Ordinal);
    }

    private static int FindOperatorRightmost(string text, string op)
    {
        // Find the rightmost occurrence to ensure lowest-precedence operator is used for split.
        // This handles cases like "a > 1 == true" → split on ==, not >.
        return text.LastIndexOf(op, StringComparison.Ordinal);
    }

    private object? ResolveOperand(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        raw = NormalizeExpression(raw);

        var single = SingleTokenPattern.Match(raw);
        if (single.Success)
        {
            return _engine.Evaluate(single.Groups[1].Value.Trim());
        }

        if (TokenPattern.IsMatch(raw))
        {
            return EvaluateString(raw);
        }

        // Check if operand contains comparison operators (for nested conditions)
        foreach (var op in new[] { ">=", "<=", ">", "<", "==", "!=" })
        {
            if (raw.Contains(op, StringComparison.Ordinal))
            {
                // Recursively evaluate as condition
                return EvaluateCondition(raw);
            }
        }

        return ParseLiteral(raw);
    }

    private static string NormalizeExpression(string expression)
    {
        if (string.IsNullOrEmpty(expression) || !expression.Contains("{{", StringComparison.Ordinal))
        {
            return expression;
        }

        return MustacheTokenPattern.Replace(expression, m => "${" + m.Groups[1].Value.Trim() + "}");
    }

    private static object? ParseLiteral(string raw)
    {
        var t = raw.Trim();

        if ((t.StartsWith('"') && t.EndsWith('"') && t.Length >= 2) ||
            (t.StartsWith('\'') && t.EndsWith('\'') && t.Length >= 2))
        {
            return t[1..^1];
        }

        if (bool.TryParse(t, out var b))
        {
            return b;
        }

        if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }

        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
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
            case bool b:
                result = b ? 1 : 0;
                return true;
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
    {
        return value switch
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
}

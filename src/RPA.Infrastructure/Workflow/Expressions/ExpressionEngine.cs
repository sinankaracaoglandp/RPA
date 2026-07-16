namespace RPA.Infrastructure.Workflow.Expressions;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

/// <summary>AST değerlendirici. Değişken çözümü VariableScope + JSON yolu; fonksiyonlar FunctionRegistry.
/// Aritmetik/karşılaştırma ExpressionEvaluator'ın eski Compare mantığıyla tutarlı.</summary>
internal sealed class ExpressionEngine
{
    private readonly VariableScope _scope;

    public ExpressionEngine(VariableScope scope)
        => _scope = scope ?? throw new ArgumentNullException(nameof(scope));

    public object? Evaluate(string rawExpression)
    {
        // Geriye uyum: tüm token içeriği birebir mevcut bir değişken adıysa (identifier olmayan
        // adlar — "my-var", boşluklu, ya da "true"/sayı gölgeleyen — dahil) doğrudan çöz; parser'a
        // sokma. Saf identifier/noktalı-yol zaten aynı sonucu verir, ama bu isimler regresyona uğrardı.
        var trimmed = rawExpression?.Trim() ?? string.Empty;
        if (trimmed.Length > 0 && _scope.TryGetVariable(trimmed, out var direct))
        {
            return direct is JToken t ? VariableScope.JTokenToNative(t) : direct;
        }
        return Evaluate(ExpressionParser.Parse(rawExpression));
    }

    public object? Evaluate(ExprNode node) => node switch
    {
        LiteralNode l => l.Value,
        VariableNode v => ResolvePath(v.Path),
        UnaryNode u => EvalUnary(u),
        FunctionNode f => EvalFunction(f),
        BinaryNode b => EvalBinary(b),
        _ => null,
    };

    private object? EvalUnary(UnaryNode u)
    {
        var v = Evaluate(u.Operand);
        if (TryToDouble(v, out var d)) { return NormalizeNumber(-d); }
        throw ExpressionErrors.Business("Tekli '-' yalnız sayıya uygulanır.");
    }

    private object? EvalFunction(FunctionNode f)
    {
        if (!FunctionRegistry.TryGet(f.Name, out var fn))
        {
            throw ExpressionErrors.Business($"Bilinmeyen fonksiyon: '{f.Name}'");
        }
        var args = f.Args.Select(Evaluate).ToList();
        var required = fn.Info.Parameters.Count(p => !p.Optional);
        var total = fn.Info.Parameters.Count;
        // Son parametre "params" (Concat) değilse argüman sayısını doğrula.
        if (args.Count < required || (total >= 0 && args.Count > total && !IsVariadic(fn)))
        {
            throw ExpressionErrors.Business($"{f.Name} {required}-{total} argüman alır, {args.Count} verildi.");
        }
        return fn.Invoke(args);
    }

    private static bool IsVariadic(ExpressionFunction fn) =>
        fn.Info.Parameters.Count == 1 && fn.Info.Parameters[0].Name == "...";

    private object? EvalBinary(BinaryNode b)
    {
        var left = Evaluate(b.Left);
        var right = Evaluate(b.Right);
        switch (b.Op)
        {
            case "+":
                if (left is string || right is string) { return Str(left) + Str(right); }
                return Arithmetic(left, right, "+");
            case "-": case "*": case "/":
                return Arithmetic(left, right, b.Op);
            default:
                return Compare(left, right, b.Op);
        }
    }

    private static object NormalizeNumber(double d) =>
        d == Math.Floor(d) && !double.IsInfinity(d) ? (object)(long)d : d;

    private static object Arithmetic(object? left, object? right, string op)
    {
        if (!TryToDouble(left, out var l) || !TryToDouble(right, out var r))
        {
            throw ExpressionErrors.Business($"Aritmetik '{op}' sayısal olmayan değere uygulandı.");
        }
        var result = op switch
        {
            "+" => l + r,
            "-" => l - r,
            "*" => l * r,
            "/" => r == 0 ? throw ExpressionErrors.Business("Sıfıra bölme.") : l / r,
            _ => 0d,
        };
        // Tam sayı operandlar + bölme değilse long döndür (ToInt/karşılaştırma tutarlılığı).
        var integral = left is long or int && right is long or int;
        return integral && op != "/" ? NormalizeNumber(result) : result;
    }

    private static string Str(object? v) => v?.ToString() ?? "";

    /// <summary>Nokta yolunu çözer: değişken + iç içe JSON alanları (eski ResolvePath ile birebir).</summary>
    private object? ResolvePath(string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { return null; }
        if (!_scope.TryGetVariable(parts[0], out var current)) { return null; }
        for (var i = 1; i < parts.Length && current is not null; i++)
        {
            current = current switch
            {
                JObject jo => jo[parts[i]],
                IReadOnlyDictionary<string, object?> dict => dict.TryGetValue(parts[i], out var v) ? v : null,
                _ => ReadPublicProperty(current, parts[i]),
            };
        }
        return current is JToken token ? VariableScope.JTokenToNative(token) : current;
    }

    private static object? ReadPublicProperty(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property is null || property.GetIndexParameters().Length != 0)
            return null;

        try
        {
            return property.GetValue(target);
        }
        catch (TargetInvocationException)
        {
            throw ExpressionErrors.Business($"Özellik değeri okunamadı: '{propertyName}'.");
        }
    }

    private static bool Compare(object? left, object? right, string op)
    {
        if (TryToDouble(left, out var dl) && TryToDouble(right, out var dr))
        {
            return op switch { "==" => dl == dr, "!=" => dl != dr, ">=" => dl >= dr, "<=" => dl <= dr, ">" => dl > dr, "<" => dl < dr, _ => false };
        }
        var cmp = string.Compare(left?.ToString() ?? "", right?.ToString() ?? "", StringComparison.Ordinal);
        return op switch { "==" => cmp == 0, "!=" => cmp != 0, ">=" => cmp >= 0, "<=" => cmp <= 0, ">" => cmp > 0, "<" => cmp < 0, _ => false };
    }

    internal static bool TryToDouble(object? value, out double result)
    {
        switch (value)
        {
            case null: result = 0; return false;
            case bool b: result = b ? 1 : 0; return true;
            case double d: result = d; return true;
            case long l: result = l; return true;
            case int i: result = i; return true;
            case decimal m: result = (double)m; return true;
            default: return double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }
    }
}

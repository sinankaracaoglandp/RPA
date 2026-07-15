namespace RPA.Infrastructure.Workflow.Expressions;

using System;
using System.Collections.Generic;
using System.Globalization;
using static RPA.Infrastructure.Workflow.Expressions.FunctionArgs;

/// <summary>Tip dönüşüm ifade fonksiyonları (kategori "Dönüşüm").</summary>
internal static class ConversionFunctions
{
    private const string Cat = "Dönüşüm";

    public static IReadOnlyList<ExpressionFunction> All => new[]
    {
        Fn("ToInt", "int", new() { P("deger", "any") }, "Tam sayıya çevirir.", "ToInt(miktar)", a => ToInt(a[0])),
        Fn("ToDecimal", "decimal", new() { P("deger", "any"), P("kültür", "string", true) },
            "Ondalık sayıya çevirir.", "ToDecimal(tutar)", a => ToDecimalImpl(a, "ToDecimal")),
        Fn("ToDouble", "double", new() { P("deger", "any"), P("kültür", "string", true) },
            "Double'a çevirir.", "ToDouble(oran)", a => (double)(decimal)ToDecimalImpl(a, "ToDouble")),
        Fn("ToStr", "string", new() { P("deger", "any"), P("desen", "string", true), P("kültür", "string", true) },
            "Metne çevirir (opsiyonel format).", "ToStr(tutar, \"N2\")", a => ToStr(a)),
        Fn("ToBool", "bool", new() { P("deger", "any") }, "Boolean'a çevirir.", "ToBool(bayrak)", a => ToBool(a[0])),
    };

    private static object ToInt(object? v)
    {
        if (ExpressionEngine.TryToDouble(v, out var d)) { return (long)Math.Truncate(d); }
        throw ExpressionErrors.Business($"ToInt: '{v}' sayıya çevrilemedi.");
    }

    private static object ToDecimalImpl(IReadOnlyList<object?> a, string fn)
    {
        var culture = Culture(fn, a, 1);
        switch (a[0])
        {
            case decimal m: return m;
            case double d: return (decimal)d;
            case long l: return (decimal)l;
            case int i: return (decimal)i;
            case string s when decimal.TryParse(s, NumberStyles.Any, culture, out var p): return p;
            default: throw ExpressionErrors.Business($"{fn}: '{a[0]}' sayıya çevrilemedi.");
        }
    }

    private static object ToStr(IReadOnlyList<object?> a)
    {
        var culture = Culture("ToStr", a, 2);
        var value = a[0];
        if (a.Count >= 2 && a[1] is not null && value is IFormattable f)
        {
            return f.ToString(AsString(a[1]), culture);
        }
        return value is IFormattable g ? g.ToString(null, culture) : AsString(value);
    }

    private static object ToBool(object? v)
    {
        switch (v)
        {
            case bool b: return b;
            case string s when bool.TryParse(s, out var p): return p;
            case string s: return s.Length > 0 && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) && s != "0";
            default: return ExpressionEngine.TryToDouble(v, out var d) ? d != 0 : v is not null;
        }
    }

    private static ExpressionFunction Fn(string name, string ret, List<ExpressionFunctionParam> ps,
        string desc, string ex, Func<IReadOnlyList<object?>, object?> invoke)
        => new(new ExpressionFunctionInfo(name, Cat, ret, ps, desc, ex), invoke);
}

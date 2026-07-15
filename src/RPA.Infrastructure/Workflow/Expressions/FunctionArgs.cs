namespace RPA.Infrastructure.Workflow.Expressions;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>Fonksiyon gövdelerinde argüman çözme + kültür yardımcıları. Hatalar → Business.</summary>
internal static class FunctionArgs
{
    // Robot makinesinin Windows bölgesel override'larından bağımsız, deterministik tr-TR
    // (new CultureInfo("tr-TR") kullanıcı override'larını miras alır → makineler arası tutarsız).
    public static readonly CultureInfo DefaultCulture = CultureInfo.GetCultureInfo("tr-TR");

    public static ExpressionFunctionParam P(string name, string type, bool optional = false) => new(name, type, optional);

    public static string AsString(object? v) => v?.ToString() ?? "";

    public static int AsInt(string fn, object? v)
    {
        if (ExpressionEngine.TryToDouble(v, out var d) && d == Math.Floor(d)) { return (int)d; }
        throw ExpressionErrors.Business($"{fn}: '{v}' tam sayı değil.");
    }

    public static DateTime AsDate(string fn, object? v)
    {
        switch (v)
        {
            case DateTime dt: return dt;
            case string s when DateTime.TryParse(s, DefaultCulture, DateTimeStyles.None, out var p): return p;
            case string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var pi): return pi;
            default: throw ExpressionErrors.Business($"{fn}: '{v}' tarihe çevrilemedi.");
        }
    }

    public static CultureInfo Culture(string fn, IReadOnlyList<object?> args, int index)
    {
        if (index >= args.Count || args[index] is null) { return DefaultCulture; }
        var name = AsString(args[index]);
        try { return CultureInfo.GetCultureInfo(name); }
        catch (CultureNotFoundException) { throw ExpressionErrors.Business($"Geçersiz kültür: '{name}'"); }
    }
}

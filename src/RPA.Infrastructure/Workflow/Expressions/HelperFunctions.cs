namespace RPA.Infrastructure.Workflow.Expressions;

using System;
using System.Collections.Generic;
using static RPA.Infrastructure.Workflow.Expressions.FunctionArgs;

/// <summary>Yardımcı ifade fonksiyonları (kategori "Yardımcı").</summary>
internal static class HelperFunctions
{
    private const string Cat = "Yardımcı";

    public static IReadOnlyList<ExpressionFunction> All => new[]
    {
        Fn("Coalesce", "any", new() { P("deger", "any"), P("yedek", "any") },
            "İlk değer null/boş ise yedeği döner.", "Coalesce(ad, \"-\")",
            a => a[0] is null || (a[0] is string s && s.Length == 0) ? a[1] : a[0]),
    };

    private static ExpressionFunction Fn(string name, string ret, List<ExpressionFunctionParam> ps,
        string desc, string ex, Func<IReadOnlyList<object?>, object?> invoke)
        => new(new ExpressionFunctionInfo(name, Cat, ret, ps, desc, ex), invoke);
}

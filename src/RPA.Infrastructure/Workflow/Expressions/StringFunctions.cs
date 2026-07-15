namespace RPA.Infrastructure.Workflow.Expressions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static RPA.Infrastructure.Workflow.Expressions.FunctionArgs;

/// <summary>Metin ifade fonksiyonları (kategori "Metin").</summary>
internal static class StringFunctions
{
    private const string Cat = "Metin";

    public static IReadOnlyList<ExpressionFunction> All => new[]
    {
        Fn("Upper", "string", new() { P("metin", "string") }, "Büyük harfe çevirir (tr-TR).", "Upper(ad)",
            a => AsString(a[0]).ToUpper(DefaultCulture)),
        Fn("Lower", "string", new() { P("metin", "string") }, "Küçük harfe çevirir (tr-TR).", "Lower(ad)",
            a => AsString(a[0]).ToLower(DefaultCulture)),
        Fn("Trim", "string", new() { P("metin", "string") }, "Baş/son boşlukları atar.", "Trim(ad)",
            a => AsString(a[0]).Trim()),
        Fn("Length", "int", new() { P("metin", "string") }, "Karakter sayısı.", "Length(ad)",
            a => (long)AsString(a[0]).Length),
        Fn("Substring", "string", new() { P("metin", "string"), P("baslangic", "int"), P("uzunluk", "int", true) },
            "Alt dize (0-tabanlı).", "Substring(ad, 0, 3)", a => Sub(a)),
        Fn("Replace", "string", new() { P("metin", "string"), P("eski", "string"), P("yeni", "string") },
            "Tüm eşleşmeleri değiştirir.", "Replace(ad, \"_\", \"-\")",
            a => AsString(a[0]).Replace(AsString(a[1]), AsString(a[2]), StringComparison.Ordinal)),
        Fn("Contains", "bool", new() { P("metin", "string"), P("alt", "string") }, "Alt dize içeriyor mu.", "Contains(ad, \"x\")",
            a => AsString(a[0]).Contains(AsString(a[1]), StringComparison.Ordinal)),
        Fn("StartsWith", "bool", new() { P("metin", "string"), P("on", "string") }, "İle başlıyor mu.", "StartsWith(ad, \"AB\")",
            a => AsString(a[0]).StartsWith(AsString(a[1]), StringComparison.Ordinal)),
        Fn("EndsWith", "bool", new() { P("metin", "string"), P("son", "string") }, "İle bitiyor mu.", "EndsWith(ad, \"z\")",
            a => AsString(a[0]).EndsWith(AsString(a[1]), StringComparison.Ordinal)),
        Fn("IndexOf", "int", new() { P("metin", "string"), P("alt", "string") }, "İlk konum (yoksa -1).", "IndexOf(ad, \"x\")",
            a => (long)AsString(a[0]).IndexOf(AsString(a[1]), StringComparison.Ordinal)),
        Fn("PadLeft", "string", new() { P("metin", "string"), P("uzunluk", "int"), P("karakter", "string", true) },
            "Sola doldurur.", "PadLeft(no, 5, \"0\")", a => AsString(a[0]).PadLeft(AsInt("PadLeft", a[1]), PadChar(a, 2))),
        Fn("PadRight", "string", new() { P("metin", "string"), P("uzunluk", "int"), P("karakter", "string", true) },
            "Sağa doldurur.", "PadRight(no, 5, \".\")", a => AsString(a[0]).PadRight(AsInt("PadRight", a[1]), PadChar(a, 2))),
        Fn("Concat", "string", new() { P("...", "any") }, "Tüm argümanları birleştirir.", "Concat(a, \"-\", b)",
            a => string.Concat(a.Select(AsString))),
    };

    private static object Sub(IReadOnlyList<object?> a)
    {
        var s = AsString(a[0]);
        var start = AsInt("Substring", a[1]);
        if (start < 0 || start > s.Length) { throw ExpressionErrors.Business($"Substring: başlangıç {start} aralık dışı."); }
        if (a.Count >= 3 && a[2] is not null)
        {
            var len = AsInt("Substring", a[2]);
            if (len < 0 || start + len > s.Length) { throw ExpressionErrors.Business($"Substring: uzunluk {len} aralık dışı."); }
            return s.Substring(start, len);
        }
        return s.Substring(start);
    }

    private static char PadChar(IReadOnlyList<object?> a, int index)
    {
        if (index >= a.Count || a[index] is null) { return ' '; }
        var s = AsString(a[index]);
        return s.Length > 0 ? s[0] : ' ';
    }

    private static ExpressionFunction Fn(string name, string ret, List<ExpressionFunctionParam> ps,
        string desc, string ex, Func<IReadOnlyList<object?>, object?> invoke)
        => new(new ExpressionFunctionInfo(name, Cat, ret, ps, desc, ex), invoke);
}

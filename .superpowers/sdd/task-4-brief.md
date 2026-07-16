## Task 4: String fonksiyonları

**Files:**
- Modify: `src/RPA.Infrastructure/Workflow/Expressions/StringFunctions.cs`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/Expressions/StringFunctionsTests.cs`

**Interfaces:**
- Consumes: `FunctionArgs`, `ExpressionFunction`.
- Produces: `StringFunctions.All`. Not: `Concat` variadic — `Parameters` tek eleman `P("...", "any")` (motorun `IsVariadic` kontrolü bunu tanır).

- [ ] **Step 1: String testlerini yaz (FAIL)**

`StringFunctionsTests.cs`:

```csharp
namespace RPA.Infrastructure.Tests.Workflow.Expressions;

using RPA.Infrastructure.Workflow;
using RPA.Infrastructure.Workflow.Expressions;
using Xunit;

public class StringFunctionsTests
{
    private static object? Eval(string expr, params (string, object?)[] vars)
    {
        var scope = new VariableScope();
        foreach (var (k, v) in vars) { scope.SetGlobalVariable(k, v); }
        return new ExpressionEngine(scope).Evaluate(expr);
    }

    [Fact] public void Upper() => Assert.Equal("ABC", Eval("Upper(\"abc\")"));
    [Fact] public void Lower() => Assert.Equal("abc", Eval("Lower(\"ABC\")"));
    [Fact] public void Trim() => Assert.Equal("x", Eval("Trim(\"  x  \")"));
    [Fact] public void Length() => Assert.Equal(3L, Eval("Length(\"abc\")"));
    [Fact] public void Substring_StartLen() => Assert.Equal("bc", Eval("Substring(\"abcd\", 1, 2)"));
    [Fact] public void Substring_StartOnly() => Assert.Equal("cd", Eval("Substring(\"abcd\", 2)"));
    [Fact] public void Replace() => Assert.Equal("a-b", Eval("Replace(\"a_b\", \"_\", \"-\")"));
    [Fact] public void Contains() => Assert.Equal(true, Eval("Contains(\"abc\", \"b\")"));
    [Fact] public void StartsWith() => Assert.Equal(true, Eval("StartsWith(\"abc\", \"ab\")"));
    [Fact] public void EndsWith() => Assert.Equal(true, Eval("EndsWith(\"abc\", \"bc\")"));
    [Fact] public void IndexOf() => Assert.Equal(1L, Eval("IndexOf(\"abc\", \"b\")"));
    [Fact] public void PadLeft() => Assert.Equal("007", Eval("PadLeft(\"7\", 3, \"0\")"));
    [Fact] public void PadRight() => Assert.Equal("7..", Eval("PadRight(\"7\", 3, \".\")"));
    [Fact] public void Concat_Variadic() => Assert.Equal("abc", Eval("Concat(\"a\", \"b\", \"c\")"));
}
```

- [ ] **Step 2: Testi çalıştır (FAIL)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~StringFunctions`
Expected: FAIL.

- [ ] **Step 3: String fonksiyonlarını yaz**

`StringFunctions.cs`:

```csharp
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
```

- [ ] **Step 4: Testi çalıştır (PASS)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~StringFunctions`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Infrastructure/Workflow/Expressions/StringFunctions.cs tests/RPA.Infrastructure.Tests/Workflow/Expressions/StringFunctionsTests.cs
git commit -m "feat(expr): metin fonksiyonlari (Upper/Substring/Replace/Concat/...)

tr-TR duyarli Upper/Lower; Concat variadic; arg araligi kontrolu → Business.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---


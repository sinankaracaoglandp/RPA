## Task 5: Dönüşüm + yardımcı fonksiyonlar

**Files:**
- Modify: `src/RPA.Infrastructure/Workflow/Expressions/ConversionFunctions.cs`
- Modify: `src/RPA.Infrastructure/Workflow/Expressions/HelperFunctions.cs`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/Expressions/ConversionFunctionsTests.cs`

**Interfaces:**
- Consumes: `FunctionArgs`.
- Produces: `ConversionFunctions.All`, `HelperFunctions.All`.

- [ ] **Step 1: Dönüşüm/yardımcı testlerini yaz (FAIL)**

`ConversionFunctionsTests.cs`:

```csharp
namespace RPA.Infrastructure.Tests.Workflow.Expressions;

using RPA.Domain.Exceptions;
using RPA.Infrastructure.Workflow;
using RPA.Infrastructure.Workflow.Expressions;
using Xunit;

public class ConversionFunctionsTests
{
    private static object? Eval(string expr, params (string, object?)[] vars)
    {
        var scope = new VariableScope();
        foreach (var (k, v) in vars) { scope.SetGlobalVariable(k, v); }
        return new ExpressionEngine(scope).Evaluate(expr);
    }

    [Fact] public void ToInt_FromString() => Assert.Equal(42L, Eval("ToInt(\"42\")"));
    [Fact] public void ToInt_FromDouble() => Assert.Equal(3L, Eval("ToInt(3.9)"));
    [Fact] public void ToInt_Invalid_Business() => Assert.Throws<BusinessException>(() => Eval("ToInt(\"abc\")"));
    [Fact] public void ToDecimal_TrCulture() => Assert.Equal(3.5m, Eval("ToDecimal(\"3,5\")"));
    [Fact] public void ToDecimal_ExplicitCulture() => Assert.Equal(3.5m, Eval("ToDecimal(\"3.5\", \"en-US\")"));
    [Fact] public void ToDouble_TrCulture() => Assert.Equal(2.5d, Eval("ToDouble(\"2,5\")"));
    [Fact] public void ToStr_Number() => Assert.Equal("42", Eval("ToStr(42)"));
    [Fact] public void ToStr_WithFormatAndCulture() => Assert.Equal("3,50", Eval("ToStr(3.5, \"N2\", \"tr-TR\")"));
    [Fact] public void ToBool_True() => Assert.Equal(true, Eval("ToBool(\"true\")"));
    [Fact] public void Coalesce_FirstNull() => Assert.Equal("yedek", Eval("Coalesce(x, \"yedek\")", ("x", null)));
    [Fact] public void Coalesce_FirstPresent() => Assert.Equal("var", Eval("Coalesce(x, \"yedek\")", ("x", "var")));
}
```

- [ ] **Step 2: Testi çalıştır (FAIL)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~ConversionFunctions`
Expected: FAIL.

- [ ] **Step 3: Dönüşüm fonksiyonlarını yaz**

`ConversionFunctions.cs`:

```csharp
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
            "Ondalık sayıya çevirir.", "ToDecimal(tutar)", a => ToDecimal(a, "ToDecimal")),
        Fn("ToDouble", "double", new() { P("deger", "any"), P("kültür", "string", true) },
            "Double'a çevirir.", "ToDouble(oran)", a => (double)ToDecimal(a, "ToDouble")),
        Fn("ToStr", "string", new() { P("deger", "any"), P("desen", "string", true), P("kültür", "string", true) },
            "Metne çevirir (opsiyonel format).", "ToStr(tutar, \"N2\")", a => ToStr(a)),
        Fn("ToBool", "bool", new() { P("deger", "any") }, "Boolean'a çevirir.", "ToBool(bayrak)", a => ToBool(a[0])),
    };

    private static object ToInt(object? v)
    {
        if (ExpressionEngine.TryToDouble(v, out var d)) { return (long)Math.Truncate(d); }
        throw ExpressionErrors.Business($"ToInt: '{v}' sayıya çevrilemedi.");
    }

    private static object ToDecimal(IReadOnlyList<object?> a, string fn)
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
```

`HelperFunctions.cs`:

```csharp
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
```

- [ ] **Step 4: Testi çalıştır (PASS) + tam ifade suite**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~Expressions` ardından `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~BaseRunner`
Expected: PASS (tüm ifade fonksiyonları + geriye uyum).

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Infrastructure/Workflow/Expressions/ConversionFunctions.cs src/RPA.Infrastructure/Workflow/Expressions/HelperFunctions.cs tests/RPA.Infrastructure.Tests/Workflow/Expressions/ConversionFunctionsTests.cs
git commit -m "feat(expr): donusum + yardimci fonksiyonlar (ToInt/ToDecimal/ToStr/Coalesce)

tr-TR varsayilan; ToDecimal/ToDouble/ToStr opsiyonel kultur. Basarisiz donusum → Business.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---


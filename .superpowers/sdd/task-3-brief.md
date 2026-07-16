## Task 3: Tarih fonksiyonları

**Files:**
- Modify: `src/RPA.Infrastructure/Workflow/Expressions/DateFunctions.cs`
- Create: `src/RPA.Infrastructure/Workflow/Expressions/FunctionArgs.cs` (argüman/kültür yardımcıları)
- Test: `tests/RPA.Infrastructure.Tests/Workflow/Expressions/DateFunctionsTests.cs`

**Interfaces:**
- Consumes: `ExpressionFunction`, `ExpressionFunctionInfo`, `ExpressionErrors`, `ExpressionEngine.TryToDouble`.
- Produces: `DateFunctions.All` (yukarıdaki iskeleti değiştirir); `FunctionArgs` yardımcıları: `AsDate`, `AsString`, `AsInt`, `Culture`, `P` (param kısayolu).

- [ ] **Step 1: Argüman yardımcısını yaz**

`FunctionArgs.cs`:

```csharp
namespace RPA.Infrastructure.Workflow.Expressions;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>Fonksiyon gövdelerinde argüman çözme + kültür yardımcıları. Hatalar → Business.</summary>
internal static class FunctionArgs
{
    public static readonly CultureInfo DefaultCulture = new("tr-TR");

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
```

- [ ] **Step 2: Tarih testlerini yaz (FAIL)**

`DateFunctionsTests.cs`:

```csharp
namespace RPA.Infrastructure.Tests.Workflow.Expressions;

using System;
using RPA.Domain.Exceptions;
using RPA.Infrastructure.Workflow;
using RPA.Infrastructure.Workflow.Expressions;
using Xunit;

public class DateFunctionsTests
{
    private static object? Eval(string expr, params (string, object?)[] vars)
    {
        var scope = new VariableScope();
        foreach (var (k, v) in vars) { scope.SetGlobalVariable(k, v); }
        return new ExpressionEngine(scope).Evaluate(expr);
    }

    [Fact]
    public void Format_TrCulture_Default()
        => Assert.Equal("15.01.2026", Eval("Format(d, \"dd.MM.yyyy\")", ("d", new DateTime(2026, 1, 15))));

    [Fact]
    public void Format_ExplicitCulture()
        => Assert.Equal("01/15/2026", Eval("Format(d, \"MM/dd/yyyy\", \"en-US\")", ("d", new DateTime(2026, 1, 15))));

    [Fact]
    public void AddDays_And_Format_Nested()
        => Assert.Equal("22.01.2026", Eval("Format(AddDays(d, 7), \"dd.MM.yyyy\")", ("d", new DateTime(2026, 1, 15))));

    [Fact]
    public void ToDate_ParsesTrFormat()
        => Assert.Equal(new DateTime(2026, 1, 15), Eval("ToDate(\"15.01.2026\", \"dd.MM.yyyy\")"));

    [Fact]
    public void Year_Month_Day()
    {
        Assert.Equal(2026L, Eval("Year(d)", ("d", new DateTime(2026, 1, 15))));
        Assert.Equal(1L, Eval("Month(d)", ("d", new DateTime(2026, 1, 15))));
        Assert.Equal(15L, Eval("Day(d)", ("d", new DateTime(2026, 1, 15))));
    }

    [Fact]
    public void DateDiffDays()
        => Assert.Equal(7L, Eval("DateDiffDays(a, b)", ("a", new DateTime(2026, 1, 22)), ("b", new DateTime(2026, 1, 15))));

    [Fact]
    public void ToDate_Invalid_ThrowsBusiness()
        => Assert.Throws<BusinessException>(() => Eval("ToDate(\"xx\", \"dd.MM.yyyy\")"));
}
```

- [ ] **Step 3: Testi çalıştır (FAIL)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~DateFunctions`
Expected: FAIL — fonksiyonlar boş.

- [ ] **Step 4: Tarih fonksiyonlarını yaz**

`DateFunctions.cs` (iskeleti değiştir):

```csharp
namespace RPA.Infrastructure.Workflow.Expressions;

using System;
using System.Collections.Generic;
using System.Globalization;
using static RPA.Infrastructure.Workflow.Expressions.FunctionArgs;

/// <summary>Tarih/zaman ifade fonksiyonları (kategori "Tarih"). Varsayılan kültür tr-TR.</summary>
internal static class DateFunctions
{
    private const string Cat = "Tarih";

    public static IReadOnlyList<ExpressionFunction> All => new[]
    {
        Fn("Now", "date", new List<ExpressionFunctionParam>(), "Şu anki tarih-saat.", "Now()",
            _ => DateTime.Now),
        Fn("Today", "date", new List<ExpressionFunctionParam>(), "Bugünün tarihi (saat 00:00).", "Today()",
            _ => DateTime.Today),
        Fn("AddDays", "date", new() { P("tarih", "date"), P("gun", "int") }, "Tarihe gün ekler.", "AddDays(Now(), 7)",
            a => AsDate("AddDays", a[0]).AddDays(AsInt("AddDays", a[1]))),
        Fn("AddMonths", "date", new() { P("tarih", "date"), P("ay", "int") }, "Tarihe ay ekler.", "AddMonths(Now(), 1)",
            a => AsDate("AddMonths", a[0]).AddMonths(AsInt("AddMonths", a[1]))),
        Fn("AddYears", "date", new() { P("tarih", "date"), P("yil", "int") }, "Tarihe yıl ekler.", "AddYears(Now(), 1)",
            a => AsDate("AddYears", a[0]).AddYears(AsInt("AddYears", a[1]))),
        Fn("AddHours", "date", new() { P("tarih", "date"), P("saat", "int") }, "Tarihe saat ekler.", "AddHours(Now(), 3)",
            a => AsDate("AddHours", a[0]).AddHours(AsInt("AddHours", a[1]))),
        Fn("AddMinutes", "date", new() { P("tarih", "date"), P("dakika", "int") }, "Tarihe dakika ekler.", "AddMinutes(Now(), 30)",
            a => AsDate("AddMinutes", a[0]).AddMinutes(AsInt("AddMinutes", a[1]))),
        Fn("Format", "string", new() { P("tarih", "date"), P("desen", "string"), P("kültür", "string", true) },
            "Tarihi verilen desene göre biçimler.", "Format(Now(), \"dd.MM.yyyy\")",
            a => AsDate("Format", a[0]).ToString(AsString(a[1]), Culture("Format", a, 2))),
        Fn("ToDate", "date", new() { P("metin", "string"), P("desen", "string", true), P("kültür", "string", true) },
            "Metni tarihe çevirir.", "ToDate(\"15.01.2026\", \"dd.MM.yyyy\")",
            a => ParseDate(a)),
        Fn("Year", "int", new() { P("tarih", "date") }, "Yıl bileşeni.", "Year(Now())", a => (long)AsDate("Year", a[0]).Year),
        Fn("Month", "int", new() { P("tarih", "date") }, "Ay bileşeni.", "Month(Now())", a => (long)AsDate("Month", a[0]).Month),
        Fn("Day", "int", new() { P("tarih", "date") }, "Gün bileşeni.", "Day(Now())", a => (long)AsDate("Day", a[0]).Day),
        Fn("DayOfWeek", "int", new() { P("tarih", "date") }, "Haftanın günü (0=Pazar).", "DayOfWeek(Now())",
            a => (long)(int)AsDate("DayOfWeek", a[0]).DayOfWeek),
        Fn("DateDiffDays", "int", new() { P("a", "date"), P("b", "date") }, "İki tarih arası gün farkı (a-b).", "DateDiffDays(a, b)",
            a => (long)Math.Round((AsDate("DateDiffDays", a[0]) - AsDate("DateDiffDays", a[1])).TotalDays)),
    };

    private static object ParseDate(IReadOnlyList<object?> a)
    {
        var s = AsString(a[0]);
        var culture = Culture("ToDate", a, 2);
        if (a.Count >= 2 && a[1] is not null)
        {
            var pattern = AsString(a[1]);
            if (DateTime.TryParseExact(s, pattern, culture, DateTimeStyles.None, out var exact)) { return exact; }
            throw ExpressionErrors.Business($"ToDate: '{s}' '{pattern}' desenine uymuyor.");
        }
        if (DateTime.TryParse(s, culture, DateTimeStyles.None, out var p)) { return p; }
        throw ExpressionErrors.Business($"ToDate: '{s}' tarihe çevrilemedi.");
    }

    private static ExpressionFunction Fn(string name, string ret, List<ExpressionFunctionParam> ps,
        string desc, string ex, Func<IReadOnlyList<object?>, object?> invoke)
        => new(new ExpressionFunctionInfo(name, Cat, ret, ps, desc, ex), invoke);
}
```

- [ ] **Step 5: Testi çalıştır (PASS)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~DateFunctions`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Infrastructure/Workflow/Expressions tests/RPA.Infrastructure.Tests/Workflow/Expressions/DateFunctionsTests.cs
git commit -m "feat(expr): tarih fonksiyonlari (Now/AddDays/Format/ToDate/...)

tr-TR varsayilan kultur; Format/ToDate opsiyonel kultur argumani. FunctionArgs
argum/kultur cozucu. Hatalar → Business.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---


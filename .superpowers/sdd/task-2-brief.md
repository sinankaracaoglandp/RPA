## Task 2: Evaluator + FunctionRegistry iskeleti + `ExpressionEvaluator` delegasyonu (geriye uyum)

**Files:**
- Create: `src/RPA.Infrastructure/Workflow/Expressions/FunctionRegistry.cs`
- Create: `src/RPA.Infrastructure/Workflow/Expressions/ExpressionEngine.cs`
- Modify: `src/RPA.Infrastructure/Workflow/ExpressionEvaluator.cs`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/Expressions/ExpressionEngineTests.cs`

**Interfaces:**
- Consumes: Task 1 AST + parser; `VariableScope` (`TryGetVariable`, `JTokenToNative`).
- Produces:
  - Public metadata: `public sealed record ExpressionFunctionInfo(string Name, string Category, string ReturnType, IReadOnlyList<ExpressionFunctionParam> Parameters, string Description, string Example);` ve `public sealed record ExpressionFunctionParam(string Name, string Type, bool Optional);`
  - `internal sealed record ExpressionFunction(ExpressionFunctionInfo Info, System.Func<IReadOnlyList<object?>, object?> Invoke);`
  - `internal static class FunctionRegistry` — `bool TryGet(string name, out ExpressionFunction fn)`, `public static IReadOnlyList<ExpressionFunctionInfo> Catalog`. Task 2'de boş/az girişle başlar; Task 3-5 doldurur.
  - `internal sealed class ExpressionEngine(VariableScope scope)` — `object? Evaluate(string rawExpression)` ve `object? Evaluate(ExprNode node)`.

- [ ] **Step 1: Engine testlerini yaz (FAIL)**

`ExpressionEngineTests.cs`:

```csharp
namespace RPA.Infrastructure.Tests.Workflow.Expressions;

using RPA.Domain.Exceptions;
using RPA.Infrastructure.Workflow;
using RPA.Infrastructure.Workflow.Expressions;
using Xunit;

public class ExpressionEngineTests
{
    private static ExpressionEngine Engine(params (string, object?)[] vars)
    {
        var scope = new VariableScope();
        foreach (var (k, v) in vars) { scope.SetGlobalVariable(k, v); }
        return new ExpressionEngine(scope);
    }

    [Fact]
    public void Variable_ResolvesFromScope()
        => Assert.Equal("abc", Engine(("ad", "abc")).Evaluate("ad"));

    [Fact]
    public void Arithmetic_IntegerAddition_ReturnsLong()
        => Assert.Equal(3L, Engine(("x", 2L)).Evaluate("x + 1"));

    [Fact]
    public void Arithmetic_Precedence()
        => Assert.Equal(7L, Engine(("x", 3L)).Evaluate("x * 2 + 1"));

    [Fact]
    public void StringConcat_WithPlus()
        => Assert.Equal("a-1", Engine(("s", "a")).Evaluate("s + \"-\" + 1"));

    [Fact]
    public void Comparison_ReturnsBool()
        => Assert.Equal(true, Engine(("x", 5L)).Evaluate("x > 3"));

    [Fact]
    public void UnknownFunction_ThrowsBusiness()
        => Assert.Throws<BusinessException>(() => Engine().Evaluate("Yok(1)"));
}
```

- [ ] **Step 2: Testi çalıştır (FAIL)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~ExpressionEngine`
Expected: FAIL — `ExpressionEngine`/`FunctionRegistry` yok.

- [ ] **Step 3: FunctionRegistry iskeletini yaz**

`FunctionRegistry.cs`:

```csharp
namespace RPA.Infrastructure.Workflow.Expressions;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Autocomplete + doküman için fonksiyon metadata'sı (public — WebAPI katalog uç noktası tüketir).</summary>
public sealed record ExpressionFunctionInfo(
    string Name, string Category, string ReturnType,
    IReadOnlyList<ExpressionFunctionParam> Parameters, string Description, string Example);

public sealed record ExpressionFunctionParam(string Name, string Type, bool Optional);

/// <summary>Çalıştırılabilir fonksiyon = metadata + invoker (değerlendirilmiş argümanlar).</summary>
internal sealed record ExpressionFunction(ExpressionFunctionInfo Info, Func<IReadOnlyList<object?>, object?> Invoke);

/// <summary>Tüm ifade fonksiyonlarının kayıt defteri (case-insensitive ad). Kategori modülleri doldurur.</summary>
internal static class FunctionRegistry
{
    private static readonly Dictionary<string, ExpressionFunction> Map = BuildMap();

    private static Dictionary<string, ExpressionFunction> BuildMap()
    {
        var all = new List<ExpressionFunction>();
        all.AddRange(DateFunctions.All);
        all.AddRange(StringFunctions.All);
        all.AddRange(ConversionFunctions.All);
        all.AddRange(HelperFunctions.All);
        return all.ToDictionary(f => f.Info.Name, f => f, StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryGet(string name, out ExpressionFunction fn) => Map.TryGetValue(name, out fn!);

    /// <summary>Studio autocomplete kataloğu (ada göre sıralı, invoker'sız metadata).</summary>
    public static IReadOnlyList<ExpressionFunctionInfo> Catalog =>
        Map.Values.Select(f => f.Info).OrderBy(i => i.Category).ThenBy(i => i.Name).ToList();
}
```

> Not: `DateFunctions`/`StringFunctions`/`ConversionFunctions`/`HelperFunctions` Task 3-5'te eklenir. Bu task'ta derlemenin geçmesi için **şimdilik dört modülü de boş `All` ile oluştur** (Task 3-5 içlerini doldurur):

`DateFunctions.cs`, `StringFunctions.cs`, `ConversionFunctions.cs`, `HelperFunctions.cs` (Task 2'de yalnız iskelet):

```csharp
namespace RPA.Infrastructure.Workflow.Expressions;

using System.Collections.Generic;

internal static class DateFunctions { public static IReadOnlyList<ExpressionFunction> All => System.Array.Empty<ExpressionFunction>(); }
```
(Aynı iskeleti `StringFunctions`, `ConversionFunctions`, `HelperFunctions` için de oluştur — her biri kendi dosyasında, `All => Array.Empty<ExpressionFunction>()`.)

- [ ] **Step 4: ExpressionEngine'i yaz**

`ExpressionEngine.cs`:

```csharp
namespace RPA.Infrastructure.Workflow.Expressions;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>AST değerlendirici. Değişken çözümü VariableScope + JSON yolu; fonksiyonlar FunctionRegistry.
/// Aritmetik/karşılaştırma ExpressionEvaluator'ın eski Compare mantığıyla tutarlı.</summary>
internal sealed class ExpressionEngine
{
    private readonly VariableScope _scope;

    public ExpressionEngine(VariableScope scope)
        => _scope = scope ?? throw new ArgumentNullException(nameof(scope));

    public object? Evaluate(string rawExpression) => Evaluate(ExpressionParser.Parse(rawExpression));

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
        d == Math.Floor(d) && !double.IsInfinity(d) ? (long)d : d;

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
                _ => null,
            };
        }
        return current is JToken token ? VariableScope.JTokenToNative(token) : current;
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
```

> `Compare`/`TryToDouble` mantığı mevcut `ExpressionEvaluator`'daki ile birebir aynıdır (geriye uyum). Task 6+ sonrası tekilleştirme final review triyajına bırakılabilir.

- [ ] **Step 5: `ExpressionEvaluator`'ı motora delege et**

`ExpressionEvaluator.cs`'te `ResolvePath` çağrılarını motora yönlendir. Değişiklikler:

1. Sınıfa alan ekle ve ctor'da oluştur:
```csharp
    private readonly Expressions.ExpressionEngine _engine;

    public ExpressionEvaluator(VariableScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _engine = new Expressions.ExpressionEngine(scope);
    }
```
2. `EvaluateValue` içindeki tek-token dalını motora çevir:
```csharp
        var single = SingleTokenPattern.Match(expression);
        if (single.Success)
        {
            return _engine.Evaluate(single.Groups[1].Value.Trim());
        }
```
3. `EvaluateString` şablon değiştirmesini motora çevir:
```csharp
        return TokenPattern.Replace(expression, m =>
        {
            var value = _engine.Evaluate(m.Groups[1].Value.Trim());
            return value?.ToString() ?? "";
        });
```
4. `ResolveOperand` içindeki iki dalı motora çevir (tek-token ve genel-token):
```csharp
        var single = SingleTokenPattern.Match(raw);
        if (single.Success)
        {
            return _engine.Evaluate(single.Groups[1].Value.Trim());
        }
        if (TokenPattern.IsMatch(raw))
        {
            return EvaluateString(raw);
        }
```
5. Eski private `ResolvePath` metodunu **kaldır** (artık motorda). `Compare`/`TryToDouble`/`ParseLiteral`/`IsTruthy` yerinde kalır (EvaluateCondition kullanır).

> Diğer her şey (TokenPattern, SingleTokenPattern, MustacheTokenPattern, NormalizeExpression, EvaluateCondition operatör-ayırma, ParseLiteral) **değişmeden** kalır. Bu, `${a} == ${b}` gibi token-arası karşılaştırmaların eski yolla çalışmasını, tek-token/şablon içeriğinin ise yeni motorla (fonksiyon destekli) çözülmesini sağlar.

- [ ] **Step 6: Engine testini çalıştır (PASS)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~ExpressionEngine`
Expected: PASS.

- [ ] **Step 7: Geriye uyum — mevcut ExpressionEvaluator senaryoları**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~BaseRunner`
Expected: PASS (mevcut `${var}`/`{{var}}`/JSON-yol/karşılaştırma/şablon senaryoları değişmeden geçer). Herhangi biri kırılırsa motor delegasyonu eski davranıştan sapmıştır — düzelt.

- [ ] **Step 8: Commit**

```bash
git add src/RPA.Infrastructure/Workflow tests/RPA.Infrastructure.Tests/Workflow/Expressions
git commit -m "feat(expr): AST evaluator + FunctionRegistry iskeleti + Evaluator delegasyonu

ExpressionEngine token icerigini degerlendirir (degisken/aritmetik/karsilastirma/
fonksiyon). ExpressionEvaluator public API korunur; ResolvePath motora tasindi.
Geriye uyum: mevcut senaryolar degismeden gecer.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---


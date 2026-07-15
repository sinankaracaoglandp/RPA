# İfade Fonksiyon Kütüphanesi + Kod Tamamlama — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `${...}` ifadelerinde iç içe + aritmetik fonksiyon çağrısını (date/string/dönüşüm) destekleyen bir ifade motoru, katalog API'si ve Studio'da satır içi autocomplete eklemek — mevcut değişken/JSON-yol/karşılaştırma davranışını bozmadan.

**Architecture:** Yeni `ExpressionEngine` (tokenizer → recursive-descent parser → AST → evaluator + `FunctionRegistry`) token içeriğini değerlendirir. `ExpressionEvaluator` public API'si (`EvaluateValue/EvaluateString/EvaluateCondition`) korunur; içeride token içeriği çözümü motora delege edilir. Fonksiyon metadata'sı `GET /api/expression/functions` ile Studio'ya taşınır; `expression-input` IDE tarzı öneri listesi sunar.

**Tech Stack:** C# (.NET 10), xUnit + Moq, Newtonsoft.Json.Linq (mevcut), Angular + vitest, ASP.NET controller.

## Global Constraints

- Onion: motor + registry Infrastructure'da; Domain'e dokunmaz. Fonksiyonlar saf/yan-etkisiz (yalnız argüman + değişken okuma).
- Geriye uyum ZORUNLU: mevcut `${var}`, `{{var}}`, `${data.alan}`, `${a} == ${b}`, şablon string, literal davranışı **birebir korunur**; mevcut `BaseRunnerTests` içindeki ExpressionEvaluator senaryoları regresyon güvencesidir.
- Hata sınıfı: parse/bilinmeyen fonksiyon/argüman/tip/kültür hatası → `BusinessException` (RPA.Domain.Exceptions), Türkçe net mesaj.
- Kültür: varsayılan `tr-TR`; `Format/ToDate/ToDecimal/ToDouble/ToStr` opsiyonel son `kültür` argümanıyla aşılır; geçersiz kültür → Business.
- Söz dizimi: `Fonksiyon(arg)` (metot zinciri YOK). Operatör önceliği: `* /` > `+ -` > ilişkisel (`> < >= <=`) > eşitlik (`== !=`), tümü sol-birleşimli. `+` string operand varsa birleştirme.
- Türkçe kullanıcı-görünür metin. Aktivite/servis adları PascalCase.
- Test eşiği: her task sonunda ilgili suite PASS + geriye uyum testleri PASS.

---

## File Structure

Backend (Infrastructure `src/RPA.Infrastructure/Workflow/Expressions/`):
- `ExpressionToken.cs` — token tipi + `ExpressionTokenizer`.
- `ExpressionAst.cs` — AST düğüm tipleri (`ExprNode` ve alt tipleri).
- `ExpressionParser.cs` — recursive-descent parser (token[] → AST).
- `ExpressionEngine.cs` — AST evaluator (scope + registry).
- `FunctionRegistry.cs` — kayıt defteri + public metadata (`ExpressionFunctionInfo`).
- `DateFunctions.cs`, `StringFunctions.cs`, `ConversionFunctions.cs`, `HelperFunctions.cs` — kategori modülleri.
- `ExpressionErrors.cs` — Business fırlatma yardımcıları + kültür çözücü.
- MODIFY `ExpressionEvaluator.cs` — token içeriği çözümünü motora delege et.

Backend API:
- `src/RPA.WebAPI/Controllers/ExpressionController.cs` — `GET /api/expression/functions`.

Studio (`src/RPA.Studio/src/app/`):
- `shared/services/expression-function.service.ts` — katalog API istemcisi + cache + filtre.
- MODIFY `studio/designer/properties/expression-input.component.ts` (+`.html`,`.scss`) — satır içi autocomplete.

Tests:
- `tests/RPA.Infrastructure.Tests/Workflow/Expressions/` — tokenizer/parser/engine/fonksiyon/geriye-uyum.
- `tests/RPA.WebAPI.Tests/ExpressionControllerTests.cs`.
- `expression-function.service.spec.ts`, `expression-input.component.spec.ts`.

---

## Task 1: Tokenizer + AST + Parser (saf, değerlendirme yok)

**Files:**
- Create: `src/RPA.Infrastructure/Workflow/Expressions/ExpressionToken.cs`
- Create: `src/RPA.Infrastructure/Workflow/Expressions/ExpressionAst.cs`
- Create: `src/RPA.Infrastructure/Workflow/Expressions/ExpressionParser.cs`
- Create: `src/RPA.Infrastructure/Workflow/Expressions/ExpressionErrors.cs`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/Expressions/ExpressionParserTests.cs`

**Interfaces:**
- Produces:
  - `internal enum ExprTokenType { Number, String, Ident, Op, LParen, RParen, Comma }`
  - `internal readonly record struct ExprToken(ExprTokenType Type, string Text)`
  - `internal static class ExpressionTokenizer { public static List<ExprToken> Tokenize(string input); }`
  - AST: `internal abstract record ExprNode;` + `LiteralNode(object? Value)`, `VariableNode(string Path)`, `FunctionNode(string Name, IReadOnlyList<ExprNode> Args)`, `UnaryNode(string Op, ExprNode Operand)`, `BinaryNode(string Op, ExprNode Left, ExprNode Right)`.
  - `internal static class ExpressionParser { public static ExprNode Parse(string input); }`
  - `internal static class ExpressionErrors { public static Exception Parse(string msg); public static Exception Business(string msg); }`

- [ ] **Step 1: Hata yardımcısını yaz**

`ExpressionErrors.cs`:

```csharp
namespace RPA.Infrastructure.Workflow.Expressions;

using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>İfade motoru hataları — tümü kullanıcı-yazımı config olduğu için BusinessException.</summary>
internal static class ExpressionErrors
{
    public static BusinessException Parse(string detail) => new($"İfade ayrıştırılamadı: {detail}");
    public static BusinessException Business(string message) => new(message);
}
```

- [ ] **Step 2: Parser testlerini yaz (FAIL)**

`ExpressionParserTests.cs`:

```csharp
namespace RPA.Infrastructure.Tests.Workflow.Expressions;

using RPA.Domain.Exceptions;
using RPA.Infrastructure.Workflow.Expressions;
using Xunit;

public class ExpressionParserTests
{
    [Fact]
    public void Parses_NestedFunctionCall()
    {
        var ast = ExpressionParser.Parse("Format(AddDays(Now(), 7), \"dd.MM.yyyy\")");
        var outer = Assert.IsType<FunctionNode>(ast);
        Assert.Equal("Format", outer.Name);
        Assert.Equal(2, outer.Args.Count);
        var inner = Assert.IsType<FunctionNode>(outer.Args[0]);
        Assert.Equal("AddDays", inner.Name);
        Assert.IsType<FunctionNode>(inner.Args[0]); // Now()
        Assert.Equal(7L, Assert.IsType<LiteralNode>(inner.Args[1]).Value);
        Assert.Equal("dd.MM.yyyy", Assert.IsType<LiteralNode>(outer.Args[1]).Value);
    }

    [Fact]
    public void Parses_ArithmeticPrecedence()
    {
        // ToInt(x) * 2 + 1  →  (ToInt(x)*2) + 1
        var ast = ExpressionParser.Parse("ToInt(x) * 2 + 1");
        var add = Assert.IsType<BinaryNode>(ast);
        Assert.Equal("+", add.Op);
        Assert.Equal(1L, Assert.IsType<LiteralNode>(add.Right).Value);
        var mul = Assert.IsType<BinaryNode>(add.Left);
        Assert.Equal("*", mul.Op);
    }

    [Fact]
    public void Parses_EqualityLowerThanRelational()
    {
        // a > 1 == true  →  (a > 1) == true
        var ast = ExpressionParser.Parse("a > 1 == true");
        var eq = Assert.IsType<BinaryNode>(ast);
        Assert.Equal("==", eq.Op);
        Assert.Equal(true, Assert.IsType<LiteralNode>(eq.Right).Value);
        Assert.Equal(">", Assert.IsType<BinaryNode>(eq.Left).Op);
    }

    [Fact]
    public void Parses_DottedVariablePath()
    {
        var ast = ExpressionParser.Parse("data.alan.ic");
        Assert.Equal("data.alan.ic", Assert.IsType<VariableNode>(ast).Path);
    }

    [Fact]
    public void Parses_StringAndNumberAndBoolLiterals()
    {
        Assert.Equal("x", Assert.IsType<LiteralNode>(ExpressionParser.Parse("\"x\"")).Value);
        Assert.Equal(42L, Assert.IsType<LiteralNode>(ExpressionParser.Parse("42")).Value);
        Assert.Equal(3.5d, Assert.IsType<LiteralNode>(ExpressionParser.Parse("3.5")).Value);
        Assert.Equal(true, Assert.IsType<LiteralNode>(ExpressionParser.Parse("true")).Value);
    }

    [Fact]
    public void Parses_UnaryMinus()
    {
        var ast = ExpressionParser.Parse("-5");
        var u = Assert.IsType<UnaryNode>(ast);
        Assert.Equal("-", u.Op);
    }

    [Theory]
    [InlineData("Format(")]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("Upper(a,)")]
    public void InvalidSyntax_ThrowsBusiness(string expr)
    {
        Assert.Throws<BusinessException>(() => ExpressionParser.Parse(expr));
    }
}
```

- [ ] **Step 3: Testi çalıştır (FAIL)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~ExpressionParser`
Expected: FAIL — tipler yok.

- [ ] **Step 4: AST'yi yaz**

`ExpressionAst.cs`:

```csharp
namespace RPA.Infrastructure.Workflow.Expressions;

using System.Collections.Generic;

/// <summary>İfade soyut söz dizim ağacı düğümleri.</summary>
internal abstract record ExprNode;

/// <summary>Sabit değer (sayı long/double, bool, string).</summary>
internal sealed record LiteralNode(object? Value) : ExprNode;

/// <summary>Nokta ile ayrılmış değişken/JSON yolu (örn. "data.alan").</summary>
internal sealed record VariableNode(string Path) : ExprNode;

/// <summary>Fonksiyon çağrısı: ad + değerlendirilecek argümanlar.</summary>
internal sealed record FunctionNode(string Name, IReadOnlyList<ExprNode> Args) : ExprNode;

/// <summary>Tekli operatör (şu an yalnız "-").</summary>
internal sealed record UnaryNode(string Op, ExprNode Operand) : ExprNode;

/// <summary>İkili operatör: + - * / == != > < >= <=.</summary>
internal sealed record BinaryNode(string Op, ExprNode Left, ExprNode Right) : ExprNode;
```

- [ ] **Step 5: Tokenizer'ı yaz**

`ExpressionToken.cs`:

```csharp
namespace RPA.Infrastructure.Workflow.Expressions;

using System.Collections.Generic;
using System.Globalization;
using System.Text;

internal enum ExprTokenType { Number, String, Ident, Op, LParen, RParen, Comma }

internal readonly record struct ExprToken(ExprTokenType Type, string Text);

/// <summary>İfade metnini token'lara ayırır. Sayılar (long/double), tırnaklı stringler
/// ("..."/'...'), nokta-yollu identifier'lar, operatörler ve ayraçlar.</summary>
internal static class ExpressionTokenizer
{
    private static readonly string[] MultiCharOps = { "==", "!=", ">=", "<=" };

    public static List<ExprToken> Tokenize(string input)
    {
        var tokens = new List<ExprToken>();
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '(') { tokens.Add(new(ExprTokenType.LParen, "(")); i++; continue; }
            if (c == ')') { tokens.Add(new(ExprTokenType.RParen, ")")); i++; continue; }
            if (c == ',') { tokens.Add(new(ExprTokenType.Comma, ",")); i++; continue; }

            if (c == '"' || c == '\'')
            {
                var sb = new StringBuilder();
                var quote = c; i++;
                while (i < input.Length && input[i] != quote)
                {
                    if (input[i] == '\\' && i + 1 < input.Length) { sb.Append(input[i + 1]); i += 2; }
                    else { sb.Append(input[i]); i++; }
                }
                if (i >= input.Length) { throw ExpressionErrors.Parse("kapanmayan tırnak"); }
                i++; // kapanış tırnağı
                tokens.Add(new(ExprTokenType.String, sb.ToString()));
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < input.Length && char.IsDigit(input[i + 1])))
            {
                var start = i;
                while (i < input.Length && (char.IsDigit(input[i]) || input[i] == '.')) { i++; }
                tokens.Add(new(ExprTokenType.Number, input[start..i]));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_' || input[i] == '.')) { i++; }
                tokens.Add(new(ExprTokenType.Ident, input[start..i]));
                continue;
            }

            var two = i + 1 < input.Length ? input.Substring(i, 2) : null;
            if (two is not null && System.Array.IndexOf(MultiCharOps, two) >= 0)
            {
                tokens.Add(new(ExprTokenType.Op, two)); i += 2; continue;
            }
            if ("+-*/<>".IndexOf(c) >= 0)
            {
                tokens.Add(new(ExprTokenType.Op, c.ToString())); i++; continue;
            }

            throw ExpressionErrors.Parse($"beklenmeyen karakter '{c}'");
        }
        return tokens;
    }

    // Newtonsoft/InvariantCulture ile sayı literali ayrıştırma (tokenizer sonrası parser kullanır).
    public static object ParseNumber(string text)
    {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) { return l; }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { return d; }
        throw ExpressionErrors.Parse($"geçersiz sayı '{text}'");
    }
}
```

- [ ] **Step 6: Parser'ı yaz**

`ExpressionParser.cs`:

```csharp
namespace RPA.Infrastructure.Workflow.Expressions;

using System.Collections.Generic;

/// <summary>Recursive-descent parser. Öncelik (düşükten yükseğe): eşitlik(==,!=) &lt; ilişkisel(&gt;,&lt;,&gt;=,&lt;=)
/// &lt; toplama(+,-) &lt; çarpma(*,/) &lt; tekli(-) &lt; birincil. Tümü sol-birleşimli.</summary>
internal static class ExpressionParser
{
    public static ExprNode Parse(string input)
    {
        var tokens = ExpressionTokenizer.Tokenize(input);
        var pos = 0;
        var node = ParseEquality(tokens, ref pos);
        if (pos != tokens.Count) { throw ExpressionErrors.Parse("fazladan girdi"); }
        return node;
    }

    private static ExprNode ParseEquality(List<ExprToken> t, ref int p)
    {
        var left = ParseRelational(t, ref p);
        while (p < t.Count && t[p].Type == ExprTokenType.Op && (t[p].Text == "==" || t[p].Text == "!="))
        {
            var op = t[p++].Text;
            var right = ParseRelational(t, ref p);
            left = new BinaryNode(op, left, right);
        }
        return left;
    }

    private static ExprNode ParseRelational(List<ExprToken> t, ref int p)
    {
        var left = ParseAdditive(t, ref p);
        while (p < t.Count && t[p].Type == ExprTokenType.Op &&
               (t[p].Text is ">" or "<" or ">=" or "<="))
        {
            var op = t[p++].Text;
            var right = ParseAdditive(t, ref p);
            left = new BinaryNode(op, left, right);
        }
        return left;
    }

    private static ExprNode ParseAdditive(List<ExprToken> t, ref int p)
    {
        var left = ParseMultiplicative(t, ref p);
        while (p < t.Count && t[p].Type == ExprTokenType.Op && (t[p].Text == "+" || t[p].Text == "-"))
        {
            var op = t[p++].Text;
            var right = ParseMultiplicative(t, ref p);
            left = new BinaryNode(op, left, right);
        }
        return left;
    }

    private static ExprNode ParseMultiplicative(List<ExprToken> t, ref int p)
    {
        var left = ParseUnary(t, ref p);
        while (p < t.Count && t[p].Type == ExprTokenType.Op && (t[p].Text == "*" || t[p].Text == "/"))
        {
            var op = t[p++].Text;
            var right = ParseUnary(t, ref p);
            left = new BinaryNode(op, left, right);
        }
        return left;
    }

    private static ExprNode ParseUnary(List<ExprToken> t, ref int p)
    {
        if (p < t.Count && t[p].Type == ExprTokenType.Op && t[p].Text == "-")
        {
            p++;
            return new UnaryNode("-", ParseUnary(t, ref p));
        }
        return ParsePrimary(t, ref p);
    }

    private static ExprNode ParsePrimary(List<ExprToken> t, ref int p)
    {
        if (p >= t.Count) { throw ExpressionErrors.Parse("ifade beklendi"); }
        var tok = t[p];

        if (tok.Type == ExprTokenType.Number) { p++; return new LiteralNode(ExpressionTokenizer.ParseNumber(tok.Text)); }
        if (tok.Type == ExprTokenType.String) { p++; return new LiteralNode(tok.Text); }

        if (tok.Type == ExprTokenType.LParen)
        {
            p++;
            var inner = ParseEquality(t, ref p);
            Expect(t, ref p, ExprTokenType.RParen, ")");
            return inner;
        }

        if (tok.Type == ExprTokenType.Ident)
        {
            p++;
            if (string.Equals(tok.Text, "true", System.StringComparison.OrdinalIgnoreCase)) { return new LiteralNode(true); }
            if (string.Equals(tok.Text, "false", System.StringComparison.OrdinalIgnoreCase)) { return new LiteralNode(false); }

            if (p < t.Count && t[p].Type == ExprTokenType.LParen)
            {
                p++;
                var args = new List<ExprNode>();
                if (!(p < t.Count && t[p].Type == ExprTokenType.RParen))
                {
                    args.Add(ParseEquality(t, ref p));
                    while (p < t.Count && t[p].Type == ExprTokenType.Comma)
                    {
                        p++;
                        args.Add(ParseEquality(t, ref p));
                    }
                }
                Expect(t, ref p, ExprTokenType.RParen, ")");
                return new FunctionNode(tok.Text, args);
            }
            return new VariableNode(tok.Text);
        }

        throw ExpressionErrors.Parse($"beklenmeyen '{tok.Text}'");
    }

    private static void Expect(List<ExprToken> t, ref int p, ExprTokenType type, string what)
    {
        if (p >= t.Count || t[p].Type != type) { throw ExpressionErrors.Parse($"'{what}' beklendi"); }
        p++;
    }
}
```

- [ ] **Step 7: Testi çalıştır (PASS)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~ExpressionParser`
Expected: PASS (tüm parser testleri).

- [ ] **Step 8: Commit**

```bash
git add src/RPA.Infrastructure/Workflow/Expressions tests/RPA.Infrastructure.Tests/Workflow/Expressions
git commit -m "feat(expr): ifade tokenizer + AST + parser

Recursive-descent parser (ic ice cagri, aritmetik, karsilastirma, oncelik).
Deger degerlendirme yok — yalniz sozdizimi agaci. Parse hatasi → Business.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

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

## Task 6: Fonksiyon kataloğu API'si

**Files:**
- Create: `src/RPA.WebAPI/Controllers/ExpressionController.cs`
- Test: `tests/RPA.WebAPI.Tests/ExpressionControllerTests.cs`

**Interfaces:**
- Consumes: `FunctionRegistry.Catalog` (public `ExpressionFunctionInfo[]`).
- Produces: `GET /api/expression/functions` → `IReadOnlyList<ExpressionFunctionInfo>`.

- [ ] **Step 1: Controller testini yaz (FAIL)**

`ExpressionControllerTests.cs` (mevcut WebAPI test deseni — `WebApplicationFactory<Program>` + token; `UiSpyTests`/`ActivitiesController` testlerini örnek al):

```csharp
namespace RPA.WebAPI.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using RPA.Infrastructure.Workflow.Expressions;
using Xunit;

public class ExpressionControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ExpressionControllerTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task GetFunctions_ReturnsCatalog_WithCategoriesAndSignatures()
    {
        var client = _factory.CreateClient();
        // Mevcut testlerdeki token üretimini kullan (ör. GenerateToken()/AuthHelper). Bu projede
        // WebAPI testleri Authorization header ekliyor — aynı yardımcıyı kullan.
        AuthTestHelper.AddBearer(client);

        var functions = await client.GetFromJsonAsync<List<ExpressionFunctionInfo>>("/api/expression/functions");

        Assert.NotNull(functions);
        Assert.Contains(functions!, f => f.Name == "Format" && f.Category == "Tarih");
        Assert.Contains(functions!, f => f.Name == "Upper" && f.Category == "Metin");
        Assert.Contains(functions!, f => f.Name == "ToInt" && f.Category == "Dönüşüm");
        var format = functions!.First(f => f.Name == "Format");
        Assert.Equal(3, format.Parameters.Count);
        Assert.True(format.Parameters[2].Optional); // kültür opsiyonel
    }
}
```

> Not: `AuthTestHelper.AddBearer` yerine bu projedeki gerçek token yardımcısını kullan (mevcut `RobotHubTests`/`UiSpyTests` nasıl token ekliyorsa aynısı). Yetki gerekmiyorsa `[AllowAnonymous]` da düşünülebilir — ama tutarlılık için diğer controller'lar gibi `[Authorize]` + test token'ı kullan.

- [ ] **Step 2: Testi çalıştır (FAIL)**

Run: `dotnet test tests/RPA.WebAPI.Tests --filter FullyQualifiedName~Expression`
Expected: FAIL — endpoint yok.

- [ ] **Step 3: Controller'ı yaz**

`ExpressionController.cs` (`ActivitiesController` desenini izler):

```csharp
namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Infrastructure.Workflow.Expressions;

/// <summary>
/// İfade fonksiyon kataloğu uç noktası — Studio autocomplete'in okuduğu tek referans.
/// <see cref="FunctionRegistry.Catalog"/> metadata'sını sunar (frontend ExpressionFunctionService tüketir).
/// </summary>
[ApiController]
[Route("api/expression")]
[Authorize]
public class ExpressionController : ControllerBase
{
    /// <summary>Tüm ifade fonksiyonlarını (ad, kategori, imza, açıklama, örnek) listeler.</summary>
    [HttpGet("functions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ExpressionFunctionInfo>> GetFunctions() => Ok(FunctionRegistry.Catalog);
}
```

- [ ] **Step 4: Testi çalıştır (PASS)**

Run: `dotnet test tests/RPA.WebAPI.Tests --filter FullyQualifiedName~Expression`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.WebAPI/Controllers/ExpressionController.cs tests/RPA.WebAPI.Tests/ExpressionControllerTests.cs
git commit -m "feat(api): GET /api/expression/functions fonksiyon katalogu

FunctionRegistry.Catalog metadata (ad/kategori/imza/aciklama) — Studio autocomplete kaynagi.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

## Task 7: Studio — `ExpressionFunctionService` (katalog istemcisi)

**Files:**
- Create: `src/RPA.Studio/src/app/shared/services/expression-function.service.ts`
- Test: `src/RPA.Studio/src/app/shared/services/expression-function.service.spec.ts`

**Interfaces:**
- Consumes: `GET /api/expression/functions`; Angular `HttpClient` (mevcut servislerin kullandığı desen — `OrchestratorService`/`ActivityCatalogService` nasıl HttpClient enjekte ediyorsa aynısı).
- Produces:
  - `interface ExpressionFunctionParam { name: string; type: string; optional: boolean; }`
  - `interface ExpressionFunctionInfo { name: string; category: string; returnType: string; parameters: ExpressionFunctionParam[]; description: string; example: string; }`
  - `class ExpressionFunctionService` — `load(): Observable<ExpressionFunctionInfo[]>` (bir kez çeker, cache'ler), `filter(prefix: string): ExpressionFunctionInfo[]` (yüklenmiş kataloğu ada göre case-insensitive filtreler).

- [ ] **Step 1: Servis testini yaz (FAIL)**

`expression-function.service.spec.ts` (mevcut servis spec desenini izle — `HttpClientTestingModule`/`provideHttpClientTesting`):

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ExpressionFunctionService, ExpressionFunctionInfo } from './expression-function.service';

const sample: ExpressionFunctionInfo[] = [
  { name: 'Format', category: 'Tarih', returnType: 'string', parameters: [], description: '', example: 'Format(Now(), "dd.MM.yyyy")' },
  { name: 'Upper', category: 'Metin', returnType: 'string', parameters: [], description: '', example: 'Upper(ad)' },
  { name: 'ToInt', category: 'Dönüşüm', returnType: 'int', parameters: [], description: '', example: 'ToInt(x)' },
];

describe('ExpressionFunctionService', () => {
  let service: ExpressionFunctionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [ExpressionFunctionService, provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ExpressionFunctionService);
    http = TestBed.inject(HttpTestingController);
  });

  it('loads and caches the catalog', () => {
    service.load().subscribe();
    http.expectOne('/api/expression/functions').flush(sample);
    service.load().subscribe(); // ikinci çağrı yeni istek YAPMAMALI
    http.expectNone('/api/expression/functions');
  });

  it('filters by case-insensitive prefix', () => {
    service.load().subscribe();
    http.expectOne('/api/expression/functions').flush(sample);
    expect(service.filter('up').map((f) => f.name)).toEqual(['Upper']);
    expect(service.filter('to').map((f) => f.name)).toEqual(['ToInt']);
    expect(service.filter('').length).toBe(3);
  });

  afterEach(() => http.verify());
});
```

- [ ] **Step 2: Testi çalıştır (FAIL)**

Run: `cd src/RPA.Studio && npx ng test --watch=false --include='**/expression-function.service.spec.ts'`
Expected: FAIL — servis yok.

- [ ] **Step 3: Servisi yaz**

`expression-function.service.ts`:

```typescript
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, shareReplay, tap } from 'rxjs';

export interface ExpressionFunctionParam {
  name: string;
  type: string;
  optional: boolean;
}

export interface ExpressionFunctionInfo {
  name: string;
  category: string;
  returnType: string;
  parameters: ExpressionFunctionParam[];
  description: string;
  example: string;
}

/**
 * İfade fonksiyon kataloğunu backend'den (GET /api/expression/functions) çeker, cache'ler ve
 * autocomplete için ada göre filtre sağlar. Katalog tek kaynak (backend FunctionRegistry).
 */
@Injectable({ providedIn: 'root' })
export class ExpressionFunctionService {
  private readonly http = inject(HttpClient);
  private cache$?: Observable<ExpressionFunctionInfo[]>;
  private loaded: ExpressionFunctionInfo[] = [];

  load(): Observable<ExpressionFunctionInfo[]> {
    if (!this.cache$) {
      this.cache$ = this.http
        .get<ExpressionFunctionInfo[]>('/api/expression/functions')
        .pipe(
          tap((fns) => (this.loaded = fns ?? [])),
          shareReplay(1),
        );
    }
    return this.cache$;
  }

  /** Yüklenmiş kataloğu ada göre (case-insensitive önek) filtreler. */
  filter(prefix: string): ExpressionFunctionInfo[] {
    const q = (prefix ?? '').trim().toLowerCase();
    if (q.length === 0) {
      return [...this.loaded];
    }
    return this.loaded.filter((f) => f.name.toLowerCase().startsWith(q));
  }
}
```

- [ ] **Step 4: Testi çalıştır (PASS)**

Run: `cd src/RPA.Studio && npx ng test --watch=false --include='**/expression-function.service.spec.ts'`
Expected: PASS (2 test).

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/shared/services/expression-function.service.ts src/RPA.Studio/src/app/shared/services/expression-function.service.spec.ts
git commit -m "feat(studio): ExpressionFunctionService — katalog istemcisi + cache + filtre

GET /api/expression/functions bir kez ceker, shareReplay ile cache'ler, ada gore filtreler.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

## Task 8: Studio — satır içi autocomplete (`expression-input`)

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.ts` (+`.html`,`.scss`)
- Test: `src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.spec.ts` (mevcut olabilir — genişlet; yoksa oluştur)

**Interfaces:**
- Consumes: `ExpressionFunctionService` (Task 7); mevcut `variables: WorkflowVariable[]` Input'u; mevcut `value`/`applyValue` altyapısı.
- Produces: bileşende autocomplete durumu + davranışı:
  - `suggestionsOpen: boolean`, `suggestions: AutocompleteItem[]`, `activeIndex: number`.
  - `type AutocompleteItem = { kind: 'variable' | 'function'; label: string; detail: string; insert: string }`.
  - `updateSuggestions(caretText: string)`, `applySuggestion(item)`, `onKeydown(event)` (↑↓/Enter/Tab/Esc).

- [ ] **Step 1: Autocomplete davranış testini yaz (FAIL)**

`expression-input.component.spec.ts`'e ekle (mevcut spec varsa; yoksa bu dosyayı oluştur; bileşeni `new` ile veya TestBed ile kur — mevcut spec hangisini kullanıyorsa onu izle). Servisi mock'la:

```typescript
import { ExpressionInputComponent } from './expression-input.component';
import { ExpressionFunctionInfo } from '../../../shared/services/expression-function.service';

function fnInfo(name: string, category: string): ExpressionFunctionInfo {
  return { name, category, returnType: 'string', parameters: [], description: '', example: `${name}()` };
}

describe('ExpressionInputComponent autocomplete', () => {
  let component: ExpressionInputComponent;
  const fnService = {
    load: () => ({ subscribe: () => undefined }),
    filter: (prefix: string) =>
      [fnInfo('Format', 'Tarih'), fnInfo('Upper', 'Metin')].filter((f) =>
        f.name.toLowerCase().startsWith(prefix.toLowerCase()),
      ),
  };

  beforeEach(() => {
    component = new ExpressionInputComponent(fnService as never);
    component.variables = [{ name: 'ad', type: 'string' } as never];
  });

  it('suggests matching functions and variables for a partial word', () => {
    component.updateSuggestions('Up');
    expect(component.suggestions.some((s) => s.kind === 'function' && s.label === 'Upper')).toBe(true);
    expect(component.suggestionsOpen).toBe(true);
  });

  it('suggests variables by partial name', () => {
    component.updateSuggestions('a');
    expect(component.suggestions.some((s) => s.kind === 'variable' && s.label === 'ad')).toBe(true);
  });

  it('inserting a function replaces the trailing partial word with Name()', () => {
    const emitted: string[] = [];
    component.valueChange.subscribe((v) => emitted.push(v));
    // Kullanıcı "x = Up" yazdı; öneri son kelime "Up"a göre açıldı.
    component.value = 'x = Up';
    component.updateSuggestions('Up');
    const upper = component.suggestions.find((s) => s.label === 'Upper')!;
    component.applySuggestion(upper);
    // "Up" silinip "Upper()" ile değişmeli → "x = Upper()" (UpUpper() DEĞİL).
    expect(emitted[emitted.length - 1]).toBe('x = Upper()');
  });

  it('Escape closes the suggestion list', () => {
    component.updateSuggestions('Up');
    component.onKeydown(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(component.suggestionsOpen).toBe(false);
  });
});
```

> Not: mevcut ctor imzası parametresizdir (`inject(ChangeDetectorRef)`). Bileşene `ExpressionFunctionService`'i **constructor injection** ile ekle (test `new` kullanıyorsa param olarak geçilebilir) VEYA `inject()` ile alıp testte TestBed kur. Mevcut spec dosyasının kurulum stilini (bare-new vs TestBed) izle; `cdr` zaten `inject` ile alınıyorsa `ExpressionFunctionService`'i de `inject` ile al ve testi TestBed'e çevir. Tutarlılık için mevcut spec stilini koru.

- [ ] **Step 2: Testi çalıştır (FAIL)**

Run: `cd src/RPA.Studio && npx ng test --watch=false --include='**/expression-input.component.spec.ts'`
Expected: FAIL — autocomplete üyeleri yok.

- [ ] **Step 3: Bileşene autocomplete ekle**

`expression-input.component.ts`'e ekle (mevcut alanları/metotları koruyarak):

1. Import + servis:
```typescript
import { ExpressionFunctionService, ExpressionFunctionInfo } from '../../../shared/services/expression-function.service';
```
2. Alanlar (sınıf gövdesine):
```typescript
  private readonly fnService = inject(ExpressionFunctionService);

  suggestionsOpen = false;
  activeIndex = 0;
  suggestions: AutocompleteItem[] = [];
  private currentPartial = '';

  ngOnInit(): void {
    this.fnService.load().subscribe();
  }
```
> `implements ControlValueAccessor` yanına `OnInit` ekle; `import { OnInit } from '@angular/core'`.
3. Tip (dosya sonuna, sınıf dışına):
```typescript
export interface AutocompleteItem {
  kind: 'variable' | 'function';
  label: string;
  detail: string;
  insert: string;
  caretOffsetFromEnd: number; // eklenen metnin sonundan imleç kaç karakter geri
}
```
4. Öneri üretimi + uygulama + klavye:
```typescript
  /** İmleç altındaki kısmi kelimeye göre değişken + fonksiyon önerilerini hesaplar. */
  updateSuggestions(partial: string): void {
    const q = (partial ?? '').trim();
    this.currentPartial = q;
    const vars: AutocompleteItem[] = (this.variables ?? [])
      .filter((v) => v.name.toLowerCase().startsWith(q.toLowerCase()))
      .map((v) => ({ kind: 'variable', label: v.name, detail: v.type ?? 'değişken', insert: `{{${v.name}}}`, caretOffsetFromEnd: 0 }));
    const fns: AutocompleteItem[] = this.fnService
      .filter(q)
      .map((f: ExpressionFunctionInfo) => ({
        kind: 'function',
        label: f.name,
        detail: `${f.category} · ${this.signature(f)}`,
        insert: `${f.name}()`,
        caretOffsetFromEnd: 1, // parantez içine konumlan
      }));
    this.suggestions = [...vars, ...fns];
    this.activeIndex = 0;
    this.suggestionsOpen = this.suggestions.length > 0 && q.length > 0;
    this.cdr.markForCheck();
  }

  applySuggestion(item: AutocompleteItem): void {
    // İmleç sonundaki kısmi kelimeyi (currentPartial) öneriyle değiştir; yoksa sona ekle.
    const base =
      this.currentPartial.length > 0 && this.value.endsWith(this.currentPartial)
        ? this.value.slice(0, this.value.length - this.currentPartial.length)
        : this.value;
    this.applyValue(`${base}${item.insert}`);
    this.suggestionsOpen = false;
    this.cdr.markForCheck();
  }

  onKeydown(event: KeyboardEvent): void {
    if (!this.suggestionsOpen) { return; }
    switch (event.key) {
      case 'ArrowDown': event.preventDefault(); this.activeIndex = Math.min(this.activeIndex + 1, this.suggestions.length - 1); break;
      case 'ArrowUp': event.preventDefault(); this.activeIndex = Math.max(this.activeIndex - 1, 0); break;
      case 'Enter':
      case 'Tab':
        if (this.suggestions[this.activeIndex]) { event.preventDefault(); this.applySuggestion(this.suggestions[this.activeIndex]); }
        break;
      case 'Escape': this.suggestionsOpen = false; break;
    }
    this.cdr.markForCheck();
  }

  private signature(f: ExpressionFunctionInfo): string {
    const ps = f.parameters.map((p) => (p.optional ? `[${p.name}]` : p.name)).join(', ');
    return `${f.name}(${ps})`;
  }
```
5. `handleInput`'u öneri güncellemesiyle bağla (mevcut gövdeye ekle):
```typescript
  handleInput(value: string): void {
    this.applyValue(value);
    this.clearVariableError();
    this.updateSuggestions(this.currentPartialWord(value));
  }

  /** İmleç sonundaki (son) kelime parçasını döndürür — basit v1: son harf öbeği. */
  private currentPartialWord(value: string): string {
    const m = /([A-Za-z_ğüşöçıİĞÜŞÖÇ][A-Za-z0-9_ğüşöçıİĞÜŞÖÇ]*)$/.exec(value ?? '');
    return m ? m[1] : '';
  }
```

- [ ] **Step 4: HTML — öneri listesi**

`expression-input.component.html`'de ana input'a `(keydown)="onKeydown($event)"` ekle ve input grubunun altına öneri paneli koy (mevcut değişken picker panelinin yanına):

```html
<ul class="suggestion-list" *ngIf="suggestionsOpen" role="listbox">
  <li
    *ngFor="let s of suggestions; let i = index"
    role="option"
    [class.active]="i === activeIndex"
    (mousedown)="applySuggestion(s)"
  >
    <span class="s-label">{{ s.label }}</span>
    <span class="s-kind" [class.fn]="s.kind === 'function'">{{ s.kind === 'function' ? 'ƒ' : '{}' }}</span>
    <span class="s-detail">{{ s.detail }}</span>
  </li>
</ul>
```
> `(mousedown)` kullan (blur'dan önce tetiklenir → seçim kaybolmaz).

- [ ] **Step 5: SCSS — öneri paneli stili**

`expression-input.component.scss`'e ekle:

```scss
.suggestion-list {
  position: absolute;
  z-index: 20;
  margin: 2px 0 0;
  padding: 4px 0;
  max-height: 220px;
  overflow-y: auto;
  min-width: 220px;
  background: var(--surface, #fff);
  border: 1px solid var(--border, #ccc);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.15);
  list-style: none;

  li {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 10px;
    cursor: pointer;
    font-size: 13px;

    &.active,
    &:hover { background: var(--hover, #eef2ff); }

    .s-kind { font-family: monospace; opacity: 0.6; &.fn { color: #6d28d9; } }
    .s-detail { margin-left: auto; opacity: 0.65; font-size: 12px; }
  }
}
```
> Panelin doğru konumlanması için input sarmalayıcıya `position: relative` olduğundan emin ol (yoksa ekle).

- [ ] **Step 6: Testi çalıştır (PASS)**

Run: `cd src/RPA.Studio && npx ng test --watch=false --include='**/expression-input.component.spec.ts'`
Expected: PASS.

- [ ] **Step 7: Studio derleme**

Run: `cd src/RPA.Studio && npm run build`
Expected: BAŞARILI (yalnız önceden var olan SCSS budget uyarıları kabul).

- [ ] **Step 8: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.ts src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.html src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.scss src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.spec.ts
git commit -m "feat(studio): expression-input satir-ici autocomplete

Degisken + fonksiyon onerileri (kismi kelime), imza/kategori ipucu, ok/Enter/Tab/Esc,
fonksiyon secince Name() + imlec parantez ici. ExpressionFunctionService kaynagi.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

## Task 9: Uçtan uca doğrulama

**Files:** yok (yalnız çalıştırma).

- [ ] **Step 1: Tüm backend testleri (+ geriye uyum)**

Run: `dotnet test`
Expected: İfade + WebAPI testleri PASS; mevcut `BaseRunner`/`ExpressionEvaluator` senaryoları PASS (regresyon yok). Önceden var olan ilgisiz hatalar (SapGuiChannel double-connect, Agent QueuePolling DI, RobotHub/UiSpy WithoutToken auth) beklenir — bunlar bu özellikle ilgisizdir; yeni bir kırılma olmamalı.

- [ ] **Step 2: Studio testleri + build**

Run: `cd src/RPA.Studio && npx ng test --watch=false` ve `npm run build`
Expected: PASS + build başarılı.

- [ ] **Step 3: (Manuel — kullanıcı) Studio'da deneme**

Studio designer'da bir property'ye `${Format(AddDays(Now(), 7), "dd.MM.yyyy")}` yaz; autocomplete açılıyor mu (fonksiyon + değişken), fonksiyon seçince `Name()` ekleniyor mu doğrula. Bir workflow koşturup ifadenin doğru değerlendiğini (tarih tr-TR biçimli) gözlemle.

> Not: gerçek çalıştırma kullanıcı ortamı gerektirir; testten sonra fonksiyon seti / kültür / autocomplete kelime-yakalama davranışında değişiklik gelebilir.

---

## Self-Review Notları

- **Spec kapsamı:** §3 motor (Task 1-2), §4 fonksiyon kütüphanesi (Task 3-5), §5 hata (her fonksiyon + engine), §6 API (Task 6), §7 autocomplete (Task 7-8), §8 testler (her task). Tümü karşılandı.
- **Geriye uyum (Global Constraint):** Task 2 Step 7 mevcut BaseRunner/ExpressionEvaluator senaryolarını çalıştırır; `${a} == ${b}` eski yol, tek-token/şablon yeni motor. `Compare`/`TryToDouble` mantığı motora birebir taşındı.
- **Tip tutarlılığı:** `ExpressionFunctionInfo(Name, Category, ReturnType, Parameters, Description, Example)` + `ExpressionFunctionParam(Name, Type, Optional)` — backend (Task 2), API (Task 6), Studio TS (Task 7) arasında birebir aynı alanlar. `AutocompleteItem` yalnız Studio (Task 8).
- **Variadic:** `Concat` `Parameters=[P("...","any")]`; `ExpressionEngine.IsVariadic` bunu tanır (Task 2 ↔ Task 4 tutarlı).
- **Kapsam dışı (YAGNI):** metot zinciri, dizi-dönüşlü fonksiyonlar (Split/Join), tasarım-zamanı sunucu doğrulama — spec §9.
- **Autocomplete ekleme:** Task 8 `applySuggestion` imleç sonundaki kısmi kelimeyi öneriyle **değiştirir** (currentPartial); "Up"→Upper "UpUpper()" değil "Upper()" verir. Sınır: yalnız değer sonundaki kelime (imleç ortada değilse) — imleç-konumlu değiştirme v2.

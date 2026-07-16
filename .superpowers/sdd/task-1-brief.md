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


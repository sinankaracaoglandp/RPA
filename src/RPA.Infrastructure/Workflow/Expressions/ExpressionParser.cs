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

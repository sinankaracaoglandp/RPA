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

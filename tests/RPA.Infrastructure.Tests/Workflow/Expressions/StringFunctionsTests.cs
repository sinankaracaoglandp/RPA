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

    [Fact] public void PadLeft_NegativeLength_ThrowsBusiness()
        => Assert.Throws<RPA.Domain.Exceptions.BusinessException>(() => Eval("PadLeft(\"7\", -1, \"0\")"));

    [Fact] public void PadRight_NegativeLength_ThrowsBusiness()
        => Assert.Throws<RPA.Domain.Exceptions.BusinessException>(() => Eval("PadRight(\"7\", -1, \".\")"));
}

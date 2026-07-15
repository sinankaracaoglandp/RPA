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

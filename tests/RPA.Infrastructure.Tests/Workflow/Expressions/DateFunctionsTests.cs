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
    public void Format_MalformedPattern_ThrowsBusiness()
        => Assert.Throws<BusinessException>(
            () => Eval("Format(d, \"'\")", ("d", new DateTime(2026, 1, 15))));

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

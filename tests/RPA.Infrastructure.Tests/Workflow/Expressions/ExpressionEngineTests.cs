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

    [Fact]
    public void Arithmetic_Subtraction_LeftAssociative()
        => Assert.Equal(5L, Engine().Evaluate("10 - 3 - 2"));

    [Fact]
    public void Variable_NonIdentifierName_ResolvesViaWholeTokenFastPath()
    {
        var scope = new VariableScope();
        scope.SetGlobalVariable("my-var", "x");
        scope.SetGlobalVariable("true", "shadowed");
        var engine = new ExpressionEngine(scope);
        Assert.Equal("x", engine.Evaluate("my-var"));
        Assert.Equal("shadowed", engine.Evaluate("true"));
    }

    [Fact]
    public void Variable_DottedPath_StillResolvesAfterFastPath()
    {
        var scope = new VariableScope();
        scope.SetGlobalVariable("data", Newtonsoft.Json.Linq.JObject.Parse("{\"alan\":\"v\"}"));
        Assert.Equal("v", new ExpressionEngine(scope).Evaluate("data.alan"));
    }
}

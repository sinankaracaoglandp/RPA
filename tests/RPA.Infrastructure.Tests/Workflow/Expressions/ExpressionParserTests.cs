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

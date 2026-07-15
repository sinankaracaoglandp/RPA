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

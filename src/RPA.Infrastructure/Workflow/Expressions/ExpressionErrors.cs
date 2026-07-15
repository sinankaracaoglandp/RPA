namespace RPA.Infrastructure.Workflow.Expressions;

using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>İfade motoru hataları — tümü kullanıcı-yazımı config olduğu için BusinessException.</summary>
internal static class ExpressionErrors
{
    public static BusinessException Parse(string detail) => new($"İfade ayrıştırılamadı: {detail}");
    public static BusinessException Business(string message) => new(message);
}

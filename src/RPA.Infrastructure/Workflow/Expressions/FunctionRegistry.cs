namespace RPA.Infrastructure.Workflow.Expressions;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Autocomplete + doküman için fonksiyon metadata'sı (public — WebAPI katalog uç noktası tüketir).</summary>
public sealed record ExpressionFunctionInfo(
    string Name, string Category, string ReturnType,
    IReadOnlyList<ExpressionFunctionParam> Parameters, string Description, string Example);

public sealed record ExpressionFunctionParam(string Name, string Type, bool Optional);

/// <summary>Çalıştırılabilir fonksiyon = metadata + invoker (değerlendirilmiş argümanlar).</summary>
internal sealed record ExpressionFunction(ExpressionFunctionInfo Info, Func<IReadOnlyList<object?>, object?> Invoke);

/// <summary>Tüm ifade fonksiyonlarının kayıt defteri (case-insensitive ad). Kategori modülleri doldurur.</summary>
internal static class FunctionRegistry
{
    private static readonly Dictionary<string, ExpressionFunction> Map = BuildMap();

    private static Dictionary<string, ExpressionFunction> BuildMap()
    {
        var all = new List<ExpressionFunction>();
        all.AddRange(DateFunctions.All);
        all.AddRange(StringFunctions.All);
        all.AddRange(ConversionFunctions.All);
        all.AddRange(HelperFunctions.All);
        return all.ToDictionary(f => f.Info.Name, f => f, StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryGet(string name, out ExpressionFunction fn) => Map.TryGetValue(name, out fn!);

    /// <summary>Studio autocomplete kataloğu (ada göre sıralı, invoker'sız metadata).</summary>
    public static IReadOnlyList<ExpressionFunctionInfo> Catalog =>
        Map.Values.Select(f => f.Info).OrderBy(i => i.Category).ThenBy(i => i.Name).ToList();
}

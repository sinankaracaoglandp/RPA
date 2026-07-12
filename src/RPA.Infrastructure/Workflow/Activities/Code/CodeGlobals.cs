namespace RPA.Infrastructure.Workflow.Activities.Code;

using System.Data;
using RPA.Domain.Interfaces;

/// <summary>
/// <c>System.InvokeCode</c> C# scriptine sunulan global API. Kullanıcı kodu workflow
/// değişkenlerini <see cref="Get(string)"/> ile okur, <see cref="Set(string, object?)"/> ile
/// yazar; ayrıca satır-listesi ↔ <see cref="DataTable"/> dönüşüm yardımcıları sağlanır.
/// </summary>
public sealed class CodeGlobals
{
    private readonly IActivityExecutionContext _ctx;

    public CodeGlobals(IActivityExecutionContext ctx)
        => _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));

    /// <summary>Script tamamlandığında workflow değişkenlerine yazılacak çıktılar.</summary>
    public Dictionary<string, object?> Outputs { get; } = new();

    /// <summary>Bir workflow değişkenini okur (yoksa null).</summary>
    public object? Get(string name)
    {
        try { return _ctx.GetVariable<object?>(name); }
        catch { return null; }
    }

    /// <summary>Bir workflow değişkenini tipli okur (yoksa/uyumsuzsa default).</summary>
    public T? Get<T>(string name)
    {
        try { return _ctx.GetVariable<T>(name); }
        catch { return default; }
    }

    /// <summary>Bir workflow değişkenine yazar (script bitince kalıcı olur).</summary>
    public void Set(string name, object? value) => Outputs[name] = value;

    /// <summary>Korelasyonlu log yazar.</summary>
    public void Log(string message) => _ctx.Log(message);

    /// <summary>Satır listesini (veya DataTable'ı) gerçek <see cref="DataTable"/>'a çevirir.</summary>
    public DataTable ToDataTable(object? rows) => DataTableConverter.ToDataTable(rows);

    /// <summary>DataTable'ı satır listesine çevirir.</summary>
    public List<Dictionary<string, object?>> ToRows(DataTable table) => DataTableConverter.ToRows(table);
}

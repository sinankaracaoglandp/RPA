namespace RPA.Infrastructure.Workflow.Activities.Code;

using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// Kullanıcının yazdığı C# kodunu (Roslyn scripting) çalıştırır. Workflow değişkenlerini
/// <c>Get("ad")</c> ile okur, <c>Set("ad", deger)</c> ile yazar; <c>ToDataTable/ToRows</c> ile
/// SAP/Excel satır verisini <see cref="System.Data.DataTable"/> olarak işleyebilir.
///
/// <para><b>GÜVENLİK:</b> Kod, robot süreci yetkileriyle <b>sandbox'sız</b> çalışır. Yalnızca
/// güvenilir workflow tasarımcılarına açılmalıdır (Kontrat: yetki/rol kontrolü çağıran katmanda).</para>
/// </summary>
public sealed class InvokeCsharpActivity : IActivity
{
    private static readonly string[] Imports =
    {
        "System",
        "System.Linq",
        "System.Collections.Generic",
        "System.Data",
        "System.Text",
        "System.Globalization",
    };

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "System.InvokeCode",
        DisplayName = "C# Kod Çalıştır",
        Category = "Kod & Veri",
        Description = "Roslyn ile C# kodu çalıştırır. Get(\"ad\") oku, Set(\"ad\", deger) yaz, "
                    + "ToDataTable(rows)/ToRows(dt) ile DataTable işle.",
        Inputs = new()
        {
            new ActivityParameter
            {
                Name = "code",
                Type = "string",
                Required = true,
                Description = "C# kod gövdesi (deyimler). Örn: var dt = ToDataTable(Get(\"rows\")); "
                            + "Set(\"adet\", dt.Rows.Count);",
            },
        },
        Outputs = new(),
        RequiredCapabilities = new() { "code" },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var code = context.GetVariable<string>("code");
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new BusinessException("'code' parametresi boş olamaz.");
        }

        var globals = new CodeGlobals(context);
        var options = ScriptOptions.Default
            .WithImports(Imports)
            .WithReferences(
                typeof(System.Data.DataTable).Assembly,
                typeof(System.Linq.Enumerable).Assembly,
                typeof(CodeGlobals).Assembly);

        context.Log("C# kod aktivitesi çalıştırılıyor.");
        try
        {
            await CSharpScript.EvaluateAsync(code, options, globals, typeof(CodeGlobals));
        }
        catch (CompilationErrorException ex)
        {
            // Derleme hatası = kullanıcı girdi hatası → Business.
            throw new BusinessException(
                "C# kod derleme hatası: " + string.Join(" | ", ex.Diagnostics.Select(d => d.ToString())));
        }

        context.Log($"C# kod aktivitesi tamamlandı ({globals.Outputs.Count} çıktı).");
        return new Dictionary<string, object?>(globals.Outputs);
    }
}

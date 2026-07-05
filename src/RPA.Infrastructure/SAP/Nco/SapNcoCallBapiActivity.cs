namespace RPA.Infrastructure.SAP.Nco;

using System.Runtime.Versioning;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// SAP NCo: BAPI/RFC çağrısı aktivitesi (Task 4.2, Spec 5.2/6). "bapiName" ve "inputs"
/// değişkenlerini alır; sonuç "result" (SapCallResult), "outputs" ve "success" çıktılarına yazılır.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapNcoCallBapiActivity : IActivity
{
    private readonly ISapDataChannel _channel;

    public SapNcoCallBapiActivity(ISapDataChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Sap.Nco.CallBapi",
        DisplayName = "SAP BAPI/RFC Çağır",
        Category = "SAP",
        Description = "SAP .NET Connector (NCo) üzerinden bir BAPI/RFC function module çalıştırır.",
        Inputs = new()
        {
            new ActivityParameter { Name = "bapiName", Type = "string", Required = true, Description = "BAPI/RFC adı" },
            new ActivityParameter { Name = "inputs", Type = "JSON", Required = false, Description = "Import parametreleri (ad-değer)" },
            new ActivityParameter { Name = "tableInputs", Type = "JSON", Required = false, Description = "Tablo import parametreleri" },
        },
        Outputs = new()
        {
            new ActivityParameter { Name = "result", Type = "JSON", Required = false, Description = "SapCallResult" },
            new ActivityParameter { Name = "outputs", Type = "JSON", Required = false, Description = "Export parametreleri" },
            new ActivityParameter { Name = "success", Type = "bool", Required = false, Description = "Başarılı mı" },
        },
        RequiredCapabilities = new() { "sap-nco" },
        ExceptionClassification = new ExceptionClassificationRule
        {
            Condition = "ReturnType == 'E' || ReturnType == 'A'",
            Classification = RPA.Domain.Enums.ExceptionType.Business,
        },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var bapiName = context.GetVariable<string>("bapiName");
        if (string.IsNullOrWhiteSpace(bapiName))
        {
            throw new BusinessException("'bapiName' parametresi boş olamaz.");
        }

        var inputs = context.GetVariable<Dictionary<string, object?>>("inputs") ?? new();
        var tableInputs = context.GetVariable<Dictionary<string, List<Dictionary<string, object?>>>>("tableInputs");

        context.Log($"SAP BAPI çağrılıyor: {bapiName}");
        var result = await _channel.CallBapiAsync(bapiName, inputs, tableInputs);

        context.SetVariable("result", result);
        context.SetVariable("outputs", result.Outputs);
        context.SetVariable("success", result.Success);

        return new()
        {
            ["result"] = result,
            ["outputs"] = result.Outputs,
            ["success"] = result.Success,
        };
    }
}

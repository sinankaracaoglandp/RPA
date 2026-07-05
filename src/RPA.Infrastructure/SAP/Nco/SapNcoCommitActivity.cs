namespace RPA.Infrastructure.SAP.Nco;

using System.Runtime.Versioning;
using RPA.Domain.Interfaces;

/// <summary>
/// SAP NCo: BAPI_TRANSACTION_COMMIT / ROLLBACK aktivitesi (Task 4.2, Spec 5.2 — explicit commit).
/// "rollback" değişkeni true ise rollback, aksi halde commit çalıştırılır. Sonuç "result" ve
/// "success" çıktılarına yazılır.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapNcoCommitActivity : IActivity
{
    private readonly ISapDataChannel _channel;

    public SapNcoCommitActivity(ISapDataChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Sap.Nco.Commit",
        DisplayName = "SAP Commit/Rollback",
        Category = "SAP",
        Description = "BAPI_TRANSACTION_COMMIT (veya rollback=true ile BAPI_TRANSACTION_ROLLBACK) çalıştırır.",
        Inputs = new()
        {
            new ActivityParameter { Name = "rollback", Type = "bool", Required = false, DefaultValue = false, Description = "true ise rollback" },
        },
        Outputs = new()
        {
            new ActivityParameter { Name = "result", Type = "JSON", Required = false, Description = "SapCallResult" },
            new ActivityParameter { Name = "success", Type = "bool", Required = false, Description = "Başarılı mı" },
        },
        RequiredCapabilities = new() { "sap-nco" },
        DefaultProperties = new() { ["rollback"] = false },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var rollback = context.GetVariable<bool>("rollback");

        context.Log(rollback ? "SAP transaksiyon rollback ediliyor." : "SAP transaksiyon commit ediliyor.");
        var result = rollback
            ? await _channel.RollbackAsync()
            : await _channel.CommitAsync();

        context.SetVariable("result", result);
        context.SetVariable("success", result.Success);

        return new()
        {
            ["result"] = result,
            ["success"] = result.Success,
        };
    }
}

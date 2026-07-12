namespace RPA.Infrastructure.SAP.SapGuiElements;

using System.Runtime.Versioning;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// SAP GUI oturumunda transaction kodu calistirir (Spec 5.3 - SAP GUI: ExecuteTransaction).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapGuiExecuteTransactionActivity : IActivity
{
    private readonly ISapGuiChannel _channel;

    public SapGuiExecuteTransactionActivity(ISapGuiChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Sap.Gui.ExecuteTransaction",
        DisplayName = "SAP GUI Islem Kodu Calistir",
        Category = "SAP",
        Description = "SAP GUI oturumunda transaction kodu calistirir.",
        Inputs = new()
        {
            new ActivityParameter { Name = "transactionCode", Type = "string", Required = true, Description = "Transaction kodu (orn. MM01)" }
        },
        Outputs = new(),
        RequiredCapabilities = new() { "sap-gui" }
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var transactionCode = context.GetVariable<string>("transactionCode");
        if (string.IsNullOrWhiteSpace(transactionCode))
        {
            throw new BusinessException("'transactionCode' parametresi bos olamaz.");
        }

        context.Log($"SAP GUI transaction calistiriliyor: {transactionCode}");
        await _channel.ExecuteTransactionAsync(transactionCode);
        return new();
    }
}

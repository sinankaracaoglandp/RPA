namespace RPA.Infrastructure.Tests.Workflow.EInvoice;

using RPA.Infrastructure.Workflow.Activities.EInvoice;
using Xunit;

public sealed class UblInvoiceParserTests
{
    [Fact]
    public void Models_ExposeStableWorkflowShape()
    {
        var invoice = new InvoiceData
        {
            InvoiceNumber = "FTR202600001",
            Lines = [new InvoiceLineData { Name = "Kalem", Quantity = 2m }],
            CustomFields = new Dictionary<string, object?> { ["orderNumber"] = "S-42" }
        };

        Assert.Equal("FTR202600001", invoice.InvoiceNumber);
        Assert.Equal(2m, invoice.Lines.Single().Quantity);
        Assert.Equal("S-42", invoice.CustomFields["orderNumber"]);
    }
}

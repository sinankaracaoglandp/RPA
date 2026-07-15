namespace RPA.Infrastructure.Tests.Workflow;

using System.Text.Json;
using RPA.Infrastructure.Workflow;
using Xunit;

public class WorkflowSchemaValidationTests
{
    [Theory]
    [InlineData("EInvoice.ReadUbl", "filePath", "invoice.xml")]
    [InlineData("EInvoice.ReadUblBatch", "filePaths", "invoice.xml")]
    public void EInvoiceActivity_WithOneSource_IsValid(string activity, string sourceName, string sourceValue)
    {
        var properties = sourceName == "filePaths"
            ? new Dictionary<string, object?> { [sourceName] = new[] { sourceValue } }
            : new Dictionary<string, object?> { [sourceName] = sourceValue };

        var result = new WorkflowValidator().ValidateWorkflowJson(Workflow(activity, properties));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Theory]
    [InlineData("EInvoice.ReadUbl", "filePath", "xmlContent")]
    [InlineData("EInvoice.ReadUblBatch", "filePaths", "xmlContents")]
    public void EInvoiceActivity_WithBothSources_IsInvalid(string activity, string firstSource, string secondSource)
    {
        var properties = new Dictionary<string, object?>
        {
            [firstSource] = firstSource.EndsWith('s') ? new[] { "invoice.xml" } : "invoice.xml",
            [secondSource] = secondSource.EndsWith('s') ? new[] { "<Invoice />" } : "<Invoice />"
        };

        var result = new WorkflowValidator().ValidateWorkflowJson(Workflow(activity, properties));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("EInvoice.ReadUbl")]
    [InlineData("EInvoice.ReadUblBatch")]
    public void EInvoiceActivity_WithoutSource_IsInvalid(string activity)
    {
        var result = new WorkflowValidator().ValidateWorkflowJson(Workflow(activity, new Dictionary<string, object?>()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("exactly one source", StringComparison.OrdinalIgnoreCase));
    }

    private static string Workflow(string activity, Dictionary<string, object?> properties) => JsonSerializer.Serialize(new
    {
        schemaVersion = "1.0",
        id = Guid.NewGuid(),
        name = "E-Invoice test",
        version = "1.0.0",
        nodes = new[] { new { id = "read", type = "activity", activity, properties } },
        connections = Array.Empty<object>()
    });
}

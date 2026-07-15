namespace RPA.Infrastructure.Tests.Workflow;

using System.Text.Json;
using RPA.Infrastructure.Workflow;
using Xunit;

public class WorkflowSchemaValidationTests
{
    [Fact]
    public void EInvoiceMappingDefinitions_DocumentMappingsAsArrays()
    {
        using var stream = typeof(WorkflowValidator).Assembly
            .GetManifestResourceStream("RPA.Infrastructure.Workflow.WorkflowSchema.json");
        Assert.NotNull(stream);
        using var schema = JsonDocument.Parse(stream!);
        var definitions = schema.RootElement.GetProperty("$defs");

        Assert.Equal("array", definitions.GetProperty("eInvoiceReadUblProperties")
            .GetProperty("properties").GetProperty("mappings").GetProperty("type").GetString());
        Assert.Equal("array", definitions.GetProperty("eInvoiceReadUblBatchProperties")
            .GetProperty("properties").GetProperty("mappings").GetProperty("type").GetString());
    }

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

    [Theory]
    [MemberData(nameof(MeaninglessBatchSources))]
    public void EInvoiceBatch_WithOnlyMeaninglessSourceElements_IsInvalid(Dictionary<string, object?> properties)
    {
        var result = new WorkflowValidator().ValidateWorkflowJson(Workflow("EInvoice.ReadUblBatch", properties));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("filePaths")]
    [InlineData("xmlContents")]
    public void EInvoiceBatch_WithAtLeastOneMeaningfulSourceElement_IsValid(string sourceName)
    {
        var properties = new Dictionary<string, object?> { [sourceName] = new object?[] { null, "  ", "invoice.xml" } };

        var result = new WorkflowValidator().ValidateWorkflowJson(Workflow("EInvoice.ReadUblBatch", properties));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Theory]
    [InlineData("mappings", "not-an-array")]
    [InlineData("outputBindings", "not-an-object")]
    [InlineData("errorMode", "Ignore")]
    public void EInvoiceSemanticProperties_WithInvalidShape_AreRejected(string property, object value)
    {
        var properties = new Dictionary<string, object?> { ["xmlContents"] = new[] { "<Invoice />" }, [property] = value };
        var result = new WorkflowValidator().ValidateWorkflowJson(Workflow("EInvoice.ReadUblBatch", properties));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void EInvoiceMapping_WithInvalidSourceOrFieldTypes_IsRejected()
    {
        var properties = new Dictionary<string, object?> { ["xmlContent"] = "<Invoice />", ["mappings"] = new object[] { new { name = "x", source = "Unknown", type = "money", required = "yes" } } };
        var result = new WorkflowValidator().ValidateWorkflowJson(Workflow("EInvoice.ReadUbl", properties));
        Assert.False(result.IsValid);
    }

    public static TheoryData<Dictionary<string, object?>> MeaninglessBatchSources => new()
    {
        new Dictionary<string, object?> { ["filePaths"] = Array.Empty<object?>() },
        new Dictionary<string, object?> { ["filePaths"] = new object?[] { null } },
        new Dictionary<string, object?> { ["filePaths"] = new object?[] { "   " } },
        new Dictionary<string, object?> { ["xmlContents"] = Array.Empty<object?>() },
        new Dictionary<string, object?> { ["xmlContents"] = new object?[] { null } },
        new Dictionary<string, object?> { ["xmlContents"] = new object?[] { "   " } }
    };

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

namespace RPA.Infrastructure.Tests.Workflow.EInvoice;

using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Workflow.Activities.EInvoice;
using Xunit;

public sealed class EInvoiceActivityTests
{
    [Fact]
    public async Task ReadUbl_XmlContent_SetsStableOutputsAndNamedBindings()
    {
        var context = FakeActivityContext.With(("xmlContent", SampleXml), ("outputBindings", "{\"invoiceNumber\":\"faturaNo\"}"));

        var outputs = await new ReadUblActivity(new UblInvoiceParser()).ExecuteAsync(context);

        Assert.Equal("FTR202600001", context.Variables["faturaNo"]);
        Assert.Equal("FTR202600001", outputs["faturaNo"]);
        Assert.IsType<InvoiceData>(context.Variables["invoice"]);
        Assert.IsType<List<InvoiceLineData>>(context.Variables["lines"]);
        Assert.IsType<Dictionary<string, object?>>(context.Variables["customFields"]);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("a.xml", "<Invoice />")]
    public async Task ReadUbl_RequiresExactlyOneSource(string? filePath, string? xmlContent)
    {
        var context = FakeActivityContext.With(("filePath", filePath), ("xmlContent", xmlContent));

        await Assert.ThrowsAsync<InvoiceParseException>(() => new ReadUblActivity(new UblInvoiceParser()).ExecuteAsync(context));
    }

    [Fact]
    public async Task ReadUbl_OutputBindings_AllowInvoicePropertiesAndCustomFields()
    {
        InvoiceMappingRule[] mapping = [new("orderNumber", "InvoiceNotes", null, null, @"Sipariş No:\s*(?<value>\S+)", "value")];
        var xml = SampleXml.Replace("<cbc:ID>FTR202600001</cbc:ID>", "<cbc:ID>FTR202600001</cbc:ID><cbc:Note>Sipariş No: S-42</cbc:Note>");
        var context = FakeActivityContext.With(("xmlContent", xml), ("mappings", mapping),
            ("outputBindings", "{\"currency\":\"paraBirimi\",\"orderNumber\":\"siparisNo\"}"));

        await new ReadUblActivity(new UblInvoiceParser()).ExecuteAsync(context);

        Assert.Equal("TRY", context.Variables["paraBirimi"]);
        Assert.Equal("S-42", context.Variables["siparisNo"]);
    }

    [Fact]
    public async Task ReadUbl_OutputBindings_RejectUnsafePropertyPaths()
    {
        var context = FakeActivityContext.With(("xmlContent", SampleXml), ("outputBindings", "{\"supplier.name\":\"supplierName\"}"));

        await Assert.ThrowsAsync<InvoiceParseException>(() => new ReadUblActivity(new UblInvoiceParser()).ExecuteAsync(context));
        Assert.DoesNotContain("supplierName", context.Variables);
    }

    [Fact]
    public async Task Batch_Continue_ReturnsSuccessAndFailureItemsInSourceOrderWithoutXmlInError()
    {
        const string brokenXml = "<broken secret='do-not-leak'";
        var context = FakeActivityContext.With(("xmlContents", new[] { SampleXml, brokenXml }), ("errorMode", "Continue"));

        await new ReadUblBatchActivity(new UblInvoiceParser()).ExecuteAsync(context);

        var results = Assert.IsType<List<InvoiceBatchItemResult>>(context.Variables["results"]);
        Assert.Equal(new[] { 0, 1 }, results.Select(result => result.SourceIndex));
        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.DoesNotContain("do-not-leak", results[1].Error);
    }

    [Fact]
    public async Task Batch_Stop_ThrowsOnFirstInvalidItem()
    {
        var context = FakeActivityContext.With(("xmlContents", new[] { SampleXml, "<broken" }), ("errorMode", "Stop"));

        await Assert.ThrowsAsync<InvoiceParseException>(() => new ReadUblBatchActivity(new UblInvoiceParser()).ExecuteAsync(context));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Batch_MissingOrBlankErrorMode_DefaultsToContinue(string? errorMode)
    {
        var context = FakeActivityContext.With(("xmlContents", new[] { SampleXml, "<broken" }), ("errorMode", errorMode));

        await new ReadUblBatchActivity(new UblInvoiceParser()).ExecuteAsync(context);

        var results = Assert.IsType<List<InvoiceBatchItemResult>>(context.Variables["results"]);
        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
    }

    [Fact]
    public async Task Batch_OutputBindings_PublishOrderedListsWithNullForFailures()
    {
        var context = FakeActivityContext.With(
            ("xmlContents", new[] { SampleXml, "<broken", SampleXml.Replace("FTR202600001", "FTR202600003") }),
            ("outputBindings", "{\"invoiceNumber\":\"invoiceNumbers\"}"));

        await new ReadUblBatchActivity(new UblInvoiceParser()).ExecuteAsync(context);

        var values = Assert.IsType<List<object?>>(context.Variables["invoiceNumbers"]);
        Assert.Equal(new object?[] { "FTR202600001", null, "FTR202600003" }, values);
    }

    [Fact]
    public async Task Batch_AllFailures_RejectsUnsafeBindingSourceWithoutPublishingTarget()
    {
        var context = FakeActivityContext.With(
            ("xmlContents", new[] { "<broken-one", "<broken-two" }),
            ("errorMode", "Continue"),
            ("outputBindings", "{\"supplier.name\":\"names\"}"));

        await Assert.ThrowsAsync<InvoiceParseException>(() => new ReadUblBatchActivity(new UblInvoiceParser()).ExecuteAsync(context));

        Assert.DoesNotContain("names", context.Variables);
    }

    [Fact]
    public void Metadata_DeclaresAllActivityInputsOutputsAndContinueDefault()
    {
        var single = new ReadUblActivity(new UblInvoiceParser()).GetMetadata();
        var batch = new ReadUblBatchActivity(new UblInvoiceParser()).GetMetadata();

        Assert.Equal(new[] { "filePath", "xmlContent", "mappings", "outputBindings" }, single.Inputs.Select(input => input.Name));
        Assert.Equal(new[] { "invoice", "lines", "customFields" }, single.Outputs.Select(output => output.Name));
        Assert.Equal(new[] { "filePaths", "xmlContents", "errorMode", "mappings", "outputBindings" }, batch.Inputs.Select(input => input.Name));
        Assert.Equal("Continue", batch.Inputs.Single(input => input.Name == "errorMode").DefaultValue);
        Assert.Equal("results", Assert.Single(batch.Outputs).Name);
    }

    [Theory]
    [InlineData("invoice")]
    [InlineData("LINES")]
    [InlineData("CustomFields")]
    [InlineData("ReSuLtS")]
    public async Task OutputBindings_RejectReservedTargetsCaseInsensitively(string target)
    {
        var context = FakeActivityContext.With(("xmlContent", SampleXml), ("outputBindings", $"{{\"invoiceNumber\":\"{target}\"}}"));

        await Assert.ThrowsAsync<InvoiceParseException>(() => new ReadUblActivity(new UblInvoiceParser()).ExecuteAsync(context));
    }

    [Fact]
    public async Task Batch_RequiresExactlyOneSourceCollection()
    {
        var context = FakeActivityContext.With(("filePaths", new[] { "a.xml" }), ("xmlContents", new[] { SampleXml }));

        await Assert.ThrowsAsync<InvoiceParseException>(() => new ReadUblBatchActivity(new UblInvoiceParser()).ExecuteAsync(context));
    }

    private sealed class FakeActivityContext : IActivityExecutionContext
    {
        public Dictionary<string, object?> Variables { get; } = new(StringComparer.Ordinal);
        public string TimeZone => "UTC";
        public Guid JobRunId { get; } = Guid.NewGuid();
        public static FakeActivityContext With(params (string Name, object? Value)[] variables)
        {
            var context = new FakeActivityContext();
            foreach (var (name, value) in variables) context.Variables[name] = value;
            return context;
        }
        public T GetVariable<T>(string name) => Variables.TryGetValue(name, out var value) && value is not null ? (T)value : default!;
        public void SetVariable(string name, object? value) => Variables[name] = value;
        public Task<string> GetCredentialAsync(string credentialName) => Task.FromResult(string.Empty);
        public Task<string?> GetAssetAsync(string assetName) => Task.FromResult<string?>(null);
        public void Log(string message, LogLevel level = LogLevel.Information) { }
    }

    private const string SampleXml = """
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
          <cbc:ID>FTR202600001</cbc:ID><cbc:DocumentCurrencyCode>TRY</cbc:DocumentCurrencyCode>
          <cac:InvoiceLine><cbc:ID>1</cbc:ID><cac:Item><cbc:Name>Kalem</cbc:Name></cac:Item></cac:InvoiceLine>
        </Invoice>
        """;
}

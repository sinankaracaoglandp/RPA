namespace RPA.Agent.Tests;

using RPA.Agent.Jobs;

public class AgentJobPayloadParserTests
{
    [Fact]
    public void Gecerli_Payload_Workflow_Ve_Argumanlari_Cozer()
    {
        var wvId = Guid.NewGuid();
        var envId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var payload = $$"""
        {
          "workflowVersionId": "{{wvId}}",
          "version": "2.1.0",
          "environmentId": "{{envId}}",
          "jsonDefinition": { "nodes": [], "connections": [] },
          "arguments": { "in_Musteri": "ACME", "in_Adet": 5, "in_Aktif": true }
        }
        """;

        var job = AgentJobPayloadParser.Parse(itemId, payload);

        Assert.Equal(itemId, job.ItemId);
        Assert.Equal(wvId, job.WorkflowVersion.Id);
        Assert.Equal("2.1.0", job.WorkflowVersion.Version);
        Assert.Equal(envId, job.WorkflowVersion.EnvironmentId);
        Assert.Contains("nodes", job.WorkflowVersion.JsonDefinition);
        Assert.Equal("ACME", job.Arguments["in_Musteri"]);
        Assert.Equal(5L, job.Arguments["in_Adet"]);
        Assert.Equal(true, job.Arguments["in_Aktif"]);
    }

    [Fact]
    public void JsonDefinition_String_Olarak_Da_Kabul_Edilir()
    {
        var payload = $$"""
        { "workflowVersionId": "{{Guid.NewGuid()}}", "jsonDefinition": "{\"nodes\":[]}" }
        """;
        var job = AgentJobPayloadParser.Parse(Guid.NewGuid(), payload);
        Assert.Equal("{\"nodes\":[]}", job.WorkflowVersion.JsonDefinition);
    }

    [Fact]
    public void Bos_Payload_FormatException_Firlatir()
        => Assert.Throws<FormatException>(() => AgentJobPayloadParser.Parse(Guid.NewGuid(), ""));

    [Fact]
    public void Gecersiz_Json_FormatException_Firlatir()
        => Assert.Throws<FormatException>(() => AgentJobPayloadParser.Parse(Guid.NewGuid(), "{ not json"));

    [Fact]
    public void WorkflowVersionId_Yoksa_FormatException_Firlatir()
        => Assert.Throws<FormatException>(() => AgentJobPayloadParser.Parse(Guid.NewGuid(), """{ "version": "1.0.0" }"""));
}

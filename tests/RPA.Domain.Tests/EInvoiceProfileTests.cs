namespace RPA.Domain.Tests;

using RPA.Domain.Entities;

public sealed class EInvoiceProfileTests
{
    [Fact]
    public void Profile_BelongsToProject_AndStartsWithoutVersions()
    {
        var projectId = Guid.NewGuid();
        var profile = new EInvoiceProfile { ProjectId = projectId, Name = "Satış Faturası" };

        Assert.Equal(projectId, profile.ProjectId);
        Assert.Empty(profile.Versions);
    }

    [Fact]
    public void PublishedVersion_CarriesSnapshotFields()
    {
        var version = new EInvoiceProfileVersion
        {
            Version = 1,
            DefinitionJson = "{\"fields\":[]}",
            OutputSchemaJson = "{\"type\":\"object\"}",
            PublishedAt = DateTime.UtcNow,
        };

        Assert.Equal(1, version.Version);
        Assert.NotEmpty(version.DefinitionJson);
        Assert.NotEmpty(version.OutputSchemaJson);
    }
}

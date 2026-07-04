namespace RPA.Infrastructure.Tests;

using RPA.Domain.Enums;
using RPA.Infrastructure.Workflow;

/// <summary>
/// Workflow JSON şema doğrulama + aktivite kataloğu testleri (Task 2.1.1).
/// Spec Bölüm 5.1, 5.3.
/// </summary>
public class WorkflowValidatorTests
{
    private static string ValidWorkflowJson => """
    {
      "schemaVersion": "1.0",
      "id": "3f2504e0-4f89-41d3-9a0c-0305e82c3301",
      "name": "Örnek Akış",
      "version": "1.0.0",
      "nodes": [
        { "id": "n1", "type": "activity", "activity": "Sap.Nco.CallBapi", "channel": "nco" },
        { "id": "n2", "type": "log", "message": "bitti", "level": "Information" }
      ],
      "connections": [
        { "from": "n1", "to": "n2", "fromPort": "success" }
      ],
      "errorHandling": { "screenshotOnError": true }
    }
    """;

    // ---------- Validator ----------

    [Fact]
    public void ValidWorkflowJson_Passes()
    {
        var validator = new WorkflowValidator();
        var result = validator.ValidateWorkflowJson(ValidWorkflowJson);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MissingRequiredField_Fails()
    {
        // "name" ve "version" eksik.
        const string json = """
        {
          "schemaVersion": "1.0",
          "id": "3f2504e0-4f89-41d3-9a0c-0305e82c3301",
          "nodes": [],
          "connections": []
        }
        """;
        var result = new WorkflowValidator().ValidateWorkflowJson(json);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void InvalidNodeType_Fails()
    {
        const string json = """
        {
          "schemaVersion": "1.0",
          "id": "3f2504e0-4f89-41d3-9a0c-0305e82c3301",
          "name": "x",
          "version": "1.0.0",
          "nodes": [ { "id": "n1", "type": "notARealType" } ],
          "connections": []
        }
        """;
        var result = new WorkflowValidator().ValidateWorkflowJson(json);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void InvalidConnectionPort_Fails()
    {
        const string json = """
        {
          "schemaVersion": "1.0",
          "id": "3f2504e0-4f89-41d3-9a0c-0305e82c3301",
          "name": "x",
          "version": "1.0.0",
          "nodes": [ { "id": "n1", "type": "log", "message": "a" } ],
          "connections": [ { "from": "n1", "to": "n1", "fromPort": "sideways" } ]
        }
        """;
        var result = new WorkflowValidator().ValidateWorkflowJson(json);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void InvalidVersionPattern_Fails()
    {
        const string json = """
        {
          "schemaVersion": "1.0",
          "id": "3f2504e0-4f89-41d3-9a0c-0305e82c3301",
          "name": "x",
          "version": "v1",
          "nodes": [],
          "connections": []
        }
        """;
        var result = new WorkflowValidator().ValidateWorkflowJson(json);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void WrongSchemaVersion_Fails()
    {
        const string json = """
        {
          "schemaVersion": "2.0",
          "id": "3f2504e0-4f89-41d3-9a0c-0305e82c3301",
          "name": "x",
          "version": "1.0.0",
          "nodes": [],
          "connections": []
        }
        """;
        var result = new WorkflowValidator().ValidateWorkflowJson(json);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void MalformedJson_FailsGracefully()
    {
        var result = new WorkflowValidator().ValidateWorkflowJson("{ not json ");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void EmptyJson_Fails()
    {
        var result = new WorkflowValidator().ValidateWorkflowJson("");

        Assert.False(result.IsValid);
    }

    // ---------- Catalog ----------

    [Fact]
    public void Catalog_HasAtLeast25Activities()
    {
        var catalog = new ActivityCatalog();
        Assert.True(catalog.ListAll().Count >= 25,
            $"En az 25 aktivite bekleniyordu, {catalog.Count} bulundu.");
    }

    [Fact]
    public void Catalog_GetActivityMetadata_ReturnsExpectedMetadata()
    {
        var catalog = new ActivityCatalog();
        var meta = catalog.GetActivityMetadata("Sap.Nco.CallBapi");

        Assert.NotNull(meta);
        Assert.Equal("SAP BAPI Çağır", meta!.DisplayName);
        Assert.Contains(meta.Inputs, i => i.Name == "bapiName" && i.Required);
        Assert.Contains(meta.Outputs, o => o.Name == "result");
        Assert.Contains("sap-nco", meta.RequiredCapabilities);
        Assert.NotNull(meta.ExceptionClassification);
        Assert.Equal(ExceptionType.Business, meta.ExceptionClassification!.Classification);
        Assert.Equal("ReturnType=='E'", meta.ExceptionClassification.Condition);
    }

    [Fact]
    public void Catalog_GetActivityMetadata_UnknownReturnsNull()
    {
        var catalog = new ActivityCatalog();
        Assert.Null(catalog.GetActivityMetadata("Does.Not.Exist"));
        Assert.Null(catalog.GetActivityMetadata(""));
    }

    [Fact]
    public void Catalog_ListByCapability_FiltersCorrectly()
    {
        var catalog = new ActivityCatalog();
        var ncoActivities = catalog.ListActivitiesByCapability("sap-nco");

        Assert.NotEmpty(ncoActivities);
        Assert.All(ncoActivities, a => Assert.Contains("sap-nco", a.RequiredCapabilities));
        Assert.Contains(ncoActivities, a => a.ActivityId == "Sap.Nco.CallBapi");
    }

    [Fact]
    public void Catalog_ListByCategory_FiltersCorrectly()
    {
        var catalog = new ActivityCatalog();
        var otp = catalog.ListActivitiesByCategory(ActivityRegistry.CatOtp);

        Assert.Single(otp);
        Assert.Equal("Otp.GetOtp", otp[0].ActivityId);
    }

    [Fact]
    public void Catalog_AllActivitiesHaveDisplayNameAndCategory()
    {
        var catalog = new ActivityCatalog();
        foreach (var (id, meta) in catalog.ListAll())
        {
            Assert.False(string.IsNullOrWhiteSpace(meta.DisplayName), $"{id} DisplayName eksik");
            Assert.False(string.IsNullOrWhiteSpace(meta.Category), $"{id} Category eksik");
            Assert.Equal(id, meta.ActivityId);
        }
    }

    [Theory]
    [InlineData("Logic.Assign")]
    [InlineData("Logic.TryCatch")]
    [InlineData("Sap.Gui.GridRead")]
    [InlineData("Sap.Nco.CallBapi")]
    [InlineData("Web.FrameSwitch")]
    [InlineData("Api.HttpRequest")]
    [InlineData("Excel.Read")]
    [InlineData("Csv.Write")]
    [InlineData("Email.WatchInbox")]
    [InlineData("Otp.GetOtp")]
    [InlineData("File.Zip")]
    public void Catalog_ContainsSpecActivities(string activityId)
    {
        var catalog = new ActivityCatalog();
        Assert.NotNull(catalog.GetActivityMetadata(activityId));
    }

    [Fact]
    public void Builder_DuplicateActivity_Throws()
    {
        var builder = new ActivityCatalogBuilder();
        builder.Activity("X.Y");
        Assert.Throws<InvalidOperationException>(() => builder.Activity("X.Y"));
    }

    [Fact]
    public void Builder_FluentApi_BuildsMetadata()
    {
        var builder = new ActivityCatalogBuilder();
        builder.Activity("Sap.Nco.CallBapi")
            .DisplayName("SAP BAPI Çağır")
            .Input("bapiName", "string", true)
            .Output("result", "JSON")
            .Capability("sap-nco")
            .ExceptionClassification("ReturnType=='E'", ExceptionType.Business);

        var catalog = new ActivityCatalog(builder.Build());
        var meta = catalog.GetActivityMetadata("Sap.Nco.CallBapi");

        Assert.NotNull(meta);
        Assert.Equal("SAP BAPI Çağır", meta!.DisplayName);
        Assert.Single(meta.RequiredCapabilities);
    }
}

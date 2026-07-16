namespace RPA.WebAPI.Tests;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RPA.Application.EInvoiceProfiles;
using RPA.Domain.Entities;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Services;
using RPA.WebAPI.Controllers;

public sealed class EInvoiceProfilesControllerTests
{
    [Fact]
    public async Task Publish_ReturnsVersionAndSchema_WithoutSampleXml()
    {
        await using var db = Database();
        var project = new Project { Name = "P" };
        db.Projects.Add(project); await db.SaveChangesAsync();
        var service = Service(db);
        var profile = await service.CreateAsync(project.Id, "Satış", null);
        await service.SaveDraftAsync(project.Id, profile.Id,
            "{\"fields\":[{\"name\":\"no\",\"source\":\"XPath\",\"valueXPath\":\"/Invoice/ID\",\"type\":\"string\"}],\"collections\":[]}");
        var controller = new EInvoiceProfilesController(service);

        var result = await controller.Publish(project.Id, profile.Id, default);

        var dto = Assert.IsType<EInvoiceProfileVersionDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, dto.Version);
        Assert.Contains("properties", dto.OutputSchemaJson);
        Assert.DoesNotContain("<Invoice", System.Text.Json.JsonSerializer.Serialize(dto));
    }

    [Fact]
    public async Task CrossProjectGet_ReturnsNotFound()
    {
        await using var db = Database();
        var a = new Project { Name = "A" }; var b = new Project { Name = "B" };
        db.Projects.AddRange(a, b); await db.SaveChangesAsync();
        var service = Service(db);
        var profile = await service.CreateAsync(a.Id, "Satış", null);

        var result = await new EInvoiceProfilesController(service).Get(b.Id, profile.Id, default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    private static EInvoiceProfileService Service(RpaDbContext db) =>
        new(db, new EInvoiceProfileDefinitionValidator());

    private static RpaDbContext Database() => new(new DbContextOptionsBuilder<RpaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}

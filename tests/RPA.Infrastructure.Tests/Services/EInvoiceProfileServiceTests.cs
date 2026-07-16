namespace RPA.Infrastructure.Tests.Services;

using Microsoft.EntityFrameworkCore;
using RPA.Application.EInvoiceProfiles;
using RPA.Domain.Entities;
using RPA.Domain.Exceptions;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Services;

public sealed class EInvoiceProfileServiceTests
{
    [Fact]
    public async Task Publish_CreatesImmutableIncrementingSnapshots()
    {
        await using var db = Database();
        var project = new Project { Name = "P" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var service = new EInvoiceProfileService(db, new EInvoiceProfileDefinitionValidator());
        var profile = await service.CreateAsync(project.Id, "Satış", null, default);

        await service.SaveDraftAsync(project.Id, profile.Id, Definition("faturaNo"), default);
        var v1 = await service.PublishAsync(project.Id, profile.Id, Guid.NewGuid(), default);
        await service.SaveDraftAsync(project.Id, profile.Id, Definition("belgeNo"), default);
        var v2 = await service.PublishAsync(project.Id, profile.Id, Guid.NewGuid(), default);

        Assert.Equal((1, 2), (v1.Version, v2.Version));
        Assert.Contains("faturaNo", v1.DefinitionJson);
        Assert.DoesNotContain("belgeNo", v1.DefinitionJson);
        Assert.Contains("belgeNo", v2.DefinitionJson);
    }

    [Fact]
    public async Task Get_FromAnotherProject_DoesNotRevealProfile()
    {
        await using var db = Database();
        var project = new Project { Name = "P" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var service = new EInvoiceProfileService(db, new EInvoiceProfileDefinitionValidator());
        var profile = await service.CreateAsync(project.Id, "Satış", null, default);

        await Assert.ThrowsAsync<BusinessException>(() => service.GetAsync(Guid.NewGuid(), profile.Id, default));
    }

    [Fact]
    public async Task Delete_SoftDeletesProfile_FromProjectLists()
    {
        await using var db = Database();
        var project = new Project { Name = "P" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var service = new EInvoiceProfileService(db, new EInvoiceProfileDefinitionValidator());
        var profile = await service.CreateAsync(project.Id, "Satış", null, default);

        await service.DeleteAsync(project.Id, profile.Id, default);

        Assert.Empty(await service.ListAsync(project.Id, default));
        Assert.True((await db.EInvoiceProfiles.IgnoreQueryFilters().SingleAsync()).IsDeleted);
    }

    private static RpaDbContext Database()
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RpaDbContext(options);
    }

    private static string Definition(string name) =>
        $$"""{"fields":[{"name":"{{name}}","source":"XPath","valueXPath":"/Invoice/ID","type":"string"}],"collections":[]}""";
}

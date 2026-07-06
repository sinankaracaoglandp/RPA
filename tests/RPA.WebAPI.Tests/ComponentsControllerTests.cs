namespace RPA.WebAPI.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Infrastructure.Services;
using RPA.Infrastructure.Workflow;
using RPA.WebAPI.Controllers;

/// <summary>
/// Faz 5 backend bağımlılığı (WP-Faz5-Backend, CLAUDE.md "Kontrat Değişikliği — 2026-07-06") —
/// ComponentsController'ın genelleştirilmiş rotaları: liste, detay, publish, approve, rol kontrolü.
/// Task 4.5'te bu rotalar "sap-login" ile hardcoded'du; artık herhangi bir componentId kabul eder.
/// </summary>
public class ComponentsControllerTests
{
    private static readonly Guid ComponentId = Guid.Parse("00000000-0000-0000-0000-000000000101");

    private static ComponentsController CreateController(
        InMemoryComponentPublishRepository repo, string[]? roles = null)
    {
        var controller = new ComponentsController(new ComponentPublishService(repo), repo);
        var identity = new ClaimsIdentity(
            (roles ?? Array.Empty<string>()).Select(r => new Claim(ClaimTypes.Role, r)),
            authenticationType: "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }

    [Fact]
    public async Task ListComponents_ReturnsOnlyPublishedVersions()
    {
        var repo = new InMemoryComponentPublishRepository();
        var controller = CreateController(repo, new[] { ComponentPublishService.DeveloperRole });

        await controller.PublishComponent(ComponentId.ToString(),
            new PublishComponentRequest { Version = "1.0.0", JsonDefinition = "{}" }, default);

        var listBefore = await controller.ListComponents(default);
        var okBefore = Assert.IsType<OkObjectResult>(listBefore.Result);
        Assert.Empty((List<ComponentVersionDto>)okBefore.Value!);

        var approver = CreateController(repo, new[] { ComponentPublishService.ApproverRole });
        await approver.ApproveComponent(ComponentId.ToString(), "1.0.0", default);

        var listAfter = await controller.ListComponents(default);
        var okAfter = Assert.IsType<OkObjectResult>(listAfter.Result);
        var published = (List<ComponentVersionDto>)okAfter.Value!;
        Assert.Single(published);
        Assert.Equal(ComponentId, published[0].ComponentId);
        Assert.Equal("Published", published[0].Status);
    }

    [Fact]
    public async Task GetComponent_ReturnsSpecificVersion()
    {
        var repo = new InMemoryComponentPublishRepository();
        var controller = CreateController(repo, new[] { ComponentPublishService.DeveloperRole });
        await controller.PublishComponent(ComponentId.ToString(),
            new PublishComponentRequest { Version = "2.0.0", JsonDefinition = "{\"a\":1}" }, default);

        var result = controller.GetComponent(ComponentId.ToString(), "2.0.0");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ComponentVersionDto>(ok.Value);
        Assert.Equal("2.0.0", dto.Version);
        Assert.Equal(ComponentStatus.Draft.ToString(), dto.Status);
    }

    [Fact]
    public void GetComponent_UnknownVersion_Returns404()
    {
        var repo = new InMemoryComponentPublishRepository();
        var controller = CreateController(repo);

        var result = controller.GetComponent(ComponentId.ToString(), "9.9.9");

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task PublishComponent_SetsDraftStatus_AndMetadata()
    {
        var repo = new InMemoryComponentPublishRepository();
        var controller = CreateController(repo, new[] { ComponentPublishService.DeveloperRole });

        var result = await controller.PublishComponent(ComponentId.ToString(),
            new PublishComponentRequest
            {
                Version = "1.0.0",
                JsonDefinition = "{}",
                DisplayName = "SAP Login",
                Description = "Authenticate to SAP GUI",
                Author = "system",
                Category = "SAP",
            }, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ComponentVersionDto>(ok.Value);
        Assert.Equal(ComponentStatus.Draft.ToString(), dto.Status);
        Assert.Equal("SAP Login", dto.DisplayName);
        Assert.Equal("SAP", dto.Category);
    }

    [Fact]
    public async Task PublishComponent_WithoutDeveloperRole_Throws()
    {
        var repo = new InMemoryComponentPublishRepository();
        var controller = CreateController(repo, new[] { "Viewer" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.PublishComponent(ComponentId.ToString(),
                new PublishComponentRequest { Version = "1.0.0", JsonDefinition = "{}" }, default));
    }

    [Fact]
    public async Task ApproveComponent_SetsPublishedStatus()
    {
        var repo = new InMemoryComponentPublishRepository();
        var developer = CreateController(repo, new[] { ComponentPublishService.DeveloperRole });
        await developer.PublishComponent(ComponentId.ToString(),
            new PublishComponentRequest { Version = "1.0.0", JsonDefinition = "{}" }, default);

        var approver = CreateController(repo, new[] { ComponentPublishService.ApproverRole });
        var result = await approver.ApproveComponent(ComponentId.ToString(), "1.0.0", default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ComponentVersionDto>(ok.Value);
        Assert.Equal(ComponentStatus.Published.ToString(), dto.Status);
    }

    [Fact]
    public async Task ApproveComponent_WithoutApproverRole_Throws()
    {
        var repo = new InMemoryComponentPublishRepository();
        var developer = CreateController(repo, new[] { ComponentPublishService.DeveloperRole });
        await developer.PublishComponent(ComponentId.ToString(),
            new PublishComponentRequest { Version = "1.0.0", JsonDefinition = "{}" }, default);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            developer.ApproveComponent(ComponentId.ToString(), "1.0.0", default));
    }

    [Fact]
    public void PublishAndApprove_HaveExpectedRoleAuthorizeAttributes()
    {
        var publishMethod = typeof(ComponentsController).GetMethod(nameof(ComponentsController.PublishComponent))!;
        var approveMethod = typeof(ComponentsController).GetMethod(nameof(ComponentsController.ApproveComponent))!;

        var publishAuth = (Microsoft.AspNetCore.Authorization.AuthorizeAttribute)
            publishMethod.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false).Single();
        var approveAuth = (Microsoft.AspNetCore.Authorization.AuthorizeAttribute)
            approveMethod.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false).Single();

        Assert.Equal(ComponentPublishService.DeveloperRole, publishAuth.Roles);
        Assert.Equal(ComponentPublishService.ApproverRole, approveAuth.Roles);
    }
}

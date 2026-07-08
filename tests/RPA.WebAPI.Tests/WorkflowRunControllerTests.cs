namespace RPA.WebAPI.Tests;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.WebAPI.Controllers;

public class WorkflowRunControllerTests
{
    [Fact]
    public async Task Run_InvalidWorkflowId_Returns400()
    {
        var controller = new WorkflowRunController(null!);

        var result = await controller.Run("not-a-guid", new WorkflowRunRequest(), default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public void Controller_IsAuthorized()
    {
        var auth = typeof(WorkflowRunController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .SingleOrDefault();

        Assert.NotNull(auth);
    }
}

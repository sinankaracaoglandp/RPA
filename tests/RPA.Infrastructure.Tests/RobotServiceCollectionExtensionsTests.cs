namespace RPA.Infrastructure.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Robots;

/// <summary>Task 3.1 — Robot servisi DI kaydı testleri.</summary>
public class RobotServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRobotServices_RegistersRepositoryAndService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<RpaDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        var result = services.AddRobotServices();

        Assert.Same(services, result);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<EfRobotRepository>(scope.ServiceProvider.GetRequiredService<IRobotRepository>());
        Assert.IsType<RobotService>(scope.ServiceProvider.GetRequiredService<IRobotService>());
    }
}

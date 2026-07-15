namespace RPA.WebAPI.Tests;

using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RPA.Infrastructure.Authentication;
using RPA.Infrastructure.Workflow.Expressions;
using Xunit;

/// <summary>
/// Task 6 — GET /api/expression/functions (FunctionRegistry.Catalog) uç noktası testleri.
/// </summary>
public class ExpressionControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ExpressionControllerTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private string GenerateToken()
    {
        using var scope = _factory.Services.CreateScope();
        var opts = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationOptions>>();
        return new JwtTokenService(opts).GenerateToken("studio-user", new[] { "Designer" });
    }

    private HttpClient AuthedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateToken());
        return client;
    }

    [Fact]
    public async Task GetFunctions_ReturnsCatalog_WithCategoriesAndSignatures()
    {
        var client = AuthedClient();

        var functions = await client.GetFromJsonAsync<List<ExpressionFunctionInfo>>("/api/expression/functions");

        Assert.NotNull(functions);
        Assert.Contains(functions!, f => f.Name == "Format" && f.Category == "Tarih");
        Assert.Contains(functions!, f => f.Name == "Upper" && f.Category == "Metin");
        Assert.Contains(functions!, f => f.Name == "ToInt" && f.Category == "Dönüşüm");
        var format = functions!.First(f => f.Name == "Format");
        Assert.Equal(3, format.Parameters.Count);
        Assert.True(format.Parameters[2].Optional); // kültür opsiyonel
    }
}

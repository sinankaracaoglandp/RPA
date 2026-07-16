## Task 6: Fonksiyon kataloğu API'si

**Files:**
- Create: `src/RPA.WebAPI/Controllers/ExpressionController.cs`
- Test: `tests/RPA.WebAPI.Tests/ExpressionControllerTests.cs`

**Interfaces:**
- Consumes: `FunctionRegistry.Catalog` (public `ExpressionFunctionInfo[]`).
- Produces: `GET /api/expression/functions` → `IReadOnlyList<ExpressionFunctionInfo>`.

- [ ] **Step 1: Controller testini yaz (FAIL)**

`ExpressionControllerTests.cs` (mevcut WebAPI test deseni — `WebApplicationFactory<Program>` + token; `UiSpyTests`/`ActivitiesController` testlerini örnek al):

```csharp
namespace RPA.WebAPI.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using RPA.Infrastructure.Workflow.Expressions;
using Xunit;

public class ExpressionControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ExpressionControllerTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task GetFunctions_ReturnsCatalog_WithCategoriesAndSignatures()
    {
        var client = _factory.CreateClient();
        // Mevcut testlerdeki token üretimini kullan (ör. GenerateToken()/AuthHelper). Bu projede
        // WebAPI testleri Authorization header ekliyor — aynı yardımcıyı kullan.
        AuthTestHelper.AddBearer(client);

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
```

> Not: `AuthTestHelper.AddBearer` yerine bu projedeki gerçek token yardımcısını kullan (mevcut `RobotHubTests`/`UiSpyTests` nasıl token ekliyorsa aynısı). Yetki gerekmiyorsa `[AllowAnonymous]` da düşünülebilir — ama tutarlılık için diğer controller'lar gibi `[Authorize]` + test token'ı kullan.

- [ ] **Step 2: Testi çalıştır (FAIL)**

Run: `dotnet test tests/RPA.WebAPI.Tests --filter FullyQualifiedName~Expression`
Expected: FAIL — endpoint yok.

- [ ] **Step 3: Controller'ı yaz**

`ExpressionController.cs` (`ActivitiesController` desenini izler):

```csharp
namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Infrastructure.Workflow.Expressions;

/// <summary>
/// İfade fonksiyon kataloğu uç noktası — Studio autocomplete'in okuduğu tek referans.
/// <see cref="FunctionRegistry.Catalog"/> metadata'sını sunar (frontend ExpressionFunctionService tüketir).
/// </summary>
[ApiController]
[Route("api/expression")]
[Authorize]
public class ExpressionController : ControllerBase
{
    /// <summary>Tüm ifade fonksiyonlarını (ad, kategori, imza, açıklama, örnek) listeler.</summary>
    [HttpGet("functions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ExpressionFunctionInfo>> GetFunctions() => Ok(FunctionRegistry.Catalog);
}
```

- [ ] **Step 4: Testi çalıştır (PASS)**

Run: `dotnet test tests/RPA.WebAPI.Tests --filter FullyQualifiedName~Expression`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.WebAPI/Controllers/ExpressionController.cs tests/RPA.WebAPI.Tests/ExpressionControllerTests.cs
git commit -m "feat(api): GET /api/expression/functions fonksiyon katalogu

FunctionRegistry.Catalog metadata (ad/kategori/imza/aciklama) — Studio autocomplete kaynagi.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---


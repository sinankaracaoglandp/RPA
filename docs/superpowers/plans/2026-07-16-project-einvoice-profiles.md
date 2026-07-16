# Project-Scoped E-Invoice Profiles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build project-scoped, versioned e-invoice mapping profiles whose dynamic object/list schemas automatically appear as typed variables in the workflow Designer.

**Architecture:** Persist profile drafts and immutable published versions in Domain/EF Core, expose project-scoped CRUD/publish APIs, and adapt the existing secure UBL parser into a profile-driven dynamic extraction engine. Add two profile activities and a Studio project tab; Designer resolves the pinned profile version's output schema and registers one object/list root variable without persisting sample XML.

**Tech Stack:** .NET 10, C#, EF Core/Npgsql, ASP.NET Core, Newtonsoft.Json, xUnit/Moq, Angular standalone components, TypeScript, Vitest.

## Global Constraints

- Follow `AGENTS.md`: TDD is mandatory (`FAIL → minimal implementation → PASS → commit`).
- Add an `AGENTS.md` contract-change entry before changing Domain entities or `WorkflowSchema.json`.
- Profiles are project-scoped; cross-project profile access must not disclose existence.
- Published versions are immutable and workflows pin `{ profileId, profileVersion }`.
- Output is object-based; `outputVariable` is the only workflow root introduced by a node.
- Profiles support root scalar fields and multiple root-level `list<object>` collections; recursive nested collections are out of scope.
- Sample XML is browser-memory-only and must never enter API payloads, persistence, workflow JSON, logs, or observer events.
- Existing `EInvoice.ReadUbl` and `EInvoice.ReadUblBatch` activities remain backward compatible.
- XML/paths/lists remain `Sensitive`; DTD, entity resolution, size/depth limits and regex timeouts remain enforced.
- Folder mode defaults to `*.xml` and `includeSubfolders=false`.

---

### Task 1: Contract Record and Domain Profile Model

**Files:**
- Modify: `AGENTS.md`
- Modify: `src/RPA.Domain/Entities/Project.cs`
- Create: `src/RPA.Domain/Entities/EInvoiceProfile.cs`
- Create: `src/RPA.Domain/Entities/EInvoiceProfileVersion.cs`
- Test: `tests/RPA.Domain.Tests/EInvoiceProfileTests.cs`

**Interfaces:**
- Produces: `EInvoiceProfile`, `EInvoiceProfileVersion`, project navigation `ICollection<EInvoiceProfile> EInvoiceProfiles`.
- Version numbers are positive integers; published definition/schema snapshots are non-empty JSON strings.

- [ ] **Step 1: Write failing Domain tests**

```csharp
[Fact]
public void Profile_BelongsToProject_AndStartsWithoutVersions()
{
    var projectId = Guid.NewGuid();
    var profile = new EInvoiceProfile { ProjectId = projectId, Name = "Satış Faturası" };
    Assert.Equal(projectId, profile.ProjectId);
    Assert.Empty(profile.Versions);
}

[Fact]
public void PublishedVersion_CarriesImmutableSnapshotFields()
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
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test tests/RPA.Domain.Tests --filter FullyQualifiedName~EInvoiceProfileTests -m:1`  
Expected: FAIL because profile entity types do not exist.

- [ ] **Step 3: Add the contract entry and minimal entities**

```csharp
public sealed class EInvoiceProfile : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DraftDefinitionJson { get; set; } = "{\"fields\":[],\"collections\":[]}";
    public Project? Project { get; set; }
    public ICollection<EInvoiceProfileVersion> Versions { get; } = new List<EInvoiceProfileVersion>();
}

public sealed class EInvoiceProfileVersion : BaseEntity
{
    public Guid ProfileId { get; set; }
    public int Version { get; set; }
    public string DefinitionJson { get; set; } = string.Empty;
    public string OutputSchemaJson { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public Guid? PublishedBy { get; set; }
    public EInvoiceProfile? Profile { get; set; }
}
```

- [ ] **Step 4: Run GREEN**

Run: `dotnet test tests/RPA.Domain.Tests --filter FullyQualifiedName~EInvoiceProfileTests -m:1`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AGENTS.md src/RPA.Domain/Entities/Project.cs src/RPA.Domain/Entities/EInvoiceProfile.cs src/RPA.Domain/Entities/EInvoiceProfileVersion.cs tests/RPA.Domain.Tests/EInvoiceProfileTests.cs
git commit -m "feat(domain): e-fatura profil kontratı"
```

### Task 2: Profile Definition Schema and Validation

**Files:**
- Create: `src/RPA.Application/EInvoiceProfiles/EInvoiceProfileDefinition.cs`
- Create: `src/RPA.Application/EInvoiceProfiles/EInvoiceProfileDefinitionValidator.cs`
- Test: `tests/RPA.Application.Tests/EInvoiceProfiles/EInvoiceProfileDefinitionValidatorTests.cs`

**Interfaces:**
- Produces: `EInvoiceProfileDefinition`, `EInvoiceFieldDefinition`, `EInvoiceCollectionDefinition` and `ValidateAndBuildSchema(string definitionJson) -> string outputSchemaJson`.
- Consumes existing mapping sources/types: `Standard | XPath | InvoiceNotes | LineNotes` and `string | integer | decimal | date | boolean`.

- [ ] **Step 1: Write failing validation tests**

```csharp
[Fact]
public void Validator_BuildsObjectSchema_WithTypedCollectionItems()
{
    var json = """{"fields":[{"name":"faturaNo","source":"XPath","valueXPath":"/Invoice/ID","type":"string"}],"collections":[{"name":"satirlar","scopeXPath":"/Invoice/InvoiceLine","fields":[{"name":"Miktar","source":"XPath","valueXPath":"./Quantity","type":"decimal"}]}]}""";
    var schema = JObject.Parse(new EInvoiceProfileDefinitionValidator().ValidateAndBuildSchema(json));
    Assert.Equal("object", (string?)schema["type"]);
    Assert.Equal("array", (string?)schema["properties"]?["satirlar"]?["type"]);
    Assert.Equal("number", (string?)schema["properties"]?["satirlar"]?["items"]?["properties"]?["Miktar"]?["type"]);
}

[Theory]
[InlineData("FaturaNo", "faturano")]
[InlineData("satirlar", "SATIRLAR")]
public void Validator_RejectsCaseInsensitiveDuplicateNames(string first, string second)
{
    var json = $$"""{"fields":[{"name":"{{first}}","source":"XPath","valueXPath":"/Invoice/ID","type":"string"},{"name":"{{second}}","source":"XPath","valueXPath":"/Invoice/IssueDate","type":"date"}],"collections":[]}""";
    Assert.Throws<BusinessException>(() => new EInvoiceProfileDefinitionValidator().ValidateAndBuildSchema(json));
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test tests/RPA.Application.Tests --filter FullyQualifiedName~EInvoiceProfileDefinitionValidatorTests -m:1`  
Expected: FAIL because validator is absent.

- [ ] **Step 3: Implement minimal definition records and validator**

```csharp
public sealed record EInvoiceProfileDefinition(
    IReadOnlyList<EInvoiceFieldDefinition> Fields,
    IReadOnlyList<EInvoiceCollectionDefinition> Collections);
public sealed record EInvoiceCollectionDefinition(
    string Name, string ScopeXPath, IReadOnlyList<EInvoiceFieldDefinition> Fields);
public sealed record EInvoiceFieldDefinition(
    string Name, string Source, string? ValueXPath, string? Regex, string? Group,
    string Type, bool Required, bool Multiple);
```

Validator rules: non-empty valid identifiers; case-insensitive uniqueness across root fields/collections and inside each collection; non-empty collection `scopeXPath`; XPath fields require `valueXPath`; supported sources/types only; no nested collections; generate JSON Schema object properties with `required` arrays.

- [ ] **Step 4: Run GREEN and layer regression**

Run: `dotnet test tests/RPA.Application.Tests -m:1`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Application/EInvoiceProfiles tests/RPA.Application.Tests/EInvoiceProfiles
git commit -m "feat(application): e-fatura profil şeması doğrulama"
```

### Task 3: EF Persistence and Versioned Profile Service

**Files:**
- Modify: `src/RPA.Infrastructure/Persistence/RpaDbContext.cs`
- Create: `src/RPA.Infrastructure/Persistence/Migrations/202607160001_AddEInvoiceProfiles.cs`
- Create: `src/RPA.Infrastructure/Services/EInvoiceProfileService.cs`
- Test: `tests/RPA.Infrastructure.Tests/Persistence/EInvoiceProfilePersistenceTests.cs`
- Test: `tests/RPA.Infrastructure.Tests/Services/EInvoiceProfileServiceTests.cs`

**Interfaces:**
- Produces service methods: `ListAsync(projectId)`, `CreateAsync(projectId, name, description)`, `GetAsync(projectId, profileId)`, `SaveDraftAsync(...)`, `PublishAsync(...)`, `ListVersionsAsync(...)`, `GetVersionAsync(...)`, `DeleteAsync(...)`.
- `PublishAsync` calls Task 2 validator and assigns `max(existing.Version)+1` transactionally.

- [ ] **Step 1: Write failing persistence/service tests**

```csharp
[Fact]
public async Task Publish_CreatesImmutableIncrementingSnapshots()
{
    var profile = await service.CreateAsync(project.Id, "Satış", null, default);
    await service.SaveDraftAsync(project.Id, profile.Id, Definition("faturaNo"), default);
    var v1 = await service.PublishAsync(project.Id, profile.Id, publisherId, default);
    await service.SaveDraftAsync(project.Id, profile.Id, Definition("belgeNo"), default);
    var v2 = await service.PublishAsync(project.Id, profile.Id, publisherId, default);
    Assert.Equal((1, 2), (v1.Version, v2.Version));
    Assert.Contains("faturaNo", v1.DefinitionJson);
}

[Fact]
public async Task Get_FromAnotherProject_DoesNotRevealProfile() =>
    await Assert.ThrowsAsync<BusinessException>(() => service.GetAsync(otherProjectId, profile.Id, default));
```

- [ ] **Step 2: Run RED**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter "FullyQualifiedName~EInvoiceProfilePersistenceTests|FullyQualifiedName~EInvoiceProfileServiceTests" -m:1`  
Expected: FAIL because DbSets/service are absent.

- [ ] **Step 3: Configure EF and implement minimal service**

Add unique indexes `(ProjectId, Name)` and `(ProfileId, Version)`, cascade profile versions, soft-delete filtering through service queries, max lengths, and PostgreSQL migration. Publish must copy draft and generated schema into a new version row; never update an existing version.

- [ ] **Step 4: Run GREEN**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter "FullyQualifiedName~EInvoiceProfile" -m:1`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Infrastructure/Persistence src/RPA.Infrastructure/Services/EInvoiceProfileService.cs tests/RPA.Infrastructure.Tests/Persistence/EInvoiceProfilePersistenceTests.cs tests/RPA.Infrastructure.Tests/Services/EInvoiceProfileServiceTests.cs
git commit -m "feat(infrastructure): sürümlü e-fatura profilleri"
```

### Task 4: Project-Scoped Profile Web API

**Files:**
- Create: `src/RPA.WebAPI/Controllers/EInvoiceProfilesController.cs`
- Modify: `src/RPA.WebAPI/Program.cs`
- Test: `tests/RPA.WebAPI.Tests/EInvoiceProfilesControllerTests.cs`

**Interfaces:**
- Produces the eight routes specified in the design spec under `/api/projects/{projectId}/einvoice-profiles`.
- DTOs never contain sample XML; published version DTO includes `definitionJson` and `outputSchemaJson`.

- [ ] **Step 1: Write failing controller tests**

```csharp
[Fact]
public async Task Publish_ReturnsVersionAndSchema_WithoutSampleXml()
{
    var response = await client.PostAsync($"/api/projects/{projectId}/einvoice-profiles/{profileId}/publish", null);
    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("outputSchemaJson", body);
    Assert.DoesNotContain("<Invoice", body);
}

[Fact]
public async Task CrossProjectGet_ReturnsNotFound()
{
    var profile = await CreateProfileAsync(projectA.Id, "Satış");
    var response = await client.GetAsync($"/api/projects/{projectB.Id}/einvoice-profiles/{profile.Id}");
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test tests/RPA.WebAPI.Tests --filter FullyQualifiedName~EInvoiceProfilesControllerTests -m:1`  
Expected: FAIL with 404 because controller is absent.

- [ ] **Step 3: Implement controller and DI registration**

Map validation errors to `400`, missing/cross-project profiles to `404`, and use authenticated user ID for `PublishedBy` when available. Accept only name/description/definition JSON request fields.

- [ ] **Step 4: Run GREEN**

Run: `dotnet test tests/RPA.WebAPI.Tests --filter FullyQualifiedName~EInvoiceProfilesControllerTests -m:1`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.WebAPI/Controllers/EInvoiceProfilesController.cs src/RPA.WebAPI/Program.cs tests/RPA.WebAPI.Tests/EInvoiceProfilesControllerTests.cs
git commit -m "feat(webapi): proje e-fatura profili uçları"
```

### Task 5: Dynamic Profile Extraction Engine

**Files:**
- Create: `src/RPA.Infrastructure/Workflow/Activities/EInvoice/EInvoiceProfileExtractor.cs`
- Modify: `src/RPA.Infrastructure/Workflow/Activities/EInvoice/UblInvoiceParser.cs`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/EInvoice/EInvoiceProfileExtractorTests.cs`

**Interfaces:**
- Produces: `Extract(string xml, EInvoiceProfileDefinition definition) -> Dictionary<string, object?>`.
- Reuses parser XML security settings and field-rule evaluation; collection fields evaluate relative to each `scopeXPath` node.

- [ ] **Step 1: Write failing scalar/multi-collection tests**

```csharp
[Fact]
public void Extract_BuildsDynamicRootAndMultipleCollections()
{
    var result = extractor.Extract(UblWithLinesAndTaxes, DefinitionWithLinesAndTaxes);
    Assert.Equal("FTR-1", result["faturaNo"]);
    var lines = Assert.IsType<List<Dictionary<string, object?>>>(result["satirlar"]);
    Assert.Equal("M-01", lines[0]["MalzemeKodu"]);
    Assert.IsType<decimal>(lines[0]["Miktar"]);
    Assert.Single(Assert.IsType<List<Dictionary<string, object?>>>(result["vergiler"]));
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~EInvoiceProfileExtractorTests -m:1`  
Expected: FAIL because extractor is absent.

- [ ] **Step 3: Extract reusable secure parsing/rule helpers and implement engine**

Do not duplicate XML reader settings or regex handling. Required missing scalar/collection child fields throw safe `InvoiceParseException` containing only rule names; optional missing values are omitted/null according to schema. Return dictionaries with `StringComparer.OrdinalIgnoreCase`.

- [ ] **Step 4: Run GREEN plus old parser regression**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter "FullyQualifiedName~EInvoiceProfileExtractorTests|FullyQualifiedName~UblInvoiceParserTests" -m:1`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Infrastructure/Workflow/Activities/EInvoice tests/RPA.Infrastructure.Tests/Workflow/EInvoice/EInvoiceProfileExtractorTests.cs
git commit -m "feat(einvoice): dinamik profil çıkarımı"
```

### Task 6: Profile Activities, Folder Source, Registry and Workflow Contract

**Files:**
- Modify: `src/RPA.Domain/WorkflowSchema.json`
- Modify: `src/RPA.Infrastructure/Workflow/ActivityRegistry.cs`
- Modify: `src/RPA.Infrastructure/Workflow/WorkflowServiceCollectionExtensions.cs`
- Modify: `src/RPA.Infrastructure/Workflow/WorkflowValidator.cs`
- Create: `src/RPA.Infrastructure/Workflow/Activities/EInvoice/ReadProfileActivities.cs`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/EInvoice/ReadProfileActivityTests.cs`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/WorkflowSchemaValidationTests.cs`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/ActivityRegistryCoverageTests.cs`

**Interfaces:**
- Produces activities `EInvoice.ReadProfile`, `EInvoice.ReadProfileBatch`.
- Consumes Task 3 version service and Task 5 extractor.
- Single output variable contains an object; batch output variable contains ordered objects/results.

- [ ] **Step 1: Write failing activity/schema tests**

```csharp
[Fact]
public async Task ReadProfile_PinsVersion_AndSetsRequestedRootVariable()
{
    context.SetVariable("profileId", profileId);
    context.SetVariable("profileVersion", 1);
    context.SetVariable("sourceMode", "XmlContent");
    context.SetVariable("xmlContent", Xml);
    context.SetVariable("outputVariable", "fatura");
    await activity.ExecuteAsync(context);
    Assert.Equal("FTR-1", Assert.IsType<Dictionary<string, object?>>(context.GetVariable("fatura"))["faturaNo"]);
}

[Fact]
public async Task BatchFolder_DefaultsToTopDirectoryXmlFilesInStableOrder()
{
    using var folder = new TemporaryDirectory();
    await File.WriteAllTextAsync(Path.Combine(folder.Path, "b.xml"), Invoice("B"));
    await File.WriteAllTextAsync(Path.Combine(folder.Path, "a.xml"), Invoice("A"));
    await File.WriteAllTextAsync(Path.Combine(folder.Path, "skip.txt"), Invoice("X"));
    Directory.CreateDirectory(Path.Combine(folder.Path, "nested"));
    await File.WriteAllTextAsync(Path.Combine(folder.Path, "nested", "c.xml"), Invoice("C"));
    var result = await ExecuteFolderBatch(folder.Path, includeSubfolders: false);
    Assert.Equal(new[] { "A", "B" }, result.Select(x => x["faturaNo"]));
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter "FullyQualifiedName~ReadProfileActivityTests|FullyQualifiedName~WorkflowSchemaValidationTests|FullyQualifiedName~ActivityRegistryCoverageTests" -m:1`  
Expected: FAIL because activities/schema entries are absent.

- [ ] **Step 3: Implement activities and contract**

Single exact-one source by `sourceMode`; batch modes `Folder | FilePaths | XmlContents`. Folder uses `Directory.EnumerateFiles(folderPath, fileFilter, includeSubfolders ? AllDirectories : TopDirectoryOnly).OrderBy(StringComparer.OrdinalIgnoreCase)`. Validate project/profile/version through workflow project context or a required `projectId` runtime argument; never accept an arbitrary cross-project version. Mark every source input `Sensitive`.

- [ ] **Step 4: Run GREEN and runner integration**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter "FullyQualifiedName~ReadProfile|FullyQualifiedName~EInvoice|FullyQualifiedName~WorkflowSchemaValidationTests|FullyQualifiedName~ActivityRegistryCoverageTests" -m:1`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Domain/WorkflowSchema.json src/RPA.Infrastructure/Workflow tests/RPA.Infrastructure.Tests/Workflow
git commit -m "feat(workflow): profil tabanlı e-fatura aktiviteleri"
```

### Task 7: Studio Profile API Models and Project Tab

**Files:**
- Modify: `src/RPA.Studio/src/app/app.routes.ts`
- Modify: `src/RPA.Studio/src/app/shared/services/project.service.ts`
- Create: `src/RPA.Studio/src/app/shared/services/einvoice-profile.service.ts`
- Create: `src/RPA.Studio/src/app/shared/models/einvoice-profile.model.ts`
- Create: `src/RPA.Studio/src/app/studio/projects/einvoice-profiles/einvoice-profiles.component.ts`
- Create: `src/RPA.Studio/src/app/studio/projects/einvoice-profiles/einvoice-profiles.component.html`
- Create: `src/RPA.Studio/src/app/studio/projects/einvoice-profiles/einvoice-profiles.component.scss`
- Test: `src/RPA.Studio/src/app/studio/projects/einvoice-profiles/einvoice-profiles.component.spec.ts`

**Interfaces:**
- Produces route `/projects/:projectId/einvoice-profiles` and typed service methods matching Task 4.

- [ ] **Step 1: Write failing route/list/publish tests**

```typescript
it('lists only profiles for the route project and opens a draft', () => {
  // navigate with projectId, expect scoped GET, render profile rows, click edit
});

it('publishes draft and refreshes immutable versions', () => {
  // click publish, expect scoped POST, show v2 and preserve v1
});
```

- [ ] **Step 2: Run RED**

Run: `npm test -- --watch=false --include=**/einvoice-profiles.component.spec.ts`  
Expected: FAIL because component/service/route are absent.

- [ ] **Step 3: Implement project tab shell and API service**

Show name, description, latest published version, draft status, edit, versions and publish actions. Do not add a global library route. Add navigation from the selected project UI to this route.

- [ ] **Step 4: Run GREEN**

Run: `npm test -- --watch=false --include=**/einvoice-profiles.component.spec.ts`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/app.routes.ts src/RPA.Studio/src/app/shared src/RPA.Studio/src/app/studio/projects
git commit -m "feat(studio): proje e-fatura profilleri sekmesi"
```

### Task 8: Profile Editor with Dynamic Collections

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping.model.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.html`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.scss`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts`
- Modify: `src/RPA.Studio/src/app/studio/projects/einvoice-profiles/einvoice-profiles.component.ts`

**Interfaces:**
- Produces `EInvoiceProfileDefinition` JSON compatible with Task 2.
- Emits definitions only; sample `XMLDocument` remains private component state.

- [ ] **Step 1: Write failing collection-editor tests**

```typescript
it('creates a collection from a repeated XML scope and relative child mappings', () => {
  component.loadSampleXml(UBL_WITH_TWO_LINES);
  component.addCollection('satirlar', '/Invoice/cac:InvoiceLine');
  component.addCollectionField('satirlar', { name: 'MalzemeKodu', source: 'XPath', valueXPath: './cac:Item/cbc:ID', type: 'string', required: true, multiple: false });
  expect(component.previewDefinition().satirlar).toHaveLength(2);
});

it('never emits sample XML while saving a draft', () => expect(JSON.stringify(emitted)).not.toContain('<Invoice'));
```

- [ ] **Step 2: Run RED**

Run: `npm test -- --watch=false --include=**/einvoice-mapping-editor.component.spec.ts`  
Expected: FAIL because collection APIs are absent.

- [ ] **Step 3: Extend editor minimally**

Keep the existing XML tree and regex worker. Add root fields/collections tabs, scope selection, relative child rule editor, collection sample row preview, identifier/duplicate validation, and definition JSON emission. Integrate the component into the profile draft screen rather than inline node properties.

- [ ] **Step 4: Run GREEN and build**

Run: `npm test -- --watch=false --include=**/{einvoice-mapping-editor,einvoice-profiles}.component.spec.ts`  
Expected: PASS.  
Run: `npm run build`  
Expected: exit 0 (existing budget/CommonJS warnings allowed).

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping* src/RPA.Studio/src/app/studio/projects/einvoice-profiles
git commit -m "feat(studio): dinamik e-fatura profil editörü"
```

### Task 9: Designer Profile Nodes and Dynamic Variable Catalog

**Files:**
- Modify: `src/RPA.Studio/src/app/shared/models/activity.model.ts`
- Modify: `src/RPA.Studio/src/app/shared/models/workflow.model.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.html`
- Modify: `src/RPA.Studio/src/app/studio/designer/designer.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/variables/variables-panel.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/variables/variables-panel.component.html`
- Test: `src/RPA.Studio/src/app/studio/designer/variables/variables-panel.component.spec.ts`
- Test: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.spec.ts`
- Test: `src/RPA.Studio/src/app/studio/designer/designer.component.spec.ts`

**Interfaces:**
- Consumes Task 4 profile/version API and `OutputSchemaJson`.
- Produces schema-aware `WorkflowVariable` with optional nested `schema` property; root type is `object` or `list<object>`.

- [ ] **Step 1: Write failing profile selection/variable tests**

```typescript
it('registers pinned profile schema under the requested object root', () => {
  selectProfile('profile-1', 2, 'fatura');
  expect(component.variables).toContainEqual(expect.objectContaining({
    name: 'fatura', type: 'object', schema: expect.objectContaining({ properties: expect.any(Object) }),
  }));
});

it('offers collection item fields inside foreach', () => {
  expect(variablePathsFor('fatura.satirlar')).toContain('satir.MalzemeKodu');
});
```

- [ ] **Step 2: Run RED**

Run: `npm test -- --watch=false --include=**/{generic-property,designer}.component.spec.ts`  
Expected: FAIL because schema-aware variables/profile picker are absent.

- [ ] **Step 3: Implement picker and derived variable catalog**

For profile activities, render project profile selector, version selector, source-mode-specific inputs and `outputVariable`. Derive the variable catalog from selected version schema every time node properties change or workflow loads; do not duplicate every child as a flat workflow variable. Show a non-mutating “new version available” warning.

- [ ] **Step 4: Run GREEN and Studio regression**

Run: `npm test -- --watch=false`  
Expected: all Studio tests PASS.  
Run: `npm run build`  
Expected: exit 0.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/shared/models src/RPA.Studio/src/app/studio/designer
git commit -m "feat(studio): profil şemasını değişken kataloğuna bağla"
```

### Task 10: End-to-End Profile Workflow and Security Regression

**Files:**
- Modify: `tests/RPA.Infrastructure.Tests/BaseRunnerTests.cs`
- Modify: `tests/RPA.Infrastructure.Tests/Workflow/EInvoice/ReadProfileActivityTests.cs`
- Modify: `tests/RPA.WebAPI.Tests/EInvoiceProfilesControllerTests.cs`
- Modify: `src/RPA.Studio/src/app/studio/designer/designer.component.spec.ts`

**Interfaces:**
- Verifies the complete contract from published profile to runtime object and Designer variable path.

- [ ] **Step 1: Write failing end-to-end tests**

```csharp
[Fact]
public async Task PublishedProfile_XmlList_ForEach_ExposesDynamicLineFields()
{
    // publish profile with satirlar.MalzemeKodu, run ReadProfileBatch from XML list,
    // ForEach faturalar then nested satirlar, record item code, assert ordered codes.
}

[Theory]
[InlineData("<Invoice><ID>SECRET-XML</ID></Invoice>")]
public async Task ProfileNodes_NeverExposeXmlToObserverOrLogs(string xml)
{
    var observer = new RecordingObserver();
    await RunProfileWorkflow(xml, observer);
    Assert.All(observer.Events, evt => Assert.DoesNotContain("SECRET-XML", JsonConvert.SerializeObject(evt)));
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter "FullyQualifiedName~PublishedProfile|FullyQualifiedName~ProfileNodes" -m:1`  
Expected: FAIL until all cross-layer wiring is complete.

- [ ] **Step 3: Make only integration wiring corrections**

Register missing DI services/metadata and correct serialization boundaries; do not add new feature behavior. Ensure profile/version lookup occurs before parsing and source data remains sensitive.

- [ ] **Step 4: Run complete verification**

Run: `dotnet test RPA.sln -m:1 -nodeReuse:false`  
Expected: all .NET tests PASS.  
Run: `npm test -- --watch=false` in `src/RPA.Studio`  
Expected: all Studio tests PASS.  
Run: `npm run build` in `src/RPA.Studio`  
Expected: exit 0.  
Run: `git diff --check`  
Expected: no whitespace errors.

- [ ] **Step 5: Commit**

```bash
git add tests src/RPA.Infrastructure src/RPA.WebAPI src/RPA.Studio
git commit -m "test(einvoice): profil tabanlı uçtan uca akış"
```

## Review Gates

- Tasks 1 and 6 change the contract: high-effort review and explicit `AGENTS.md` impact audit.
- Tasks 3–6 touch persistence/runtime/security: high-effort code review plus security review.
- Tasks 7–9 touch Studio/Designer: medium review; Task 9 gets high review because it changes variable semantics.
- Task 10 requires final whole-branch review from the merge base and verification-before-completion evidence.

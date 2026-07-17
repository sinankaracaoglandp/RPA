### Task 1: Contract package and licensing models

**Files:**
- Modify: `AGENTS.md`
- Create: `src/RPA.Domain/Enums/AgentIdentityStatus.cs`
- Create: `src/RPA.Domain/Entities/LicenseInstallation.cs`
- Create: `src/RPA.Domain/Entities/AgentIdentity.cs`
- Create: `src/RPA.Domain/Entities/AgentActivation.cs`
- Create: `src/RPA.Domain/Licensing/LicenseDocuments.cs`
- Create: `src/RPA.Domain/Interfaces/ILicenseService.cs`
- Create: `src/RPA.Domain/Interfaces/IAgentIdentityRepository.cs`
- Test: `tests/RPA.Domain.Tests/LicensingContractTests.cs`

**Interfaces:**
- Produces: `AgentIdentityStatus`, `OfflineLicensePayload`, `SignedLicenseDocument`, `InstallationRequestDocument`, `LicenseStatus`, `ILicenseService`, and `IAgentIdentityRepository`.

- [ ] **Step 1: Write the contract-change entry and failing contract tests**

Add a dated `Kontrat Değişikliği — 2026-07-16 (Offline Agent Licensing)` section naming Domain, Infrastructure, WebAPI, Agent, Studio, and LicenseGenerator. Write tests asserting these exact states and seat semantics:

```csharp
[Theory]
[InlineData(AgentIdentityStatus.PendingActivation, false)]
[InlineData(AgentIdentityStatus.Activated, true)]
[InlineData(AgentIdentityStatus.Disabled, true)]
[InlineData(AgentIdentityStatus.Deactivated, false)]
public void AgentIdentityStatus_ConsumesSeat_AsSpecified(AgentIdentityStatus status, bool expected)
    => Assert.Equal(expected, status.ConsumesSeat());

[Fact]
public void OfflineLicensePayload_RequiresStableIdentityFields()
{
    var payload = OfflineLicensePayload.Create("LIC-1", 1, "ACME", "install-1", "ABC", 5,
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2027-01-01T00:00:00Z"), ["Studio"]);
    Assert.Equal(5, payload.MaxActivatedAgents);
    Assert.Equal(1, payload.Revision);
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test tests/RPA.Domain.Tests/RPA.Domain.Tests.csproj --filter FullyQualifiedName~LicensingContractTests -v minimal`

Expected: compilation fails because licensing contracts do not exist.

- [ ] **Step 3: Implement minimal contracts**

Use immutable records for transport documents and BaseEntity-derived EF entities. Implement:

```csharp
public enum AgentIdentityStatus { PendingActivation, Activated, Disabled, Deactivated }
public static bool ConsumesSeat(this AgentIdentityStatus value) =>
    value is AgentIdentityStatus.Activated or AgentIdentityStatus.Disabled;
```

Define `ILicenseService.GetStatusAsync`, `ExportInstallationRequestAsync`, `ImportAsync`, and `EnsureAgentCapacityAsync`. Define repository operations for create, lookup, list, activation, disable, deactivate, and credential rotation without exposing credential hashes to WebAPI DTOs.

- [ ] **Step 4: Run GREEN**

Run the filtered Domain test, then `dotnet test tests/RPA.Domain.Tests/RPA.Domain.Tests.csproj -v minimal`.

- [ ] **Step 5: Commit**

```bash
git add AGENTS.md src/RPA.Domain tests/RPA.Domain.Tests/LicensingContractTests.cs
git commit -m "feat(domain): offline lisans ve agent kimlik kontratlari"
```


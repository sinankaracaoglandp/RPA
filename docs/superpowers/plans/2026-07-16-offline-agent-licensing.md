# Offline Agent Licensing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver installation-bound offline licenses, activated-agent seat enforcement, secure agent enrollment/JWT authentication, controlled offline execution, Studio administration, and a separate vendor license generator.

**Architecture:** The runtime verifies canonical vendor-signed license documents against an installation identity whose private key is machine protected. WebAPI is the sole enforcement authority; it atomically controls activated seats and issues short-lived agent JWTs after credential verification. Agent clients share a refreshing token provider and a 15-minute connectivity lease, while Studio exposes license and agent management and a separate vendor tool signs licenses.

**Tech Stack:** .NET 10, C#, ASP.NET Core, EF Core/Npgsql, SignalR, RSA-PSS/SHA-256, PBKDF2, Windows DPAPI, Angular 22, Vitest.

## Global Constraints

- Follow failing test → minimal implementation → passing test → commit for every task.
- Never persist or log plaintext activation codes, agent credentials, private keys, or complete JWTs.
- WebAPI is the only license and seat enforcement authority.
- Agent JWT lifetime is 10 minutes; proactive renewal starts 2 minutes before expiry.
- Connectivity lease is 15 minutes; a running node may finish, but no next node starts after lease expiry.
- Activation codes expire after 15 minutes and are single-use.
- `Disabled` agents consume seats; `Deactivated` and `PendingActivation` agents do not.
- The first release is offline; hybrid validation remains an explicit documented backlog.

---

## File Structure

- `src/RPA.Domain/Licensing/`: immutable license payloads, installation requests, agent states, repository/service contracts.
- `src/RPA.Infrastructure/Licensing/`: canonical serialization, signature verification, installation identity protection, license service.
- `src/RPA.Infrastructure/Persistence/`: EF mappings, repositories, transaction-safe seat activation, migration.
- `src/RPA.WebAPI/Licensing/`: customer license endpoints and administrator agent endpoints.
- `src/RPA.WebAPI/Authentication/AgentAuthController.cs`: activation, token exchange, and lease renewal.
- `src/RPA.Agent/Authentication/`: protected credential store and refreshing access-token provider.
- `src/RPA.Agent/Connectivity/`: lease state, durable outbox boundary, and node-start gate.
- `src/RPA.Studio/src/app/orchestrator/licensing/`: license status/import/request UI.
- `src/RPA.Studio/src/app/orchestrator/agents/`: seat-aware agent administration UI.
- `tools/RPA.LicenseGenerator/`: vendor-only request import and signed license generation CLI.

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

### Task 2: Canonical signing and installation binding

**Files:**
- Create: `src/RPA.Infrastructure/Licensing/CanonicalLicenseSerializer.cs`
- Create: `src/RPA.Infrastructure/Licensing/VendorLicenseVerifier.cs`
- Create: `src/RPA.Infrastructure/Licensing/IInstallationKeyStore.cs`
- Create: `src/RPA.Infrastructure/Licensing/DpapiInstallationKeyStore.cs`
- Create: `src/RPA.Infrastructure/Licensing/InstallationIdentityService.cs`
- Test: `tests/RPA.Infrastructure.Tests/Licensing/LicenseCryptographyTests.cs`

**Interfaces:**
- Consumes: Task 1 documents.
- Produces: `CanonicalLicenseSerializer.SerializePayload`, `IVendorLicenseVerifier.Verify`, and `IInstallationIdentityService.GetOrCreateAsync`.

- [ ] **Step 1: Write failing cryptography tests**

Use a test RSA key pair. Assert identical payloads serialize to identical UTF-8 bytes regardless of input feature order; RSA-PSS/SHA-256 verification succeeds for an untouched payload and fails after changing `MaxActivatedAgents`, installation fingerprint, or signature.

- [ ] **Step 2: Run RED**

Run: `dotnet test tests/RPA.Infrastructure.Tests/RPA.Infrastructure.Tests.csproj --filter FullyQualifiedName~LicenseCryptographyTests -v minimal`

Expected: compilation failure for missing serializer/verifier.

- [ ] **Step 3: Implement canonical verification and key protection**

Canonical JSON must use fixed property order, UTF-8, invariant ISO-8601 UTC timestamps, ordinally sorted distinct feature codes, and no indentation. Verify with:

```csharp
RSA.VerifyData(canonicalBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)
```

`DpapiInstallationKeyStore` protects PKCS#8 private-key bytes with `ProtectedData.Protect(..., DataProtectionScope.LocalMachine)` and writes atomically under configured application data. Keep filesystem and protection APIs injectable so tests never depend on the host DPAPI store.

- [ ] **Step 4: Run GREEN and regression**

Run filtered tests, then the full Infrastructure test project.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Infrastructure/Licensing tests/RPA.Infrastructure.Tests/Licensing
git commit -m "feat(licensing): imzali lisans ve kurulum kimligi dogrulamasi"
```

### Task 3: Persistence and atomic seat enforcement

**Files:**
- Modify: `src/RPA.Infrastructure/Persistence/RpaDbContext.cs`
- Create: `src/RPA.Infrastructure/Persistence/Repositories/EfAgentIdentityRepository.cs`
- Create: `src/RPA.Infrastructure/Persistence/Repositories/EfLicenseInstallationRepository.cs`
- Create: `src/RPA.Infrastructure/Licensing/LicenseService.cs`
- Create: `src/RPA.Infrastructure/Migrations/*_OfflineAgentLicensing.cs`
- Test: `tests/RPA.Infrastructure.Tests/Licensing/AgentSeatEnforcementTests.cs`

**Interfaces:**
- Consumes: Task 1 contracts and Task 2 verifier.
- Produces: transaction-safe implementations used by WebAPI.

- [ ] **Step 1: Write failing repository/service tests**

Cover 0/1, 1/1, and concurrent final-seat activation; exactly one of two concurrent activation attempts may succeed. Cover `Disabled` retaining the seat and `Deactivated` releasing it. Assert activation hash is consumed once and plaintext is absent from tracked entities.

- [ ] **Step 2: Run RED**

Run the filtered Infrastructure tests and confirm missing repository/service failures.

- [ ] **Step 3: Implement mappings and transaction**

Add unique indexes for installation ID, `(LicenseInstallationId, MachineFingerprint)`, and activation-code lookup hash. Use an explicit transaction; lock the license installation row with PostgreSQL `FOR UPDATE`, recount consuming states, reject at capacity with `AGENT_LICENSE_LIMIT_REACHED`, transition state, mark activation consumed, and save the new credential hash atomically.

- [ ] **Step 4: Add migration and run GREEN**

Run:

```powershell
dotnet ef migrations add OfflineAgentLicensing --project src/RPA.Infrastructure --startup-project src/RPA.WebAPI
dotnet test tests/RPA.Infrastructure.Tests/RPA.Infrastructure.Tests.csproj --filter FullyQualifiedName~AgentSeatEnforcementTests -v minimal
```

Then run all Infrastructure tests.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Infrastructure/Persistence src/RPA.Infrastructure/Licensing/LicenseService.cs src/RPA.Infrastructure/Migrations tests/RPA.Infrastructure.Tests/Licensing
git commit -m "feat(persistence): agent koltuk kotasini atomik uygula"
```

### Task 4: License and agent-auth WebAPI

**Files:**
- Create: `src/RPA.WebAPI/Licensing/LicenseController.cs`
- Create: `src/RPA.WebAPI/Licensing/AgentsController.cs`
- Create: `src/RPA.WebAPI/Authentication/AgentAuthController.cs`
- Create: `src/RPA.Infrastructure/Authentication/AgentTokenService.cs`
- Modify: `src/RPA.WebAPI/Program.cs`
- Modify: `src/RPA.WebAPI/Hubs/StudioHub.cs`
- Modify: `src/RPA.WebAPI/Robots/RobotHub.cs`
- Test: `tests/RPA.WebAPI.Tests/OfflineLicenseApiTests.cs`
- Test: `tests/RPA.WebAPI.Tests/AgentAuthenticationTests.cs`
- Test: `tests/RPA.WebAPI.Tests/StudioHubAuthorizationTests.cs`

**Interfaces:**
- Produces the API surface and policies from the design; agent JWT claims are `agent_id`, `installation_id`, `client_type=agent`, and `token_use=access`.

- [ ] **Step 1: Write failing endpoint and hub-policy tests**

Assert license import rejects altered/wrong-installation documents; an administrator can create an activation code but a normal designer cannot; activation returns a credential once; token exchange returns a 10-minute access token; disabled/deactivated agents are rejected. Connect separate Studio and agent tokens and assert each cannot invoke the other policy's hub methods.

- [ ] **Step 2: Run RED**

Run the three filtered WebAPI test classes. Expected: 404/missing policy and missing type failures.

- [ ] **Step 3: Implement endpoints and policies**

Register policies:

```csharp
options.AddPolicy("LicenseAdministrator", p => p.RequireRole("Administrator"));
options.AddPolicy("StudioSpyUser", p => p.RequireRole("Designer", "Administrator"));
options.AddPolicy("AgentClient", p => p.RequireClaim("client_type", "agent"));
```

Keep controller DTOs secret-safe. Return the activation code and initial agent credential only from their creation responses. Decorate StudioHub methods with method-level policies and derive agent identity from claims.

- [ ] **Step 4: Run GREEN and WebAPI regression**

Run filtered tests and then all WebAPI tests.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.WebAPI src/RPA.Infrastructure/Authentication tests/RPA.WebAPI.Tests
git commit -m "feat(webapi): offline lisans ve agent token APIleri"
```

### Task 5: Agent credential store and shared token provider

**Files:**
- Modify: `src/RPA.Agent/Configuration/AgentOptions.cs`
- Create: `src/RPA.Agent/Authentication/IAgentCredentialStore.cs`
- Create: `src/RPA.Agent/Authentication/DpapiAgentCredentialStore.cs`
- Create: `src/RPA.Agent/Authentication/AgentAccessTokenProvider.cs`
- Create: `src/RPA.Agent/Authentication/AgentEnrollmentClient.cs`
- Modify: `src/RPA.Agent/Hub/RobotHubClient.cs`
- Modify: `src/RPA.Agent/UISpy/SpyHubCommandHostedService.cs`
- Modify: `src/RPA.Agent/UISpy/SapGuiSpyService.cs`
- Modify: `src/RPA.Agent/AgentServiceCollectionExtensions.cs`
- Test: `tests/RPA.Agent.Tests/Authentication/AgentAccessTokenProviderTests.cs`
- Test: `tests/RPA.Agent.Tests/UISpy/SpyHubAuthenticationTests.cs`

**Interfaces:**
- Produces `IAgentAccessTokenProvider.GetTokenAsync(CancellationToken)` shared by all SignalR clients.

- [ ] **Step 1: Write failing provider and SignalR configuration tests**

Assert concurrent calls perform one token request, cached tokens are reused outside the two-minute renewal window, expiring tokens refresh, failed refresh does not expose credentials, and both StudioHub connections plus RobotHub configure `AccessTokenProvider`.

- [ ] **Step 2: Run RED**

Run the two filtered Agent test classes; confirm missing provider/configuration failures.

- [ ] **Step 3: Implement minimal enrollment, storage, and refresh**

Use a semaphore to serialize refresh. Decode expiry only after successful API response; do not treat JWT claims as authorization decisions on the client. Configure every hub with:

```csharp
.WithUrl(hubUrl, o => o.AccessTokenProvider = () => tokenProvider.GetTokenAsync(CancellationToken.None))
```

Store the long-lived credential through DPAPI LocalMachine and never in `appsettings.json`.

- [ ] **Step 4: Run GREEN and Agent regression**

Run filtered tests and the complete Agent test project.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Agent tests/RPA.Agent.Tests
git commit -m "feat(agent): guvenli credential ve yenilenen hub tokeni"
```

### Task 6: Connectivity lease and safe node boundary

**Files:**
- Create: `src/RPA.Agent/Connectivity/ConnectivityLease.cs`
- Create: `src/RPA.Agent/Connectivity/AgentOutbox.cs`
- Create: `src/RPA.Domain/Interfaces/IExecutionContinuationGate.cs`
- Modify: `src/RPA.Infrastructure/Workflow/BaseRunner.cs`
- Modify: `src/RPA.Agent/Jobs/JobExecutor.cs`
- Test: `tests/RPA.Agent.Tests/Connectivity/ConnectivityLeaseTests.cs`
- Test: `tests/RPA.Infrastructure.Tests/BaseRunnerConnectivityGateTests.cs`

**Interfaces:**
- Produces `IExecutionContinuationGate.EnsureMayStartNodeAsync(Guid jobRunId, string nodeId, CancellationToken)`.

- [ ] **Step 1: Write failing lease and runner tests**

With a fake clock, assert 14:59 permits the next node and 15:00 blocks it. Assert disconnect does not cancel the current node, but the runner consults the gate before the next node. Assert outbox keys make repeated flushes idempotent and capacity overflow is explicit.

- [ ] **Step 2: Run RED**

Run both filtered test classes and confirm missing gate/lease behavior.

- [ ] **Step 3: Implement lease, gate, and bounded durable outbox**

Persist outbox records under configured agent data with atomic replacement. Represent suspension with a dedicated system-level exception/result that preserves job and next-node identity. Do not change activity public signatures.

- [ ] **Step 4: Run GREEN and regressions**

Run filtered tests, all Agent tests, and all Infrastructure tests.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Domain/Interfaces/IExecutionContinuationGate.cs src/RPA.Agent/Connectivity src/RPA.Agent/Jobs/JobExecutor.cs src/RPA.Infrastructure/Workflow/BaseRunner.cs tests
git commit -m "feat(agent): offline lease sonunda guvenli duraklatma"
```

### Task 7: Studio license administration

**Files:**
- Create: `src/RPA.Studio/src/app/orchestrator/licensing/license.models.ts`
- Create: `src/RPA.Studio/src/app/orchestrator/licensing/license.service.ts`
- Create: `src/RPA.Studio/src/app/orchestrator/licensing/license-page.component.ts`
- Create: `src/RPA.Studio/src/app/orchestrator/licensing/license-page.component.html`
- Create: `src/RPA.Studio/src/app/orchestrator/licensing/license-page.component.css`
- Create: `src/RPA.Studio/src/app/orchestrator/licensing/license-page.component.spec.ts`
- Modify: `src/RPA.Studio/src/app/app.routes.ts`
- Modify the existing Orchestrator navigation component discovered during implementation.

**Interfaces:**
- Consumes `GET /api/license`, installation-request export, and license import.

- [ ] **Step 1: Write failing component/service tests**

Assert display of customer, edition, validity, features, `used/max` seats; installation-request download; `.lic` upload; stable API error rendering; and absence of license/secret payloads from local/session storage.

- [ ] **Step 2: Run RED**

Run: `npm test -- --watch=false --include src/app/orchestrator/licensing/license-page.component.spec.ts`

Expected: missing component/service failures.

- [ ] **Step 3: Implement the standalone Angular page**

Add `/orchestrator/licensing`, use the existing authenticated HTTP client/interceptor, use a hidden file input restricted to `.lic,application/json`, and revoke object URLs after installation-request download.

- [ ] **Step 4: Run GREEN and Studio regression**

Run the filtered test, `npm test -- --watch=false`, and `npm run build`.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app
git commit -m "feat(studio): lisans yonetimi ve kurulum talebi ekrani"
```

### Task 8: Studio activated-agent management

**Files:**
- Create: `src/RPA.Studio/src/app/orchestrator/agents/agent-license.models.ts`
- Create: `src/RPA.Studio/src/app/orchestrator/agents/agent-license.service.ts`
- Modify: existing `src/RPA.Studio/src/app/orchestrator/robots/*` page files, or create a focused child component beside them.
- Test: `src/RPA.Studio/src/app/orchestrator/agents/agent-license-page.component.spec.ts`

**Interfaces:**
- Consumes agent list/create/activation-code/disable/deactivate/rotate endpoints.

- [ ] **Step 1: Write failing UI tests**

Assert state badges, seat usage, create pending agent, activation code shown exactly once, confirmation before disable/deactivate, disabled retaining a seat, deactivated releasing it after API refresh, and no secret written to browser storage.

- [ ] **Step 2: Run RED**

Run the focused Vitest file and confirm missing behavior.

- [ ] **Step 3: Implement minimal management UI**

Use modal state held only in component memory for activation codes. Clear it on close/navigation. Refresh authoritative seat status from WebAPI after every mutation.

- [ ] **Step 4: Run GREEN, all Studio tests, and build**

Run the focused test, full tests, and production build.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/orchestrator
git commit -m "feat(studio): aktive agent koltuk yonetimi"
```

### Task 9: Vendor license generator

**Files:**
- Create: `tools/RPA.LicenseGenerator/RPA.LicenseGenerator.csproj`
- Create: `tools/RPA.LicenseGenerator/Program.cs`
- Create: `tools/RPA.LicenseGenerator/LicenseGenerationService.cs`
- Create: `tools/RPA.LicenseGenerator/PrivateKeyLoader.cs`
- Create: `tests/RPA.LicenseGenerator.Tests/RPA.LicenseGenerator.Tests.csproj`
- Create: `tests/RPA.LicenseGenerator.Tests/LicenseGenerationTests.cs`
- Modify: `RPA.sln`

**Interfaces:**
- Consumes Task 1 documents and Task 2 canonical serializer.
- Produces a vendor-only CLI: `generate --request <file> --output <file> --key <file> --license-id ... --customer-id ... --max-agents N --expires YYYY-MM-DD --features A,B`.

- [ ] **Step 1: Write failing generator tests**

Assert invalid request rejection, `max-agents <= 0` rejection, expiry-before-issue rejection, private-key load failure without secret disclosure, valid signed output, and runtime verifier acceptance.

- [ ] **Step 2: Run RED**

Run the LicenseGenerator test project; expect missing project/types.

- [ ] **Step 3: Implement the minimal non-interactive CLI**

Use explicit arguments suitable for a secured vendor pipeline. Read the encrypted PEM/PKCS#8 private key from a supplied path and password from an environment variable named by an argument; never print either. Emit the signed document atomically.

- [ ] **Step 4: Run GREEN and solution build**

Run generator tests and `dotnet build RPA.sln -c Release`.

- [ ] **Step 5: Commit**

```bash
git add tools/RPA.LicenseGenerator tests/RPA.LicenseGenerator.Tests RPA.sln
git commit -m "feat(tools): offline musteri lisansi ureticisi"
```

### Task 10: End-to-end acceptance, documentation, and hybrid backlog

**Files:**
- Create: `tests/RPA.WebAPI.Tests/OfflineLicensingEndToEndTests.cs`
- Create: `docs/operations/offline-licensing.md`
- Create: `docs/backlog/hybrid-licensing.md`
- Modify: `docs/plans/2026-07-04-implementation.md`

**Interfaces:**
- Validates all prior tasks as one customer journey.

- [ ] **Step 1: Write failing end-to-end scenario**

Create an installation request, generate a test-vendor license, import it, activate exactly the licensed count, reject the next activation, obtain agent JWT, connect agent and Studio SignalR clients with separated permissions, simulate 15 minutes offline at a node boundary, deactivate one agent, and activate a replacement.

- [ ] **Step 2: Run RED and identify only integration gaps**

Run the single E2E test. Fix setup errors until it fails on an actual missing integration behavior; do not alter production code before that point.

- [ ] **Step 3: Implement only the missing wiring and write operations docs**

Document vendor issuance, customer import, server migration, agent activation, credential rotation, backup exclusions, and incident response. The hybrid backlog must retain these exact future items: central validation, signed revocation, duplicate-use detection, signed online lease, offline grace, transfer/deactivation, vendor audit, TPM hardening, privacy-preserving telemetry, and migration without agent re-enrollment.

- [ ] **Step 4: Run full verification**

Run:

```powershell
dotnet test RPA.sln -v minimal
dotnet build RPA.sln -c Release
Set-Location src/RPA.Studio
npm test -- --watch=false
npm run build
```

Expected: all commands exit 0 with no test failures.

- [ ] **Step 5: Security checks**

Run repository searches for credential/token logging and plaintext persistence, inspect generated migration constraints/indexes, and manually verify copied/modified license rejection. Record evidence in the operations document without recording secrets.

- [ ] **Step 6: Commit**

```bash
git add tests/RPA.WebAPI.Tests/OfflineLicensingEndToEndTests.cs docs src/RPA.WebAPI src/RPA.Agent src/RPA.Infrastructure
git commit -m "test(licensing): offline lisanslama kabul akisini dogrula"
```

## Final Review Gates

- Run `superpowers:verification-before-completion` and retain exact command output.
- Run `superpowers:requesting-code-review` with high effort because licensing, authentication, cryptography, and workflow execution are critical paths.
- Perform an additional security review covering key custody, canonicalization, replay, concurrency, authorization, secret storage, and offline bypass attempts.
- Use `superpowers:finishing-a-development-branch` only after all review findings are resolved and the worktree is clean.

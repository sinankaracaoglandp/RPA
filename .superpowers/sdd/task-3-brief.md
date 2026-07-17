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


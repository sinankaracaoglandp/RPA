# Task 3 Report — Persistence and atomic seat enforcement

## Outcome

Implemented EF Core persistence for license installations, agent identities, and one-time activations; repository CRUD/state transitions; license import/status/capacity service; migration; and atomic final-seat activation.

PostgreSQL activation starts an explicit transaction and locks the selected `LicenseInstallations` row with `FOR UPDATE` before recounting `Activated`/`Disabled` identities. Agent transition, activation consumption, and credential-hash persistence are committed together. Stable failures include `AGENT_LICENSE_LIMIT_REACHED`, `ACTIVATION_CODE_INVALID`, `ACTIVATION_CODE_EXPIRED`, `LICENSE_MISSING`, and `LICENSE_EXPIRED`.

SQLite cannot faithfully exercise PostgreSQL row-level locking. Focused concurrency tests therefore use a provider-specific process-local keyed semaphore only for non-Npgsql providers. The production Npgsql path always uses the database transaction plus row lock; the test seam does not replace or weaken it.

## Files

- `src/RPA.Infrastructure/Persistence/RpaDbContext.cs`
- `src/RPA.Infrastructure/Persistence/Repositories/EfAgentIdentityRepository.cs`
- `src/RPA.Infrastructure/Persistence/Repositories/EfLicenseInstallationRepository.cs`
- `src/RPA.Infrastructure/Licensing/LicenseService.cs`
- `src/RPA.Infrastructure/Licensing/LicenseDocumentJson.cs`
- `src/RPA.Infrastructure/Migrations/20260716084447_OfflineAgentLicensing.cs`
- `src/RPA.Infrastructure/Migrations/20260716084447_OfflineAgentLicensing.Designer.cs`
- `src/RPA.Infrastructure/Migrations/RpaDbContextModelSnapshot.cs`
- `tests/RPA.Infrastructure.Tests/Licensing/AgentSeatEnforcementTests.cs`

## TDD and verification evidence

- RED: filtered test failed with `CS0234` because `RPA.Infrastructure.Persistence.Repositories` did not exist.
- GREEN: `dotnet test tests/RPA.Infrastructure.Tests/RPA.Infrastructure.Tests.csproj --filter FullyQualifiedName~AgentSeatEnforcementTests -v minimal --disable-build-servers -m:1 -p:UseSharedCompilation=false --no-restore` — exit 0, 6 passed, 0 failed.
- Full Infrastructure: `dotnet test tests/RPA.Infrastructure.Tests/RPA.Infrastructure.Tests.csproj -v minimal --disable-build-servers -m:1 -p:UseSharedCompilation=false --no-restore` — exit 0, 674 passed, 0 failed.
- WebAPI startup build for EF tooling: exit 0, 0 errors (existing warnings only).
- Migration generation: `dotnet ef migrations add OfflineAgentLicensing --project src/RPA.Infrastructure --startup-project src/RPA.WebAPI --no-build` — exit 0.
- `git diff --check` — exit 0; only existing line-ending warnings were printed.
- Migration inspection confirmed unique indexes for `InstallationId`, `(LicenseInstallationId, MachineFingerprint)`, and `ActivationCodeHash`, plus both foreign keys.

## Covered acceptance cases

- 0/1 activation succeeds.
- 1/1 activation fails with `AGENT_LICENSE_LIMIT_REACHED`.
- `Disabled` retains a seat; `Deactivated` releases it.
- Activation hash is consumed once.
- Tracked entities contain hashes, not supplied plaintext sentinels.
- Two concurrent final-seat attempts result in exactly one success.

## Self-review and concerns

- Transaction rollback is implicit on disposal for all pre-commit exceptions; no partial activation/code consumption is saved.
- Capacity recount excludes soft-deleted identities and includes only `Activated`/`Disabled`.
- Deactivation clears the credential hash.
- The PostgreSQL lock SQL uses EF interpolation for the identifier value and a fixed table name.
- No live PostgreSQL concurrency integration test was run in this environment. SQLite verifies orchestration through the documented non-production serialization seam; production row-lock semantics are visible in code and should receive a PostgreSQL integration test in the deployment test environment.
- EF initially required a serialized startup-project restore/build. Migration generation then succeeded with `--no-build`; MSBuild parallelism remains an environment risk, not a product failure.
- Existing NU1608/NU1900, nullable, and Windows-platform analyzer warnings remain unchanged.

## Commit

`b331120 feat(persistence): agent koltuk kotasini atomik uygula`

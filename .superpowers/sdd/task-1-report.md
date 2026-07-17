# Task 1 Report — Contract package and licensing models

## Status

DONE_WITH_CONCERNS

## Implementation

- Added the 2026-07-16 Offline Agent Licensing contract-change entry to `AGENTS.md`, naming Domain, Infrastructure persistence/authentication, WebAPI, Agent, Studio, and LicenseGenerator.
- Added the four-state `AgentIdentityStatus` contract and exact seat-consumption rule (`Activated` and `Disabled` consume seats).
- Added `BaseEntity`-derived persistence models for license installations, agent identities, and one-time activations.
- Added immutable records for the offline payload, signed document, installation request, and license status.
- Added `ILicenseService` operations for status, installation-request export, import, and capacity enforcement.
- Added `IAgentIdentityRepository` operations for create, lookup, list, activation, disable, deactivate, and credential rotation.
- Credential and activation-code plaintext is absent from persistence contracts. Only hashes occur in persistence entities/repository mutation inputs; immutable transport documents expose no hash or secret fields.

## TDD Evidence

### RED

Command:

`dotnet test tests/RPA.Domain.Tests/RPA.Domain.Tests.csproj --filter FullyQualifiedName~LicensingContractTests -v minimal`

Result: exit code 1. Compilation failed for the expected missing-feature reason:

- `CS0234`: `RPA.Domain.Licensing` did not exist.
- `CS0246`: `AgentIdentityStatus` could not be found.

### GREEN — filtered contract tests

Command:

`dotnet test tests/RPA.Domain.Tests/RPA.Domain.Tests.csproj --filter FullyQualifiedName~LicensingContractTests -v minimal`

Result: exit code 0; 5 passed, 0 failed, 0 skipped.

### GREEN — complete Domain test project

Command:

`dotnet test tests/RPA.Domain.Tests/RPA.Domain.Tests.csproj -v minimal`

Result: exit code 0; 12 passed, 0 failed, 0 skipped.

## Files

- `AGENTS.md`
- `src/RPA.Domain/Enums/AgentIdentityStatus.cs`
- `src/RPA.Domain/Entities/LicenseInstallation.cs`
- `src/RPA.Domain/Entities/AgentIdentity.cs`
- `src/RPA.Domain/Entities/AgentActivation.cs`
- `src/RPA.Domain/Licensing/LicenseDocuments.cs`
- `src/RPA.Domain/Interfaces/ILicenseService.cs`
- `src/RPA.Domain/Interfaces/IAgentIdentityRepository.cs`
- `tests/RPA.Domain.Tests/LicensingContractTests.cs`

## Commit

`65df410 feat(domain): offline lisans ve agent kimlik kontratlari`

## Self-review

- Confirmed all required files and named public contracts are present.
- Confirmed transport documents are immutable records and EF models derive from `BaseEntity`.
- Confirmed the exact four seat-state expectations are tested.
- Confirmed feature input is copied to a deterministically ordered array rather than retaining a mutable caller collection.
- Confirmed no plaintext activation code, agent credential, private key, or token field was introduced.
- Confirmed `git diff --check` exits successfully.
- Confirmed unrelated existing modifications in `.superpowers/sdd/progress.md` and `.superpowers/sdd/task-1-brief.md` are not part of the task staging set.

## Concerns

- Both test runs emit `NU1900` warnings because the sandbox cannot reach `https://api.nuget.org/v3/index.json` for vulnerability metadata. Restore/build/test still complete successfully with zero test failures.

## Review Fix — Immutable Feature Collections

### Finding and implementation

Code review identified that `OfflineLicensePayload` and `LicenseStatus` retained caller-owned
`IReadOnlyList<string>` instances. Both records now expose `ImmutableArray<string>` feature snapshots
created by defensive-copy constructors. `OfflineLicensePayload` additionally normalizes its snapshot
with ordinal sorting and ordinal distinctness so canonical payload behavior is deterministic.

Changed files:

- `src/RPA.Domain/Licensing/LicenseDocuments.cs`
- `tests/RPA.Domain.Tests/LicensingContractTests.cs`

### RED

Command:

`dotnet test tests/RPA.Domain.Tests/RPA.Domain.Tests.csproj --filter FullyQualifiedName~LicensingContractTests -v minimal`

Result: exit code 1; 2 failed, 5 passed. Both new tests demonstrated that mutating the original
source lists changed the record contents. The payload failure also showed retained duplicate and
non-canonical feature ordering.

### GREEN

Focused command:

`dotnet test tests/RPA.Domain.Tests/RPA.Domain.Tests.csproj --filter FullyQualifiedName~LicensingContractTests -v minimal`

Result: exit code 0; 7 passed, 0 failed, 0 skipped.

Full command:

`dotnet test tests/RPA.Domain.Tests/RPA.Domain.Tests.csproj -v minimal`

Result: exit code 0; 14 passed, 0 failed, 0 skipped.

Both GREEN runs continued to emit only the previously documented `NU1900` vulnerability-feed warning.

### Fix commit

`9b5810b fix(domain): lisans ozelliklerini degistirilemez yap`

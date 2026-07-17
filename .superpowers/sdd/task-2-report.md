# Task 2 Report — Canonical signing and installation binding

## Status

COMPLETE

## Implementation

- Added fixed-order, compact UTF-8 canonical payload serialization.
- Normalized timestamps to invariant round-trip UTC strings and features to ordinally sorted distinct values.
- Added RSA-PSS/SHA-256 vendor signature verification with ordinal algorithm validation and safe rejection of malformed signatures.
- Added installation identity/key-store seams and stable identity derivation from product ID and the SHA-256 public-key fingerprint.
- Persisted PKCS#8 private-key bytes through an injectable protection seam.
- Added LocalMachine DPAPI protection and injectable filesystem operations using a temporary file followed by atomic replacement.
- Kept tests host-independent by using only in-memory filesystem, key store, and protection fakes.

## TDD evidence

### RED

Controller-confirmed command after diagnosing MSBuild parallel-node hangs:

`dotnet test tests\RPA.Infrastructure.Tests\RPA.Infrastructure.Tests.csproj --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~LicenseCryptographyTests -v minimal`

Result: exit code 1 in 22.2 seconds. Expected compilation errors identified the missing `RPA.Infrastructure.Licensing` namespace and missing `IInstallationKeyStore`, `IInstallationKeyProtection`, and `IInstallationFileSystem` types.

Earlier attempts without `-m:1 -p:UseSharedCompilation=false` emitted no output and were terminated after 120 and 40 seconds. Root cause was the environment's MSBuild parallel-node hang; all subsequent commands used a single node and disabled shared compilation.

### Focused GREEN

Command:

`dotnet test tests\RPA.Infrastructure.Tests\RPA.Infrastructure.Tests.csproj --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~LicenseCryptographyTests -v minimal`

Result: exit code 0 in 22.2 seconds; 5 passed, 0 failed, 0 skipped.

### Full Infrastructure regression

Command:

`dotnet test tests\RPA.Infrastructure.Tests\RPA.Infrastructure.Tests.csproj --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -v minimal`

Result: exit code 0 in 19.6 seconds; 667 passed, 0 failed, 0 skipped.

## Changed files

- `src/RPA.Infrastructure/RPA.Infrastructure.csproj`
- `src/RPA.Infrastructure/Licensing/CanonicalLicenseSerializer.cs`
- `src/RPA.Infrastructure/Licensing/VendorLicenseVerifier.cs`
- `src/RPA.Infrastructure/Licensing/IInstallationKeyStore.cs`
- `src/RPA.Infrastructure/Licensing/DpapiInstallationKeyStore.cs`
- `src/RPA.Infrastructure/Licensing/InstallationIdentityService.cs`
- `tests/RPA.Infrastructure.Tests/Licensing/LicenseCryptographyTests.cs`

## Self-review

- Confirmed canonical property order exactly matches the signed payload contract.
- Confirmed UTC conversion is independent of local timezone and culture.
- Confirmed feature normalization uses `StringComparer.Ordinal` for both distinctness and ordering.
- Confirmed verifier signs only canonical payload bytes and requires RSA-PSS/SHA-256.
- Confirmed private key export/import uses PKCS#8 and the public key uses SubjectPublicKeyInfo.
- Confirmed production DPAPI uses `DataProtectionScope.LocalMachine` while tests never call host DPAPI.
- Confirmed no Task 3 or later licensing behavior was introduced.

## Concerns

- Test output includes the repository's existing compiler/platform warnings and `NU1900` because vulnerability metadata cannot be fetched from NuGet in the restricted environment. Tests and builds still complete with zero failures.
- DPAPI is intentionally Windows-only; analyzer warnings identify that platform constraint.

## Commit

`26dbcde feat(licensing): imzali lisans ve kurulum kimligi dogrulamasi`

## Review fix — concurrent first-start winner selection

### Finding and implementation

Independent services/processes could both observe an absent key, generate different identities, and overwrite the same destination. The key-store contract now exposes atomic `TrySaveAsync`; DPAPI persistence writes to a GUID-suffixed temporary file and performs a no-overwrite move. A losing creator reloads and returns the persisted winner. Temporary/protected/private key buffers touched by this path are cleared where ownership permits.

### RED

Command:

`dotnet test tests\RPA.Infrastructure.Tests\RPA.Infrastructure.Tests.csproj --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~LicenseCryptographyTests -v minimal`

Result: exit code 1 in 6.9 seconds. Expected compile failures showed the old contracts still required `IInstallationKeyStore.SaveAsync` and `IInstallationFileSystem.MoveAtomically`, so two independent stores could not coordinate atomic winner selection.

### Focused GREEN

Same command after implementation: exit code 0 in 22.9 seconds; 6 passed, 0 failed, 0 skipped.

### Full Infrastructure GREEN

Command:

`dotnet test tests\RPA.Infrastructure.Tests\RPA.Infrastructure.Tests.csproj --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -v minimal`

Result: exit code 0 in 19.6 seconds; 668 passed, 0 failed, 0 skipped.

### Review-fix commit

`101c45e fix(licensing): kurulum kimligi olusturma yarisini onle`

### Deferred minor

Verifier null payload/signature robustness remains deferred as permitted by the review request; current domain record contract is non-nullable.

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


# Task 9 — Vendor license generator

**Commit:** 934c6ad (`feat(tools): offline musteri lisansi ureticisi`)
**Plan:** docs/superpowers/plans/2026-07-16-offline-agent-licensing.md — Task 9

## What was built

`tools/RPA.LicenseGenerator/` — a vendor-only, non-interactive CLI that reads an installation
request, binds a canonical license payload to the installation identity, signs it with the vendor
private key (RSA-PSS/SHA-256), and writes the signed document atomically.

- `RPA.LicenseGenerator.csproj` — exe, references Domain + Infrastructure. Dependency direction is
  one-way: nothing in the shipped runtime references this tool.
- `LicenseGenerationService.cs` — `LicenseGenerationException`, `LicenseGenerationOptions.Parse`
  (argument surface + validation), `LicenseGenerationService.Generate` (request validation, payload
  construction, signing, atomic write).
- `PrivateKeyLoader.cs` — encrypted PEM/PKCS#8 loading; password read only from the named env var.
- `Program.cs` — entry point; prints a secret-free summary, or a clean error + usage (exit 1).
- `tests/RPA.LicenseGenerator.Tests/LicenseGenerationTests.cs` — 11 tests.

**Canonical JSON is NOT reimplemented.** Signing goes through the production
`CanonicalLicenseSerializer.SerializePayload`, so the bytes signed here are byte-identical to the
bytes `VendorLicenseVerifier` re-serializes and verifies. A second implementation would drift and
produce signatures the runtime rejects.

## Argument surface

```
RPA.LicenseGenerator generate \
  --request <installation-request.json> --output <customer.lic> \
  --key <vendor-encrypted-pkcs8.pem> --key-password-env <ENV_VAR_NAME> \
  --license-id <id> --customer-id <id> --customer-name <name> --edition <edition> \
  --max-agents <N> --expires <YYYY-MM-DD> [--issued <YYYY-MM-DD>] [--revision <N>] \
  [--features <A,B>]
```

`--edition` and `--customer-name` are explicit REQUIRED arguments per the contract entry
"2026-07-16 (Offline Agent Licensing — payload edition + müşteri adı)"; `OfflineLicensePayload`
throws on null/whitespace, so the CLI rejects missing/blank values with a clean message before
constructing the payload. Defaults: `--issued` = now (UTC), `--revision` = 1, `--features` = empty.
No prompts anywhere — suitable for a secured vendor pipeline.

## Keeping the password and key out of all output

- The password is **never** an argument; only the *name* of an env var is passed, so the secret
  never reaches the process list or shell history.
- `PrivateKeyLoader` deliberately does **not** chain the inner exception — crypto/IO exception text
  can echo key material or the password. The message names only the env var, never its value.
- Verified by test: a wrong-password load failure's `ToString()` contains neither password, nor the
  string `PRIVATE KEY`, nor the key PEM, and `InnerException` is null.
- `Program.cs` prints only non-secret payload fields.

## Verification (actual numbers)

| Command | Result |
|---|---|
| `dotnet test tests/RPA.LicenseGenerator.Tests/RPA.LicenseGenerator.Tests.csproj -v minimal` | **11/11 pass** |
| `dotnet build RPA.sln -c Release` | **0 errors** (278 pre-existing warnings) |
| `dotnet test tests/RPA.Domain.Tests/RPA.Domain.Tests.csproj -v minimal` | **18/18** (baseline 18) |
| `dotnet test tests/RPA.Infrastructure.Tests/RPA.Infrastructure.Tests.csproj -v minimal` | **678/678** (baseline 678) |
| `dotnet test tests/RPA.WebAPI.Tests/RPA.WebAPI.Tests.csproj -v minimal` | **114/114** (baseline 114) |
| `dotnet test tests/RPA.Agent.Tests/RPA.Agent.Tests.csproj -v minimal` | **138/138** (baseline 138) |

No regressions added; no pre-existing failures existed to fix.

RED was observed first: the test project failed to compile with CS0103/CS0246 for
`LicenseGenerationOptions`, `LicenseGenerationService`, `PrivateKeyLoader`, and
`LicenseGenerationException` before any implementation existed.

Rejections covered by test: invalid/incomplete request document, request fingerprint not matching
its own public key, `--max-agents <= 0`, expiry before issue, missing/blank `--edition`, missing/blank
`--customer-name`, unknown argument, valueless argument, missing key file, unset password env var,
and private-key load failure without secret disclosure. Acceptance by the real runtime verifier
(`VendorLicenseVerifier`) is asserted end-to-end with a test key pair.

Beyond tests, the shipped binary was driven once for real: an **OpenSSL**-generated encrypted PKCS#8
key (real-world format, not just .NET-exported) produced a valid `.lic` with exit 0 and no leftover
temp file; the wrong-password run printed the clean error with no secret and exit 1.

## Deviations from the plan

- Added `--issued`, `--revision`, and `--key-password-env` beyond the plan's documented argument
  list. `--key-password-env` is required by the plan's own prose ("password from an environment
  variable named by an argument"). `--issued`/`--revision` are optional with defaults; `--revision`
  is needed because `LicenseService.ImportAsync` enforces a strictly increasing revision, so
  re-issuing a license to an existing installation is impossible without it.
- The generator validates that the request's fingerprint matches its embedded public key. Not
  required by the plan, but the vendor should not sign a payload bound to a tampered fingerprint.

## Deferred minors

- Output is written with `File.Move(..., overwrite: true)`, which is atomic on the same volume; the
  generator does not detect a cross-volume `--output`. Acceptable for a vendor pipeline writing
  locally.
- No `--help` flag; usage prints on any error. Adding an explicit help path is cosmetic.
- Feature codes are not validated against a known catalogue — no such catalogue exists yet.

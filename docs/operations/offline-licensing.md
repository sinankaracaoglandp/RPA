# Offline Agent Licensing — Operations Guide

**Spec:** `docs/superpowers/specs/2026-07-16-offline-agent-licensing-design.md`
**Plan:** `docs/superpowers/plans/2026-07-16-offline-agent-licensing.md`
**Scope:** first (offline) release. Online validation and revocation are backlog —
see `docs/backlog/hybrid-licensing.md`.

WebAPI is the **only** license and seat enforcement authority. Studio and the Agent display and
consume; they never decide.

---

## 1. Operational defaults

| Setting | Value |
|---|---|
| Agent JWT lifetime | 10 minutes |
| Token proactive renewal | 2 minutes before expiry |
| Offline connectivity lease | 15 minutes since last **successful** server validation |
| Activation-code lifetime | 15 minutes, single use |
| Seat accounting | `Activated` + `Disabled` consume a seat; `PendingActivation` + `Deactivated` do not |

Configuration keys (WebAPI):

| Key | Meaning |
|---|---|
| `Licensing:VendorPublicKeyPem` | Vendor **public** key. Ships with the product. |
| `Licensing:ProductId` | Product identifier bound into the installation ID (default `RPA.Platform`). |
| `Licensing:KeyDirectory` | Where the DPAPI-protected installation private key lives (default `App_Data/Licensing`). |
| `Licensing:CustomerReference` | Free-text reference embedded in the installation request. |

> **Warning — test vendor key.** `src/RPA.WebAPI/Program.cs` falls back to a hard-coded
> `TestOnlyVendorPublicKeyPem` when `Licensing:VendorPublicKeyPem` is unset. Production
> deployments **must** set the real vendor public key; otherwise licenses signed by the
> corresponding *test* private key would be accepted.

---

## 2. Vendor issuance

The vendor signing private key exists **only** in the license-generator environment. It is never
present in a customer runtime and is never stored in the customer Vault.

1. Receive the customer's installation request JSON (section 3, step 1).
2. Run the vendor-only CLI:

```bash
export RPA_LICENSE_KEY_PASSWORD='...'          # password is read ONLY from a named env var
dotnet run --project tools/RPA.LicenseGenerator -- generate \
  --request  ./acme-installation-request.json \
  --output   ./acme.lic \
  --key      ./vendor-private-key.pem \
  --key-password-env RPA_LICENSE_KEY_PASSWORD \
  --license-id   LIC-ACME-001 \
  --customer-id  ACME \
  --customer-name "ACME Sanayi A.S." \
  --edition      enterprise \
  --max-agents   25 \
  --expires      2027-07-16 \
  --features     agent,sap
```

Notes:
- `--customer-name` and `--edition` are **mandatory** — they are part of the signed payload.
- The generator verifies that the request's fingerprint matches its own public key before signing;
  a tampered request is not signed.
- Re-issuing to the same installation requires `--revision` **greater than** the installed
  revision. Import rejects an equal/older revision with `LICENSE_REVISION_INVALID` (downgrade guard).
- The password is never passed as an argument (it would land in the process table / shell history).
- The generator is **not** a dependency of the shipped runtime (one-way: tool → Domain/Infrastructure).

Keep a vendor-side record of every issuance/replacement. The offline release has no audit trail of
its own — see the hybrid backlog.

---

## 3. Customer import

1. **Export the installation request.** Studio → `/orchestrator/licensing` → download, or
   `GET /api/license/installation-request` (`LicenseAdministrator`). On first call the Orchestrator
   generates a 3072-bit RSA installation key pair; the private key is written DPAPI-protected
   (machine scope) under `Licensing:KeyDirectory` and **never leaves the machine**. The request
   contains only the public key, its SHA-256 fingerprint, the installation ID, the product ID and
   a customer reference.
2. Send the request to the vendor; receive a `.lic` file.
3. **Import.** Studio → `/orchestrator/licensing` → select the `.lic`, or
   `POST /api/license/import`. WebAPI validates, in order:
   vendor signature → installation ID + fingerprint match → expiry → monotonic revision.
4. Verify `GET /api/license/status`: customer, edition, validity, features, `activatedAgents` /
   `maxActivatedAgents`.

Stable error codes: `LICENSE_MISSING`, `LICENSE_EXPIRED`, `LICENSE_SIGNATURE_INVALID`,
`LICENSE_INSTALLATION_MISMATCH`, `LICENSE_REVISION_INVALID`, `AGENT_LICENSE_LIMIT_REACHED`,
`ACTIVATION_CODE_INVALID`, `ACTIVATION_CODE_EXPIRED`, `AGENT_DISABLED`, `AGENT_DEACTIVATED`,
`AGENT_CREDENTIAL_INVALID`, `AGENT_NOT_ACTIVATED`.

---

## 4. Server migration

The installation identity is bound to the machine (DPAPI machine scope). A migration therefore
**invalidates the old license**:

1. Install the Orchestrator on the new server.
2. Export a **new** installation request (a new key pair and installation ID are generated).
3. Ask the vendor for a **newly signed** license for the new installation ID.
4. Import it; re-activate agents (agents must re-enroll — see backlog item
   "migration without agent re-enrollment").

The offline release **cannot** remotely disable the old installation. The commercial agreement and
the vendor's issuance records are the only controls. Do not attempt to copy `Licensing:KeyDirectory`
to the new host as a shortcut: the DPAPI blob is machine-bound and will not decrypt.

---

## 5. Agent activation

1. Studio → `/orchestrator/agents` → **create agent** (state `PendingActivation`, consumes no seat).
2. **Generate activation code** (`POST /api/agents/{id}/activation-code`). The code is shown **once**,
   lives 15 minutes, is single-use, and is stored only as a hash.
3. On the agent host, configure `Agent:AgentId` (shown when the agent is created in Studio) and
   `Agent:InstallationId` (shown on the licensing screen), then run the agent **once** in activation
   mode:

   ```powershell
   dotnet run --project src/RPA.Agent -- --activate <ACTIVATION-CODE>
   # deployed: RPA.Agent.exe --activate <ACTIVATION-CODE>
   ```

   This posts the code to `POST /api/agent-auth/activate` with the installation ID and machine
   fingerprint, then **exits** — it does not start the service loop. WebAPI validates license +
   capacity, transitions the identity to `Activated`, consumes the code, and returns the long-lived
   agent credential **once**. The agent protects it with DPAPI machine scope
   (`IAgentCredentialStore`) and never logs it. Afterwards start the agent normally (no flag).

   The code is single-use: a failed attempt needs a fresh code from Studio.
4. The agent exchanges the credential at `POST /api/agent-auth/token` for a ~10-minute JWT
   (`sub=agent:{id}`, `agent_id`, `installation_id`, `client_type=agent`, `token_use=access`) and
   uses it for `/hubs/robot` and `/hubs/studio` via the shared token provider.

Activation, seat count, state transition, code consumption and credential issuance happen in **one**
database transaction; on PostgreSQL the installation row is locked (`SELECT … FOR UPDATE`) so two
concurrent requests cannot both claim the final seat.

**Disable** (`POST /api/agents/{id}/disable`) blocks authentication and new work but **keeps** the
seat. **Deactivate** (`POST /api/agents/{id}/deactivate`) revokes credentials and **frees** the seat;
re-activation needs a new code and free capacity.

---

## 6. Credential rotation

Endpoint: `POST /api/agents/{id}/rotate-credential` (`LicenseAdministrator`), Studio →
`/orchestrator/agents` → rotate action (offered only on `Activated` rows).

Real flow:

1. Administrator confirms the rotation in Studio.
2. WebAPI generates a new credential with the **same** generator/hash scheme as activation, persists
   **only** the hash, and returns the plaintext **once** in the response body.
3. The **previous credential is invalid immediately**: token exchange compares only
   `AgentIdentity.CredentialHash`, so the moment the hash is overwritten the old value matches
   nowhere. Already-issued JWTs are not revoked — they expire with their own ≤10-minute lifetime.
4. Studio shows the new credential once from an in-memory signal (cleared on close/`ngOnDestroy`);
   it is never written to local/sessionStorage.
5. **The operator carries the new credential to the agent host manually** (same as the activation
   code). The agent does not fetch a rotated credential by itself — out of scope for this release.

Only `Activated` agents may rotate. Any other state returns `409 AGENT_NOT_ACTIVATED` and the stored
credential is left untouched (rotating a `PendingActivation`/`Deactivated` agent would be meaningless —
they receive a credential from the activation flow — and a `Disabled` agent cannot obtain a token anyway).

Rotate when: a credential may have been exposed, an operator with access leaves, or on a scheduled
hygiene interval.

---

## 7. Connectivity and offline behaviour

The connectivity lease permits at most **15 minutes** since the last successful server validation.
It is fed by the agent heartbeat loop (default 30 s): a successful heartbeat renews the lease
(`RecordServerValidation`); a failed one marks the agent disconnected **without** shortening the
lease. When the lease expires, `ConnectivityLeaseContinuationGate` blocks the **next** node —
the currently running node always reaches its normal completion boundary — and the run is reported
as `ExecutionSuspendedException` (system-level interruption) carrying the job run ID and the node
that did not start.

Disabled / deactivated / expired-license / invalid-license responses are **terminal** for new work
and must not be retried as transient network errors.

**Not implemented in this release** (see backlog / task 10 report): `POST /api/agent-auth/refresh-lease`
(spec lists it; the heartbeat is the lease feed instead), and resume-after-reconnect from the
suspended node.

---

## 8. Backup and restore exclusions

**Never** include in backups, images, or golden templates:

| Path / item | Why |
|---|---|
| `Licensing:KeyDirectory` (default `App_Data/Licensing`) | Installation **private** key (DPAPI machine scope). Useless off-machine and must not be duplicated — a restored copy on another host is a second installation. |
| Agent credential store (DPAPI, agent host) | Agent plaintext credential. |
| Vendor private key | Vendor environment only. Never on a customer host, never in the customer Vault. |
| `RPA_LICENSE_KEY_PASSWORD` (or equivalent env var) | Vendor key password. |

Safe to back up: the `LicenseInstallations` / `AgentIdentities` / `AgentActivations` tables. They
hold public documents and hashes only (`SignedLicenseDocument` is a vendor-signed public artifact;
credentials and activation codes exist only as hashes).

**Restore reality check.** Restoring the database onto a *different* host gives an installation whose
key directory no longer decrypts → a new installation ID → the stored license fails
`LICENSE_INSTALLATION_MISMATCH`. Treat it as a migration (section 4).

---

## 9. Incident response

| Incident | Response |
|---|---|
| Agent credential exposed | Rotate (section 6). If the host itself is compromised: deactivate the identity (frees the seat), rebuild, activate a fresh identity. |
| Activation code leaked | Codes are single-use and expire in 15 min. If unconsumed, ignore it — it is unusable once the agent activates. If **consumed by someone else**, deactivate that identity and re-issue. |
| Agent host lost/stolen | Deactivate immediately (revokes credentials, frees the seat). Issued JWTs stay valid ≤10 minutes — this window is unavoidable offline. |
| Suspected license copy to another installation | The copy cannot import (`LICENSE_INSTALLATION_MISMATCH`). Offline release has **no** remote revocation — escalate commercially. Backlog: signed revocation + duplicate-use detection. |
| Vendor private key compromised | Rotate the vendor key pair, ship a product update carrying the new public key, re-issue all customer licenses. There is no in-product remedy. |
| Installation key directory deleted | A new key pair (and installation ID) is generated on next request → the existing license stops matching. Restore the directory from the machine's own protected backup or treat as a migration (section 4). |
| Seat limit reached unexpectedly | `Disabled` agents still consume seats. List `/api/agents`; deactivate what is genuinely retired. |

---

## 10. Security verification evidence

Recorded for Task 10 Step 5 on 2026-07-16, branch `feature/offline-agent-licensing`.
**No secret values are recorded here** — only the checks and their outcomes.

### 10.1 No credential/token/code logging

```
grep -rniE "Log(Information|Debug|Warning|Error|Trace|Critical).*(credential|activationCode|accessToken|privateKey|secret|plaintext)" --include=*.cs src tools
```

7 hits, **all in pre-existing Vault/Credentials code, all logging only the secret's *key/name*,
never its value** (`Vault secret alındı: {Key}`, `Credential kaydedildi: {Key}, tags: {Tags}`).
**Zero** hits in any licensing file (`src/RPA.*/Licensing/`, `Authentication/AgentAuthController.cs`,
`RPA.Agent/Authentication/`, `tools/RPA.LicenseGenerator/`). The generator also deliberately does not
chain the inner crypto exception when a private key fails to load (its text can reflect key/password
material); a test asserts the error output contains no password/PEM/`PRIVATE KEY` marker.

### 10.2 No plaintext persistence

```
grep -rnE "public string\??\s+(Credential|ActivationCode|Secret|PrivateKey)\b" --include=*.cs src
```

2 hits, **neither is licensing persistence**: `AuthenticationOptions.Secret` (JWT signing secret read
from configuration) and `CredentialsController.Secret` (request DTO forwarded to the Vault). Licensing
entities expose hashes only — `AgentIdentity.CredentialHash`, `AgentActivation.ActivationCodeHash`.
Plaintext credentials/codes exist only in a single HTTP response body and in agent DPAPI storage.

### 10.3 Migration constraints and indexes

`src/RPA.Infrastructure/Migrations/20260716084447_OfflineAgentLicensing.cs`:

| Constraint | Purpose |
|---|---|
| PK on `LicenseInstallations` / `AgentIdentities` / `AgentActivations` | identity |
| **UNIQUE** `IX_LicenseInstallations_InstallationId` | one row per installation — prevents a second, parallel installation record |
| **UNIQUE** `IX_AgentActivations_ActivationCodeHash` | no two activation records share a code hash |
| **UNIQUE** `IX_AgentIdentities_LicenseInstallationId_MachineFingerprint` | one identity per machine per installation — blocks seat inflation by re-registering one host |
| FK `AgentIdentities → LicenseInstallations`, `AgentActivations → AgentIdentities` | referential integrity |
| `NOT NULL` on `InstallationId`, `PublicKey`, `PublicKeyFingerprint`, `ProductId`, `Status`, `ActivationCodeHash`, `ExpiresAt` | no half-formed identity rows |

Column review: the only secret-adjacent columns are `CredentialHash` and `ActivationCodeHash`
(hashes). There is **no** plaintext credential or activation-code column.
`LicenseInstallations.SignedLicenseDocument` holds the vendor-signed public document (not a secret).

### 10.4 Copied / modified license rejection

Verified mechanically rather than by hand, so it stays verified —
`tests/RPA.WebAPI.Tests/OfflineLicensingEndToEndTests.CopiedOrEditedLicense_IsRejected` runs against
the real installation identity, the real `LicenseService` and a real vendor RSA key pair:

- **Copied** (validly signed for a *different* installation ID) → `400 LICENSE_INSTALLATION_MISMATCH`.
- **Edited after signing** (`maxActivatedAgents` 2 → 999) → `400 LICENSE_SIGNATURE_INVALID`.

`OfflineLicensingEndToEndTests.CompleteCustomerJourney_FromInstallationRequestToReplacementAgent`
additionally proves, end to end: exactly the licensed number of agents can activate, the next
activation is refused with `AGENT_LICENSE_LIMIT_REACHED`, an activation code cannot be replayed,
agent/Studio SignalR permissions are separated, deactivation frees a seat and immediately blocks
token issuance, and a replacement agent can take the freed seat.

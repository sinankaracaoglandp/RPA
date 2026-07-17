# Task 10 — End-to-end acceptance, documentation, hybrid backlog

**Commit:** bb309b5 · **Branch:** feature/offline-agent-licensing · **Date:** 2026-07-16

## Verification (actual output)

| Command | Result |
|---|---|
| `dotnet test RPA.sln -v minimal` | Domain **18**, Agent **142** (was 138), LicenseGenerator **11**, Infrastructure **678**, WebAPI **116** (was 114) — 0 failed, 0 skipped |
| `dotnet build RPA.sln -c Release` | **0 Hata** (only NU1900 offline-nuget warnings) |
| `npx ng test --watch=false` (src/RPA.Studio) | **319 passed / 43 files** |
| `npm run build` | success → `dist/RPA.Studio` |

**No pre-existing failures found.** The baseline run at HEAD *before* any change was fully green and
matched the briefed baselines exactly (Domain 18 / Infra 678 / WebAPI 114 / Agent 138 / Generator 11 /
Studio 319/43). The historically-flaky suites named in the brief (SAP GUI double-connect, Agent
QueuePolling DI, RobotHub/UiSpy auth) did **not** fail. Nothing was weakened or skipped.

## What the E2E genuinely exercises vs. simulates

`tests/RPA.WebAPI.Tests/OfflineLicensingEndToEndTests.cs` — 2 tests.

**Real** (no mocks in the licensing path): real installation identity (RSA-3072 +
`DpapiInstallationKeyStore` into a temp dir), a real test-vendor RSA key pair injected via
`Licensing:VendorPublicKeyPem`, signing through the **production** `CanonicalLicenseSerializer` (so the
signed bytes are the verified bytes), real `LicenseService` / `EfAgentIdentityRepository` / EF /
HTTP endpoints / SignalR connections / JWT validation. `IRobotService` is the only mock (RobotHub
dependency, irrelevant to licensing).

`CompleteCustomerJourney_…` covers: installation request → sign → import → status (edition,
customerName, seats) → activate exactly 2 of 2 → activation-code replay refused → 3rd activation
`AGENT_LICENSE_LIMIT_REACHED` → agent JWT (claims + ~10 min) → SignalR permission separation (agent
token connects `/hubs/robot`, cannot `StartSpy`; Designer cannot `Register`) → 15-min offline node
boundary → deactivate (seat freed, old credential's token exchange 401) → replacement takes the seat.
`CopiedOrEditedLicense_IsRejected` covers: copied → `LICENSE_INSTALLATION_MISMATCH`; edited
`maxActivatedAgents` 2→999 → `LICENSE_SIGNATURE_INVALID`.

**Simulated — stated honestly:** only step 8. Time is a fake `TimeProvider` (no real 15-min wait), and
the gate there is a **test double mirroring `ConnectivityLease` semantics**, because `RPA.Agent` is
`net10.0-windows` and cannot be referenced from the `net10.0` WebAPI test project. The **real**
`BaseRunner` is driven, so what that step proves is real: the runner suspends before the next node.
The real lease is covered in `RPA.Agent.Tests` (below).

## Step 2 discipline

RED was reached without touching production code. Two setup errors were fixed in the test only:
(1) `LicenseStatus` has no parameterless deserialization ctor → read status as `JsonElement`;
(2) the EF **InMemory** provider throws on `BeginTransactionAsync` → `Ignore(TransactionIgnoredWarning)`.
That suppression is a test-provider limitation only — production is Npgsql with a real transaction and
`SELECT … FOR UPDATE`, whose concurrency is covered by Infrastructure tests.

Notably the WebAPI E2E did **not** surface the unwired gate — it structurally cannot reach Agent DI.
The gap was found by reading `AddAgentCore`, and closed with its own RED test in `RPA.Agent.Tests`.

## Lease wiring (Task 6's deliberate gap — now closed)

`tests/RPA.Agent.Tests/Connectivity/ConnectivityLeaseWiringTests.cs` (+4 tests). RED first
(`HeartbeatBackgroundService` had no 5-arg ctor).

1. `AddAgentCore` registers `ConnectivityLease` as a **singleton** (per-scope leases would restart the
   15 minutes forever) and `IExecutionContinuationGate` → `ConnectivityLeaseContinuationGate`.
   `BaseRunner` (transient) resolves its optional `continuationGate` from DI.
2. `HeartbeatBackgroundService` took an optional `ConnectivityLease? lease` (last param, default null →
   existing callers/tests unaffected). Successful heartbeat = "last **successful** server validation" →
   `RecordServerValidation()`; failure → `MarkDisconnected()` only (the lease duration is **not**
   shortened — the running node must reach its boundary). Heartbeat interval (30 s default) ≪ 15 min.

The wiring is proven **by behaviour**, not by a registration assertion:
`ResolvedWorkflowRunner_SuspendsAtNodeBoundary_WhenLeaseExpired` resolves the real `IWorkflowRunner`
from the agent's own container and shows it suspending at the node boundary once the lease expires.

The brief suggested building on `IAgentHubConnectionFactory`. I did **not**: the heartbeat is the
lease-renewal feed, and a hub connect/disconnect decorator would only drive `IsConnected` /
`MarkDisconnected`, which **have no consumer yet** ("stop accepting new jobs" is unwritten). Adding it
would be speculative dead code. Recorded as remaining work instead.

## Deviations

- `POST /api/agent-auth/refresh-lease` is in the spec's API surface but **does not exist** and was not
  added — a full endpoint + agent-side renewal loop is more than "only the missing wiring", and the
  heartbeat already feeds the lease. Documented, not faked.
- Step 5's "manually verify copied/modified license rejection" was mechanized as
  `CopiedOrEditedLicense_IsRejected` so it stays verified.
- One AGENTS.md contract entry added (Task 10 — lease wiring). No interface signatures changed.

## Security evidence (Step 5)

Recorded in `docs/operations/offline-licensing.md` §10, **without secrets**:
credential/token logging grep (7 hits, all pre-existing Vault code logging only the *key*, zero in
licensing); plaintext-persistence grep (2 hits, neither licensing persistence; entities are hash-only);
migration `20260716084447_OfflineAgentLicensing` constraints (UNIQUE on `InstallationId`,
`ActivationCodeHash`, and `(LicenseInstallationId, MachineFingerprint)`; FKs; no plaintext columns);
copied/edited rejection.

Noted in the ops doc as a production warning: `Program.cs` falls back to a hard-coded
`TestOnlyVendorPublicKeyPem` when `Licensing:VendorPublicKeyPem` is unset.

## Genuinely undone

- `POST /api/agent-auth/refresh-lease` (spec API surface).
- Hub connect/disconnect → `IsConnected`; no consumer of "stop accepting new jobs" exists.
- Resume-after-reconnect from the suspended node (Task 6 deferred; still deferred).
- Agent-side pickup of a rotated credential (operator carries it manually).
- `AgentOutbox` is still not wired into the job/log reporting path (Task 6 deferred minor).
- Final Review Gates from the plan (high-effort code review + security review) not run — outside Task 10.

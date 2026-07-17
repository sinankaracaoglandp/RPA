# Task 6 Report — Connectivity lease and safe node boundary

Branch: `feature/offline-agent-licensing`

Note: a same-named report from an unrelated earlier plan (StudioHub `text-offset` whitelist) was
preserved as `task-6-textoffset-report.md` — same convention Task 5 used.

## What was built

- `src/RPA.Domain/Interfaces/IExecutionContinuationGate.cs` — `EnsureMayStartNodeAsync(jobRunId,
  nodeId, ct)`. Consulted only at the node boundary.
- `src/RPA.Domain/Exceptions/ExecutionSuspendedException.cs` — dedicated system-level interruption
  (derives from `RPA.Domain.Exceptions.SystemException`), preserves `JobRunId` + `NextNodeId`.
- `src/RPA.Agent/Connectivity/ConnectivityLease.cs` — `ConnectivityLease` (15-minute max offline
  interval, driven through `TimeProvider`; boundary is exclusive → 14:59 valid, 15:00 expired;
  `MarkDisconnected()` stops new work but does NOT shorten the lease or cancel anything) and
  `ConnectivityLeaseContinuationGate` (lease-backed `IExecutionContinuationGate`).
- `src/RPA.Agent/Connectivity/AgentOutbox.cs` — bounded durable outbox: keyed entries (re-enqueueing
  a key is a no-op; `Acknowledge` of unknown/already-acked keys is a no-op → repeated flushes are
  idempotent), atomic persistence (temp file + `File.Move(overwrite: true)`), corrupt file treated
  as empty, and `AgentOutboxOverflowException` — capacity overflow is explicit, never a silent drop.
- `src/RPA.Infrastructure/Workflow/BaseRunner.cs` — optional trailing ctor param
  `IExecutionContinuationGate? continuationGate` (null → no boundary, existing callers unchanged);
  gate is called in `RunSequenceAsync` *before* each node; a dedicated catch logs suspension as a
  warning and returns `Fail(suspended, …)` with checkpoint data.
- `src/RPA.Agent/Jobs/JobExecutor.cs` + `JobExecutionOutcome.cs` — new derived `IsSuspended`;
  suspension logs as a controlled interruption rather than an error.
- `AGENTS.md` — dated contract-change entry (Task 6) naming Domain, Infrastructure, and Agent.

Activity public signatures were not changed.

## Tests

- `tests/RPA.Agent.Tests/Connectivity/ConnectivityLeaseTests.cs` (8): 14:59 permits / 15:00 blocks
  (fake clock, no real waits), renewal clears disconnect, disconnect does not invalidate the lease
  or cancel the current node, gate permits while valid, gate suspension preserves job + next-node
  identity, JobExecutor surfaces suspension as a system-level interruption.
- `tests/RPA.Agent.Tests/Connectivity/AgentOutboxTests.cs` (5): duplicate key stores one entry,
  acknowledge idempotent across repeated flushes, entries survive restart with keys still
  idempotent, overflow is explicit, duplicate key at capacity does not overflow.
- `tests/RPA.Infrastructure.Tests/BaseRunnerConnectivityGateTests.cs` (4): gate consulted before
  every node, suspension before next node preserves identity, blocked node does not run, no gate →
  unchanged behavior.

RED: `error CS0234: 'Connectivity' … 'RPA.Agent' ad alanında yok` (Agent) and
`error CS0246: 'IExecutionContinuationGate' … bulunamadı` (Infrastructure).

GREEN (filtered): Agent `Connectivity` 13/13; Infrastructure `BaseRunnerConnectivityGateTests` 4/4.

Regression (full projects):
- `RPA.Agent.Tests`: `Başarısız: 0, Başarılı: 138, Toplam: 138` (baseline 125 + 13 new).
- `RPA.Infrastructure.Tests`: `Başarısız: 0, Başarılı: 678, Toplam: 678` (baseline 674 + 4 new).

No new failures. The known pre-existing failures did not reproduce in these two projects on this run.

## Deviations

- `ExecutionSuspendedException` lives in `src/RPA.Domain/Exceptions/` rather than inside
  `IExecutionContinuationGate.cs` — it is an exception, and the Domain layer already has a
  dedicated `Exceptions/` folder. The plan's commit file list was extended accordingly.
- `AgentOutboxOverflowException` and `ConnectivityLeaseContinuationGate` are co-located in the two
  planned Connectivity files (repo precedent: `IWebSinglePicker` inside `SpySessionCoordinator.cs`).
- The contract entry went into `AGENTS.md`, which is where the Task 1 licensing entry lives and
  what the plan targets (the worktree also has a separate, older `CLAUDE.md`).
- `JobExecutionOutcome.IsSuspended` is additive/derived; no existing member changed.

## Deferred minors

- Nothing yet constructs `ConnectivityLeaseContinuationGate` in DI or feeds `ConnectivityLease`
  from real hub connect/disconnect events or `POST /api/agent-auth/refresh-lease`. The lease, gate,
  and outbox are unit-complete but not yet wired into `AddAgentCore` / `AgentHubConnectionFactory`
  — belongs with the reconnect/backoff and lease-renewal loop.
- `AgentOutbox` has no configured path in `AgentOptions` yet (constructed with an explicit path);
  add `EffectiveOutboxFilePath` alongside `EffectiveCredentialFilePath` when the flush loop lands.
- Resume-after-reconnect (re-authorizing the job and continuing from `NextNodeId`) is not
  implemented; the exception carries the identity needed for it.
- `AgentOutbox.Persist` rewrites the whole set per mutation — fine at capacity 500, worth revisiting
  if capacity grows substantially.

# Final Review Fixes Report — Job → Agent Dispatch

## Finding 1 (doc-only) — RobotDispatcher XML summary corrected
File: `src/RPA.Infrastructure/Scheduling/RobotDispatcher.cs`
Rewrote class `<summary>` to accurately state: candidates are Online robots whose Tags cover
`Trigger.TargetRobotTags` with free capacity, ordered by most-free-capacity then FRESHEST
heartbeat (`ThenByDescending LastHeartbeat`). Explicitly notes `Trigger.Priority` is NOT used in
robot selection (persisted for future Pending-queue ordering). Also cleaned up the stray inline
comment on the `ThenByDescending` line that referenced Priority. No logic changed.

## Finding 2 (behavior) — Pending outcome
Files: `src/RPA.Domain/Interfaces/ITriggerService.cs`, `src/RPA.Infrastructure/Scheduling/TriggerService.cs`,
`tests/RPA.Infrastructure.Tests/TriggerServiceTests.cs`.

TDD steps:
1. Added `Assert.Equal(TriggerExecutionOutcome.Pending, result.Outcome);` to
   `ExecuteTrigger_NoRobot_JobRunPending`. Ran filtered test — FAILED to compile:
   `error CS0117: 'TriggerExecutionOutcome' bir 'Pending' tanımı içermiyor` (expected RED).
2. Added `Pending` to `TriggerExecutionOutcome` enum with Turkish XML doc, and
   `TriggerExecutionResult.Pending(JobRun jobRun)` static factory mirroring existing ones.
3. In `TriggerService.ExecuteTriggerAsync`, the no-robot branch now returns
   `TriggerExecutionResult.Pending(jobRun)` instead of `Executed(jobRun)`. Robot-assigned branch,
   Queued branch, and Skipped branch unchanged (still return `Executed`/`Queued`/`Skipped`
   respectively).
4. Reran filtered tests — all GREEN (see below), including
   `ExecuteTrigger_AssignsSelectedRobot` still asserting `Executed`.

Command + output:
```
dotnet test tests/RPA.Infrastructure.Tests --filter TriggerServiceTests -v minimal
...
Başarılı!  - Başarısız:     0, Başarılı:    10, Atlanan:     0, Toplam:    10, Süre: 1 s - RPA.Infrastructure.Tests.dll (net10.0)
```

WebAPI `/fire` path: `TriggersController.Fire` only special-cases `Outcome == NotFound`; all other
outcomes (including new `Pending`) flow through `result.Outcome.ToString()` into
`TriggerFireResultDto.Outcome` unchanged — no code change needed there. Confirmed via full solution
build (below) that this compiles cleanly.

## Finding 3 (doc-only) — known limitations documented
File: `docs/superpowers/specs/2026-07-14-job-agent-dispatch-design.md`
Added new "### 5.1 Bilinen kısıtlamalar" subsection under "## 5. Kapsam dışı" documenting:
(a) the capacity-read/AssignedRobotId-write race between `RobotDispatcher.SelectRobotAsync` and
`TriggerService`, to be re-validated under a transaction/row-lock once the agent handoff/poll
protocol is built; and (b) that `Trigger.Priority` is persisted but not yet used in dispatch
selection (planned for future Pending-queue ordering).

## Build
```
dotnet build
...
Oluşturma başarılı oldu.
    7 Uyarı
    0 Hata
```
(Warnings are pre-existing CA1416/NU1608/CS8625 noise unrelated to this change.)

## Commit
See `git log -1` — message:
`fix(dispatch): Pending outcome + dispatcher doc duzeltme + bilinen kisitlama notu`

## Known pre-existing unrelated failures (not touched)
SapGuiChannel double-connect, Agent HostedService QueueAgentJobSource DI, RobotHub/UiSpy auth —
predate this feature, out of scope per task instructions.

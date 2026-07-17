# Task 6: WebAPI StudioHub — `text-offset` whitelist

## Status: DONE

### Summary
Successfully added the `"text-offset"` spy kind to StudioHub's `SupportedKinds` whitelist, enabling Vision.ClickTextOffset feature to work with the Studio→Agent SignalR pipeline.

### Changes Made
1. **`src/RPA.WebAPI/Hubs/StudioHub.cs`** (line 31): Added `"text-offset"` to SupportedKinds HashSet
2. **`tests/RPA.WebAPI.Tests/UiSpyTests.cs`** (line 220): Added `[InlineData("text-offset")]` to parametrized test

### Build & Test Results
- Build: SUCCESS (RPA.WebAPI.csproj compiled with 22 warnings, 0 errors)
- Tests: ALL PASSED (5/5 parametrized tests for StudioHub_StartSpy_AcceptsSupportedKind)
  - sap: PASS
  - web: PASS
  - desktop: PASS
  - image: PASS
  - text-offset: PASS (NEW)

### Commit
- SHA: `c88d92b`
- Message: "feat(hub): StudioHub text-offset spy kind whitelist"

### Test Coverage
Found and updated parametrized test `StudioHub_StartSpy_AcceptsSupportedKind` in UiSpyTests.cs. Added InlineData case for "text-offset".

---

**Completed:** 2026-07-15

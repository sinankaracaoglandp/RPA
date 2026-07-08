# Playwright Web Activities Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Web activities use a real Playwright-controlled browser session for attended automation.

**Architecture:** Add a web automation session manager abstraction in Infrastructure. Activities call the abstraction; the production implementation owns Playwright browser/page lifecycle.

**Tech Stack:** .NET 10, Microsoft.Playwright, xUnit, existing `IActivity` workflow contract.

## Global Constraints

- Follow TDD: failing test, minimal implementation, passing test.
- Do not change Domain activity contracts.
- Keep first scope to `Web.Open`, `Web.Goto`, `Web.Fill`, `Web.Click`, `Web.GetText`, `Web.WaitFor`.
- Default visible browser mode is `headless=false`.

---

### Task 1: Session Manager Contract and Activity Wiring

**Files:**
- Create: `src/RPA.Infrastructure/Workflow/Activities/Web/IWebAutomationSessionManager.cs`
- Modify: `src/RPA.Infrastructure/Workflow/Activities/Web/WebOpenActivity.cs`
- Modify: `src/RPA.Infrastructure/Workflow/Activities/Web/WebRuntimeActivities.cs`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/WebActivityTests.cs`

**Interfaces:**
- Produces: `IWebAutomationSessionManager.OpenAsync`, `GotoAsync`, `FillAsync`, `ClickAsync`, `GetTextAsync`, `WaitForAsync`.

- [ ] Write failing tests for `Web.Open`, `Web.Goto`, `Web.Fill`, `Web.Click`, `Web.GetText`, `Web.WaitFor` calling a fake manager.
- [ ] Run `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter WebActivityTests` and verify expected failures.
- [ ] Implement activities against the manager abstraction.
- [ ] Run the same test command and verify pass.

### Task 2: Playwright Production Implementation

**Files:**
- Modify: `src/RPA.Infrastructure/RPA.Infrastructure.csproj`
- Create: `src/RPA.Infrastructure/Workflow/Activities/Web/PlaywrightWebAutomationSessionManager.cs`
- Modify: `src/RPA.Infrastructure/Workflow/WorkflowServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: `IWebAutomationSessionManager`.
- Produces: DI registration for production Playwright session manager.

- [ ] Add `Microsoft.Playwright` package reference.
- [ ] Implement browser normalization for `chromium`, `chrome`, `edge`.
- [ ] Register `IWebAutomationSessionManager` as singleton.
- [ ] Run `dotnet build src/RPA.Infrastructure/RPA.Infrastructure.csproj --no-restore`.

### Task 3: Verification

**Files:**
- Test: `tests/RPA.Infrastructure.Tests/Workflow/ActivityRegistryCoverageTests.cs`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/WebActivityTests.cs`

- [ ] Run `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter WebActivityTests`.
- [ ] Run `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter ActivityRegistryCoverageTests`.
- [ ] Run `dotnet build src/RPA.Infrastructure/RPA.Infrastructure.csproj --no-restore`.
- [ ] Report browser install requirement: `pwsh src/RPA.Infrastructure/bin/Debug/net10.0/playwright.ps1 install chromium` or equivalent after successful restore/build.

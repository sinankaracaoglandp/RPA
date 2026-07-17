### Task 5: Agent credential store and shared token provider

**Files:**
- Modify: `src/RPA.Agent/Configuration/AgentOptions.cs`
- Create: `src/RPA.Agent/Authentication/IAgentCredentialStore.cs`
- Create: `src/RPA.Agent/Authentication/DpapiAgentCredentialStore.cs`
- Create: `src/RPA.Agent/Authentication/AgentAccessTokenProvider.cs`
- Create: `src/RPA.Agent/Authentication/AgentEnrollmentClient.cs`
- Modify: `src/RPA.Agent/Hub/RobotHubClient.cs`
- Modify: `src/RPA.Agent/UISpy/SpyHubCommandHostedService.cs`
- Modify: `src/RPA.Agent/UISpy/SapGuiSpyService.cs`
- Modify: `src/RPA.Agent/AgentServiceCollectionExtensions.cs`
- Test: `tests/RPA.Agent.Tests/Authentication/AgentAccessTokenProviderTests.cs`
- Test: `tests/RPA.Agent.Tests/UISpy/SpyHubAuthenticationTests.cs`

**Interfaces:**
- Produces `IAgentAccessTokenProvider.GetTokenAsync(CancellationToken)` shared by all SignalR clients.

- [ ] **Step 1: Write failing provider and SignalR configuration tests**

Assert concurrent calls perform one token request, cached tokens are reused outside the two-minute renewal window, expiring tokens refresh, failed refresh does not expose credentials, and both StudioHub connections plus RobotHub configure `AccessTokenProvider`.

- [ ] **Step 2: Run RED**

Run the two filtered Agent test classes; confirm missing provider/configuration failures.

- [ ] **Step 3: Implement minimal enrollment, storage, and refresh**

Use a semaphore to serialize refresh. Decode expiry only after successful API response; do not treat JWT claims as authorization decisions on the client. Configure every hub with:

```csharp
.WithUrl(hubUrl, o => o.AccessTokenProvider = () => tokenProvider.GetTokenAsync(CancellationToken.None))
```

Store the long-lived credential through DPAPI LocalMachine and never in `appsettings.json`.

- [ ] **Step 4: Run GREEN and Agent regression**

Run filtered tests and the complete Agent test project.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Agent tests/RPA.Agent.Tests
git commit -m "feat(agent): guvenli credential ve yenilenen hub tokeni"
```


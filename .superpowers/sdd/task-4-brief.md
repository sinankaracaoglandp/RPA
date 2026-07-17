### Task 4: License and agent-auth WebAPI

**Files:**
- Create: `src/RPA.WebAPI/Licensing/LicenseController.cs`
- Create: `src/RPA.WebAPI/Licensing/AgentsController.cs`
- Create: `src/RPA.WebAPI/Authentication/AgentAuthController.cs`
- Create: `src/RPA.Infrastructure/Authentication/AgentTokenService.cs`
- Modify: `src/RPA.WebAPI/Program.cs`
- Modify: `src/RPA.WebAPI/Hubs/StudioHub.cs`
- Modify: `src/RPA.WebAPI/Robots/RobotHub.cs`
- Test: `tests/RPA.WebAPI.Tests/OfflineLicenseApiTests.cs`
- Test: `tests/RPA.WebAPI.Tests/AgentAuthenticationTests.cs`
- Test: `tests/RPA.WebAPI.Tests/StudioHubAuthorizationTests.cs`

**Interfaces:**
- Produces the API surface and policies from the design; agent JWT claims are `agent_id`, `installation_id`, `client_type=agent`, and `token_use=access`.

- [ ] **Step 1: Write failing endpoint and hub-policy tests**

Assert license import rejects altered/wrong-installation documents; an administrator can create an activation code but a normal designer cannot; activation returns a credential once; token exchange returns a 10-minute access token; disabled/deactivated agents are rejected. Connect separate Studio and agent tokens and assert each cannot invoke the other policy's hub methods.

- [ ] **Step 2: Run RED**

Run the three filtered WebAPI test classes. Expected: 404/missing policy and missing type failures.

- [ ] **Step 3: Implement endpoints and policies**

Register policies:

```csharp
options.AddPolicy("LicenseAdministrator", p => p.RequireRole("Administrator"));
options.AddPolicy("StudioSpyUser", p => p.RequireRole("Designer", "Administrator"));
options.AddPolicy("AgentClient", p => p.RequireClaim("client_type", "agent"));
```

Keep controller DTOs secret-safe. Return the activation code and initial agent credential only from their creation responses. Decorate StudioHub methods with method-level policies and derive agent identity from claims.

- [ ] **Step 4: Run GREEN and WebAPI regression**

Run filtered tests and then all WebAPI tests.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.WebAPI src/RPA.Infrastructure/Authentication tests/RPA.WebAPI.Tests
git commit -m "feat(webapi): offline lisans ve agent token APIleri"
```


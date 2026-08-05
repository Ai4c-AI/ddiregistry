# Keycloak OIDC Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Keycloak OIDC login to the Web and real Keycloak-backed token validation to MCP, with Compose and Docker-conditional tests.

**Architecture:** MCP remains a provider-neutral JwtBearer protected resource configured with a realm issuer. Web retains local ASP.NET Core Identity sessions and roles, adding an external `Keycloak` code-plus-PKCE scheme that binds only existing local users. A versioned realm import is used by Compose and Testcontainers.

**Tech Stack:** .NET 10, ASP.NET Core Identity, Microsoft.AspNetCore.Authentication.OpenIdConnect 10.0.2, xUnit 2.9.2, Testcontainers.Keycloak 4.6.0, Xunit.SkippableFact 1.5.23, Keycloak 26.0.

## Global Constraints

- Preserve the local password, registration, password-reset, schema, and Identity-role paths.
- Never add a Keycloak SDK, Keycloak administration API call, production secret, or Keycloak data volume.
- Web registers `Keycloak` only when Authority, ClientId, and ClientSecret are all configured.
- Web uses code flow with PKCE, `/signin-oidc`, and scopes `openid profile email`.
- A Keycloak external login binds only to an existing `AspNetUsers` email match; it never creates a user or maps Keycloak roles.
- MCP scopes remain `ddi.registry.read` and `ddi.registry.write`; real-container tests skip only when Docker is unavailable.
- Compose uses `http://keycloak.localtest.me:8180/realms/ddi-registry` as the single Keycloak issuer. Configure `KC_HTTP_PORT` and `KC_HOSTNAME` with port 8180, publish host port 8180, and add `keycloak.localtest.me` as the Keycloak Docker network alias.

---

## File Structure

- Create `infra/keycloak/realm.json` for clients, scopes, redirect URI, and test user.
- Create `src/Ddi.Registry.Mcp/Dockerfile` and modify `docker-compose.yaml` for local topology.
- Modify Web project/configuration/login files; create `src/Ddi.Registry.Web.Tests` and add it to the solution.
- Modify MCP test project/factory; create `KeycloakFixture.cs` and `KeycloakOidcIntegrationTests.cs`.
- Modify `README.md` for local workflow.

### Task 1: Realm and Compose

**Files:**
- Create: `infra/keycloak/realm.json`
- Create: `src/Ddi.Registry.Mcp/Dockerfile`
- Modify: `docker-compose.yaml`

**Interfaces:**
- Produces realm `ddi-registry` and clients `registry-web`, `mcp-client`, `mcp-service-client`, `wrong-audience-client`.
- Produces Compose services `db`, `keycloak`, `registry`, and `mcp`.

- [ ] **Step 1: Write the failing configuration check**

Run `docker compose config`.

Expected: it is valid but lacks `keycloak` and `mcp`, so it cannot validate OIDC locally.

- [ ] **Step 2: Create the test realm**

Create `infra/keycloak/realm.json` with `realm: "ddi-registry"`, `enabled: true`, `accessTokenLifespan: 300`, and this test user:

```json
{
  "username": "mcp-test-user",
  "enabled": true,
  "emailVerified": true,
  "email": "test@example.com",
  "credentials": [{ "type": "password", "value": "local-test-password", "temporary": false }]
}
```

Define protocol `openid-connect` scopes `ddi.registry.read`, `ddi.registry.write`, and `mcp-audience`. `mcp-audience` contains `oidc-audience-mapper` configured with `included.client.audience: mcp-client` and `access.token.claim: true`.

Define these client contracts:

| Client | Required configuration |
| --- | --- |
| `registry-web` | confidential, secret `local-registry-web-secret`, standard flow enabled, redirect `http://localhost:8000/signin-oidc`, web origin `http://localhost:8000`, default scopes `profile email` |
| `mcp-client` | public, direct grants enabled only, default scopes `profile email ddi.registry.read ddi.registry.write mcp-audience` |
| `mcp-service-client` | confidential, secret `local-mcp-service-secret`, service accounts enabled only, default scopes `ddi.registry.read mcp-audience` |
| `wrong-audience-client` | public, direct grants enabled only, default scopes `profile email ddi.registry.read ddi.registry.write` and no `mcp-audience` |
| `short-lived-client` | public, direct grants enabled only, `access.token.lifespan=5` via client attributes, default scopes `profile email ddi.registry.read ddi.registry.write mcp-audience` — used only for the expired-token test |

- [ ] **Step 3: Add the MCP image and Compose services**

Create `src/Ddi.Registry.Mcp/Dockerfile` using the existing Web Dockerfile's multi-stage pattern: restore MCP and Data projects from the root, publish `Ddi.Registry.Mcp.csproj` with `/p:UseAppHost=false`, use `mcr.microsoft.com/dotnet/aspnet:10.0`, expose `8080`, and end with:

```dockerfile
ENTRYPOINT ["dotnet", "Ddi.Registry.Mcp.dll"]
```

Add this behavior to `docker-compose.yaml`:

```yaml
keycloak:
  image: quay.io/keycloak/keycloak:26.0
  command: ["start-dev", "--import-realm"]
  ports: ["8180:8180"]
  environment:
    KC_HTTP_PORT: "8180"
    KC_HOSTNAME: http://keycloak.localtest.me:8180
    KC_BOOTSTRAP_ADMIN_USERNAME: ${KEYCLOAK_ADMIN:-admin}
    KC_BOOTSTRAP_ADMIN_PASSWORD: ${KEYCLOAK_ADMIN_PASSWORD:-local-admin-password}
  volumes:
    - ./infra/keycloak/realm.json:/opt/keycloak/data/import/realm.json:ro
  networks:
    default:
      aliases: [keycloak.localtest.me]
```

Give Keycloak a health check targeting the management port (Keycloak 26 dev mode exposes `/health/ready` on port 9000): Add `mcp`, built from the new Dockerfile, on host port `8001`, with `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_HTTP_PORTS=8080`, the existing database connection, Authority `http://keycloak.localtest.me:8180/realms/ddi-registry`, Audience `mcp-client`, both MCP scopes, and dependencies on healthy DB and Keycloak. Add the three `Authentication__Keycloak__*` variables to `registry`, using that same Authority, `registry-web`, and `${REGISTRY_WEB_CLIENT_SECRET:-local-registry-web-secret}`, and make it wait for Keycloak. Do not add a Keycloak volume.

- [ ] **Step 4: Verify and commit**

Run `docker compose config`.

Expected: exit 0 with all four services and no Keycloak named volume.

Run `git add infra/keycloak/realm.json src/Ddi.Registry.Mcp/Dockerfile docker-compose.yaml; git commit -m "feat: add local Keycloak OIDC environment"`.

### Task 2: Web Test Host, OIDC, and Existing-Account Binding

**Files:**
- Modify: `src/Ddi.Registry.Web/Ddi.Registry.Web.csproj`
- Modify: `src/Ddi.Registry.Web/Startup.cs`
- Modify: `src/Ddi.Registry.Web/appsettings.json.dist`
- Modify: `src/Ddi.Registry.Web/Areas/Identity/Pages/Account/ExternalLogin.cshtml.cs`
- Modify: `src/Ddi.Registry.Web/Areas/Identity/Pages/Account/ExternalLogin.cshtml`
- Create: `src/Ddi.Registry.Web/Services/ExternalLoginAccountLinker.cs`
- Create: `src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj`
- Create: `src/Ddi.Registry.Web.Tests/WebOidcApplicationFactory.cs`
- Create: `src/Ddi.Registry.Web.Tests/KeycloakConfigurationTests.cs`
- Create: `src/Ddi.Registry.Web.Tests/ExternalLoginAccountLinkerTests.cs`
- Modify: `Ddi.Registry.Web.sln`

**Interfaces:**
- Consumes `Authentication:Keycloak:{Authority,ClientId,ClientSecret}`.
- Produces scheme `Keycloak` and `ExternalLoginAccountLinker.LinkAsync(ExternalLoginInfo, string)` with linked, missing-user, and failure outcomes.

- [ ] **Step 1: Create the isolated Web test host**

Create a `net10.0` xUnit project referencing Web and Data, with Microsoft.NET.Test.Sdk 17.12.0, xUnit 2.9.2, xunit.runner.visualstudio 2.8.2, Microsoft.AspNetCore.Mvc.Testing 10.0.2, and Microsoft.EntityFrameworkCore.InMemory 10.0.2. Add it with `dotnet sln Ddi.Registry.Web.sln add src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj`.

Subclass `WebApplicationFactory<Ddi.Registry.Web.Program>`, set environment `Testing`, replace `ApplicationDbContext` with `UseInMemoryDatabase(Guid.NewGuid().ToString())`, and inject either full or empty Keycloak config. Update `Startup.UpdateDatabase` so Testing calls `context.Database.EnsureCreatedAsync()` while other environments call `MigrateAsync()`. Seed `test@example.com` through `UserManager<ApplicationUser>`.

- [ ] **Step 2: Write failing Web tests**

In the Task 3 test project, write tests to prove that complete config includes `Keycloak` in `IAuthenticationSchemeProvider.GetAllSchemesAsync()`, incomplete config does not, a `Keycloak` login key `keycloak-subject` binds to seeded `test@example.com`, and unknown `unknown@example.com` remains absent from `UserManager.FindByEmailAsync`.

- [ ] **Step 3: Run the test to verify failure**

Run `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~Keycloak"`.

Expected: FAIL because no OIDC handler is registered and the current callback presents an account-creation form.

- [ ] **Step 4: Register the conditional handler**

Add package:

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="10.0.2" />
```

Add this non-secret configuration template:

```json
"Authentication": {
  "Keycloak": {
    "Authority": "https://keycloak.example/realms/ddi-registry",
    "ClientId": "registry-web",
    "ClientSecret": "set-through-user-secrets-or-environment"
  }
},
```

After `AddIdentity` in `Startup.ConfigureServices`, read all three values. When none are empty, call `AddAuthentication().AddOpenIdConnect("Keycloak", options => ...)` with `SignInScheme = IdentityConstants.ExternalScheme`, Authority, ClientId, ClientSecret, CallbackPath `/signin-oidc`, `ResponseType = OpenIdConnectResponseType.Code`, `UsePkce = true`, `SaveTokens = false`, `GetClaimsFromUserInfoEndpoint = true`, cleared scopes plus `openid`, `profile`, `email`, and `ClaimActions.MapUniqueJsonKey(ClaimTypes.Email, "email")`. Do not set any default scheme.

- [ ] **Step 5: Bind instead of creating accounts**

`ExternalLoginAccountLinker` finds the local user with `UserManager.FindByEmailAsync(email)` and invokes `AddLoginAsync(user, info)`. It must never call `CreateAsync`. The callback retains its existing success and lockout behavior. For an unbound login it requires `ClaimTypes.Email`, calls the linker, logs the specific failure reason at Information level (missing email claim, email not found, or AddLoginAsync failure), and redirects to Login with exactly `Unable to sign in with the external provider.` for missing email, unknown user, or binding error. Successful binding calls `SignInAsync` for the existing user.

Replace `OnPostConfirmationAsync` with the same generic error redirect. Replace the registration form in `ExternalLogin.cshtml` with a non-actionable unavailable-account message. Add `<InternalsVisibleTo Include="Ddi.Registry.Web.Tests" />` in `Ddi.Registry.Web.csproj` for direct linker tests.

- [ ] **Step 6: Verify and commit**

Run `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~Keycloak"`.

Expected: PASS; only configured Keycloak appears, only existing accounts link, and unknown external email never creates a user.

Run `git add src/Ddi.Registry.Web; git commit -m "feat: add Keycloak external login to registry web"`.

### Task 3: Real Keycloak MCP Verification

**Files:**
- Modify: `src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj`
- Modify: `src/Ddi.Registry.Mcp.Tests/McpWebApplicationFactory.cs`
- Create: `src/Ddi.Registry.Mcp.Tests/KeycloakFixture.cs`
- Create: `src/Ddi.Registry.Mcp.Tests/KeycloakOidcIntegrationTests.cs`

**Interfaces:**
- Produces fixture Authority plus password, service, and wrong-audience access tokens.
- Produces real-JwtBearer factory mode that does not install `TestAuthHandler`.

- [ ] **Step 1: Write failing real-token tests**

Add `[SkippableFact]` tests named `KeycloakPasswordToken_InitializesAndListsTools`, `KeycloakPasswordToken_RequestAgency_MapsEmailToSeededLocalUser`, `KeycloakServiceToken_ReadToolSucceeds_WriteToolReturnsMissingScope`, `KeycloakWrongAudienceToken_IsRejectedWithUnauthorized`, `KeycloakExpiredPasswordToken_IsRejectedWithUnauthorized`, and `KeycloakUnauthenticatedRequest_IsRejectedWithUnauthorized`. Each begins with `Skip.IfNot(fixture.Started, "Docker daemon is not available; skipping the Keycloak Testcontainer test.")`.

- [ ] **Step 2: Implement the fixture**

Add `Testcontainers.Keycloak` 4.6.0 and copy `../../infra/keycloak/realm.json` to test output. Implement `IAsyncLifetime` with:

```csharp
_container = new KeycloakBuilder()
    .WithImage("quay.io/keycloak/keycloak:26.0")
    .WithUsername("admin")
    .WithPassword("local-admin-password")
    .WithResourceMapping(realmPath, "/opt/keycloak/data/import/realm.json")
    .WithCommand("start-dev", "--import-realm")
    .Build();
await _container.StartAsync();
```

Set Authority from `new Uri(_container.GetBaseAddress(), "realms/ddi-registry/")`, wait until its discovery document has `jwks_uri`, and catch only startup errors to mark the fixture unavailable. Form-post to `${Authority}/protocol/openid-connect/token` for `mcp-client` password grant, `mcp-service-client` client credentials, and `wrong-audience-client` password grant; deserialize `access_token`.

- [ ] **Step 3: Preserve JwtBearer in real factory mode**

Add `UseRealOidc`, `OidcAuthority`, and `OidcAudience` to `McpWebApplicationFactory`. In real mode, set OIDC environment values before host build but do not override authenticate/challenge defaults or add `TestAuthHandler`; retain InMemory database and `Seed()`. Add a helper that sets `Authorization: Bearer {accessToken}` on the existing `McpHttpTestClient` HttpClient.

- [ ] **Step 4: Make the assertions pass**

The password token initializes, lists exactly four tools, and creates `us.keycloaktest`; its `CreatorId`, `AdminContactId`, and `TechnicalContactId` equal `TestAuthHandler.SeedUserId`. The service token reads agencies but its write call contains `Missing required scope 'ddi.registry.write'`. Wrong-audience and absent token initialize requests return `401`. Obtain a token from `short-lived-client` (realm accessTokenLifespan is 300 s; this client overrides to 5 s), use `await Task.Delay(TimeSpan.FromSeconds(6))`, then assert `401` for the expired token.

- [ ] **Step 5: Verify and commit**

Run `dotnet test src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj --filter "FullyQualifiedName~KeycloakOidcIntegrationTests"` and then `dotnet test src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj`.

Expected: real tests PASS with Docker and SKIP without it; existing tests still PASS.

Run `git add src/Ddi.Registry.Mcp.Tests infra/keycloak/realm.json; git commit -m "test: verify MCP OIDC against Keycloak"`.

### Task 4: Documentation and Final Validation

**Files:**
- Modify: `README.md`

**Interfaces:**
- Documents local-only administration, ephemeral realm state, browser login, and MCP token acquisition.

- [ ] **Step 1: Document the local workflow**

Add a section stating Keycloak is development/test only at `http://keycloak.localtest.me:8180`, has ephemeral state, defaults to `admin` / `local-admin-password`, and accepts `KEYCLOAK_ADMIN` / `KEYCLOAK_ADMIN_PASSWORD` overrides. Document `mcp-test-user` / `local-test-password`, Web's Keycloak login button, and that local client secrets must be replaced outside Compose.

Include:

```powershell
$token = (Invoke-RestMethod -Method Post http://keycloak.localtest.me:8180/realms/ddi-registry/protocol/openid-connect/token -ContentType 'application/x-www-form-urlencoded' -Body 'grant_type=password&client_id=mcp-client&username=mcp-test-user&password=local-test-password&scope=openid%20ddi.registry.read%20ddi.registry.write').access_token
```

- [ ] **Step 2: Run final verification**

Run `docker compose config; dotnet build Ddi.Registry.Web.sln; dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj; dotnet test src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj`.

Expected: config and build pass; Web tests pass; MCP tests pass with container tests skipped only when Docker is absent.

- [ ] **Step 3: Run Docker smoke validation and commit**

Run `docker compose up --build -d; docker compose ps; docker compose down`.

Expected: DB, Keycloak, registry, and MCP start; shutting down leaves no Keycloak state.

Run `git add README.md; git commit -m "docs: describe local Keycloak OIDC workflow"`.
# Keycloak OIDC Support Design

- Date: 2026-08-05
- Status: Approved design, ready for implementation planning
- Scope: Add Keycloak-backed OpenID Connect support to the DDI Registry Web and MCP services, with local Compose validation and Docker-conditional automated integration tests.

## Goals

- Let users sign in to the Web application through Keycloak without removing the existing ASP.NET Core Identity password login.
- Validate MCP Bearer tokens against a real Keycloak issuer, including discovery, JWKS signing keys, issuer, audience, scopes, and identity claims.
- Make a complete local Keycloak, Web, MCP, and PostgreSQL environment available through Docker Compose.
- Keep the automated test suite runnable on developer machines without Docker by skipping only the Keycloak container tests.

## Non-goals

- Migrate Web authorization roles or permissions from ASP.NET Core Identity into Keycloak.
- Replace the existing local login, registration, password reset, or local Identity data model.
- Deploy or operate a production Keycloak instance.
- Store production passwords, client secrets, or administrator credentials in source control.

## Architecture

The MCP service remains a generic OAuth 2.0 protected resource. Its existing JwtBearer and MCP protected-resource metadata configuration remains provider-neutral. In local environments, `MCP:Oidc:Authority` points to the Keycloak realm issuer and `MCP:Oidc:Audience` identifies the MCP resource client.

The Web application retains ASP.NET Core Identity as its application session and authorization authority. It adds a named `Keycloak` OpenID Connect external-login scheme using authorization code flow with PKCE. The handler uses a separate Keycloak client from MCP because browser redirect URIs and client credentials have distinct requirements.

The Keycloak realm is test-only and stored as a versioned import file. It declares the following clients and identities:

| Client or user | Purpose | Permissions |
| --- | --- | --- |
| `registry-web` | Web external login using authorization code plus PKCE | OpenID Connect identity claims |
| `mcp-client` | Password-grant integration tests | `ddi.registry.read` and `ddi.registry.write` |
| `mcp-service-client` | Client-credentials integration tests | `ddi.registry.read` only |
| Test user | Maps by verified email to a seeded local `AspNetUsers` user | Used for password-grant MCP requests and Web external login |

The Keycloak clients issue tokens whose issuer is the realm URL, whose audience includes the MCP resource audience, and whose claims include `email`, `sub`, and the assigned scopes.

## Docker Compose

The Compose environment includes the existing PostgreSQL and Web services plus:

- `keycloak`: imports the versioned test realm on startup; exposes its administration and login UI for local development; uses a documented local-only administrator default that can be overridden through environment variables.
- `mcp`: builds and runs the MCP host, configures its Authority using the Compose-internal Keycloak realm address, and waits for PostgreSQL and Keycloak readiness.
- `registry`: receives the Web Keycloak Authority, client ID, client secret, and callback configuration. It waits for Keycloak readiness before startup.

The realm's redirect URI includes the local Web callback path `/signin-oidc`. No production hostname, credential, or secret is encoded in the realm import. Compose documentation distinguishes the local defaults from production configuration.

## Web Login Flow

1. The login page lists `Keycloak` alongside the existing local password form through ASP.NET Core Identity's external-login scheme discovery.
2. A user selects Keycloak and completes the authorization-code flow with PKCE.
3. The callback validates state, nonce, issuer, signature, and standard OpenID Connect response fields through the ASP.NET Core handler.
4. Identity attempts to find an existing external login. When absent, the external-login confirmation flow locates the existing local account by verified email and binds the Keycloak provider key to it. It does not silently create an authorized local account.
5. Missing email, a failed OIDC response, or a failed account binding returns a generic user-facing login error and records diagnostic detail only in server logs.

Application roles remain in the local Identity database. Keycloak realm roles do not directly grant Web application permissions.

## MCP Token Flow

1. The integration fixture starts Keycloak and waits for the realm discovery document.
2. A password-grant request obtains a real user token from `mcp-client`; the token carries the email of the seeded local Identity user.
3. The MCP host uses the Keycloak discovery document and JWKS to validate the token. Read operations require `ddi.registry.read`; `request_agency` requires `ddi.registry.write` and maps the caller to the existing local user.
4. A client-credentials request from `mcp-service-client` obtains a read-only token. Read operations succeed while write operations return the existing explicit missing-scope tool result.
5. Missing token, invalid audience, and missing scope requests retain their established rejection semantics: unauthorized HTTP requests receive `401` and authenticated callers without a required scope receive an explicit MCP error result.

## Testing

Existing handler-based MCP tests remain as fast unit and behavior coverage. New Keycloak integration tests use a Testcontainers Keycloak fixture and the same realm import as Compose. The fixture detects Docker availability before starting containers; it reports the Keycloak tests as skipped when Docker is unavailable, without hiding failures when Docker is available.

The real-provider tests cover:

- Discovery and JWKS-backed authentication of a password-grant user token.
- Correct issuer and audience validation.
- Read and write scope enforcement for password-grant tokens.
- Read-only client-credentials token behavior and write denial.
- Email-to-local-user mapping for `request_agency`.
- Unauthenticated and invalid-audience rejection.

Web tests cover conditional registration of the Keycloak scheme, its absence when its configuration is incomplete, and the existing-account external-login binding behavior. Protocol-level validation remains covered by the Keycloak integration fixture rather than mocked in Web tests.

## Files and Boundaries

Expected changes are limited to the Compose definition, Keycloak realm assets, Web authentication/configuration/tests, MCP container/configuration/tests, and local setup documentation. The shared database schema and current local Identity role model are unchanged.

No application runtime code calls Keycloak administration APIs or takes a dependency on a Keycloak SDK. Keycloak remains an OpenID Connect provider selected entirely through configuration.
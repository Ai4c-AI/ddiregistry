[![DOI](https://zenodo.org/badge/163341427.svg)](https://zenodo.org/badge/latestdoi/163341427)

# DDI Agency Registry software

## Platform

The DDI Agency Registry software runs on Linux, macOS, and Windows on the .NET 6 platform.
* https://www.microsoft.com/net/core/

## About

The DDI Agency Registry is a component of a free global unique identifier system for metadata producing organizations. The registry system provides an agency identifier which acts as both a namespace in a globally unique id as well as a pointer for distributed service resolution.
Based on ISO/IEC 11179 Part 6 International Registration Data Identifiers (11179 IRDIs), DDI agency identifiers are used as the Registration Authority (11179 RA) in a IRDI and in DDI URNs.

The DDI Agency Resolver is a free DNS SRV record-based resolution service for DDI agency identifiers, and enables discovery of web services and data services provided by an agency.

## Getting Started

The web application can be started using Docker compose using the following command:

```bash
docker-compose up --build
```

Once the services are up and running, visit [http://localhost:8000](http://localhost:8000)
on your browser to access the web application.

### Local Keycloak test identity provider

Docker Compose also starts a local Keycloak instance at
[http://keycloak.localtest.me:8180](http://keycloak.localtest.me:8180). The imported
`ddi-registry` realm is intended for local development and automated integration tests
only. It has no persistent volume, so recreating the Keycloak container imports a fresh
realm from `infra/keycloak/realm.json`.

The local admin account defaults to `admin` / `local-admin-password`; override it with
`KEYCLOAK_ADMIN` and `KEYCLOAK_ADMIN_PASSWORD`. The realm includes the test user
`mcp-test-user` / `local-test-password`, with the `ddi.registry.read` and
`ddi.registry.write` scopes. Replace all Compose default credentials before using this
configuration outside a local development environment.

To obtain a local MCP test token:

```bash
curl -X POST http://keycloak.localtest.me:8180/realms/ddi-registry/protocol/openid-connect/token \
	-H "Content-Type: application/x-www-form-urlencoded" \
	-d "grant_type=password&client_id=mcp-client&username=mcp-test-user&password=local-test-password&scope=openid%20ddi.registry.read%20ddi.registry.write"
```

The web application retains local ASP.NET Core Identity accounts and roles. A Keycloak
login can only link to an existing local account with the same email address; it never
creates a new registry account.

## Links

- [DDI Agency Registry]
- [DDI Alliance]
- [DDI Registry GitHub]

![DDI Logo][logo]

[DDI Alliance]: https://www.ddialliance.org
[DDI Agency Registry]: https://registry.ddialliance.org
[DDI Registry GitHub]: https://github.com/Colectica/ddiregistry/
[logo]: https://github.com/Colectica/ddiregistry/raw/master/src/Ddi.Registry.Web/wwwroot/assets/logo.png "DDI Alliance"

# LupiraDavApi

The Lupira platform's **CalDAV/CardDAV gateway**: one stateless .NET 10 service that serves phones and
desktops (iOS/macOS, Android via DAVx5, Thunderbird) a **single unified DAV account** — VEVENT calendars,
VTODO task lists, and address books — by translating the DAV protocol onto the LAN-only
[`/dav-backend` contract](docs/dav-backend-contract.md) of three domain services:

| Path marker | Backend | Payload |
|---|---|---|
| `/dav/u/{email}/cal/ev-{id}/` | lupira-cal-api | `text/calendar` (VEVENT) |
| `/dav/u/{email}/cal/td-{id}/` | lupira-tasks-api | `text/calendar` (VTODO) |
| `/dav/u/{email}/card/{id}/` | lupira-contact-api | `text/vcard` (3.0) |

One `calendar-home-set` lists both calendar kinds (clients split them by
`supported-calendar-component-set`); `addressbook-home-set` lists the address books. The collection-path
markers make routing stateless — the gateway holds **no database**: blobs, ETags, and sync tokens pass
through verbatim, and idempotency/concurrency is the backends' UID + ETag precondition semantics
(the gateway never retries a write).

- **Auth (client-facing)**: HTTP Basic → Authentik LDAP outpost bind (gated by the `caldav-users` group).
- **Auth (backend-facing)**: one client-credentials client (`lupira-dav-svc`) minting per-backend-audience
  bearers via scopes; the acting user rides the `{email}` path segment.
- **Failure rule**: any backend error during an enumeration → **503 for the whole response** — never a
  partial home listing (clients treat a vanished collection as deleted).
- Discovery: `/.well-known/caldav` + `/.well-known/carddav` → `/dav/`.

## Develop

```bash
dotnet test LupiraDavApi.slnx                       # unit (pure protocol: path/XML/credentials)
dotnet test tests/LupiraDavApi.IntegrationTests     # gateway over in-process backend stubs (no network)
```

In Development, Basic auth accepts any password (no LDAP) and backends are called with `X-Dev-User`
instead of a service bearer. Deployment config: DevOps repo `APIs/lupira-dav-api/` (authoritative);
[`deploy/`](deploy/) is a genericized mirror.

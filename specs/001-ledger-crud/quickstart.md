# Quickstart: Basic CRUD for Ledger Services

**Feature**: `001-ledger-crud` · **Plan**: `./plan.md` · **Date**: 2026-04-27

This quickstart documents how a developer brings the ledger service up
on a clean machine, exercises the gRPC contract, and validates that
observability, caching, and persistence are wired correctly. It is
written so a `/speckit-implement` run can use it as a smoke-test recipe.

Per Constitution v2.10.0 (Tech Stack > Local development
orchestration), the canonical local bring-up is **`dotnet run --project
src/AppHost`** — a single command that starts Postgres, Redis, the
Api, and the .NET Aspire dashboard (which doubles as the local OTLP
collector). `docker compose` remains as a documented fallback only.

---

## 1. Prerequisites

- **.NET 10 SDK** (latest stable). Verify with `dotnet --info` →
  `.NET SDK ... Version: 10.x`.
- **.NET Aspire is delivered as NuGet packages** (`Aspire.Hosting.AppHost`,
  `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.Redis`, plus the
  `Aspire.AppHost.Sdk` MSBuild SDK) referenced directly by
  `src/AppHost/`; pinned in `Directory.Packages.props` via CPM. The
  legacy `aspire` workload is deprecated — do **not** run
  `dotnet workload install aspire`. See
  https://aka.ms/aspire/support-policy. Docker remains the container
  backend used by Aspire's Postgres/Redis hosting integrations under
  the hood.
- **Docker** (for the container backend Aspire uses for Postgres +
  Redis, and for Testcontainers in the test suite). Docker Desktop or
  Colima both work.
- Optional: **`grpcurl`** for ad-hoc gRPC calls
  (`brew install grpcurl` on macOS).

---

## 2. Repository layout (after `/speckit-implement` runs)

```text
src/
├── Domain/                       (zero deps)
├── Application/                  → Domain
├── Infrastructure/               → Application
├── Infrastructure.Migrations/    DbUp init-container console (no Application dep)
├── Contracts/                    (zero src/ deps)
├── Api/                          → Application + Infrastructure + Contracts
└── AppHost/                      .NET Aspire orchestration (dev-loop only;
                                  references Api but Api does NOT reference back)

tests/
├── Domain.Tests/
├── Application.Tests/
├── Infrastructure.Tests/         (Testcontainers Postgres + Redis)
└── Api.IntegrationTests/         (WebApplicationFactory<Program> + GrpcChannel
                                   + Testcontainers — NOT Aspire.Hosting.Testing)

monitoring/
└── ledger-service.json           (Grafana dashboard, repo root)

Directory.Build.props
Directory.Packages.props
SddDemo.Ledger.sln                (includes the AppHost project)
docker-compose.dev.yml            (FALLBACK only)
```

---

## 3. One-shot local setup (canonical — Aspire AppHost)

```bash
# 1. Restore + build the whole solution (including the AppHost).
dotnet restore
dotnet build --no-restore -warnaserror

# 2. Bring up Postgres + Redis + Api + Aspire dashboard with one command.
dotnet run --project src/AppHost
#
#   → opens the .NET Aspire dashboard in your browser
#     (typically http://localhost:18888)
#   → exposes Postgres on an Aspire-mapped port (visible in the dashboard)
#   → exposes Redis on an Aspire-mapped port (visible in the dashboard)
#   → starts the Api with ConnectionStrings:ledger and ConnectionStrings:redis
#     injected automatically
#   → starts the OTLP collector backed by the Aspire dashboard
#     (logs / metrics / traces show up live in the dashboard's panels)
```

Schema is applied automatically: the AppHost wires
`src/Infrastructure.Migrations/` (a DbUp console) as an Aspire
**init-container** that runs before the Api boots — see Constitution
v2.11.0 (Tech Stack > Local development orchestration > Schema
migrations). DbUp's `schemaversions` table is the source of truth on
which scripts have already been applied; on subsequent restarts the
migrator skips them in <100 ms.

Schema scripts live as embedded SQL under
`src/Infrastructure.Migrations/Scripts/NNNN_*.sql`. New scripts are
append-only — never edit a committed script; ship `NNNN+1` instead.
DbUp is the only sanctioned migration tool (Constitution v2.11.0 >
Tech Stack > Persistence > Schema management).

Once the AppHost is running, the Api is reachable at the URL the
dashboard prints under the `api` resource — typically
`https://localhost:5001` (gRPC + HTTP/2). HTTP sidecar endpoints:

- `https://localhost:5001/swagger` — Swagger UI (Development only).
- `https://localhost:5001/health/live` — liveness probe.
- `https://localhost:5001/health/ready` — readiness (DB + Redis +
  backplane).

Every request MUST carry an `X-Owner-Id: <uuid>` header — the service
uses it for ownership scoping (FR-009) and audit-actor logging
(FR-012). The constitution (v3.0.0) does not govern authentication, so
a missing or unparseable header is rejected as malformed input
(`Validation` → `InvalidArgument`), not as an auth failure. Locally,
`AnonymousCurrentUser` falls back to a configured fixed developer UUID
in `Development` (set via `Identity:DevOwnerId` in
`src/Api/appsettings.Development.json` — bound to
`AnonymousCurrentUserOptions` in `Program.cs`) so Swagger / `grpcurl`
calls succeed without sending the header by hand.

### 3a. Fallback bring-up (`docker-compose.dev.yml` — SDK-less environments only)

For environments without the .NET SDK (CI smoke jobs, air-gapped
boxes, language-agnostic onboarding), the project ships a
`docker-compose.dev.yml` that mirrors the AppHost's resource set
(same image versions, ports, env vars):

```bash
docker compose -f docker-compose.dev.yml up -d
#   postgres:  localhost:5432  user=ledger  password=ledger  db=ledger
#   redis:     localhost:6379

# Apply DbUp migrations against the compose-managed Postgres.
ConnectionStrings__ledger="Host=localhost;Port=5432;Username=ledger;Password=ledger;Database=ledger" \
  dotnet run --project src/Infrastructure.Migrations

dotnet run --project src/Api
```

**This path does not start the Aspire dashboard.** Without the
dashboard you lose the local OTLP endpoint — point your standalone
OTLP collector container at the Api via `OTEL_EXPORTER_OTLP_ENDPOINT`
or accept that local traces/logs/metrics will not be aggregated.
Production export targets are unaffected.

> When both files exist, the AppHost is canonical — divergence between
> the AppHost resource graph and `docker-compose.dev.yml` (image
> versions, ports, env vars) is a review blocker (Constitution Tech
> Stack > Local development orchestration).

---

## 4. End-to-end smoke flow (User Stories 1–4)

Run with `grpcurl` against the running service. Replace `OWNER` with
the configured dev UUID; in production the gateway supplies it.

### 4.1 Create a ledger (US1, FR-001)

```bash
grpcurl -d '{
  "name": "Operating Account",
  "description": "Primary operating ledger",
  "currency_code": "USD"
}' -H 'x-owner-id: OWNER' \
  localhost:5001 sddDemo.ledger.v1.Ledgers/CreateLedger
```

Expected: a `LedgerView` with `status = LEDGER_STATUS_ACTIVE`, an `id`
(UUID), and a non-empty `version_token`. Save the `id` and
`version_token` for the next steps.

### 4.2 Get the ledger (US2, FR-005)

```bash
grpcurl -d '{ "id": "<id>" }' \
  -H 'x-owner-id: OWNER' \
  localhost:5001 sddDemo.ledger.v1.Ledgers/GetLedger
```

The second invocation should be served from FusionCache L1 (latency
drops by an order of magnitude). On the third invocation from a second
replica, L2 (Redis) and the backplane synchronise the entry.

### 4.3 List ledgers (US2, FR-006)

```bash
grpcurl -d '{ "page_size": 50 }' \
  -H 'x-owner-id: OWNER' \
  localhost:5001 sddDemo.ledger.v1.Ledgers/ListLedgers
```

Expected: the just-created ledger appears. Archived ledgers are excluded
unless `"include_archived": true` is set.

### 4.4 Update the ledger (US3, FR-007)

```bash
grpcurl -d '{
  "id": "<id>",
  "version_token": "<base64 from previous response>",
  "description": "Renamed for FY26",
  "update_mask": { "paths": ["description"] }
}' -H 'x-owner-id: OWNER' \
  localhost:5001 sddDemo.ledger.v1.Ledgers/UpdateLedger
```

Expected: a fresh `LedgerView` with the new description, an updated
`last_modified_at`, and a NEW `version_token` (the old one is now stale).

Conflict path: re-issue the same call with the *old* `version_token`.
Expected: `RpcException` with `StatusCode = AlreadyExists` and an
error code `ledger.conflict` per `ResultToRpcExceptionMapper`.

### 4.5 Archive then attempt to mutate (US3, FR-007a)

```bash
# Archive.
grpcurl -d '{
  "id": "<id>", "version_token": "<latest>",
  "status": "LEDGER_STATUS_ARCHIVED",
  "update_mask": { "paths": ["status"] }
}' -H 'x-owner-id: OWNER' \
  localhost:5001 sddDemo.ledger.v1.Ledgers/UpdateLedger

# Attempt to rename while archived → InvalidArgument.
grpcurl -d '{
  "id": "<id>", "version_token": "<latest>",
  "name": "Renamed",
  "update_mask": { "paths": ["name"] }
}' -H 'x-owner-id: OWNER' \
  localhost:5001 sddDemo.ledger.v1.Ledgers/UpdateLedger

# Un-archive (the only allowed transition while archived).
grpcurl -d '{
  "id": "<id>", "version_token": "<latest>",
  "status": "LEDGER_STATUS_ACTIVE",
  "update_mask": { "paths": ["status"] }
}' -H 'x-owner-id: OWNER' \
  localhost:5001 sddDemo.ledger.v1.Ledgers/UpdateLedger
```

### 4.6 Delete (US4, FR-008)

```bash
grpcurl -d '{ "id": "<id>", "version_token": "<latest>" }' \
  -H 'x-owner-id: OWNER' \
  localhost:5001 sddDemo.ledger.v1.Ledgers/DeleteLedger

# Subsequent Get should return NOT_FOUND.
grpcurl -d '{ "id": "<id>" }' \
  -H 'x-owner-id: OWNER' \
  localhost:5001 sddDemo.ledger.v1.Ledgers/GetLedger
```

Re-creating a ledger with the same name must succeed immediately
(FR-008).

---

## 5. Validating the cross-cutting wiring

### 5.1 Audit log (FR-012)

After running the smoke flow, query the database directly (admin/ops
path; no end-user surface). When using the AppHost, copy the Postgres
connection string from the Aspire dashboard:

```sql
SELECT id, actor_id, ledger_id, event_type, event_at
  FROM ledger_audit
 WHERE ledger_id = '<id>'
 ORDER BY event_at;
```

Expected: rows for `event_type = 1` (Create), `2` (Update — multiple),
`3` (Delete). All within 5 s of the corresponding gRPC call (SC-005).

### 5.2 Cache hit/miss (Aspire dashboard)

The Aspire dashboard's **Metrics** tab surfaces FusionCache counters
under the `SddDemo.Ledger` meter; expect non-zero values during the
smoke flow:

- `fusioncache_cache_hits_total{cache="default", level="L1"}` — non-zero
  after the second `GetLedger`.
- `fusioncache_cache_hits_total{cache="default", level="L2"}` — non-zero
  if you scale the API to two replicas (Aspire `WithReplicas(2)`) and
  read from the second.

### 5.3 Health probes

```bash
curl -fsk https://localhost:5001/health/live
curl -fsk https://localhost:5001/health/ready
```

`/health/ready` MUST return 503 if Postgres or Redis is down. With the
AppHost, stop a resource from the dashboard's "Resources" tab to
verify; with the compose fallback, `docker compose stop postgres`.

### 5.4 Trace correlation (Aspire dashboard)

The Aspire dashboard's **Traces** tab shows every gRPC call as a
single trace covering: gRPC server span → handler → repository → SQL
command → audit insert (same `trace_id`). Logs from the same call
carry the same `trace_id` field — pivot from a span to its log lines
in the dashboard's **Structured logs** tab.

When running through the compose fallback (no Aspire dashboard), point
your own OTLP collector at the Api via `OTEL_EXPORTER_OTLP_ENDPOINT`
to get the same view.

---

## 6. Running the test suite

Tests use **Testcontainers** for Postgres + Redis, NOT Aspire — per
Constitution v2.10.0 the AppHost is explicitly NOT a test host
(carve-out under Tech Stack > Local development orchestration).
`Aspire.Hosting.Testing` / `DistributedApplicationTestingBuilder` MUST
NOT appear on the canonical test path.

```bash
# Unit tests (no Docker required).
dotnet test tests/Domain.Tests
dotnet test tests/Application.Tests

# Infrastructure + integration (Testcontainers; needs Docker running).
dotnet test tests/Infrastructure.Tests
dotnet test tests/Api.IntegrationTests

# Coverage with patch-coverage gate (≥80%).
dotnet test \
  --collect:"XPlat Code Coverage" \
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura

reportgenerator \
  -reports:"**/coverage.cobertura.xml" \
  -targetdir:"./TestResults/coverage" \
  -reporttypes:"Html;TextSummary"
```

The integration suite (`Api.IntegrationTests`) hosts the service via
`WebApplicationFactory<Program>`, opens a `GrpcChannel` over the
in-memory test server, and exercises the generated client end-to-end —
the canonical pattern from constitution Principle II.

---

## 7. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `dotnet run --project src/AppHost` exits with "Aspire.Hosting.AppHost not found" | Aspire packages not restored | Run `dotnet restore`; verify `Directory.Packages.props` pins `Aspire.Hosting.AppHost`, `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.Redis`. |
| AppHost starts but Api fails to connect to Postgres/Redis | Connection-string injection not wired | Verify `Api/Program.cs` reads `builder.Configuration.GetConnectionString("ledger")` / `"redis"` (the names used in `AppHost/Program.cs`). |
| `relation "<table>" does not exist` at first request | Migrator init-container did not run (likely because AppHost wiring missed `WaitFor(migrator)` or the script in `src/Infrastructure.Migrations/Scripts/` is not embedded). Verify `schemaversions` table exists in Postgres; if missing, the migrator never ran — check the dashboard's `migrator` resource state and stderr. |
| `StatusCode.Internal` with correlation ID | Unhandled exception caught by safety-net interceptor | Inspect the Aspire dashboard's Structured logs filtered by `trace_id`, or — under the compose fallback — your log sink. |
| Update returns `AlreadyExists` immediately after a fresh read | Two clients raced and the other one won | Re-fetch the ledger and retry with the new `version_token` (FR-007). |
| Cache appears stale after an update | Tag invalidation skipped | Verify the command handler called `RemoveByTagAsync("owner:{ownerId}")` and (for Update/Delete) `RemoveByTagAsync("ledger:{ledgerId}")`. |
| `RpcException` with no detail on the wire | By design (Principle VI safety net) | The full exception is in logs with the same `trace_id`; never echoed on the wire. |
| Tests reference `Aspire.Hosting.Testing` | v2.10.0 carve-out violation | Remove the reference; use `WebApplicationFactory<Program>` + Testcontainers per `tests/Api.IntegrationTests/` pattern. |

# Quickstart: Basic CRUD for Ledger Services

**Feature**: `001-ledger-crud` · **Plan**: `./plan.md` · **Date**: 2026-04-27

This quickstart documents how a developer brings the ledger service up
on a clean machine, exercises the gRPC contract, and validates that
observability, caching, and persistence are wired correctly. It is
written so a `/speckit-implement` run can use it as a smoke-test recipe.

---

## 1. Prerequisites

- **.NET 10 SDK** (latest stable). Verify with `dotnet --info` →
  `.NET SDK ... Version: 10.x`.
- **Docker** (for PostgreSQL + Redis containers used by integration
  tests and local dev). Docker Desktop or Colima both work.
- **`dotnet-ef` global tool**: `dotnet tool install --global dotnet-ef`
  (used only for migrations against `Infrastructure.Migrations`).
- Optional: **`grpcurl`** for ad-hoc gRPC calls
  (`brew install grpcurl` on macOS).

---

## 2. Repository layout (after `/speckit-implement` runs)

```text
src/
├── Domain/                       (zero deps)
├── Application/                  → Domain
├── Infrastructure/               → Application
├── Infrastructure.Migrations/    → Application      (EF Core, migrations only)
├── Contracts/                    (zero src/ deps)
└── Api/                          → Application + Infrastructure + Contracts

tests/
├── Domain.Tests/
├── Application.Tests/
├── Infrastructure.Tests/
└── Api.IntegrationTests/

monitoring/
└── ledger-service.json           (Grafana dashboard, repo root)

Directory.Build.props
Directory.Packages.props
SddDemo.Ledger.sln
```

---

## 3. One-shot local setup

```bash
# 1. Bring up Postgres + Redis for local development.
docker compose -f docker-compose.dev.yml up -d
#   postgres:  localhost:5432  user=ledger  password=ledger  db=ledger
#   redis:     localhost:6379

# 2. Run migrations against the dev database.
dotnet ef database update \
    --project src/Infrastructure.Migrations \
    --startup-project src/Infrastructure.Migrations \
    --connection "Host=localhost;Port=5432;Username=ledger;Password=ledger;Database=ledger"

# 3. Restore + build.
dotnet restore
dotnet build --no-restore -warnaserror

# 4. Run the service.
dotnet run --project src/Api
#   gRPC + HTTP/2  → https://localhost:5001
#   HTTP sidecar  → https://localhost:5001/swagger
#                   https://localhost:5001/health/live
#                   https://localhost:5001/health/ready
```

Inside the on-prem perimeter, the trusted gateway sets the
`X-Owner-Id: <uuid>` header on every request; locally the
`AnonymousCurrentUser` falls back to a configured fixed developer UUID
in `Development` (set via `LedgerOptions:DevOwnerId` in
`appsettings.Development.json`).

---

## 4. End-to-end smoke flow (User Stories 1–4)

Run with `grpcurl` against the running service. Replace `OWNER` with the
configured dev UUID; in production the gateway supplies it.

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
path; no end-user surface):

```sql
SELECT id, actor_id, ledger_id, event_type, event_at
  FROM ledger_audit
 WHERE ledger_id = '<id>'
 ORDER BY event_at;
```

Expected: rows for `event_type = 1` (Create), `2` (Update — multiple),
`3` (Delete). All within 5 s of the corresponding gRPC call (SC-005).

### 5.2 Cache hit/miss

`/metrics` (Prometheus scrape) should show FusionCache counters
incrementing during the smoke flow:

- `fusioncache_cache_hits_total{cache="default", level="L1"}` — non-zero
  after the second `GetLedger`.
- `fusioncache_cache_hits_total{cache="default", level="L2"}` — non-zero
  if you scale the API to two replicas and read from the second.

### 5.3 Health probes

```bash
curl -fsk https://localhost:5001/health/live
curl -fsk https://localhost:5001/health/ready
```

`/health/ready` MUST return 503 if Postgres or Redis is down; bring
either container down with `docker compose stop postgres` to verify.

### 5.4 Trace correlation

With the OTLP collector running (or .NET Aspire dashboard in dev), every
gRPC call should produce a single trace covering: gRPC server span →
handler → repository → SQL command → audit insert (same trace_id). Logs
from the same call MUST carry the same `trace_id` field.

---

## 6. Running the test suite

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
| `EnsureCreated` error at startup | Migration project not referenced or DB not migrated | `dotnet ef database update --project src/Infrastructure.Migrations` |
| `StatusCode.Internal` with correlation ID | Unhandled exception caught by safety-net interceptor | Inspect logs at `Error` level, filter by `trace_id`. |
| Update returns `AlreadyExists` immediately after a fresh read | Two clients raced and the other one won | Re-fetch the ledger and retry with the new `version_token` (FR-007). |
| Cache appears stale after an update | Tag invalidation skipped | Verify the command handler called `RemoveByTagAsync("owner:{ownerId}")` and (for Update/Delete) `RemoveByTagAsync("ledger:{ledgerId}")`. |
| `RpcException` with no detail on the wire | By design (Principle VI safety net) | The full exception is in logs with the same `trace_id`; never echoed on the wire. |

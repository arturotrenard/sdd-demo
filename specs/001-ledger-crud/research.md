# Phase 0 Research: Basic CRUD for Ledger Services

**Feature**: `001-ledger-crud` · **Spec**: `./spec.md` · **Date**: 2026-04-27

This document resolves the open technical questions raised by the spec and the
Constitution Check, and records the rationale for each choice. It is the
single source of truth for the assumptions baked into `plan.md`,
`data-model.md`, `contracts/`, and `quickstart.md`.

---

## 1. Runtime, language, and SDK

- **Decision**: .NET 10 LTS, C# (latest stable shipped with .NET 10),
  `<TargetFramework>net10.0</TargetFramework>` solution-wide. Nullable
  reference types on; `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
  in `Directory.Build.props`. Central Package Management via
  `Directory.Packages.props` at the repo root.
- **Rationale**: User input pins .NET 10. Constitution Principle I and the
  Tech Stack section both mandate `net10.0`, NRT, warnings-as-errors, and
  CPM.
- **Alternatives considered**: .NET 8 (rejected — not the latest LTS named
  in the constitution); multi-targeting (rejected — no documented consumer
  for shared libraries in this feature).

## 2. Service surface — gRPC-first

- **Decision**: One gRPC service `Ledgers` exposing `CreateLedger`,
  `GetLedger`, `ListLedgers`, `UpdateLedger`, `DeleteLedger`. HTTP surface
  restricted to `/health/live`, `/health/ready`, `/swagger`,
  `/swagger/v1/swagger.json`. Proto package
  `sddDemo.ledger.v1`; file at `src/Contracts/Protos/ledger.v1.proto`.
  `Microsoft.AspNetCore.Grpc.JsonTranscoding` +
  `Microsoft.AspNetCore.Grpc.Swagger` wired so Swagger UI is live and
  documents the same wire format.
- **Rationale**: Constitution Principle III (gRPC-first). The spec is a
  service-style integration with no UI requirement (Assumptions in
  `spec.md`), so gRPC is the natural transport. Swagger gives humans a
  browsable contract without forking documentation.
- **Alternatives considered**: REST controllers (rejected — Principle III
  forbids them in new code); minimal-API JSON only (rejected — same
  reason; minimal APIs are reserved for the health + Swagger sidecar).

## 3. Persistence — PostgreSQL via Npgsql, DbUp (migrations) + Dapper (runtime)

- **Decision**: PostgreSQL 16+ as the database. Schema management via
  DbUp running as an Aspire init-container — a Generic Host console
  in `src/Infrastructure.Migrations/` (`Microsoft.NET.Sdk` +
  `OutputType=Exe`) that hosts a `BackgroundService` invoking DbUp
  against the Aspire-injected `ConnectionStrings:ledger`, then signals
  `IHostApplicationLifetime.StopApplication`. SQL ships under
  `Scripts/NNNN_*.sql` as embedded resources; DbUp's `schemaversions`
  table tracks applied scripts. Runtime CRUD via vanilla Dapper over a
  pooled `NpgsqlDataSource` registered as a singleton
  (`services.AddNpgsqlDataSource(...)`). All queries parameterised; no
  string concatenation into SQL.
- **Rationale**: Constitution Tech Stack > Persistence mandates the
  split responsibility: DbUp for schema, Dapper for runtime queries.
  DbUp is intentionally lightweight (raw SQL + a `schemaversions`
  tracking table) and fits the Aspire init-container pattern cleanly:
  the AppHost runs the migrator before the Api boots; no manual
  schema-application step exists in the dev loop. Pooled
  `NpgsqlDataSource` is the
  recommended high-throughput runtime entry point.
- **Alternatives considered**: ORM-driven migrations (rejected —
  Constitution names DbUp explicitly); raw `NpgsqlCommand` for runtime
  (allowed but Dapper's `QueryAsync<T>` / `ExecuteAsync` is the named
  idiom and keeps mapping out of the repository);
  `Dapper.Contrib`/`SimpleCRUD` (rejected by the constitution).

## 4. Caching — FusionCache L1 + L2 (Redis) + Backplane

- **Decision**: `ZiggyCreatures.FusionCache` is the only cache abstraction.
  Topology:
  - L1: `IMemoryCache` (in-process).
  - L2: Redis via `IDistributedCache` using
    `ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis`.
  - Backplane: Redis pub/sub via the same package.
  - Serializer: `FusionCacheSystemTextJsonSerializer`.
  - `DefaultEntryOptions`: `IsFailSafeEnabled = true`, soft timeout
    chosen above measured factory P50 once we have benchmark numbers
    (initial value `200ms` to be tuned via the
    `dotnet-diag:microbenchmarking` skill before merge).
- **Caching scope and decorator placement**:
  - `IFusionCache` is consumed by **decorators wrapping repositories**
    (e.g., `CachingLedgerRepository : ILedgerRepository`), wired with
    `services.Decorate<ILedgerRepository, CachingLedgerRepository>()`
    (Scrutor). Caching MUST NOT be embedded inside the repository itself.
  - **Cached operations**: `GetById(ownerId, ledgerId)` and
    `List(ownerId, page, includeArchived)`. Writes (`Create`, `Update`,
    `Delete`) are NOT cached.
  - **Cache keys / tags**:
    - Per-ledger key: `ledger:{ownerId}:{ledgerId}`.
    - Per-list key: `ledger:list:{ownerId}:{includeArchived}:{page}:{pageSize}`.
    - Tags: every entry tagged with `owner:{ownerId}` and (for the
      single-fetch entry) `ledger:{ledgerId}`.
  - **Result-pattern interaction (constitution)**: only
    `Result.Success` values are cached. `Result.Failure` (e.g.,
    `NotFound`) is NEVER cached — a follow-up create can immediately
    remediate it.
  - **Invalidation**: command handlers (Create/Update/Delete) call
    `IFusionCache.RemoveByTagAsync("owner:{ownerId}")` after a successful
    write. `Update` and `Delete` additionally call
    `RemoveByTagAsync("ledger:{ledgerId}")`. The plan calls this out
    per the constitution's "Result-pattern interaction" rule.
- **Rationale**: Constitution Tech Stack > Caching mandates FusionCache
  with the L1+L2+Backplane topology (no carve-out justified for this
  feature) and forbids caching inside the repository class. User input
  pins Redis as the cache, which is exactly the L2/backplane combination
  FusionCache supports.
- **Alternatives considered**: Memory-only L1 (rejected — explicitly
  forbidden as a default); Microsoft `HybridCache` (rejected — superseded
  by FusionCache in the constitution); embedding cache in the
  `DapperLedgerRepository` (rejected — Principle VI > Repository caching
  Decorator mandate).

## 5. Optimistic concurrency — version token shape

- **Decision**: A monotonically-increasing 64-bit integer column `version`
  on the `ledger` row, returned to clients as an opaque base64 string in
  the protobuf message (`bytes version_token = N;`). Every successful
  `UPDATE` sets `version = version + 1` in the same SQL statement
  (`UPDATE ledger SET ..., version = version + 1 WHERE id = $id AND
  version = $expected_version RETURNING version, ...`). Zero-row affected
  on the `WHERE` clause maps to `Result.Failure(new Error("ledger.conflict",
  ..., ErrorType.Conflict))` → `StatusCode.AlreadyExists` per the
  boundary translator.
- **Rationale**: A bigint version is cheap, monotonic, and lets us encode
  the "compare-and-swap" atomically inside the `UPDATE` so the database
  is the single arbiter of conflicts (no SELECT-then-UPDATE race). Wire
  representation is opaque so a future migration to a different scheme
  (rowversion, ULID, etc.) does not break clients. FR-002 + FR-007
  require token return on every read and rejection of stale tokens with a
  conflict response.
- **Alternatives considered**:
  - PostgreSQL `xmin` system column (rejected — leaks transaction IDs
    and changes on `VACUUM FULL`/`CLUSTER` operations);
  - `last_modified_at` timestamp (rejected — not strictly monotonic
    across clock skew);
  - UUIDv7 / ULID per-update token (works, but adds a generation step
    with no benefit over an atomic `version + 1`).

## 6. Audit log — schema, writes, retention

- **Decision**:
  - Table `ledger_audit` with columns `id bigserial`, `actor_id uuid`,
    `ledger_id uuid`, `event_type smallint` (1=create, 2=update,
    3=delete), `event_at timestamptz default now()`,
    `payload jsonb` (snapshot of the post-event ledger row, or the
    pre-delete row for `delete`). Index on `(event_at)` for purge,
    `(ledger_id, event_at desc)` for admin lookup.
  - Audit rows are written **inside the same transaction** as the
    state-changing SQL — same `NpgsqlConnection` + `NpgsqlTransaction`,
    committed atomically. This satisfies SC-005 (audit appears within
    5 s) trivially: it is synchronous with the operation.
  - **Retention purge**: a daily `IHostedService` runs
    `DELETE FROM ledger_audit WHERE event_at < now() - interval '1 year'`
    at 03:00 UTC. The job logs the row count deleted at `Information`
    level and exposes a counter `ledger.audit.purged` (Principle IV).
  - **Access surface**: NO gRPC method, NO HTTP endpoint exposes the
    audit log (FR-012). Read access is via direct SQL by admin/ops only;
    that is out of scope for this feature.
- **Rationale**: Storing the snapshot `payload` lets ops reconstruct
  pre-/post-update state without joining historical revisions. Daily
  purge is simpler than per-row TTL and keeps the index small. Writing in
  the same transaction is the only way to guarantee no orphaned audit
  entries (or missing ones) under crash conditions.
- **Alternatives considered**:
  - Outbox + async audit writer (rejected — adds infrastructure for no
    payoff at this scale; the spec explicitly requires within-5-second
    visibility, and synchronous is the safest path);
  - PostgreSQL row-level triggers (rejected — pushes business logic into
    the database and bypasses the Application layer; harder to test);
  - Per-row `expires_at` + partial-index purge (works but more moving
    parts than a daily `DELETE`).

## 7. Identity context — `ICurrentUser` (feature-level seam)

- **Decision**: Introduce
  `Application/Abstractions/Identity/ICurrentUser.cs` with a single
  `Result<Guid> ResolveOwnerId()` method. Provide an
  `Infrastructure/Identity/AnonymousCurrentUser.cs` implementation that
  reads the caller-supplied `X-Owner-Id` request header (a UUID) and
  falls back to a configured fixed developer UUID in `Development`.
  Register as `Scoped`. A missing or unparseable header returns
  `Result.Failure(... ErrorType.Validation)` — there is no auth concept
  in this service.
- **Rationale**: The constitution (v3.0.0) does not govern authentication
  or authorization, but this feature still needs to differentiate
  behaviour by caller identity (FR-009 ownership scoping, FR-012 audit
  actor). `ICurrentUser` is therefore a **feature-level seam**, not a
  constitutional readiness stub: it isolates handlers from the transport
  detail of how the owner UUID arrives, and keeps owner resolution out
  of every command/query DTO.
- **Alternatives considered**:
  - Pass `ownerId` as an explicit field in every Application command/query
    (rejected — couples handlers to transport and clutters every DTO with
    a field every caller would have to populate identically);
  - No abstraction, read `HttpContext` directly from handlers (rejected —
    couples Application to ASP.NET hosting and makes unit-testing
    handlers awkward).

## 8. Validation strategy — Data Annotations + `IValidatableObject`

- **Decision**: Every DTO-shaped type carries Data Annotations:
  - **Application command/query inputs** (`CreateLedgerCommand`,
    `UpdateLedgerCommand`, `GetLedgerQuery`, `ListLedgersQuery`,
    `DeleteLedgerCommand`): `[Required]`, `[StringLength(100)]`,
    `[StringLength(500)]`, custom `[ValidIsoCurrency]`,
    `[Range(1, 200)]` on page-size, etc.
  - **Translation/mapping records** between protobuf messages and
    Application commands (one record per gRPC method): the same
    attributes applied to the hand-written record. Protobuf-generated
    types in `Contracts/` are NOT decorated (carve-out per the
    constitution); validation runs on the mapped record.
  - **Configuration shapes** (`LedgerOptions`, `FusionCacheOptions`):
    decorated and bound via `IOptions<T>` with
    `ValidateDataAnnotations()` + `ValidateOnStart()`.
  - **Domain types** (`Ledger`): the `LedgerBuilder.Build()` calls
    `DomainValidator.Validate(ledger)` before returning `Success`.
  - **Cross-property invariants** (e.g., "name not whitespace-only"):
    `IValidatableObject.Validate(ValidationContext)`.
- **Currency validation**: a `ValidIsoCurrencyAttribute : ValidationAttribute`
  that consults a `static readonly ImmutableHashSet<string>` of ISO 4217
  alphabetic codes (sourced once from
  `System.Globalization.CultureInfo.GetCultures(CultureTypes.SpecificCultures)`
  → `RegionInfo.ISOCurrencySymbol` and frozen at startup).
- **Rationale**: Constitution Principle VI > Validation. No bare `if`
  checks in handlers or builders. Centralised through
  `DomainValidator.Validate<T>` so every tier produces the same
  `Result.Failure(new Error("Validation", "...", ErrorType.Validation))`
  shape.
- **Alternatives considered**: FluentValidation (rejected — Data
  Annotations + `IValidatableObject` is the constitution's chosen idiom);
  hardcoded currency list in code (works, but pulling from `RegionInfo`
  keeps the list authoritative and avoids drift).

## 9. Pagination — keyset over offset

- **Decision**: Cursor-based (keyset) pagination keyed by
  `(last_modified_at DESC, id DESC)`. The cursor is an opaque
  base64-encoded `(timestamp, id)` tuple. Default page size 50, max 200,
  enforced at the Application command. Listing is deterministic
  (`ORDER BY last_modified_at DESC, id DESC`).
- **Rationale**: SC-003 requires p95 <1 s for users with up to 1 000
  ledgers; keyset pagination scales without `OFFSET` performance
  degradation and is stable under concurrent inserts. Determinism
  (Acceptance Scenario 2.4) requires an explicit secondary sort key on
  `id`.
- **Alternatives considered**: `OFFSET/LIMIT` (acceptable at 1 000 rows
  but degrades on growth and is non-stable under inserts; rejected
  because keyset costs nothing extra here);
  full result set with no pagination (rejected — FR-006 requires basic
  pagination).

## 10. Observability wiring (per `dotnet-observability` skill)

- **Decision**: Single `UseOtlpExporter()` call in `Program.cs`; resource
  attributes `service.name=ledger-service`, `service.version=<gitsha>`.
  Sources/meters:
  - `ActivitySource("SddDemo.Ledger.*")` (registered with glob
    `AddSource("SddDemo.Ledger.*")`);
  - `IMeterFactory.Create("SddDemo.Ledger")` for counters
    `ledger.created`, `ledger.updated`, `ledger.deleted`,
    `ledger.archived`, `ledger.audit.purged` and a histogram
    `ledger.operation.duration` with explicit buckets
    `[10ms, 25ms, 50ms, 100ms, 250ms, 500ms, 1s, 2.5s, 5s, 10s]`.
  - FusionCache OTel instrumentation registered with the same provider.
- **Local OTLP endpoint**: per the v2.10.0 Tech Stack amendment, the
  local collector is the **.NET Aspire dashboard** spun up by
  `src/AppHost/` (see §15). The dashboard publishes the OTLP endpoint
  (`OTEL_EXPORTER_OTLP_ENDPOINT`) as a connection string; the AppHost
  forwards it to the Api project so the existing `UseOtlpExporter()`
  call requires no `F5`-specific wiring. Production deployments still
  export to OTLP → Prometheus + Tempo + Loki unchanged.
- Health checks: `/health/live` (always 200), `/health/ready` validates
  DB (Npgsql) + Redis + cache backplane.
- Dashboard: committed at `monitoring/ledger-service.json` (repo root,
  per Principle IV). Generated and published via Phase 4 of the
  `dotnet-observability` skill.
- **Rationale**: Constitution Principle IV mandates the 5-phase flow,
  dot-namespacing, low-cardinality tags, explicit histogram buckets, and
  `/health/*` filtering from tracing. The Aspire dashboard satisfies
  the local-collector requirement without an extra container, while
  the production path remains the same OTLP → backend pipeline.

## 11. Test strategy

- **Decision**:
  - **Unit tests** (`Domain.Tests`, `Application.Tests`,
    `Infrastructure.Tests`): xUnit + FluentAssertions, mocks via
    NSubstitute. Failure-path assertions use
    `result.IsFailure.Should().BeTrue()` /
    `result.Error!.Code.Should().Be("…")` — never `Should().Throw<>()`
    for code that returns `Result`.
  - **Integration tests** (`Api.IntegrationTests`):
    `WebApplicationFactory<Program>` hosts the service in-memory, opens a
    `GrpcChannel` over `factory.CreateClient()`, and exercises the
    generated gRPC client end-to-end. PostgreSQL is provisioned via
    Testcontainers (`Testcontainers.PostgreSql`) so migrations actually
    run; Redis via `Testcontainers.Redis` so the FusionCache L2 +
    backplane are exercised.
  - **Coverage**: ≥80% patch coverage gate (constitution Principle II)
    measured by `coverlet.collector` and a diff-aware reporter. Whole-
    project coverage is reported for trend tracking.
- **Rationale**: Constitution Principle II + the spec's CRUD surface
  fits cleanly into per-handler unit tests + per-method gRPC integration
  tests. Testcontainers gives deterministic DB/Redis without leaking
  state between test runs.

## 12. Background work — audit purge

- **Decision**: A `BackgroundService`
  `AuditRetentionPurgeService` runs once at startup (catch-up) and then
  daily at 03:00 UTC via a `PeriodicTimer`. It calls
  `IAuditRepository.PurgeOlderThanAsync(TimeSpan.FromDays(365), ct)`
  which executes the `DELETE` statement and increments the
  `ledger.audit.purged` counter. Unhandled exceptions crash the host (the
  default .NET 6+ behaviour), per the constitution's "exceptions still
  allowed > BackgroundService failures" rule.
- **Rationale**: A small, in-process daily job is the simplest thing that
  meets FR-012's retention requirement. It keeps the deployment surface
  to one container.
- **Alternatives considered**: External cron (works, but adds a deploy
  artefact); PostgreSQL `pg_cron` (works, but couples retention logic to
  the DB and bypasses the Application layer / observability counter).

## 13. Currency list refresh

- **Decision**: ISO 4217 alphabetic codes are read once at process
  startup from `RegionInfo`/`CultureInfo` and frozen into an
  `ImmutableHashSet<string>` exposed as a singleton
  `ICurrencyCatalog.IsSupported(string code)`. No runtime refresh.
- **Rationale**: ISO 4217 changes rarely; pulling from the BCL avoids a
  hand-maintained list. A process restart picks up any framework update.

## 14. Local development orchestration — .NET Aspire AppHost (v2.10.0)

- **Decision**: A new `src/AppHost/SddDemo.Ledger.AppHost.csproj`
  project — referencing `Aspire.Hosting.AppHost`,
  `Aspire.Hosting.PostgreSQL`, and `Aspire.Hosting.Redis` — owns local-
  developer orchestration. Its `Program.cs` declares the resources via
  Aspire hosting integrations and wires the connection-string
  references into the `Api` project:

  ```csharp
  var builder = DistributedApplication.CreateBuilder(args);

  var postgres = builder.AddPostgres("postgres")
      .WithDataVolume()                       // persistent across restarts
      .AddDatabase("ledger");

  var redis = builder.AddRedis("redis");

  builder.AddProject<Projects.SddDemo_Ledger_Api>("api")
      .WithReference(postgres)
      .WithReference(redis)
      .WaitFor(postgres)
      .WaitFor(redis);

  builder.Build().Run();
  ```

  Canonical bring-up: **`dotnet run --project src/AppHost`**. This
  single command starts Postgres, Redis, the Api, and the Aspire
  dashboard (which publishes the local OTLP endpoint — see §10).
  `Api/Program.cs` reads `ConnectionStrings:ledger` and
  `ConnectionStrings:redis` (injected by Aspire) for `AddNpgsqlDataSource`
  and the FusionCache Redis L2 + backplane registration; no per-
  environment branching is needed.

- **Carve-outs** (load-bearing, per the v2.10.0 amendment):
  - **Tests stay on Testcontainers** (Principle II). The integration
    suite continues to host the service via `WebApplicationFactory<Program>`
    + `GrpcChannel`, with Postgres and Redis spun per test class via
    `Testcontainers.PostgreSql` / `Testcontainers.Redis`.
    `Aspire.Hosting.Testing` / `DistributedApplicationTestingBuilder`
    are explicitly NOT used — Aspire is a dev-loop tool, not a test
    host, and re-introducing it on the test path would duplicate the
    per-test isolation Testcontainers already provides and re-litigate
    the `WebApplicationFactory<Program>` integration pattern.
  - **Production does not use the AppHost.** Deployments target plain
    containers (compose/Helm/k8s manifests). The AppHost project is a
    developer-experience artifact; `Api`/`Application`/`Infrastructure`/
    `Domain` MUST NOT reference it back. The solution file lists it,
    but no runtime project does.
  - **`docker-compose.dev.yml` is a documented fallback only** for
    SDK-less environments (CI smoke jobs, air-gapped boxes,
    language-agnostic onboarding). When both exist they MUST stay in
    sync (image versions, ports, env vars). The AppHost is canonical;
    divergence is a review blocker.

- **Migrations interplay (init-container)**: the AppHost registers
  `src/Infrastructure.Migrations/` as a project resource that depends on
  Postgres being ready, and the Api waits on the migrator's "Running"
  state via `.WaitFor(migrator)`. The migrator is a Generic Host
  console that runs DbUp against the Aspire-injected
  `ConnectionStrings:ledger`, applies any pending `Scripts/NNNN_*.sql`
  embedded resources, and exits cleanly. Result: schema is present
  before the first request lands, with no manual step in the dev loop
  (Constitution v2.11.0 > Tech Stack > Local development orchestration
  > Schema migrations).

- **Rationale**: The v2.10.0 amendment makes the Aspire AppHost the
  single one-command bring-up across every service in the workspace.
  It eliminates the multi-step "compose up → migrate → run" recipe and
  consolidates the local OTLP collector into the same dashboard that
  shows logs/metrics/traces interactively. The carve-outs are the
  load-bearing part — keeping Testcontainers on the test path
  preserves the constitution Principle II discipline, and forbidding
  AppHost references from runtime projects keeps the deployment
  artifact untouched.

- **Alternatives considered**:
  - Plain `docker compose` only (rejected — v2.10.0 makes the AppHost
    NON-NEGOTIABLE; compose may remain only as fallback).
  - `Aspire.Hosting.Testing` for the test path (rejected — explicit
    v2.10.0 carve-out forbids it; Testcontainers already provides the
    isolation guarantee and `WebApplicationFactory<Program>` is the
    canonical integration host).
  - Embedding the AppHost in `Api` itself (rejected — runtime projects
    MUST NOT reference the AppHost; that would couple the deployment
    artifact to a developer-experience tool).

## 15. Out of scope (re-confirmed from `spec.md`)

The following are **not** addressed in this plan:

- Authentication / authorisation: out of constitutional scope (v3.0.0).
  `ICurrentUser` reads the caller-supplied `X-Owner-Id` header for
  owner-scoping / audit purposes only;
- Posting transactions or journal entries into a ledger;
- Changing a ledger's currency post-creation;
- An end-user-facing audit-log viewer;
- Any UI / front-end.

---

## Summary table

| Area | Choice |
|---|---|
| Runtime | .NET 10 LTS, C# (latest) |
| Transport | gRPC (`Grpc.AspNetCore` + JsonTranscoding + Swagger) |
| Proto package | `sddDemo.ledger.v1` |
| DB | PostgreSQL 16+ via Npgsql; pooled `NpgsqlDataSource` |
| Schema | DbUp init-container in `src/Infrastructure.Migrations/` (`Scripts/NNNN_*.sql`) |
| Runtime CRUD | Vanilla Dapper, parameterised SQL |
| Cache | FusionCache L1+L2 (Redis) + Backplane; decorator via Scrutor |
| Concurrency | bigint `version` column; opaque base64 token on the wire |
| Audit | `ledger_audit` table; same-tx writes; daily purge >1 yr |
| Identity | `ICurrentUser` stub + header-based anonymous impl |
| Validation | Data Annotations + `IValidatableObject` + `DomainValidator` |
| Pagination | Keyset on `(last_modified_at DESC, id DESC)`; default 50 |
| Observability | OTLP single exporter; FusionCache OTel; dashboard at repo root |
| Tests | xUnit + FluentAssertions + NSubstitute; Testcontainers for IT |
| Audit purge | `BackgroundService` daily at 03:00 UTC |
| Local-dev orchestration | `src/AppHost/` .NET Aspire project; `dotnet run --project src/AppHost`; Aspire dashboard = local OTLP endpoint; compose = fallback only; tests stay on Testcontainers |

All `NEEDS CLARIFICATION` markers from the Technical Context are
resolved by the choices above.

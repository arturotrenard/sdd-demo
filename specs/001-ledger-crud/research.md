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

## 3. Persistence — PostgreSQL via Npgsql, EF (migrations only) + Dapper (runtime)

- **Decision**: PostgreSQL 16+ as the database. Schema management via
  EF Core 10 migrations in a sibling project
  `src/Infrastructure.Migrations/` with its own `LedgerMigrationsDbContext`
  (NOT registered for runtime DI). Runtime CRUD via vanilla Dapper over a
  pooled `NpgsqlDataSource` registered as a singleton
  (`services.AddNpgsqlDataSource(...)`). All queries parameterised; no
  string concatenation into SQL.
- **Rationale**: Constitution Tech Stack > Persistence mandates the split
  responsibility: EF for schema only, Dapper for runtime queries. Pooled
  `NpgsqlDataSource` is the recommended high-throughput entry point.
- **Alternatives considered**: EF Core for runtime CRUD (rejected — banned
  by the constitution); raw `NpgsqlCommand` (allowed but Dapper's
  `QueryAsync<T>` / `ExecuteAsync` is the named idiom and keeps mapping
  out of the repository); `Dapper.Contrib`/`SimpleCRUD` (rejected by the
  constitution).

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

## 7. Identity context — `ICurrentUser` stub (architectural readiness)

- **Decision**: Introduce
  `Application/Abstractions/Identity/ICurrentUser.cs` with a `UserId UserId`
  property and a single-method shape. Provide an
  `Infrastructure/Identity/AnonymousCurrentUser.cs` implementation that
  reads a stable `X-Owner-Id` request header (set by the trusted gateway
  inside the on-prem perimeter) and falls back to a configured fixed
  developer ID in `Development`. Register as `Scoped`.
- **Rationale**: Constitution Principle V — auth is deferred for
  on-premise deployment, BUT this feature differentiates behaviour by
  caller identity (FR-009: ownership scoping; FR-012: actor in the
  audit log). The architectural-readiness sub-section makes
  `ICurrentUser` **REQUIRED** in that case so the future swap to a real
  authenticated implementation is a configuration change, not a handler
  diff. The header-based stub is a documented pattern for trusted-gateway
  on-prem.
- **Alternatives considered**:
  - Pass `ownerId` as an explicit field in every Application command/query
    (rejected — couples handlers to transport, makes the future auth
    swap a sweep across every handler);
  - No abstraction, hardcode a placeholder owner (rejected — defeats the
    architectural-readiness rule and is review-blocking under V).

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
- Health checks: `/health/live` (always 200), `/health/ready` validates
  DB (Npgsql) + Redis + cache backplane.
- Dashboard: committed at `monitoring/ledger-service.json` (repo root,
  per Principle IV). Generated and published via Phase 4 of the
  `dotnet-observability` skill.
- **Rationale**: Constitution Principle IV mandates the 5-phase flow,
  dot-namespacing, low-cardinality tags, explicit histogram buckets, and
  `/health/*` filtering from tracing.

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

## 14. Out of scope (re-confirmed from `spec.md`)

The following are **not** addressed in this plan:

- Authentication / authorisation implementation (deferred per Principle V;
  `ICurrentUser` stub only);
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
| Schema | EF Core 10 migrations in `src/Infrastructure.Migrations/` |
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

All `NEEDS CLARIFICATION` markers from the Technical Context are
resolved by the choices above.

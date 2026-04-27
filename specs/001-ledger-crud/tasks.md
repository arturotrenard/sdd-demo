---
description: "Task list for Basic CRUD for Ledger Services"
---

# Tasks: Basic CRUD for Ledger Services

**Input**: Design documents from `/specs/001-ledger-crud/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ledger.v1.proto, quickstart.md

**Tests**: REQUIRED. Constitution Principle II mandates xUnit + FluentAssertions + NSubstitute with a ≥80% patch-coverage gate. Tests are written FIRST per story and MUST fail before the implementation tasks land.

**Organization**: Tasks are grouped by user story so each can be implemented and verified independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User-story label (US1, US2, US3, US4); omitted in Setup, Foundational, and Polish phases
- File paths are absolute relative to the repo root (`/Users/arturotrenard/projects/sdd-demo`)

## Path Conventions

- Source: `src/{Domain,Application,Infrastructure,Infrastructure.Migrations,Contracts,Api}/`
- Tests: `tests/{Domain.Tests,Application.Tests,Infrastructure.Tests,Api.IntegrationTests}/`
- Monitoring: `monitoring/`
- Repo root: `Directory.Build.props`, `Directory.Packages.props`, `SddDemo.Ledger.sln`, `docker-compose.dev.yml`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Bootstrap the .NET 10 solution, central package management, and the local Postgres + Redis dev stack so every later task has a stable foundation.

- [ ] T001 Create the solution file `SddDemo.Ledger.sln` at the repo root and add the six source projects (`src/Domain`, `src/Application`, `src/Infrastructure`, `src/Infrastructure.Migrations`, `src/Contracts`, `src/Api`) and four test projects (`tests/Domain.Tests`, `tests/Application.Tests`, `tests/Infrastructure.Tests`, `tests/Api.IntegrationTests`) per `plan.md` §Project Structure.
- [ ] T002 Author `Directory.Build.props` at the repo root pinning `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>`, and the project-wide analyzers (per Constitution Principle I and `dotnet-msbuild:directory-build-organization`).
- [ ] T003 [P] Author `Directory.Packages.props` at the repo root with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` and pin every NuGet listed in `plan.md` §Primary Dependencies (gRPC, Npgsql, Dapper, FusionCache + Redis backplane, Scrutor, OpenTelemetry, xUnit/FluentAssertions/NSubstitute/Testcontainers) per `dotnet-nuget:convert-to-cpm`.
- [ ] T004 [P] Create `docker-compose.dev.yml` at the repo root provisioning Postgres 16 (`localhost:5432`, db/user/password=`ledger`) and Redis (`localhost:6379`) per `quickstart.md` §3.
- [ ] T005 [P] Create `src/Domain/SddDemo.Ledger.Domain.csproj`, `src/Application/SddDemo.Ledger.Application.csproj`, `src/Infrastructure/SddDemo.Ledger.Infrastructure.csproj`, `src/Infrastructure.Migrations/SddDemo.Ledger.Infrastructure.Migrations.csproj`, `src/Contracts/SddDemo.Ledger.Contracts.csproj`, and `src/Api/SddDemo.Ledger.Api.csproj` with the one-directional `<ProjectReference>` graph from `plan.md` §Project Structure (Application→Domain; Infrastructure→Application; Infrastructure.Migrations→Application; Api→Application+Infrastructure+Contracts).
- [ ] T006 [P] Create the four test project files (`tests/Domain.Tests/SddDemo.Ledger.Domain.Tests.csproj`, `tests/Application.Tests/SddDemo.Ledger.Application.Tests.csproj`, `tests/Infrastructure.Tests/SddDemo.Ledger.Infrastructure.Tests.csproj`, `tests/Api.IntegrationTests/SddDemo.Ledger.Api.IntegrationTests.csproj`) referencing xUnit, FluentAssertions, NSubstitute, `coverlet.collector`, and (for the latter two) `Testcontainers.PostgreSql` + `Testcontainers.Redis`.
- [ ] T007 Configure `.editorconfig` and analyzer rule severities at the repo root to enforce nullable + style rules called out in Constitution Principle I (Idiomatic C# & Code Quality).

**Checkpoint**: `dotnet restore && dotnet build` succeeds against the empty solution graph.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the cross-cutting primitives — Result/Error, validation, identity stub, currency catalog, persistence wiring, observability, gRPC pipeline, and the audit-retention background job — that every user story will consume.

**⚠️ CRITICAL**: No user-story phase may begin until this phase is complete.

### Domain primitives

- [ ] T008 [P] Implement `src/Domain/Common/ErrorType.cs` (enum: `Validation`, `NotFound`, `Conflict`, `Forbidden`, `Failure`) per `data-model.md` §3.
- [ ] T009 [P] Implement `src/Domain/Common/Error.cs` as `public sealed record Error(string Code, string Message, ErrorType Type)` per `data-model.md` §3.
- [ ] T010 [P] Implement `src/Domain/Common/Result.cs` as `Result` and `Result<T>` `readonly record struct` types with `Success`/`Failure` factories, `IsSuccess`/`IsFailure`/`Error`/`Value`, and `Map`/`Bind` helpers (Constitution Principle VI > Result pattern).
- [ ] T011 Implement `src/Domain/Common/DomainValidator.cs` as a static helper wrapping `Validator.TryValidateObject` (with `validateAllProperties: true`) and aggregating `ValidationResult`s into `Result.Failure(new Error("validation", ..., ErrorType.Validation))` per `research.md` §8 (depends on T009, T010).

### Currency

- [ ] T012 [P] Implement `src/Domain/Currency/ICurrencyCatalog.cs` exposing `bool IsSupported(string code)` per `data-model.md` §3.
- [ ] T013 [P] Implement `src/Domain/Currency/ValidIsoCurrencyAttribute.cs` as a `ValidationAttribute` that resolves `ICurrencyCatalog` from the `ValidationContext` per `research.md` §8 (depends on T012).
- [ ] T014 Implement `src/Infrastructure/Currency/CurrencyCatalog.cs` populating an `ImmutableHashSet<string>` from `CultureInfo.GetCultures(CultureTypes.SpecificCultures) → RegionInfo.ISOCurrencySymbol` once at construction per `research.md` §13 (depends on T012).

### Identity

- [ ] T015 [P] Implement `src/Application/Abstractions/Identity/ICurrentUser.cs` exposing `Guid UserId { get; }` per `research.md` §7.
- [ ] T016 Implement `src/Infrastructure/Identity/AnonymousCurrentUser.cs` reading `X-Owner-Id` from `IHttpContextAccessor` and falling back to `LedgerOptions.DevOwnerId` in Development per `research.md` §7 and `quickstart.md` §3 (depends on T015).

### Persistence base

- [ ] T017 Implement `src/Application/Abstractions/Persistence/ILedgerRepository.cs` with the five methods (`CreateAsync`, `UpdateAsync`, `DeleteAsync`, `GetByIdAsync`, `ListAsync`) signed exactly as in `data-model.md` §5 (depends on T010).
- [ ] T018 [P] Implement `src/Application/Abstractions/Persistence/IAuditRepository.cs` with `WriteAsync(AuditEntry, NpgsqlConnection, NpgsqlTransaction, CancellationToken)` and `PurgeOlderThanAsync(TimeSpan, CancellationToken)` per `data-model.md` §5 and `research.md` §6.
- [ ] T019 Implement `src/Infrastructure/Persistence/DataSourceFactory.cs` using `NpgsqlDataSourceBuilder` to register a pooled singleton `NpgsqlDataSource` per `research.md` §3.
- [ ] T020 Embed initial Dapper SQL resources at `src/Infrastructure/Persistence/Sql/Ledger/{Insert,UpdateOptimistic,DeleteOptimistic,GetById,ListKeyset}.sql` and `src/Infrastructure/Persistence/Sql/Audit/{Insert,PurgeOlderThan}.sql` matching the columns and predicates in `data-model.md` §2 and `research.md` §5–6, marked `<EmbeddedResource>` in the `.csproj`.

### EF Core migrations project

- [ ] T021 Implement `src/Infrastructure.Migrations/LedgerMigrationsDbContext.cs` modelling the `ledger` and `ledger_audit` tables exactly as defined in `data-model.md` §2 (NOT registered for runtime DI per `research.md` §3).
- [ ] T022 Generate the initial EF migration (`dotnet ef migrations add Initial --project src/Infrastructure.Migrations --startup-project src/Infrastructure.Migrations`) and verify the produced SQL matches the schema, indexes, and check constraints in `data-model.md` §2 (depends on T021).

### Observability + health

- [ ] T023 Implement OpenTelemetry wiring in `src/Api/Observability/OpenTelemetryRegistration.cs` with a single `UseOtlpExporter()`, resource attributes `service.name=ledger-service`/`service.version=<gitsha>`, ASP.NET Core + gRPC client + FusionCache instrumentation, and the explicit histogram buckets listed in `research.md` §10 (per `dotnet-aspnet:configuring-opentelemetry-dotnet` and `dotnet-observability` Phases 1–3).
- [ ] T024 [P] Implement `src/Api/Observability/LedgerMetrics.cs` exposing the counters `ledger.created`, `ledger.updated`, `ledger.deleted`, `ledger.archived`, `ledger.audit.purged` and the histogram `ledger.operation.duration` via `IMeterFactory.Create("SddDemo.Ledger")` per `research.md` §10.
- [ ] T025 [P] Implement `src/Api/Health/HealthChecksRegistration.cs` mapping `/health/live` (always 200) and `/health/ready` (Npgsql + Redis + FusionCache backplane probes) per `research.md` §10 and `quickstart.md` §5.3.

### Caching wiring

- [ ] T026 Register FusionCache in `src/Api/Caching/FusionCacheRegistration.cs` with L1 `IMemoryCache`, L2 `IDistributedCache` over Redis (`Microsoft.Extensions.Caching.StackExchangeRedis`), `FusionCacheSystemTextJsonSerializer`, the Redis backplane (`ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis`), `DefaultEntryOptions { IsFailSafeEnabled = true, FactorySoftTimeout = 200ms }`, and the OTel instrumentation hook per `research.md` §4 (per the `fusion-cache` skill).

### gRPC pipeline

- [ ] T027 Wire `src/Contracts/SddDemo.Ledger.Contracts.csproj` with `<Protobuf Include="Protos\**\*.proto" GrpcServices="Both" />` and copy `specs/001-ledger-crud/contracts/ledger.v1.proto` into `src/Contracts/Protos/ledger.v1.proto` as the build-time source of truth per `plan.md` §Project Structure.
- [ ] T028 Implement `src/Api/Grpc/ResultToRpcExceptionMapper.cs` translating `Result.Failure` codes/types to gRPC status codes per the table in `plan.md` Constitution Check (Principle VI) and the boundary rules in `research.md` §5 (`Conflict → AlreadyExists`, `NotFound → NotFound`, `Validation → InvalidArgument`, `Forbidden → PermissionDenied`, default `Internal`).
- [ ] T029 [P] Implement `src/Api/Grpc/Interceptors/ExceptionSafetyNetInterceptor.cs` as the FIRST interceptor in the chain — catches unhandled exceptions, logs with the request `trace_id`, and rethrows a sanitised `RpcException(StatusCode.Internal, "internal_error")` per Constitution Principle VI and `quickstart.md` §7.
- [ ] T030 [P] Implement `src/Api/Grpc/Interceptors/ValidationInterceptor.cs` running `DomainValidator.Validate` on the `*RequestMap` for the incoming RPC and short-circuiting with `InvalidArgument` on failure (per `research.md` §8).

### Background work

- [ ] T031 Implement `src/Infrastructure/Background/AuditRetentionPurgeService.cs` as a `BackgroundService` that runs the purge once at startup and then daily at 03:00 UTC via `PeriodicTimer`, calling `IAuditRepository.PurgeOlderThanAsync(TimeSpan.FromDays(365), ct)` and incrementing `ledger.audit.purged` per `research.md` §12 (depends on T018, T024).

### Composition root

- [ ] T032 Implement `src/Api/Program.cs` composing all registrations: `AddNpgsqlDataSource`, `AddSingleton<ICurrencyCatalog>`, `AddScoped<ICurrentUser, AnonymousCurrentUser>`, `AddScoped<ILedgerRepository, LedgerRepository>` then `services.Decorate<ILedgerRepository, CachingLedgerRepository>()` (Scrutor), gRPC + JsonTranscoding + Swagger (Development-only), `MapGrpcService<LedgersService>()`, the two interceptors registered in chain order (safety net first, validation second), the health-check endpoints, and the OpenTelemetry registration. HTTP surface restricted to `/health/*`, `/swagger`, `/swagger/v1/swagger.json` (`plan.md` §Constraints; `research.md` §2). Stub `LedgersService` empty for now — story phases fill in each RPC.
- [ ] T033 [P] Implement `src/Api/Configuration/LedgerOptions.cs` with `[Required]` Data Annotations (e.g., `DevOwnerId`, page-size defaults) and bind via `services.AddOptions<LedgerOptions>().BindConfiguration("Ledger").ValidateDataAnnotations().ValidateOnStart()` per `research.md` §8 (depends on T032).

### Foundational tests

- [ ] T034 [P] Add `tests/Domain.Tests/Common/ResultTests.cs` covering Success/Failure construction, `Map`, `Bind`, and equality semantics for `Result`/`Result<T>` (depends on T010).
- [ ] T035 [P] Add `tests/Domain.Tests/Common/DomainValidatorTests.cs` asserting attribute aggregation produces a single `Error("validation", ..., ErrorType.Validation)` carrying every member name (depends on T011).
- [ ] T036 [P] Add `tests/Infrastructure.Tests/Currency/CurrencyCatalogTests.cs` asserting common ISO codes (`USD`, `EUR`, `JPY`) return `true` and unknown codes (`XYZ`, `""`, `null`) return `false` (depends on T014).
- [ ] T037 [P] Add `tests/Api.IntegrationTests/Health/HealthEndpointsTests.cs` using `WebApplicationFactory<Program>` to assert `/health/live` returns 200 and `/health/ready` returns 200 with all containers up (depends on T025, T032).

**Checkpoint**: All cross-cutting primitives compile, the host boots, `/health/ready` is green against Testcontainers Postgres + Redis, and the gRPC service registry is in place with no RPCs implemented yet.

---

## Phase 3: User Story 1 - Create a new ledger (Priority: P1) 🎯 MVP

**Goal**: An authenticated caller can `CreateLedger` (gRPC) with a name + ISO-4217 currency + optional description; the server persists the row, writes a `Create` audit entry in the same transaction, returns a `LedgerView` with `id`, `version_token`, and timestamps, and rejects duplicate names within the same owner with a clear conflict.

**Independent Test**: Run the `CreateLedger` gRPC call from `quickstart.md` §4.1 with a unique name → expect a `LedgerView`. Re-issue the same call with the same name → expect `RpcException(StatusCode = AlreadyExists, "ledger.name_already_exists")`. Inspect `ledger_audit` → expect one row with `event_type = 1` for the created ledger (FR-001, FR-003, FR-004, FR-012).

### Domain for US1

- [ ] T038 [P] [US1] Implement `src/Domain/Ledgers/LedgerStatus.cs` (enum: `Active = 1`, `Archived = 2`) per `data-model.md` §1.1.
- [ ] T039 [P] [US1] Implement `src/Domain/Auditing/AuditEventType.cs` (enum: `Create = 1`, `Update = 2`, `Delete = 3`) per `data-model.md` §1.2.
- [ ] T040 [P] [US1] Implement `src/Domain/Ledgers/LedgerErrors.cs` exposing the canonical static `Error` instances (`NameAlreadyExists`, `NotFound`, `Conflict`, `ArchivedReadOnly`, `ArchivedCannotDelete`, `Validation`) per `data-model.md` §3.2.
- [ ] T041 [US1] Implement `src/Domain/Ledgers/Ledger.cs` as `sealed class : IValidatableObject` with private constructor, `private init` properties, the Data Annotations from `data-model.md` §3.1, the cross-property `Validate(...)` (whitespace name, `CreatedAt <= LastModifiedAt`), and a nested `LedgerBuilder` whose `Build()` returns `DomainValidator.Validate(...)` (depends on T011, T013, T038).
- [ ] T042 [US1] Implement `src/Domain/Auditing/AuditEntry.cs` as `sealed class` with private constructor, `private init` properties (`Id`, `ActorId`, `LedgerId`, `EventType`, `EventAt`, `Payload` as `JsonDocument`), Data Annotations matching `data-model.md` §1.2, and a nested `AuditEntryBuilder.Build() → Result<AuditEntry>` (depends on T011, T039).

### Application + mapping for US1

- [ ] T043 [US1] Implement `src/Application/Features/Ledgers/Commands/CreateLedger/CreateLedgerCommand.cs` exactly as specified in `data-model.md` §4 (Data Annotations on each property).
- [ ] T044 [US1] Implement `src/Application/Features/Ledgers/Commands/CreateLedger/CreateLedgerHandler.cs` registered `AddScoped<CreateLedgerHandler>()` — resolves `ICurrentUser`, builds a `Ledger` with a UUIDv7 `Id` and `Version = 1`, builds a `Create` `AuditEntry` with the post-state JSON snapshot, calls `ILedgerRepository.CreateAsync`, returns the resulting `Result<Ledger>`, and increments `ledger.created` on success (per `research.md` §6 + §10; depends on T015, T017, T018, T024, T041, T042, T043).
- [ ] T045 [US1] Implement `src/Application/Features/Ledgers/Commands/CreateLedger/CreateLedgerRequestMap.cs` mapping `CreateLedgerRequest` (proto) → validated `CreateLedgerRequestMap` → `CreateLedgerCommand` exactly as in `data-model.md` §4.1 (trimmed name, normalised currency to upper invariant; `DomainValidator.Validate`).

### Infrastructure for US1

- [ ] T046 [US1] Implement `src/Infrastructure/Persistence/AuditRepository.cs` with `WriteAsync` issuing the embedded `Audit/Insert.sql` against the supplied `NpgsqlConnection` + `NpgsqlTransaction` (same-tx invariant from `research.md` §6) and `PurgeOlderThanAsync` issuing `Audit/PurgeOlderThan.sql` (depends on T018, T020).
- [ ] T047 [US1] Implement `src/Infrastructure/Persistence/LedgerRepository.cs` with `CreateAsync(Ledger, AuditEntry, ct)` opening an `NpgsqlConnection`+`NpgsqlTransaction` from the pooled `NpgsqlDataSource`, executing `Ledger/Insert.sql` and `IAuditRepository.WriteAsync` in the same transaction, mapping `23505` (`unique_violation`) on `ux_ledger_owner_name_lower` to `Result.Failure(LedgerErrors.NameAlreadyExists)`, and committing on success (depends on T017, T019, T020, T040, T046).
- [ ] T048 [US1] Implement `src/Infrastructure/Persistence/CachingLedgerRepository.cs` decorating `ILedgerRepository`; for `CreateAsync` it forwards to the inner repo and on `IsSuccess` calls `IFusionCache.RemoveByTagAsync($"owner:{ownerId}", ct)` (Get/List/Update/Delete are added in later story phases) per `research.md` §4 (depends on T017, T026, T047).

### gRPC surface for US1

- [ ] T049 [US1] Implement `src/Api/Grpc/LedgersService.cs` `CreateLedger(...)` override — calls `CreateLedgerRequestMap.From(request)`, dispatches to `CreateLedgerHandler`, and returns `ledger.ToLedgerView()` (a small mapper from Domain `Ledger` to proto `LedgerView`, encoding `Version` as 8-byte big-endian into `version_token`); on failure delegates to `ResultToRpcExceptionMapper`. Add the `Domain → LedgerView` mapper alongside as `src/Api/Grpc/LedgerViewMapper.cs` (depends on T028, T044, T045).

### Tests for US1

- [ ] T050 [P] [US1] `tests/Domain.Tests/Ledgers/LedgerBuilderTests.cs` — happy-path build returns `Success`; whitespace-only name returns `Failure` with code `validation`; unsupported currency returns `Failure` with code `validation`; description > 500 chars returns `Failure`; `CreatedAt > LastModifiedAt` returns `Failure` (depends on T041).
- [ ] T051 [P] [US1] `tests/Application.Tests/Ledgers/CreateLedgerHandlerTests.cs` — using NSubstitute fakes for `ILedgerRepository`, `ICurrentUser`, `IFusionCache`: asserts the handler resolves `OwnerId` from `ICurrentUser`, constructs a `Ledger` with `Status=Active`, `Version=1`, builds a `Create` `AuditEntry` with the post-state payload, calls `CreateAsync` exactly once, and propagates failures unchanged (depends on T044).
- [ ] T052 [P] [US1] `tests/Infrastructure.Tests/Persistence/LedgerRepository_CreateAsyncTests.cs` — Testcontainers Postgres; verifies a successful insert returns `Result<Ledger>` with `Version=1`, that `ledger_audit` has exactly one row with `event_type=1`, that a duplicate-name insert (case-insensitive) returns `Result.Failure(LedgerErrors.NameAlreadyExists)`, and that a failed insert leaves no `ledger_audit` row (atomic same-tx) (depends on T047).
- [ ] T053 [P] [US1] `tests/Api.IntegrationTests/Ledgers/CreateLedgerEndpointTests.cs` — `WebApplicationFactory<Program>` + Testcontainers + `GrpcChannel.ForAddress(factory.CreateClient())`. Acceptance Scenarios 1.1 (happy path), 1.2 (duplicate-name → `AlreadyExists`), 1.3 (missing required field → `InvalidArgument`); Edge: unsupported currency → `InvalidArgument`; FR-009: missing `X-Owner-Id` is rejected (depends on T049).

**Checkpoint**: User Story 1 is independently demonstrable — `quickstart.md` §4.1 runs end-to-end, `ledger.created` counter increments, the audit row appears within 5 s (SC-005), and `dotnet test` passes for `Domain.Tests`/`Application.Tests`/`Infrastructure.Tests`/`Api.IntegrationTests`.

---

## Phase 4: User Story 2 - View ledgers (Priority: P1)

**Goal**: An authenticated caller can `GetLedger(id)` for a single owned ledger and `ListLedgers(...)` with keyset pagination + `include_archived` filter, served through the FusionCache decorator.

**Independent Test**: After creating two ledgers (US1), `ListLedgers` returns both in deterministic order (`last_modified_at DESC, id DESC`); `GetLedger` by id returns full details; `GetLedger` for an unknown or non-owned id returns `NOT_FOUND` without leaking ownership; paginating past the page size returns the next page (FR-005, FR-006, FR-009; Acceptance Scenarios 2.1–2.4).

### Application + mapping for US2

- [ ] T054 [P] [US2] Implement `src/Application/Features/Ledgers/Queries/GetLedger/GetLedgerQuery.cs` with `[Required] Guid Id` per `data-model.md` §4.
- [ ] T055 [P] [US2] Implement `src/Application/Features/Ledgers/Queries/ListLedgers/ListLedgersQuery.cs` and `src/Application/Features/Ledgers/Queries/ListLedgers/LedgerListPage.cs` (immutable `record LedgerListPage(IReadOnlyList<Ledger> Items, string? NextPageCursor)`) per `data-model.md` §4.
- [ ] T056 [US2] Implement `src/Application/Features/Ledgers/Queries/GetLedger/GetLedgerHandler.cs` (`AddScoped`) — resolves `OwnerId` from `ICurrentUser` and dispatches to `ILedgerRepository.GetByIdAsync(id, ownerId, ct)` (depends on T015, T017, T054).
- [ ] T057 [US2] Implement `src/Application/Features/Ledgers/Queries/ListLedgers/ListLedgersHandler.cs` (`AddScoped`) — clamps `PageSize` to `[1, 200]` (default 50), forwards to `ILedgerRepository.ListAsync`, and returns the page (depends on T015, T017, T055).
- [ ] T058 [P] [US2] Implement `src/Application/Features/Ledgers/Queries/GetLedger/GetLedgerRequestMap.cs` mapping `GetLedgerRequest` (proto) → validated `GetLedgerRequestMap` → `GetLedgerQuery` (parses canonical UUID; `DomainValidator.Validate`) per `data-model.md` §4.1.
- [ ] T059 [P] [US2] Implement `src/Application/Features/Ledgers/Queries/ListLedgers/ListLedgersRequestMap.cs` mapping `ListLedgersRequest` (proto) → validated `ListLedgersRequestMap` → `ListLedgersQuery` (defaults `page_size=0` to 50, validates `[Range(1,200)]`).

### Infrastructure for US2

- [ ] T060 [US2] Extend `src/Infrastructure/Persistence/LedgerRepository.cs` with `GetByIdAsync(ledgerId, ownerId, ct)` running `Ledger/GetById.sql` (parameterised, owner-scoped) and returning `Result.Failure(LedgerErrors.NotFound)` when the row is absent or owned by another user (FR-009 — no information leak) (depends on T020, T040, T047).
- [ ] T061 [US2] Extend `src/Infrastructure/Persistence/LedgerRepository.cs` with `ListAsync(ownerId, includeArchived, pageCursor, pageSize, ct)` running `Ledger/ListKeyset.sql` with `ORDER BY last_modified_at DESC, id DESC LIMIT @pageSize + 1`, decoding the opaque base64 `(timestamp, id)` cursor, and emitting a non-null `NextPageCursor` only when the extra row was returned (per `research.md` §9) (depends on T020, T060).
- [ ] T062 [US2] Extend `src/Infrastructure/Persistence/CachingLedgerRepository.cs` with `GetByIdAsync` (cache key `ledger:{ownerId}:{ledgerId}`, tags `owner:{ownerId}` + `ledger:{ledgerId}`) and `ListAsync` (cache key `ledger:list:{ownerId}:{includeArchived}:{pageCursor}:{pageSize}`, tag `owner:{ownerId}`); only `Result.Success` is cached (per `research.md` §4) (depends on T026, T048, T060, T061).

### gRPC surface for US2

- [ ] T063 [US2] Implement `LedgersService.GetLedger(...)` and `LedgersService.ListLedgers(...)` overrides in `src/Api/Grpc/LedgersService.cs` — dispatch through the `*RequestMap`s and the corresponding handler, map `Result.Failure(LedgerErrors.NotFound)` to `RpcException(StatusCode.NotFound)` via `ResultToRpcExceptionMapper`, and project results through `LedgerViewMapper` (depends on T049, T056, T057, T058, T059, T062).

### Tests for US2

- [ ] T064 [P] [US2] `tests/Application.Tests/Ledgers/GetLedgerHandlerTests.cs` and `tests/Application.Tests/Ledgers/ListLedgersHandlerTests.cs` — NSubstitute on `ILedgerRepository`/`ICurrentUser`. Verify owner-scoping, page-size clamping (0→50, 500→`Failure`/`InvalidArgument`), and that `Result.Failure(NotFound)` is propagated unchanged (depends on T056, T057).
- [ ] T065 [P] [US2] `tests/Infrastructure.Tests/Persistence/LedgerRepository_GetListTests.cs` — Testcontainers Postgres seeded with rows for two owners. Asserts owner-scoped read, NOT_FOUND on missing/not-owned, deterministic ordering (`last_modified_at DESC, id DESC`), keyset cursor round-trip across two pages, and that `include_archived=false` excludes archived rows (depends on T060, T061).
- [ ] T066 [P] [US2] `tests/Infrastructure.Tests/Persistence/CachingLedgerRepository_ReadTests.cs` — Testcontainers Postgres + Redis. First `GetByIdAsync` calls the inner repo; second hits L1 (verify via NSubstitute call count on a wrapping mock); only `Success` is cached (a `Failure(NotFound)` followed by an inserted row resolves on the second call); list-page caching keys differ by `(includeArchived, pageCursor, pageSize)` (depends on T062).
- [ ] T067 [P] [US2] `tests/Api.IntegrationTests/Ledgers/GetAndListLedgersEndpointTests.cs` — Acceptance Scenarios 2.1, 2.2, 2.3 (NOT_FOUND with no ownership leak), 2.4 (pagination); FR-009 cross-owner read returns NOT_FOUND identical to a missing ledger (depends on T063).

**Checkpoint**: Read paths for User Story 2 are demonstrable end-to-end, served by the cache decorator, with deterministic ordering and keyset pagination.

---

## Phase 5: User Story 3 - Update an existing ledger (Priority: P2)

**Goal**: An authenticated caller can `UpdateLedger` to change name/description/status with a valid `version_token`. Optimistic concurrency rejects stale tokens; archived ledgers reject every change except un-archive; the audit log records every successful update.

**Independent Test**: Acceptance Scenarios 3.1–3.6 from `spec.md` and `quickstart.md` §4.4–§4.5 — happy-path description change, invalid-currency reject, cross-owner reject, archived-read-only reject, un-archive succeeds, stale `version_token` returns `AlreadyExists` (FR-007, FR-007a).

### Application + mapping for US3

- [ ] T068 [US3] Implement `src/Application/Features/Ledgers/Commands/UpdateLedger/UpdateLedgerCommand.cs` per `data-model.md` §4 (`Id`, `VersionToken`, optional `Name`/`Description`/`Status`).
- [ ] T069 [US3] Implement `src/Application/Features/Ledgers/Commands/UpdateLedger/UpdateLedgerHandler.cs` (`AddScoped`) — loads the current ledger via `GetByIdAsync` (owner-scoped), enforces the state machine in `data-model.md` §1.1 (Active→{Active,Archived,attribute updates}; Archived→only `Status=Active`; otherwise `Failure(LedgerErrors.ArchivedReadOnly)`), builds the new `Ledger` with `Version = current + 1` and `LastModifiedAt = TimeProvider.GetUtcNow()`, builds an `Update` `AuditEntry`, calls `ILedgerRepository.UpdateAsync(updated, expectedVersion, audit, ct)`, increments `ledger.updated` (and `ledger.archived` on a status transition to Archived) on success (depends on T015, T017, T024, T040, T041, T042, T068).
- [ ] T070 [US3] Implement `src/Application/Features/Ledgers/Commands/UpdateLedger/UpdateLedgerRequestMap.cs` mapping `UpdateLedgerRequest` → validated `UpdateLedgerRequestMap` → `UpdateLedgerCommand`, decoding `version_token` (8-byte big-endian → `long`), honouring `update_mask` so only listed fields are forwarded (per `contracts/ledger.v1.proto` and `data-model.md` §4.1).

### Infrastructure for US3

- [ ] T071 [US3] Extend `src/Infrastructure/Persistence/LedgerRepository.cs` with `UpdateAsync(Ledger updated, long expectedVersion, AuditEntry audit, ct)` issuing `Ledger/UpdateOptimistic.sql` (`UPDATE ... SET ..., version = version + 1 WHERE id = @id AND owner_id = @ownerId AND version = @expected RETURNING ...`); zero rows affected → `Result.Failure(LedgerErrors.Conflict)`; row missing on a follow-up `GetByIdAsync` → `Result.Failure(LedgerErrors.NotFound)`; writes the audit row in the same transaction; maps `unique_violation` on rename to `Result.Failure(LedgerErrors.NameAlreadyExists)` (depends on T020, T040, T046, T047).
- [ ] T072 [US3] Extend `src/Infrastructure/Persistence/CachingLedgerRepository.cs` with `UpdateAsync` — forwards to the inner repo and on `IsSuccess` calls `IFusionCache.RemoveByTagAsync($"owner:{ownerId}", ct)` and `RemoveByTagAsync($"ledger:{ledgerId}", ct)` per `research.md` §4 (depends on T026, T062, T071).

### gRPC surface for US3

- [ ] T073 [US3] Implement `LedgersService.UpdateLedger(...)` override in `src/Api/Grpc/LedgersService.cs` — dispatches through `UpdateLedgerRequestMap` and `UpdateLedgerHandler`; maps `Conflict` → `AlreadyExists`, `NotFound` → `NotFound`, `Validation` (including `ArchivedReadOnly`) → `InvalidArgument` via `ResultToRpcExceptionMapper`; returns the new `LedgerView` with the refreshed `version_token` (depends on T028, T069, T070, T072).

### Tests for US3

- [ ] T074 [P] [US3] `tests/Application.Tests/Ledgers/UpdateLedgerHandlerTests.cs` — covers each branch of the state machine: Active rename, Active→Archived, Archived→Active, Archived rename returns `Failure(ArchivedReadOnly)`, mismatched expected version is propagated unchanged, ledger not owned returns `Failure(NotFound)` without distinguishing from missing (depends on T069).
- [ ] T075 [P] [US3] `tests/Infrastructure.Tests/Persistence/LedgerRepository_UpdateAsyncTests.cs` — Testcontainers Postgres: successful update increments `version` and writes one `event_type=2` audit row in the same transaction; stale `expected` returns `Failure(Conflict)` and writes no audit row; rename to a duplicate name returns `Failure(NameAlreadyExists)` (depends on T071).
- [ ] T076 [P] [US3] `tests/Infrastructure.Tests/Persistence/CachingLedgerRepository_InvalidationTests.cs` — Testcontainers Postgres + Redis: a successful update invalidates both `owner:{ownerId}` and `ledger:{ledgerId}` tags (subsequent `GetByIdAsync` and `ListAsync` both miss L1 and re-load) (depends on T072).
- [ ] T077 [P] [US3] `tests/Api.IntegrationTests/Ledgers/UpdateLedgerEndpointTests.cs` — Acceptance Scenarios 3.1–3.6, including the optimistic-concurrency loser receiving `RpcException(StatusCode = AlreadyExists, "ledger.conflict")` exactly as specified in `quickstart.md` §4.4 (depends on T073).

**Checkpoint**: Update flow is demonstrable; concurrent writers are arbitrated by the database; archived ledgers are read-only except for un-archive.

---

## Phase 6: User Story 4 - Delete a ledger (Priority: P3)

**Goal**: An authenticated caller can hard-delete an `Active` ledger they own; archived ledgers reject delete with a clear "un-archive first" error; subsequent reads return NOT_FOUND; the audit row remains for the retention window.

**Independent Test**: Acceptance Scenarios 4.1–4.4 — happy-path delete then NOT_FOUND on Get; cross-owner delete leaves the ledger intact and returns NOT_FOUND; deleting an already-deleted ledger returns NOT_FOUND; deleting an archived ledger returns InvalidArgument with code `ledger.archived.cannot_delete`. Re-creating a ledger with the same name immediately after delete succeeds (FR-008).

### Application + mapping for US4

- [ ] T078 [US4] Implement `src/Application/Features/Ledgers/Commands/DeleteLedger/DeleteLedgerCommand.cs` per `data-model.md` §4.
- [ ] T079 [US4] Implement `src/Application/Features/Ledgers/Commands/DeleteLedger/DeleteLedgerHandler.cs` (`AddScoped`) — loads the ledger owner-scoped, rejects archived with `Failure(LedgerErrors.ArchivedCannotDelete)`, builds a `Delete` `AuditEntry` with the pre-delete row snapshot, calls `ILedgerRepository.DeleteAsync(id, ownerId, expectedVersion, audit, ct)`, increments `ledger.deleted` on success (depends on T015, T017, T040, T042, T078).
- [ ] T080 [US4] Implement `src/Application/Features/Ledgers/Commands/DeleteLedger/DeleteLedgerRequestMap.cs` mapping `DeleteLedgerRequest` → validated `DeleteLedgerRequestMap` → `DeleteLedgerCommand`, decoding `version_token` (8-byte big-endian → `long`).

### Infrastructure for US4

- [ ] T081 [US4] Extend `src/Infrastructure/Persistence/LedgerRepository.cs` with `DeleteAsync(ledgerId, ownerId, expectedVersion, audit, ct)` running `Ledger/DeleteOptimistic.sql` (`DELETE FROM ledger WHERE id = @id AND owner_id = @ownerId AND version = @expected RETURNING id`); writes the audit row in the same transaction; zero rows → `Result.Failure(LedgerErrors.NotFound)` when the row is absent and `Result.Failure(LedgerErrors.Conflict)` when the row exists but the version mismatches (handler distinguishes via a pre-fetch) (depends on T020, T040, T046, T071).
- [ ] T082 [US4] Extend `src/Infrastructure/Persistence/CachingLedgerRepository.cs` with `DeleteAsync` — forwards and on `IsSuccess` calls `IFusionCache.RemoveByTagAsync($"owner:{ownerId}", ct)` and `RemoveByTagAsync($"ledger:{ledgerId}", ct)` (depends on T026, T072, T081).

### gRPC surface for US4

- [ ] T083 [US4] Implement `LedgersService.DeleteLedger(...)` override in `src/Api/Grpc/LedgersService.cs` returning `DeleteLedgerResponse` (empty) on success and routing failures through `ResultToRpcExceptionMapper` (`ArchivedCannotDelete → InvalidArgument`, `NotFound → NotFound`, `Conflict → AlreadyExists`) (depends on T028, T079, T080, T082).

### Tests for US4

- [ ] T084 [P] [US4] `tests/Application.Tests/Ledgers/DeleteLedgerHandlerTests.cs` — covers active-delete success, archived-delete rejection, owner-mismatch returning `Failure(NotFound)` (no leak), version-mismatch propagating as `Failure(Conflict)` (depends on T079).
- [ ] T085 [P] [US4] `tests/Infrastructure.Tests/Persistence/LedgerRepository_DeleteAsyncTests.cs` — Testcontainers Postgres: successful delete removes the row and writes an `event_type=3` audit row in the same transaction; the audit row remains after delete (FR-008); a follow-up Create with the same name succeeds (depends on T081).
- [ ] T086 [P] [US4] `tests/Api.IntegrationTests/Ledgers/DeleteLedgerEndpointTests.cs` — Acceptance Scenarios 4.1–4.4; verifies that immediately re-creating a ledger with the same name succeeds end-to-end through `CreateLedger` (depends on T083).

**Checkpoint**: All four user stories are independently functional; the full `quickstart.md` §4 smoke flow runs green from end to end.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Tune, document, instrument, and ratchet the gates.

- [ ] T087 [P] Add the Grafana dashboard at `monitoring/ledger-service.json` covering `ledger.created`/`updated`/`deleted`/`archived`/`audit.purged` counters, the `ledger.operation.duration` histogram (P50/P95/P99 panels with the explicit buckets from `research.md` §10), FusionCache hit/miss panels, and the `/health/ready` panel (per `dotnet-observability` Phase 4 + `quickstart.md` §5.2).
- [ ] T088 [P] Run a BenchmarkDotNet microbenchmark on the `LedgerRepository.GetByIdAsync` factory under realistic Testcontainers conditions to derive the FusionCache `FactorySoftTimeout` per `plan.md` §Skills referenced and `dotnet-diag:microbenchmarking`; commit the result and update the `FusionCacheRegistration` value if the measured P50 ≠ the placeholder 200 ms.
- [ ] T089 [P] Run the full `quickstart.md` §4 smoke flow against a locally-running `dotnet run --project src/Api` instance using `grpcurl`; capture the produced trace IDs and confirm a single trace covers `gRPC server → handler → repository → SQL command → audit insert` (per `quickstart.md` §5.4).
- [ ] T090 Add a coverage gate step to the build (`dotnet test --collect:"XPlat Code Coverage"` + ReportGenerator + a diff-aware patch-coverage check) failing the build below 80% per Constitution Principle II and `quickstart.md` §6.
- [ ] T091 [P] Run `dotnet list package --vulnerable --include-transitive` and ensure the build fails on any reported vulnerability per Constitution Principle V > Always-on.
- [ ] T092 [P] Validate the proto snapshot at `specs/001-ledger-crud/contracts/ledger.v1.proto` is byte-identical to the build-time source at `src/Contracts/Protos/ledger.v1.proto` (a small CI script `diff -q`); flag any drift as a review-blocking change per Constitution Principle III.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No external dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1. **Blocks every user story.**
- **User Story 1 (Phase 3, P1)**: Depends only on Phase 2. MVP slice.
- **User Story 2 (Phase 4, P1)**: Depends on Phase 2; **also depends on US1's Domain types `Ledger`/`LedgerErrors`/`AuditEntry` and the `LedgerRepository.CreateAsync` plumbing** because the read paths reuse `LedgerRepository` and `LedgerViewMapper`. In practice run US1 → US2 sequentially or coordinate Domain tasks (T038–T042) once and split the rest.
- **User Story 3 (Phase 5, P2)**: Depends on Phase 2 + the Domain/repository foundation built in US1 (T041, T042, T046, T047) and the `LedgerViewMapper` introduced in US1.
- **User Story 4 (Phase 6, P3)**: Depends on Phase 2 + the same Domain/repository foundation (T041, T042, T046, T047) and `ResultToRpcExceptionMapper`.
- **Polish (Phase 7)**: Depends on every user story being complete and green.

### Within Each User Story

- Tests for the story (the `[P]`-marked test files) are written FIRST, run, and observed to FAIL before the implementation tasks land.
- Domain types → Application command/query + RequestMap → Infrastructure repository extension → Caching decorator extension → gRPC service override → integration test.
- Within each tier, tasks marked `[P]` touch different files and may be executed in parallel.

### Parallel Opportunities

- **Phase 1**: T003, T004, T005, T006 are independent file additions — all `[P]`.
- **Phase 2**: T008/T009/T010/T012/T013/T015/T018/T024/T025/T029/T030/T033/T034/T035/T036/T037 are all `[P]` (different files; only T011, T014, T016, T017, T019, T020, T021, T022, T023, T026, T027, T028, T031, T032 are sequential or have explicit deps inside the phase).
- **Within US1**: T038, T039, T040 are `[P]` (three different files); the four `[P]` test files (T050–T053) can run in parallel once the implementation lands.
- **Across stories** (with multiple developers and after Phase 2 + the shared US1 Domain/repo plumbing): US3 and US4 can be developed concurrently because they touch separate command folders, separate repository methods, and separate gRPC overrides; US2 can run alongside US3/US4 as long as the shared `LedgerRepository`/`CachingLedgerRepository` files are coordinated through `[P]`-vs-non-`[P]` discipline.

---

## Parallel Example: User Story 1

```bash
# Domain primitives for US1 in parallel:
Task: "T038 [US1] Implement src/Domain/Ledgers/LedgerStatus.cs"
Task: "T039 [US1] Implement src/Domain/Auditing/AuditEventType.cs"
Task: "T040 [US1] Implement src/Domain/Ledgers/LedgerErrors.cs"

# Test files for US1 in parallel (run AFTER implementation; they MUST fail before implementation lands):
Task: "T050 [US1] tests/Domain.Tests/Ledgers/LedgerBuilderTests.cs"
Task: "T051 [US1] tests/Application.Tests/Ledgers/CreateLedgerHandlerTests.cs"
Task: "T052 [US1] tests/Infrastructure.Tests/Persistence/LedgerRepository_CreateAsyncTests.cs"
Task: "T053 [US1] tests/Api.IntegrationTests/Ledgers/CreateLedgerEndpointTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup).
2. Complete Phase 2 (Foundational) — all primitives, gRPC pipeline, observability, health, FusionCache wiring, and the audit purge service.
3. Complete Phase 3 (User Story 1) — Create flow with audit + cache invalidation.
4. **STOP and VALIDATE**: run `quickstart.md` §4.1 against the running service; confirm `ledger.created` increments, the audit row appears within 5 s, and `tests/Api.IntegrationTests/Ledgers/CreateLedgerEndpointTests.cs` is green.
5. Demo / deploy the MVP slice.

### Incremental Delivery

1. MVP (Phases 1–3) delivers **Create**.
2. Add Phase 4 (US2) → demo the **read** paths with caching.
3. Add Phase 5 (US3) → demo **update** with optimistic concurrency and archive semantics.
4. Add Phase 6 (US4) → demo **delete** with archive rejection and audit retention.
5. Phase 7 polish — dashboard, FusionCache tuning, coverage gate, vuln scan, contract drift check.

### Parallel Team Strategy

After Phase 2 plus the shared US1 Domain/repository plumbing (T038–T042, T046, T047, T049 — the `LedgerViewMapper`):

- Developer A: drive US2 (Get/List + caching read decorator + endpoint + tests).
- Developer B: drive US3 (Update with optimistic concurrency + cache invalidation + endpoint + tests).
- Developer C: drive US4 (Delete + archive rejection + cache invalidation + endpoint + tests).

Coordinate edits to `src/Infrastructure/Persistence/LedgerRepository.cs`, `src/Infrastructure/Persistence/CachingLedgerRepository.cs`, and `src/Api/Grpc/LedgersService.cs` (each story extends these) via small, story-scoped commits.

---

## Notes

- Every task carries an explicit file path and (where applicable) a `[US#]` traceability label.
- Tests are mandatory (Constitution Principle II) and use xUnit + FluentAssertions + NSubstitute; integration tests open a `GrpcChannel` over `WebApplicationFactory<Program>` with Testcontainers Postgres + Redis (per `research.md` §11 and `quickstart.md` §6).
- Failure-path assertions on code that returns `Result` use `result.IsFailure.Should().BeTrue()` + `result.Error!.Code.Should().Be("...")`, never `Should().Throw<>()`.
- Caching decorator only caches `Result.Success`; `Result.Failure` is never cached, and writes invalidate by `owner:{ownerId}` (and additionally `ledger:{ledgerId}` for Update/Delete).
- gRPC failures are translated only at the API boundary via `ResultToRpcExceptionMapper`; handlers and repositories never throw `RpcException` directly (Constitution Principle VI).
- Stop at any checkpoint to ship the increment behind a feature flag or release-branch gate.

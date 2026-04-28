---
description: "Task list for Basic CRUD for Ledger Services"
---

# Tasks: Basic CRUD for Ledger Services

**Input**: Design documents from `/specs/001-ledger-crud/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ledger.v1.proto, quickstart.md

**Tests**: REQUIRED and **TDD-ordered** per Constitution Principle II (NON-NEGOTIABLE). Within every user-story phase, the test files are written FIRST against the to-be-built signature, MUST be observed to FAIL (red), and only then do the matching implementation tasks turn them green. Dependency arrows below reflect this: implementation tasks depend on the matching red test existing first. Coverage gate: ≥80 % patch coverage (Constitution II), enforced in Phase 7.

**Local-dev orchestration**: per Constitution v2.10.0 (Tech Stack > Local development orchestration), every service MUST ship a `src/AppHost/` .NET Aspire project as the canonical one-command bring-up. Tests stay on Testcontainers (Aspire is NOT a test host); production does NOT use the AppHost (it is a developer-experience artifact only).

**Organization**: Tasks are grouped by user story so each can be implemented and verified independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User-story label (US1, US2, US3, US4); omitted in Setup, Foundational, and Polish phases
- File paths are absolute relative to the repo root (`/Users/arturotrenard/projects/sdd-demo`)

## Path Conventions

- Source: `src/{Domain,Application,Infrastructure,Infrastructure.Migrations,Contracts,Api,AppHost}/`
- Tests: `tests/{Domain.Tests,Application.Tests,Infrastructure.Tests,Api.IntegrationTests,Performance}/`
- Monitoring: `monitoring/`
- Repo root: `Directory.Build.props`, `Directory.Packages.props`, `SddDemo.Ledger.sln`, `docker-compose.dev.yml` (fallback only)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Bootstrap the .NET 10 solution, central package management, the AppHost orchestration project (canonical local bring-up per v2.10.0), and the compose fallback so every later task has a stable foundation.

- [X] T001 Create the solution file `SddDemo.Ledger.sln` at the repo root and add the 12 projects (six source: `src/Domain`, `src/Application`, `src/Infrastructure`, `src/Infrastructure.Migrations`, `src/Contracts`, `src/Api`; one orchestration: `src/AppHost`; five test/benchmarks: `tests/Domain.Tests`, `tests/Application.Tests`, `tests/Infrastructure.Tests`, `tests/Api.IntegrationTests`, `tests/Performance`) per `plan.md` §Project Structure.
- [X] T002 Author `Directory.Build.props` at the repo root pinning `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>`, and the project-wide analyzers (per Constitution Principle I and `dotnet-msbuild:directory-build-organization`).
- [X] T003 [P] Author `Directory.Packages.props` at the repo root with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` and pin every NuGet listed in `plan.md` §Primary Dependencies — including the Aspire packages (`Aspire.Hosting.AppHost`, `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.Redis`) used only by the AppHost project, and `BenchmarkDotNet` for the Phase 7 perf gate — per `dotnet-nuget:convert-to-cpm`.
- [X] T004 [P] Create `docker-compose.dev.yml` at the repo root provisioning Postgres 16 (`localhost:5432`, db/user/password=`ledger`) and Redis (`localhost:6379`) per `quickstart.md` §3a. **Mark this file in a comment header as the FALLBACK path only** — `dotnet run --project src/AppHost` is canonical per v2.10.0; the compose file MUST stay in sync with the AppHost resource graph (image versions, ports, env vars).
- [X] T005 [P] Create `src/Domain/SddDemo.Ledger.Domain.csproj`, `src/Application/SddDemo.Ledger.Application.csproj`, `src/Infrastructure/SddDemo.Ledger.Infrastructure.csproj`, `src/Infrastructure.Migrations/SddDemo.Ledger.Infrastructure.Migrations.csproj`, `src/Contracts/SddDemo.Ledger.Contracts.csproj`, and `src/Api/SddDemo.Ledger.Api.csproj` with the one-directional `<ProjectReference>` graph from `plan.md` §Project Structure (Application→Domain; Infrastructure→Application; Infrastructure.Migrations→Application; Api→Application+Infrastructure+Contracts).
- [X] T006 [P] Create `src/AppHost/SddDemo.Ledger.AppHost.csproj` referencing `Aspire.Hosting.AppHost`, `Aspire.Hosting.PostgreSQL`, and `Aspire.Hosting.Redis`, with a `<ProjectReference Include="..\Api\SddDemo.Ledger.Api.csproj" />` (per Constitution Tech Stack > Local development orchestration). The Api project MUST NOT reference the AppHost back; T088 enforces this in CI.
- [X] T007 [P] Implement `src/AppHost/Program.cs` wiring Postgres + Redis + the DbUp **migrator init-container** + the Api via Aspire hosting integrations per `research.md` §14 and Constitution v2.11.0 (Tech Stack > Local development orchestration). Required chain: `var postgres = builder.AddPostgres("postgres").WithDataVolume().AddDatabase("ledger");` `var redis = builder.AddRedis("redis");` `var migrator = builder.AddProject<Projects.SddDemo_Ledger_Infrastructure_Migrations>("migrator").WithReference(postgres).WaitFor(postgres);` `builder.AddProject<Projects.SddDemo_Ledger_Api>("api").WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName).WithReference(postgres).WithReference(redis).WaitFor(postgres).WaitFor(redis).WaitFor(migrator);` `builder.Build().Run();`. Three NON-NEGOTIABLE rules enforced here (Constitution v2.10.2 + v2.11.0): (i) `WithEnvironment("ASPNETCORE_ENVIRONMENT", ...)` MUST be chained on the Api — Aspire 9.x does not auto-propagate it and Dev-gated middleware (Swagger, `AnonymousCurrentUser` Dev fallback) silently disappears otherwise; (ii) the migrator project MUST be registered as a project resource and the Api MUST `.WaitFor(migrator)` so schema is applied before the first request lands; (iii) ad-hoc `Process.Start` / `Exec` is forbidden — all bring-up flows through Aspire hosting integrations.
- [X] T007a [P] Create `src/AppHost/Properties/launchSettings.json` with at least an `https` profile defining `applicationUrl` (HTTPS by default), `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL`, and `ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL` per Constitution v2.10.1 (Tech Stack > Local development orchestration > `Properties/launchSettings.json` is MANDATORY). Without this file Aspire 9.x throws `OptionsValidationException` at host build time and the canonical bring-up never reaches the dashboard. Depends on T006.
- [X] T008 [P] Create the four functional-test project files (`tests/Domain.Tests/SddDemo.Ledger.Domain.Tests.csproj`, `tests/Application.Tests/SddDemo.Ledger.Application.Tests.csproj`, `tests/Infrastructure.Tests/SddDemo.Ledger.Infrastructure.Tests.csproj`, `tests/Api.IntegrationTests/SddDemo.Ledger.Api.IntegrationTests.csproj`) referencing xUnit, FluentAssertions, NSubstitute, `coverlet.collector`, and (for the latter two) `Testcontainers.PostgreSql` + `Testcontainers.Redis`. **`Aspire.Hosting.Testing` MUST NOT be added** — explicit v2.10.0 carve-out (Aspire is not a test host).
- [X] T009 [P] Create the perf-gate project at `tests/Performance/SddDemo.Ledger.Performance.csproj` referencing `BenchmarkDotNet`, `Testcontainers.PostgreSql`, `Testcontainers.Redis`, and `Microsoft.AspNetCore.Mvc.Testing` (for `WebApplicationFactory<Program>`); marked `<IsPackable>false</IsPackable>`. **Console-style entry point** (`Program.cs` calling `BenchmarkRunner.Run<...>(args)`); xUnit / MSTest are NOT referenced — perf benchmarks run via `dotnet run -c Release --project tests/Performance` (NOT `dotnet test`). This project hosts the SC-002 / SC-003 benchmarks (T080) and the FusionCache substantiation benchmark (T081). Aspire is forbidden here too — perf benchmarks use the same Testcontainers harness as the integration suite.
- [X] T010 [P] Configure `.editorconfig` and analyzer rule severities at the repo root to enforce nullable + style rules called out in Constitution Principle I (Idiomatic C# & Code Quality).

**Checkpoint**: `dotnet restore && dotnet build` succeeds against the empty solution graph; `dotnet run --project src/AppHost` launches the Aspire dashboard with Postgres + Redis + a stub Api resource (Api will start failing on missing endpoints until Phase 2 completes — that's expected).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the cross-cutting primitives — Result/Error, validation, identity stub, currency catalog, persistence wiring, observability, gRPC pipeline, and the audit-retention background job — that every user story will consume. The Api composition root (T035) reads connection strings injected by the AppHost (`ConnectionStrings:ledger`, `ConnectionStrings:redis`) so the same `Program.cs` runs under `dotnet run --project src/AppHost` (canonical) and `dotnet run --project src/Api` against the compose fallback.

**⚠️ CRITICAL**: No user-story phase may begin until this phase is complete.

### Foundational tests (TDD red — written first per Principle II)

- [X] T011 [P] Add `tests/Domain.Tests/Common/ResultTests.cs` covering `Success`/`Failure` construction, `Map`, `Bind`, and equality semantics for `Result`/`Result<T>`. **MUST fail to build** before T013 lands (the type does not yet exist).
- [X] T012 [P] Add `tests/Domain.Tests/Common/DomainValidatorTests.cs` asserting attribute aggregation produces a single `Error("validation", ..., ErrorType.Validation)` carrying every member name. **MUST fail to build** before T014 lands.

### Domain primitives (turn T011/T012 green)

- [X] T013 [P] Implement `src/Domain/Common/ErrorType.cs` (enum: `Validation`, `NotFound`, `Conflict`, `Forbidden`, `Failure` — no `Unauthorized` member: authn/authz are out of constitutional scope per v3.0.0; missing-owner-header surfaces as `Validation`), `src/Domain/Common/Error.cs` (`public sealed record Error(string Code, string Message, ErrorType Type)`), and `src/Domain/Common/Result.cs` (`Result` and `Result<T>` `readonly record struct` with `Success`/`Failure` factories, `IsSuccess`/`IsFailure`/`Error`/`Value`, and `Map`/`Bind` helpers) per `data-model.md` §3 and Constitution Principle VI > Result pattern. **Turns T011 green.**
- [X] T014 Implement `src/Domain/Common/DomainValidator.cs` as a static helper wrapping `Validator.TryValidateObject` (with `validateAllProperties: true`) and aggregating `ValidationResult`s into `Result.Failure(new Error("validation", ..., ErrorType.Validation))` per `research.md` §8 (depends on T013). **Turns T012 green.**

### Currency

- [X] T015 [P] Add `tests/Infrastructure.Tests/Currency/CurrencyCatalogTests.cs` asserting common ISO codes (`USD`, `EUR`, `JPY`) return `true` and unknown codes (`XYZ`, `""`, `null`) return `false`. **MUST fail to build** before T016/T017/T018 land.
- [X] T016 [P] Implement `src/Domain/Currency/ICurrencyCatalog.cs` exposing `bool IsSupported(string code)` per `data-model.md` §3.
- [X] T017 [P] Implement `src/Domain/Currency/ValidIsoCurrencyAttribute.cs` as a `ValidationAttribute` that resolves `ICurrencyCatalog` from the `ValidationContext` per `research.md` §8 (depends on T016).
- [X] T018 Implement `src/Infrastructure/Currency/CurrencyCatalog.cs` populating an `ImmutableHashSet<string>` from `CultureInfo.GetCultures(CultureTypes.SpecificCultures) → RegionInfo.ISOCurrencySymbol` once at construction per `research.md` §13 (depends on T016). **Turns T015 green.**

### Identity

- [X] T019 [P] Implement `src/Application/Abstractions/Identity/ICurrentUser.cs` exposing `Result<Guid> ResolveOwnerId()` per `research.md` §7.
- [X] T020 Implement `src/Infrastructure/Identity/AnonymousCurrentUser.cs` reading `X-Owner-Id` from `IHttpContextAccessor` and falling back to `Identity:DevOwnerId` (bound to `AnonymousCurrentUserOptions`) in Development per `research.md` §7 and the FR-010 clarification in `spec.md` §Clarifications (depends on T019). Authn/authz are out of constitutional scope per v3.0.0 — there is no auth concept in this service, so in non-Development environments a missing/unparseable header MUST cause `UserId` resolution to fail with `Result.Failure(... ErrorType.Validation)` (malformed input — `identity.missing_owner` / `identity.invalid_owner`), which the boundary translator maps to `StatusCode.InvalidArgument`.
- [X] T020a Create `src/Api/appsettings.Development.json` populating `Identity:DevOwnerId` with a fixed UUID so the canonical bring-up (`dotnet run --project src/AppHost`) reaches the Swagger happy path on a representative POST without the developer hand-crafting the `X-Owner-Id` header on every request. The constitution (v3.0.0) no longer mandates this fallback — it is a feature-level dev-experience choice for FR-009 / FR-012 owner-scoping; in non-Development environments the header is required and a missing/unparseable value surfaces as `Validation`. Depends on T020.

### Persistence base

- [X] T021 Implement `src/Application/Abstractions/Persistence/ILedgerRepository.cs` with the five methods (`CreateAsync`, `UpdateAsync`, `DeleteAsync`, `GetByIdAsync`, `ListAsync`) signed exactly as in `data-model.md` §5 (depends on T013).
- [X] T022 [P] Implement `src/Application/Abstractions/Persistence/IAuditRepository.cs` with `WriteAsync(AuditEntry, NpgsqlConnection, NpgsqlTransaction, CancellationToken)` and `PurgeOlderThanAsync(TimeSpan, CancellationToken)` per `data-model.md` §5 and `research.md` §6.
- [X] T023 Implement `src/Infrastructure/Persistence/DataSourceFactory.cs` using `NpgsqlDataSourceBuilder` to register a pooled singleton `NpgsqlDataSource`. The factory MUST consume `ConnectionStrings:ledger` (the name the AppHost injects per `research.md` §14) so that `dotnet run --project src/AppHost` and the compose fallback both resolve the same configuration key.
- [X] T024 Embed initial Dapper SQL resources at `src/Infrastructure/Persistence/Sql/Ledger/{Insert,UpdateOptimistic,DeleteOptimistic,GetById,ListKeyset}.sql` and `src/Infrastructure/Persistence/Sql/Audit/{Insert,PurgeOlderThan}.sql` matching the columns and predicates in `data-model.md` §2 and `research.md` §5–6, marked `<EmbeddedResource>` in the `.csproj`.

### DbUp migrations project (Aspire init-container)

- [X] T025 Implement `src/Infrastructure.Migrations/` as a Generic Host console (`Microsoft.NET.Sdk` + `<OutputType>Exe</OutputType>`) hosting a `BackgroundService` that runs DbUp against `ConnectionStrings:ledger` (Aspire-injected) and then signals `IHostApplicationLifetime.StopApplication`, per Constitution v2.11.0 (Tech Stack > Persistence > Schema management). Reference `dbup-core` + `dbup-postgresql`.
- [X] T026 Author `src/Infrastructure.Migrations/Scripts/0001_Initial.sql` (embedded resource) creating the `ledger` and `ledger_audit` tables with the columns, indexes, and check constraints in `data-model.md` §2. Scripts are append-only — never edit `0001_*` after merge; ship `0002_*` and forward (depends on T025).

### Observability + health

- [X] T027 [P] Add `tests/Api.IntegrationTests/Health/HealthEndpointsTests.cs` using `WebApplicationFactory<Program>` configured with `UseEnvironment("Production")` (so HSTS / `UseHttpsRedirection` paths are exercised) + Testcontainers Postgres + Redis (NOT `Aspire.Hosting.Testing`) to assert `/health/live` returns 200 and `/health/ready` returns 200 with all containers up; failing readiness on stopped Postgres returns 503. **MUST fail to build** before T028/T029/T035 land.
- [X] T028 Implement OpenTelemetry wiring in `src/Api/Observability/OpenTelemetryRegistration.cs` with a single `UseOtlpExporter()`, resource attributes `service.name=ledger-service`/`service.version=<gitsha>`, ASP.NET Core + gRPC client + FusionCache instrumentation, and the explicit histogram buckets listed in `research.md` §10 (per `dotnet-aspnet:configuring-opentelemetry-dotnet` and `dotnet-observability` Phases 1–3). **No `F5`-specific OTLP wiring is required** — the AppHost (T007) supplies `OTEL_EXPORTER_OTLP_ENDPOINT` automatically per `research.md` §10 and §14.
- [X] T028a [P] Implement `src/Application/Abstractions/Observability/ILedgerMetrics.cs` exposing the method-style seam (`RecordCreated`, `RecordUpdated`, `RecordDeleted`, `RecordArchived`, `RecordAuditPurged(long)`, `RecordOperationDuration(string operation, string outcome, double seconds)`) per `plan.md` §Project Structure (Application stays free of `System.Diagnostics.Metrics` types). Required for handlers (T047/T066/T074) to emit metrics without crossing the Application→Api layer boundary.
- [X] T029 [P] Implement `src/Api/Observability/LedgerMetrics.cs` as `sealed class LedgerMetrics : ILedgerMetrics` exposing the counters `ledger.created`, `ledger.updated`, `ledger.deleted`, `ledger.archived`, `ledger.audit.purged` and the histogram `ledger.operation.duration` via `IMeterFactory.Create("SddDemo.Ledger")` per `research.md` §10. Register both `services.AddSingleton<LedgerMetrics>()` and `services.AddSingleton<ILedgerMetrics>(sp => sp.GetRequiredService<LedgerMetrics>())` so the OTel meter registration (`AddMeter(LedgerMetrics.MeterName)`) and the Application-layer injection of `ILedgerMetrics` resolve to the same instance. Depends on T028a.
- [X] T030 Implement `src/Api/Health/HealthChecksRegistration.cs` mapping `/health/live` (always 200) and `/health/ready` (Npgsql + Redis + FusionCache backplane probes) per `research.md` §10 and `quickstart.md` §5.3. **Turns T027 green** (in conjunction with T035).

### Caching wiring

- [X] T031 Register FusionCache in `src/Api/Caching/FusionCacheRegistration.cs` with L1 `IMemoryCache`, L2 `IDistributedCache` over Redis (`Microsoft.Extensions.Caching.StackExchangeRedis`), `FusionCacheSystemTextJsonSerializer`, the Redis backplane (`ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis`), `DefaultEntryOptions { IsFailSafeEnabled = true, FactorySoftTimeout = 200ms }` (**placeholder — replaced before merge by T081's measured value** per Constitution Tech Stack > Caching), and the OTel instrumentation hook per `research.md` §4 (per the `fusion-cache` skill). Read the Redis connection string from `ConnectionStrings:redis` (the AppHost-injected name per `research.md` §14).

### gRPC pipeline

- [X] T032 Wire `src/Contracts/SddDemo.Ledger.Contracts.csproj` with `<Protobuf Include="Protos\**\*.proto" GrpcServices="Both" />` and copy `specs/001-ledger-crud/contracts/ledger.v1.proto` into `src/Contracts/Protos/ledger.v1.proto` as the build-time source of truth per `plan.md` §Project Structure.
- [X] T033 Implement `src/Api/Grpc/ResultToRpcExceptionMapper.cs` translating `Result.Failure` codes/types to gRPC status codes per the table in `plan.md` Constitution Check (Principle VI) and the boundary rules in `research.md` §5 (`Conflict → AlreadyExists`, `NotFound → NotFound`, `Validation → InvalidArgument`, `Forbidden → PermissionDenied`, default `Internal`). Authn/authz are out of constitutional scope (v3.0.0) — there is no `Unauthorized` `ErrorType` and no `Unauthenticated` mapping in this service.
- [X] T034 [P] Implement `src/Api/Grpc/Interceptors/ExceptionSafetyNetInterceptor.cs` as the FIRST interceptor in the chain — catches unhandled exceptions, logs with the request `trace_id`, and rethrows a sanitised `RpcException(StatusCode.Internal, "internal_error")` per Constitution Principle VI and `quickstart.md` §7. Implement `src/Api/Grpc/Interceptors/ValidationInterceptor.cs` running `DomainValidator.Validate` on the `*RequestMap` for the incoming RPC and short-circuiting with `InvalidArgument` on failure (per `research.md` §8).

### Background work

- [X] T035 Implement `src/Api/Program.cs` composing all registrations: `AddNpgsqlDataSource(builder.Configuration.GetConnectionString("ledger"))` + Redis from `ConnectionStrings:redis` (both names match the AppHost wiring in T007), `AddSingleton<ICurrencyCatalog>`, `AddScoped<ICurrentUser, AnonymousCurrentUser>`, `AddScoped<ILedgerRepository, LedgerRepository>` then `services.Decorate<ILedgerRepository, CachingLedgerRepository>()` (Scrutor), gRPC + JsonTranscoding + Swagger (Development-only), `MapGrpcService<LedgersService>()`, the two interceptors registered in chain order (safety net first, validation second), the health-check endpoints, the OpenTelemetry registration, **`UseHttpsRedirection()` and `UseHsts()` registered for non-Development environments** per Constitution Principle V (TLS / HSTS hardening). HTTP surface restricted to `/health/*`, `/swagger`, `/swagger/v1/swagger.json` (`plan.md` §Constraints; `research.md` §2). Stub `LedgersService` empty for now — story phases fill in each RPC.
- [X] T036 [P] Implement `src/Api/Configuration/LedgerOptions.cs` with `[Required]` Data Annotations (e.g., `DevOwnerId`, page-size defaults) and bind via `services.AddOptions<LedgerOptions>().BindConfiguration("Ledger").ValidateDataAnnotations().ValidateOnStart()` per `research.md` §8. **No secret-shaped values are permitted in `appsettings.*.json`** per Constitution Principle V (secrets management) — secrets MUST flow from User Secrets (Development) or env vars / on-prem secret manager (deployed environments). The Aspire AppHost injects connection strings via `ConnectionStrings:*`, satisfying the rule for the only secrets in this feature; if any future option becomes secret-shaped, document the source explicitly in this file's comments.
- [X] T037 Implement `src/Infrastructure/Background/AuditRetentionPurgeService.cs` as a `BackgroundService` that runs the purge once at startup and then daily at 03:00 UTC via `PeriodicTimer`, calling `IAuditRepository.PurgeOlderThanAsync(TimeSpan.FromDays(365), ct)` and incrementing `ledger.audit.purged` per `research.md` §12 (depends on T022, T029).

**Checkpoint**: All cross-cutting primitives compile, the host boots under both `dotnet run --project src/AppHost` (canonical) and the compose fallback, `/health/ready` is green (T027 turns green), and the gRPC service registry is in place with no RPCs implemented yet.

---

## Phase 3: User Story 1 - Create a new ledger (Priority: P1) 🎯 MVP

**Goal**: A caller can `CreateLedger` (gRPC) with a name + ISO-4217 currency + optional description; the server persists the row, writes a `Create` audit entry in the same transaction, returns a `LedgerView` with `id`, `version_token`, and timestamps, and rejects duplicate names within the same owner with a clear conflict.

**Independent Test**: Run the `CreateLedger` gRPC call from `quickstart.md` §4.1 with a unique name → expect a `LedgerView`. Re-issue the same call with the same name → expect `RpcException(StatusCode = AlreadyExists, "ledger.name_already_exists")`. Inspect `ledger_audit` → expect one row with `event_type = 1` for the created ledger AND that the `event_at` minus the operation start is `< 5 s` (SC-005). FR coverage: FR-001, FR-003, FR-004, FR-012.

### TDD red — Tests for US1 (written FIRST; MUST fail before implementation lands)

- [X] T038 [P] [US1] Write `tests/Domain.Tests/Ledgers/LedgerBuilderTests.cs` against the to-be-built `Ledger` + `LedgerBuilder`: happy-path build returns `Success`; whitespace-only name returns `Failure` with code `validation`; unsupported currency returns `Failure`; description > 500 chars returns `Failure`; `CreatedAt > LastModifiedAt` returns `Failure`. **MUST fail to build** before T044 lands.
- [X] T039 [P] [US1] Write `tests/Application.Tests/Ledgers/CreateLedgerHandlerTests.cs` against the to-be-built `CreateLedgerHandler` using NSubstitute fakes for `ILedgerRepository`, `ICurrentUser`, `IFusionCache`: asserts the handler resolves `OwnerId` from `ICurrentUser`, constructs a `Ledger` with `Status=Active`, `Version=1`, builds a `Create` `AuditEntry` with the post-state payload, calls `CreateAsync` exactly once, increments `ledger.created` once on success, and propagates failures unchanged. **MUST fail to build** before T047 lands.
- [X] T040 [P] [US1] Write `tests/Infrastructure.Tests/Persistence/LedgerRepository_CreateAsyncTests.cs` against the to-be-built `LedgerRepository.CreateAsync` with Testcontainers Postgres: a successful insert returns `Result<Ledger>` with `Version=1`, `ledger_audit` has exactly one row with `event_type=1` AND `(event_at - operationStart) < TimeSpan.FromSeconds(5)` (SC-005), a duplicate-name insert (case-insensitive) returns `Result.Failure(LedgerErrors.NameAlreadyExists)`, and a failed insert leaves no `ledger_audit` row (atomic same-tx). **MUST fail to build** before T050 lands.
- [X] T041 [P] [US1] Write `tests/Api.IntegrationTests/Ledgers/CreateLedgerEndpointTests.cs` against the to-be-built `LedgersService.CreateLedger` using `WebApplicationFactory<Program>` configured with `UseEnvironment("Production")` (so the `AnonymousCurrentUser` fallback to `DevOwnerId` is disabled and a missing `X-Owner-Id` is rejected) + Testcontainers + `GrpcChannel.ForAddress(factory.CreateClient())` (NOT `Aspire.Hosting.Testing`). Acceptance Scenarios 1.1 (happy path), 1.2 (duplicate-name → `AlreadyExists`), 1.3 (missing required field → `InvalidArgument`); Edge: unsupported currency → `InvalidArgument`; FR-009/FR-010: missing `X-Owner-Id` in non-Development is rejected with `InvalidArgument` (`identity.missing_owner`) — authn/authz are out of constitutional scope per v3.0.0, so this is malformed input, not an auth failure. **MUST fail to build** before T051 lands.

### Implementation for US1 (turns T038–T041 green)

- [X] T042 [P] [US1] Implement `src/Domain/Ledgers/LedgerStatus.cs` (enum: `Active = 1`, `Archived = 2`) per `data-model.md` §1.1.
- [X] T043 [P] [US1] Implement `src/Domain/Auditing/AuditEventType.cs` (enum: `Create = 1`, `Update = 2`, `Delete = 3`) per `data-model.md` §1.2.
- [X] T044 [US1] Implement `src/Domain/Ledgers/LedgerErrors.cs` (canonical static `Error` instances: `NameAlreadyExists`, `NotFound`, `Conflict`, `ArchivedReadOnly`, `ArchivedCannotDelete`, `Validation`) per `data-model.md` §3.2 AND `src/Domain/Ledgers/Ledger.cs` as `sealed class : IValidatableObject` with private constructor, `private init` properties, the Data Annotations from `data-model.md` §3.1, the cross-property `Validate(...)` (whitespace name, `CreatedAt <= LastModifiedAt`), and a nested `LedgerBuilder` whose `Build()` returns `DomainValidator.Validate(...)` (depends on T014, T017, T038, T042). **Turns T038 green.**
- [X] T045 [US1] Implement `src/Domain/Auditing/AuditEntry.cs` as `sealed class` with private constructor, `private init` properties (`Id`, `ActorId`, `LedgerId`, `EventType`, `EventAt`, `Payload` as `JsonDocument`), Data Annotations matching `data-model.md` §1.2, and a nested `AuditEntryBuilder.Build() → Result<AuditEntry>` (depends on T014, T043).
- [X] T046 [US1] Implement `src/Application/Features/Ledgers/Commands/CreateLedger/CreateLedgerCommand.cs` exactly as specified in `data-model.md` §4 (Data Annotations on each property), and `src/Application/Features/Ledgers/Commands/CreateLedger/CreateLedgerRequestMap.cs` mapping `CreateLedgerRequest` (proto) → validated `CreateLedgerRequestMap` → `CreateLedgerCommand` per `data-model.md` §4.1 (trimmed name, normalised currency upper-invariant; `DomainValidator.Validate`).
- [X] T047 [US1] Implement `src/Application/Features/Ledgers/Commands/CreateLedger/CreateLedgerHandler.cs` registered `AddScoped<CreateLedgerHandler>()` — resolves `ICurrentUser`, builds a `Ledger` with a UUIDv7 `Id` and `Version = 1`, builds a `Create` `AuditEntry` with the post-state JSON snapshot, calls `ILedgerRepository.CreateAsync`, returns the resulting `Result<Ledger>`, and increments `ledger.created` on success (per `research.md` §6 + §10; depends on T019, T021, T022, T029, T039, T044, T045, T046). **Turns T039 green.**
- [X] T048 [US1] Implement `src/Infrastructure/Persistence/AuditRepository.cs` with `WriteAsync` issuing the embedded `Audit/Insert.sql` against the supplied `NpgsqlConnection` + `NpgsqlTransaction` (same-tx invariant from `research.md` §6) and `PurgeOlderThanAsync` issuing `Audit/PurgeOlderThan.sql` (depends on T022, T024).
- [X] T049 [US1] Implement `src/Infrastructure/Persistence/LedgerRepository.cs` with `CreateAsync(Ledger, AuditEntry, ct)` opening an `NpgsqlConnection`+`NpgsqlTransaction` from the pooled `NpgsqlDataSource`, executing `Ledger/Insert.sql` and `IAuditRepository.WriteAsync` in the same transaction, mapping `23505` (`unique_violation`) on `ux_ledger_owner_name_lower` to `Result.Failure(LedgerErrors.NameAlreadyExists)`, and committing on success (depends on T021, T023, T024, T044, T048).
- [X] T050 [US1] Implement `src/Infrastructure/Persistence/CachingLedgerRepository.cs` decorating `ILedgerRepository`; for `CreateAsync` it forwards to the inner repo and on `IsSuccess` calls `IFusionCache.RemoveByTagAsync($"owner:{ownerId}", ct)` (Get/List/Update/Delete are added in later story phases) per `research.md` §4 (depends on T021, T031, T040, T049). **Turns T040 green.**
- [X] T051 [US1] Implement `src/Api/Grpc/LedgerViewMapper.cs` (Domain `Ledger` → proto `LedgerView`, encoding `Version` as 8-byte big-endian into `version_token`) and the `CreateLedger(...)` override in `src/Api/Grpc/LedgersService.cs` — calls `CreateLedgerRequestMap.From(request)`, dispatches to `CreateLedgerHandler`, projects via `LedgerViewMapper`; on failure delegates to `ResultToRpcExceptionMapper` (depends on T033, T041, T046, T047, T050). **Turns T041 green.**

**Checkpoint**: User Story 1 is independently demonstrable — `quickstart.md` §4.1 runs end-to-end against the AppHost-launched stack, `ledger.created` counter increments live in the Aspire dashboard, the audit row appears within 5 s (SC-005, asserted in T040), and `dotnet test` passes for `Domain.Tests` / `Application.Tests` / `Infrastructure.Tests` / `Api.IntegrationTests`.

---

## Phase 4: User Story 2 - View ledgers (Priority: P1)

**Goal**: A caller can `GetLedger(id)` for a single owned ledger and `ListLedgers(...)` with keyset pagination + `include_archived` filter, served through the FusionCache decorator.

**Independent Test**: After creating two ledgers (US1), `ListLedgers` returns both in deterministic order (`last_modified_at DESC, id DESC`); `GetLedger` by id returns full details; `GetLedger` for an unknown or non-owned id returns `NOT_FOUND` without leaking ownership; paginating past the page size returns the next page (FR-005, FR-006, FR-009; SC-004 cross-owner; Acceptance Scenarios 2.1–2.4).

### TDD red — Tests for US2

- [X] T052 [P] [US2] Write `tests/Application.Tests/Ledgers/GetLedgerHandlerTests.cs` and `tests/Application.Tests/Ledgers/ListLedgersHandlerTests.cs` against the to-be-built handlers. NSubstitute on `ILedgerRepository`/`ICurrentUser`. Verify owner-scoping, page-size clamping (0→50, 500→`Failure`/`InvalidArgument`), and that `Result.Failure(NotFound)` is propagated unchanged. **MUST fail to build** before T056/T057 land.
- [X] T053 [P] [US2] Write `tests/Infrastructure.Tests/Persistence/LedgerRepository_GetListTests.cs` against the to-be-built `GetByIdAsync`/`ListAsync` with Testcontainers Postgres seeded with rows for two owners. Asserts owner-scoped read, NOT_FOUND on missing/not-owned, deterministic ordering (`last_modified_at DESC, id DESC`), keyset cursor round-trip across two pages, and that `include_archived=false` excludes archived rows. **MUST fail to build** before T060/T061 land.
- [X] T054 [P] [US2] Write `tests/Infrastructure.Tests/Persistence/CachingLedgerRepository_ReadTests.cs` against the to-be-built decorator extensions with Testcontainers Postgres + Redis. First `GetByIdAsync` calls the inner repo; second hits L1 (verify via NSubstitute call count on a wrapping mock); only `Success` is cached (a `Failure(NotFound)` followed by an inserted row resolves on the second call); list-page caching keys differ by `(includeArchived, pageCursor, pageSize)`. **MUST fail to build** before T062 lands.
- [X] T055 [P] [US2] Write `tests/Api.IntegrationTests/Ledgers/GetAndListLedgersEndpointTests.cs` against the to-be-built `LedgersService.GetLedger`/`ListLedgers` overrides with `UseEnvironment("Production")` + Testcontainers. Acceptance Scenarios 2.1, 2.2, 2.3 (NOT_FOUND with no ownership leak), 2.4 (pagination); FR-009 cross-owner read returns NOT_FOUND identical to a missing ledger (SC-004). **MUST fail to build** before T063 lands.

### Implementation for US2 (turns T052–T055 green)

- [X] T056 [P] [US2] Implement `src/Application/Features/Ledgers/Queries/GetLedger/GetLedgerQuery.cs` (with `[Required] Guid Id`) and `src/Application/Features/Ledgers/Queries/GetLedger/GetLedgerRequestMap.cs` (parses canonical UUID; `DomainValidator.Validate`), and `src/Application/Features/Ledgers/Queries/GetLedger/GetLedgerHandler.cs` (`AddScoped`) — resolves `OwnerId` from `ICurrentUser` and dispatches to `ILedgerRepository.GetByIdAsync(id, ownerId, ct)` per `data-model.md` §4 / §4.1 (depends on T019, T021, T052).
- [X] T057 [P] [US2] Implement `src/Application/Features/Ledgers/Queries/ListLedgers/ListLedgersQuery.cs`, `src/Application/Features/Ledgers/Queries/ListLedgers/LedgerListPage.cs` (immutable `record LedgerListPage(IReadOnlyList<Ledger> Items, string? NextPageCursor)`), `src/Application/Features/Ledgers/Queries/ListLedgers/ListLedgersRequestMap.cs` (defaults `page_size=0` to 50, validates `[Range(1,200)]`), and `src/Application/Features/Ledgers/Queries/ListLedgers/ListLedgersHandler.cs` (`AddScoped`) — clamps `PageSize` to `[1, 200]` (default 50), forwards to `ILedgerRepository.ListAsync` per `data-model.md` §4 (depends on T019, T021, T052). **Together T056 + T057 turn T052 green.**
- [X] T058 [US2] Extend `src/Infrastructure/Persistence/LedgerRepository.cs` with `GetByIdAsync(ledgerId, ownerId, ct)` running `Ledger/GetById.sql` (parameterised, owner-scoped) and returning `Result.Failure(LedgerErrors.NotFound)` when the row is absent or owned by another user (FR-009 — no information leak) (depends on T024, T044, T049).
- [X] T059 [US2] Extend `src/Infrastructure/Persistence/LedgerRepository.cs` with `ListAsync(ownerId, includeArchived, pageCursor, pageSize, ct)` running `Ledger/ListKeyset.sql` with `ORDER BY last_modified_at DESC, id DESC LIMIT @pageSize + 1`, decoding the opaque base64 `(timestamp, id)` cursor, and emitting a non-null `NextPageCursor` only when the extra row was returned (per `research.md` §9) (depends on T024, T058). **Together T058 + T059 turn T053 green.**
- [X] T060 [US2] Extend `src/Infrastructure/Persistence/CachingLedgerRepository.cs` with `GetByIdAsync` (cache key `ledger:{ownerId}:{ledgerId}`, tags `owner:{ownerId}` + `ledger:{ledgerId}`) and `ListAsync` (cache key `ledger:list:{ownerId}:{includeArchived}:{pageCursor}:{pageSize}`, tag `owner:{ownerId}`); only `Result.Success` is cached (per `research.md` §4) (depends on T031, T050, T058, T059). **Turns T054 green.**
- [X] T061 [US2] Implement `LedgersService.GetLedger(...)` and `LedgersService.ListLedgers(...)` overrides in `src/Api/Grpc/LedgersService.cs` — dispatch through the `*RequestMap`s and the corresponding handler, map `Result.Failure(LedgerErrors.NotFound)` to `RpcException(StatusCode.NotFound)` via `ResultToRpcExceptionMapper`, and project results through `LedgerViewMapper` (depends on T051, T056, T057, T060). **Turns T055 green.**

**Checkpoint**: Read paths for User Story 2 are demonstrable end-to-end, served by the cache decorator, with deterministic ordering and keyset pagination.

---

## Phase 5: User Story 3 - Update an existing ledger (Priority: P2)

**Goal**: A caller can `UpdateLedger` to change name/description/status with a valid `version_token`. Optimistic concurrency rejects stale tokens; archived ledgers reject every change except un-archive; the audit log records every successful update.

**Independent Test**: Acceptance Scenarios 3.1–3.6 from `spec.md` and `quickstart.md` §4.4–§4.5 — happy-path description change, invalid-currency reject, cross-owner reject, archived-read-only reject, un-archive succeeds, stale `version_token` returns `AlreadyExists` (FR-007, FR-007a).

### TDD red — Tests for US3

- [X] T062 [P] [US3] Write `tests/Application.Tests/Ledgers/UpdateLedgerHandlerTests.cs` against the to-be-built handler — covers each branch of the state machine: Active rename, Active→Archived, Archived→Active, Archived rename returns `Failure(ArchivedReadOnly)`, mismatched expected version is propagated unchanged, ledger not owned returns `Failure(NotFound)` without distinguishing from missing. **MUST fail to build** before T066 lands.
- [X] T063 [P] [US3] Write `tests/Infrastructure.Tests/Persistence/LedgerRepository_UpdateAsyncTests.cs` against the to-be-built `UpdateAsync` with Testcontainers Postgres: successful update increments `version` and writes one `event_type=2` audit row in the same transaction with `(event_at - operationStart) < 5 s` (SC-005); stale `expected` returns `Failure(Conflict)` and writes no audit row; rename to a duplicate name returns `Failure(NameAlreadyExists)`. **MUST fail to build** before T067 lands.
- [X] T064 [P] [US3] Write `tests/Infrastructure.Tests/Persistence/CachingLedgerRepository_InvalidationTests.cs` against the to-be-built decorator extension with Testcontainers Postgres + Redis: a successful update invalidates both `owner:{ownerId}` and `ledger:{ledgerId}` tags (subsequent `GetByIdAsync` and `ListAsync` both miss L1 and re-load). **MUST fail to build** before T068 lands.
- [X] T065 [P] [US3] Write `tests/Api.IntegrationTests/Ledgers/UpdateLedgerEndpointTests.cs` against the to-be-built `LedgersService.UpdateLedger` with `UseEnvironment("Production")` + Testcontainers. Acceptance Scenarios 3.1–3.6, including the optimistic-concurrency loser receiving `RpcException(StatusCode = AlreadyExists, "ledger.conflict")` exactly as specified in `quickstart.md` §4.4. **MUST fail to build** before T069 lands.

### Implementation for US3 (turns T062–T065 green)

- [X] T066 [US3] Implement `src/Application/Features/Ledgers/Commands/UpdateLedger/UpdateLedgerCommand.cs` (per `data-model.md` §4: `Id`, `VersionToken`, optional `Name`/`Description`/`Status`), `src/Application/Features/Ledgers/Commands/UpdateLedger/UpdateLedgerRequestMap.cs` (decodes `version_token` 8-byte big-endian → `long`; honours `update_mask` so only listed fields are forwarded), and `src/Application/Features/Ledgers/Commands/UpdateLedger/UpdateLedgerHandler.cs` (`AddScoped`) — loads the current ledger via `GetByIdAsync` (owner-scoped), enforces the state machine in `data-model.md` §1.1 (Active→{Active,Archived,attribute updates}; Archived→only `Status=Active`; otherwise `Failure(LedgerErrors.ArchivedReadOnly)`), builds the new `Ledger` with `Version = current + 1` and `LastModifiedAt = TimeProvider.GetUtcNow()`, builds an `Update` `AuditEntry`, calls `ILedgerRepository.UpdateAsync(updated, expectedVersion, audit, ct)`, increments `ledger.updated` (and `ledger.archived` on a status transition to Archived) on success (depends on T019, T021, T029, T044, T045, T062). **Turns T062 green.**
- [X] T067 [US3] Extend `src/Infrastructure/Persistence/LedgerRepository.cs` with `UpdateAsync(Ledger updated, long expectedVersion, AuditEntry audit, ct)` issuing `Ledger/UpdateOptimistic.sql` (`UPDATE ... SET ..., version = version + 1 WHERE id = @id AND owner_id = @ownerId AND version = @expected RETURNING ...`); zero rows affected → `Result.Failure(LedgerErrors.Conflict)`; row missing on a follow-up `GetByIdAsync` → `Result.Failure(LedgerErrors.NotFound)`; writes the audit row in the same transaction; maps `unique_violation` on rename to `Result.Failure(LedgerErrors.NameAlreadyExists)` (depends on T024, T044, T048, T049, T063). **Turns T063 green.**
- [X] T068 [US3] Extend `src/Infrastructure/Persistence/CachingLedgerRepository.cs` with `UpdateAsync` — forwards to the inner repo and on `IsSuccess` calls `IFusionCache.RemoveByTagAsync($"owner:{ownerId}", ct)` and `RemoveByTagAsync($"ledger:{ledgerId}", ct)` per `research.md` §4 (depends on T031, T060, T064, T067). **Turns T064 green.**
- [X] T069 [US3] Implement `LedgersService.UpdateLedger(...)` override in `src/Api/Grpc/LedgersService.cs` — dispatches through `UpdateLedgerRequestMap` and `UpdateLedgerHandler`; maps `Conflict` → `AlreadyExists`, `NotFound` → `NotFound`, `Validation` (including `ArchivedReadOnly`) → `InvalidArgument` via `ResultToRpcExceptionMapper`; returns the new `LedgerView` with the refreshed `version_token` (depends on T033, T065, T066, T068). **Turns T065 green.**

**Checkpoint**: Update flow is demonstrable; concurrent writers are arbitrated by the database; archived ledgers are read-only except for un-archive.

---

## Phase 6: User Story 4 - Delete a ledger (Priority: P3)

**Goal**: A caller can hard-delete an `Active` ledger they own; archived ledgers reject delete with a clear "un-archive first" error; subsequent reads return NOT_FOUND; the audit row remains for the retention window.

**Independent Test**: Acceptance Scenarios 4.1–4.4 — happy-path delete then NOT_FOUND on Get; cross-owner delete leaves the ledger intact and returns NOT_FOUND; deleting an already-deleted ledger returns NOT_FOUND; deleting an archived ledger returns InvalidArgument with code `ledger.archived.cannot_delete`. Re-creating a ledger with the same name immediately after delete succeeds (FR-008).

### TDD red — Tests for US4

- [X] T070 [P] [US4] Write `tests/Application.Tests/Ledgers/DeleteLedgerHandlerTests.cs` against the to-be-built handler — covers active-delete success, archived-delete rejection, owner-mismatch returning `Failure(NotFound)` (no leak), version-mismatch propagating as `Failure(Conflict)`. **MUST fail to build** before T074 lands.
- [X] T071 [P] [US4] Write `tests/Infrastructure.Tests/Persistence/LedgerRepository_DeleteAsyncTests.cs` against the to-be-built `DeleteAsync` with Testcontainers Postgres: successful delete removes the row and writes an `event_type=3` audit row in the same transaction with `(event_at - operationStart) < 5 s` (SC-005); the audit row remains after delete (FR-008); a follow-up Create with the same name succeeds. **MUST fail to build** before T075 lands.
- [X] T072 [P] [US4] Write `tests/Api.IntegrationTests/Ledgers/DeleteLedgerEndpointTests.cs` against the to-be-built `LedgersService.DeleteLedger` with `UseEnvironment("Production")` + Testcontainers. Acceptance Scenarios 4.1–4.4; verifies that immediately re-creating a ledger with the same name succeeds end-to-end through `CreateLedger`. **MUST fail to build** before T077 lands.

### Implementation for US4 (turns T070–T072 green)

- [X] T073 [US4] Implement `src/Application/Features/Ledgers/Commands/DeleteLedger/DeleteLedgerCommand.cs` per `data-model.md` §4 and `src/Application/Features/Ledgers/Commands/DeleteLedger/DeleteLedgerRequestMap.cs` (decoding `version_token` 8-byte big-endian → `long`).
- [X] T074 [US4] Implement `src/Application/Features/Ledgers/Commands/DeleteLedger/DeleteLedgerHandler.cs` (`AddScoped`) — loads the ledger owner-scoped, rejects archived with `Failure(LedgerErrors.ArchivedCannotDelete)`, builds a `Delete` `AuditEntry` with the pre-delete row snapshot, calls `ILedgerRepository.DeleteAsync(id, ownerId, expectedVersion, audit, ct)`, increments `ledger.deleted` on success (depends on T019, T021, T044, T045, T070, T073). **Turns T070 green.**
- [X] T075 [US4] Extend `src/Infrastructure/Persistence/LedgerRepository.cs` with `DeleteAsync(ledgerId, ownerId, expectedVersion, audit, ct)` running `Ledger/DeleteOptimistic.sql` (`DELETE FROM ledger WHERE id = @id AND owner_id = @ownerId AND version = @expected RETURNING id`); writes the audit row in the same transaction; zero rows → `Result.Failure(LedgerErrors.NotFound)` when the row is absent and `Result.Failure(LedgerErrors.Conflict)` when the row exists but the version mismatches (handler distinguishes via a pre-fetch) (depends on T024, T044, T048, T049, T067, T071). **Turns T071 green.**
- [X] T076 [US4] Extend `src/Infrastructure/Persistence/CachingLedgerRepository.cs` with `DeleteAsync` — forwards and on `IsSuccess` calls `IFusionCache.RemoveByTagAsync($"owner:{ownerId}", ct)` and `RemoveByTagAsync($"ledger:{ledgerId}", ct)` (depends on T031, T068, T075).
- [X] T077 [US4] Implement `LedgersService.DeleteLedger(...)` override in `src/Api/Grpc/LedgersService.cs` returning `DeleteLedgerResponse` (empty) on success and routing failures through `ResultToRpcExceptionMapper` (`ArchivedCannotDelete → InvalidArgument`, `NotFound → NotFound`, `Conflict → AlreadyExists`) (depends on T033, T072, T074, T076). **Turns T072 green.**

**Checkpoint**: All four user stories are independently functional; the full `quickstart.md` §4 smoke flow runs green from end to end against the AppHost-launched stack.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Tune, document, instrument, and ratchet the gates — including the v2.10.0 AppHost compliance checks and the SC-002/SC-003 perf gate (Constitution Workflow > Performance budgets + `dotnet-diag:microbenchmarking`).

- [ ] T078 [P] Add the Grafana dashboard at `monitoring/ledger-service.json` covering `ledger.created`/`updated`/`deleted`/`archived`/`audit.purged` counters, the `ledger.operation.duration` histogram (P50/P95/P99 panels with the explicit buckets from `research.md` §10), FusionCache hit/miss panels, and the `/health/ready` panel (per `dotnet-observability` Phase 4 + `quickstart.md` §5.2). The same dashboard JSON loads in the local Aspire dashboard's "Dashboards" view and the production Grafana.
- [ ] T079 [P] Add an aggregated cross-owner scoping sweep at `tests/Api.IntegrationTests/Ledgers/CrossOwnerScopingSweepTests.cs` exercising every gRPC method (`Create`/`Get`/`List`/`Update`/`Delete`) as one owner against another owner's ledger, verifying SC-004 ("100 % of cross-owner attempts rejected") at the suite level — the per-RPC tests T041/T055/T065/T072 already cover the surface, but this sweep proves the property as a single signal.
- [ ] T080 [P] Implement `tests/Performance/Benchmarks/LedgerCrudBenchmarks.cs` using BenchmarkDotNet — boots `WebApplicationFactory<Program>` once via `[GlobalSetup]` against Testcontainers Postgres + Redis, opens a `GrpcChannel`, and benchmarks `CreateLedger` / `GetLedger` (cache hit + miss) / `ListLedgers` (1 000-row owner) / `UpdateLedger` / `DeleteLedger`. **Assert SC-002 (p99 < 1 s for single-ledger CRUD) and SC-003 (p95 < 1 s for `ListLedgers` over 1 000 rows)** by failing the run if any reported percentile exceeds the budget. Output is committed under `benchmarks/results/` for trend tracking (per Constitution Workflow > Performance budgets and `dotnet-diag:microbenchmarking`).
- [ ] T081 [P] Implement `tests/Performance/Benchmarks/LedgerRepositoryGetByIdBenchmark.cs` using BenchmarkDotNet — measures the `LedgerRepository.GetByIdAsync` factory P50 under realistic Testcontainers conditions. **Use the measured P50 to derive the FusionCache `FactorySoftTimeout` value** and update T031's `FusionCacheRegistration` if the measured P50 ≠ 200 ms; this task is **blocking on merge** of any PR that ships T031 (Constitution Tech Stack > Caching forbids unsubstantiated guesses).
- [ ] T082 Run the full `quickstart.md` §4 smoke flow against `dotnet run --project src/AppHost` (the canonical bring-up) using `grpcurl`. The DbUp init-container migrator (T025/T026) is registered in the AppHost as a project resource the Api waits on (`.WaitFor(migrator)`), so schema is applied automatically before the first request — no manual migration step is permitted (Constitution v2.11.0 > Tech Stack > Local development orchestration > Schema migrations). Capture the produced trace IDs in the Aspire dashboard's Traces tab and confirm a single trace covers `gRPC server → handler → repository → SQL command → audit insert` (per `quickstart.md` §5.4).
- [ ] T083 [P] Run the same `quickstart.md` §4 smoke flow against the compose fallback (`docker compose -f docker-compose.dev.yml up -d` + migrations + `dotnet run --project src/Api`) per `quickstart.md` §3a to verify connection-string parity with the AppHost graph (image versions, ports, env vars all match). Divergence is a review blocker per Constitution Tech Stack > Local development orchestration.
- [ ] T084 Add a coverage gate step to the build (`dotnet test --collect:"XPlat Code Coverage"` + ReportGenerator + a diff-aware patch-coverage check) failing the build below 80 % per Constitution Principle II and `quickstart.md` §6.
- [ ] T085 [P] Run `dotnet list package --vulnerable --include-transitive` and ensure the build fails on any reported vulnerability per Constitution Principle V (vulnerable-dependency scanning).
- [ ] T086 [P] Validate the proto snapshot at `specs/001-ledger-crud/contracts/ledger.v1.proto` is byte-identical to the build-time source at `src/Contracts/Protos/ledger.v1.proto` (a small CI script `diff -q`); flag any drift as a review-blocking change per Constitution Principle III.
- [ ] T087 [P] Add a CI guard that fails the build if any test project file references `Aspire.Hosting.Testing` or `DistributedApplicationTestingBuilder` (a `grep` step over `tests/**/*.csproj` and `tests/**/*.cs`) — enforcing the v2.10.0 carve-out that Aspire is NOT a test host (per Constitution Tech Stack > Local development orchestration; `quickstart.md` §6).
- [ ] T088 [P] Add a CI guard that fails the build if `src/Api`, `src/Application`, `src/Infrastructure`, `src/Infrastructure.Migrations`, or `src/Domain` carry a `<ProjectReference>` to `src/AppHost` (a `grep`/XPath step) — enforcing the v2.10.0 rule that runtime projects MUST NOT reference the AppHost back (per Constitution Tech Stack > Local development orchestration; `plan.md` §Project Structure).
- [ ] T089 ~~Auth-deferral tracker~~ — **DROPPED in v3.0.0**: the constitution no longer governs authentication or authorization, so there is no deferred-auth state to track. Owner identity for FR-009 / FR-012 is a feature-level concern resolved via the `X-Owner-Id` header.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No external dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1. **Blocks every user story.**
- **User Story 1 (Phase 3, P1)**: Depends only on Phase 2. MVP slice.
- **User Story 2 (Phase 4, P1)**: Depends on Phase 2 + the shared US1 Domain/repository plumbing (T044, T045, T048, T049, T051 — `LedgerViewMapper`).
- **User Story 3 (Phase 5, P2)**: Depends on Phase 2 + the same Domain/repository foundation built in US1 (T044, T045, T048, T049) and `ResultToRpcExceptionMapper` (T033).
- **User Story 4 (Phase 6, P3)**: Depends on Phase 2 + the same Domain/repository foundation (T044, T045, T048, T049, T067) and `ResultToRpcExceptionMapper`.
- **Polish (Phase 7)**: Depends on every user story being complete and green; T082/T083 also depend on the AppHost (T006/T007), the migrations (T026), and the compose file (T004); T080 (perf gate) and T081 (FusionCache substantiation) are merge-blocking for any PR that ships T031.

### Within Each User Story (TDD ordering — Principle II)

1. **Red**: write the test files first (the `[P]`-marked test tasks at the top of each story phase). Run them; they MUST fail to build (the target type does not yet exist) — this is the "watch it fail" step.
2. **Green**: land the implementation tasks in dependency order — Domain types → Application command/query + RequestMap → Infrastructure repository extension → Caching decorator extension → gRPC service override.
3. **Refactor**: clean up while the tests stay green.

Within each tier, tasks marked `[P]` touch different files and may be executed in parallel.

### Parallel Opportunities

- **Phase 1**: T003, T004, T005, T006, T007, T008, T009, T010 are independent file additions — all `[P]`.
- **Phase 2 (TDD red)**: T011/T012/T015/T027 are `[P]` and run BEFORE the matching impl tasks.
- **Phase 2 (impl)**: T013/T016/T017/T019/T022/T029/T034/T036 are all `[P]`; T014/T018/T020/T021/T023/T024/T025/T026/T028/T030/T031/T032/T033/T035/T037 are sequential or have explicit deps inside the phase.
- **Within each user story**: all test files (e.g., T038–T041 for US1) are `[P]` and run before any impl; the Domain primitive impls (T042/T043 for US1) are `[P]`.
- **Across stories** (after Phase 2 + the shared US1 Domain/repo plumbing): US3 and US4 can be developed concurrently because they touch separate command folders, separate repository methods, and separate gRPC overrides; US2 can run alongside US3/US4 as long as the shared `LedgerRepository`/`CachingLedgerRepository` files are coordinated through `[P]`-vs-non-`[P]` discipline.

---

## Parallel Example: User Story 1 (TDD-ordered)

```bash
# Step 1 (RED): write all four US1 tests in parallel; run; they MUST fail to build.
Task: "T038 [US1] tests/Domain.Tests/Ledgers/LedgerBuilderTests.cs"
Task: "T039 [US1] tests/Application.Tests/Ledgers/CreateLedgerHandlerTests.cs"
Task: "T040 [US1] tests/Infrastructure.Tests/Persistence/LedgerRepository_CreateAsyncTests.cs"
Task: "T041 [US1] tests/Api.IntegrationTests/Ledgers/CreateLedgerEndpointTests.cs"

# Step 2 (GREEN): land the Domain primitives in parallel.
Task: "T042 [US1] src/Domain/Ledgers/LedgerStatus.cs"
Task: "T043 [US1] src/Domain/Auditing/AuditEventType.cs"

# Step 3 (GREEN): drive the rest of the impl (T044 → T051) in dependency order; observe each test transition red → green.
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup) — including the AppHost (T006/T007). After this step, `dotnet run --project src/AppHost` MUST launch Postgres, Redis, the (stub) Api, and the Aspire dashboard.
2. Complete Phase 2 (Foundational) — write the foundational red tests (T011, T012, T015, T027) FIRST, observe failures, then implement the primitives. End with the gRPC pipeline + Aspire-aware connection strings (T023/T031/T035) green.
3. Complete Phase 3 (User Story 1) — write the four US1 red tests (T038–T041) FIRST, observe failures, then implement the Create flow (T042–T051) until all four go green.
4. **STOP and VALIDATE**: run `quickstart.md` §4.1 against the AppHost-launched service (after applying migrations per T082); confirm `ledger.created` increments live in the Aspire dashboard's Metrics tab, the audit row appears within 5 s (asserted in T040), and `dotnet test` passes for `Domain.Tests` / `Application.Tests` / `Infrastructure.Tests` / `Api.IntegrationTests`.
5. Demo / deploy the MVP slice. Note: production deployment uses plain containers / k8s — not the AppHost (per Constitution Tech Stack > Local development orchestration carve-out).

### Incremental Delivery

1. MVP (Phases 1–3) delivers **Create**.
2. Add Phase 4 (US2) → demo the **read** paths with caching.
3. Add Phase 5 (US3) → demo **update** with optimistic concurrency and archive semantics.
4. Add Phase 6 (US4) → demo **delete** with archive rejection and audit retention.
5. Phase 7 polish — dashboard, perf gate (SC-002/SC-003), FusionCache tuning, AppHost smoke + compose-parity smoke, coverage gate, vuln scan, contract drift check, AppHost compliance guards.

### Parallel Team Strategy

After Phase 2 plus the shared US1 Domain/repository plumbing (T042–T045, T048, T049, T051 — the `LedgerViewMapper`):

- Developer A: drive US2 (write T052–T055 red, then T056–T061 green).
- Developer B: drive US3 (write T062–T065 red, then T066–T069 green).
- Developer C: drive US4 (write T070–T072 red, then T073–T077 green).

Coordinate edits to `src/Infrastructure/Persistence/LedgerRepository.cs`, `src/Infrastructure/Persistence/CachingLedgerRepository.cs`, and `src/Api/Grpc/LedgersService.cs` (each story extends these) via small, story-scoped commits.

---

## Notes

- Every task carries an explicit file path and (where applicable) a `[US#]` traceability label.
- Tests are mandatory and TDD-ordered (Constitution Principle II — NON-NEGOTIABLE). Test files are written FIRST, observed to FAIL (build or assertion), and only then turned green by the matching impl tasks. Failure-path assertions on code that returns `Result` use `result.IsFailure.Should().BeTrue()` + `result.Error!.Code.Should().Be("...")`, never `Should().Throw<>()`. Integration tests open a `GrpcChannel` over `WebApplicationFactory<Program>` (configured with `UseEnvironment("Production")` so HTTPS hardening + missing/unparseable owner-header rejection paths are exercised) with **Testcontainers** Postgres + Redis (per `research.md` §11 and `quickstart.md` §6). `Aspire.Hosting.Testing` is forbidden on the test path (v2.10.0 carve-out); T087 enforces this in CI.
- Caching decorator only caches `Result.Success`; `Result.Failure` is never cached, and writes invalidate by `owner:{ownerId}` (and additionally `ledger:{ledgerId}` for Update/Delete).
- gRPC failures are translated only at the API boundary via `ResultToRpcExceptionMapper`; handlers and repositories never throw `RpcException` directly (Constitution Principle VI).
- Local-dev bring-up is `dotnet run --project src/AppHost` (canonical, v2.10.0 NON-NEGOTIABLE). The compose file remains as a documented fallback only; T083 verifies the two paths stay in sync. Production does NOT use the AppHost; T088 enforces that no runtime project references it back.
- Stop at any checkpoint to ship the increment behind a feature flag or release-branch gate.

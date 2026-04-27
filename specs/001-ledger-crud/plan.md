# Implementation Plan: Basic CRUD for Ledger Services

**Branch**: `001-ledger-crud` | **Date**: 2026-04-27 | **Spec**: [`./spec.md`](./spec.md)
**Input**: Feature specification from `/specs/001-ledger-crud/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Build a small gRPC microservice (`Ledgers`) on .NET 10 that provides
single-user-owned CRUD for ledger records, with optimistic concurrency,
ISO-4217 currency validation, archive/un-archive semantics, hard-delete,
and a non-user-facing audit log retained for 1 year. Persistence is
PostgreSQL via Npgsql (EF Core for migrations only; vanilla Dapper for
runtime CRUD); caching is FusionCache with L1 + Redis L2 + Redis
backplane, applied as a Scrutor decorator over the repository. Authn/authz
is deferred per Principle V (on-prem deployment) but `ICurrentUser` is
introduced now as an architectural-readiness stub because the feature
differentiates by caller identity (FR-009 owner-scoping, FR-012 audit
actor). HTTP surface is restricted to `/health/*` and Swagger; business
operations are gRPC-only with JsonTranscoding for documentation.

See [`./research.md`](./research.md) for the full set of resolved
technical questions, [`./data-model.md`](./data-model.md) for entity
shapes and SQL schema, [`./contracts/ledger.v1.proto`](./contracts/ledger.v1.proto)
for the wire contract, and [`./quickstart.md`](./quickstart.md) for
local bring-up.

## Technical Context

**Language/Version**: C# (latest stable shipped with .NET 10), targeting `net10.0`. Nullable reference types enabled; `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` solution-wide.
**Primary Dependencies**: `Grpc.AspNetCore`, `Grpc.Tools`, `Microsoft.AspNetCore.Grpc.JsonTranscoding`, `Microsoft.AspNetCore.Grpc.Swagger`; `Npgsql`, `Npgsql.DependencyInjection`, `Npgsql.EntityFrameworkCore.PostgreSQL` (migrations sub-project only), `Dapper`; `ZiggyCreatures.FusionCache`, `ZiggyCreatures.FusionCache.Serialization.SystemTextJson`, `ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis`, `Microsoft.Extensions.Caching.StackExchangeRedis`; `Scrutor`; `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.GrpcNetClient`; xUnit + FluentAssertions + NSubstitute + `coverlet.collector` + `Testcontainers.PostgreSql` + `Testcontainers.Redis` for tests.
**Storage**: PostgreSQL 16+ via Npgsql (pooled `NpgsqlDataSource`). Schema managed by EF Core 10 migrations in `src/Infrastructure.Migrations/`; runtime queries use vanilla Dapper. Redis (latest stable) provides the FusionCache L2 cache and backplane.
**Testing**: `dotnet test` with xUnit. Integration tests host the service via `WebApplicationFactory<Program>`, open a `GrpcChannel` over the test server's `HttpClient`, and exercise the generated client. Postgres/Redis provisioned per-test-class via Testcontainers. Coverage gate: ≥80% **patch coverage** (constitution Principle II).
**Target Platform**: Linux server (containerised), on-premise deployment behind a trusted network boundary. TLS terminated at the service or trusted ingress. HTTP/2 required for gRPC; cleartext h2c forbidden.
**Project Type**: gRPC microservice. Single service repository with the standard 5-project Clean Architecture layout (`Domain`, `Application`, `Infrastructure`, `Contracts`, `Api`) plus `Infrastructure.Migrations` per the persistence split-responsibility model.
**Performance Goals**: SC-002 — p99 < 1 s for CRUD on a single ledger under nominal load. SC-003 — p95 < 1 s for a single page of `ListLedgers` for users with up to 1 000 ledgers. SC-005 — audit log entry visible within 5 s of the operation completing (achieved by writing the audit row in the same transaction as the state change).
**Constraints**: gRPC-only for business operations; HTTP restricted to `/health/live`, `/health/ready`, `/swagger`, `/swagger/v1/swagger.json`. Result pattern across layer boundaries — no exceptions for expected failures. Builder pattern for Domain types (no public constructors). Validation via Data Annotations + `IValidatableObject` (no bare `if`s). Hard-delete semantics for ledgers; 1-year audit retention. Optimistic concurrency via opaque `version_token`.
**Scale/Scope**: Single tenant inside the on-prem perimeter. Targets up to ~1 000 ledgers per owner (per SC-003); the keyset pagination scheme allows growth beyond that without redesign. Listing default page size 50, max 200.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Idiomatic C# & Code Quality | ✅ Pass | `net10.0`, NRT on, warnings-as-errors, analyzers, CPM at repo root, `sealed class` for Domain (private constructors via Builders), `record` for Application DTOs/translation maps. `CancellationToken` plumbed through every public async method. |
| II. Test-First Discipline | ✅ Pass | xUnit + FluentAssertions + NSubstitute. Integration tests via `WebApplicationFactory<Program>` + `GrpcChannel`. Testcontainers for Postgres + Redis. ≥80% patch-coverage gate via coverlet + a diff-aware reporter. `dotnet-test:*` skills are the canonical helpers. |
| III. API-First Contracts & Versioning (gRPC-first) | ✅ Pass | Single `.proto` (`ledger.v1.proto`) under `src/Contracts/Protos/`; planning snapshot at `specs/001-ledger-crud/contracts/`. Package `sddDemo.ledger.v1`. JsonTranscoding + Swagger wired. HTTP restricted to health + Swagger. Versioning by package; only additive changes within v1. Errors flow as `Result.Failure` → `ResultToRpcExceptionMapper` (no direct `RpcException` throws). |
| IV. Observability & Operability | ✅ Pass | Single `UseOtlpExporter()`; resource attrs `service.name=ledger-service`, `service.version=<gitsha>`. `IMeterFactory.Create` and static `ActivitySource` per the `dotnet-observability` 5-phase flow. Dot-namespaced metric names; explicit histogram buckets. FusionCache OTel instrumentation registered alongside. `/health/live` + `/health/ready` (DB + Redis + backplane). Dashboard committed at `monitoring/ledger-service.json` (repo root). `IOptions<T>` with `ValidateDataAnnotations()` + `ValidateOnStart()`. |
| V. Security by Default — Always-on | ✅ Pass | TLS enforced (HSTS + `UseHttpsRedirection` outside Development); secrets via User Secrets / env vars / on-prem secret manager — never `appsettings.*.json`; server-side validation at every tier (Principle VI > Validation tiers); CI runs `dotnet list package --vulnerable --include-transitive`. |
| V. Security by Default — Deferral & readiness | ✅ Pass with declared scope | On-prem deployment within a trusted network — authn/authz **explicitly deferred**. Re-activation triggers (cloud / multi-tenant / untrusted clients) NOT met. Architectural readiness preserved: gRPC interceptor chain in place; `ICurrentUser` introduced now (REQUIRED because FR-009 + FR-012 differentiate by caller identity); Swagger UI Development-only by default. Re-activation, when triggered, will be config not refactor. |
| VI. Design & Structure — Layers, OO, CQRS, Result, Validation, Builder, Patterns | ✅ Pass | Five projects under `src/` + `Infrastructure.Migrations`; one-directional refs enforced by `ProjectReference`. CQRS via folders + `AddScoped<TVerbNounHandler>()` (no MediatR). `Result<T>` from Domain/Application/Infrastructure; only the `Api` boundary translates to `RpcException` via `Api/Grpc/ResultToRpcExceptionMapper.cs`. Outermost gRPC interceptor + HTTP `IExceptionHandler` as the safety net. Builder pattern on `Ledger` and `AuditEntry` (private ctors). Data Annotations + `IValidatableObject` + `DomainValidator.Validate<T>` everywhere DTO-shaped (commands, queries, translation maps, options, Domain types); protobuf messages carve-out — validated at the next hop on the mapped record. Repository caching via `CachingLedgerRepository` decorator wired with `services.Decorate<>()` (Scrutor); only `Result.Success` cached; Update/Delete invalidate by tag. |

**Gate result**: All gates pass without violations. The Complexity
Tracking table below is empty — no constitutional waivers required.

**Skills referenced (per the constitution's mandates)**:

- `dotnet-aspnet:configuring-opentelemetry-dotnet` — for the
  `AddOpenTelemetry()` wiring in `Program.cs`.
- `dotnet-observability` — canonical 5-phase flow for instrumentation,
  dashboards, and Phase 4 publish to `monitoring/ledger-service.json`.
- `fusion-cache` — canonical reference for the L1+L2+Backplane wiring
  in `Program.cs` and the decorator pattern.
- `dotnet-msbuild:directory-build-organization` and
  `dotnet-nuget:convert-to-cpm` — for the repo-root
  `Directory.Build.props` and `Directory.Packages.props`.
- `dotnet-test:writing-mstest-tests` is **not** applicable — this
  project uses xUnit (Principle II); test authoring follows
  `dotnet-test:test-anti-patterns` and the
  `dotnet-test:code-testing-agent` pipeline. `dotnet-test:run-tests`
  + `dotnet-test:coverage-analysis` + `dotnet-test:crap-score` cover
  execution and the coverage gate.
- `dotnet-data:optimizing-ef-core-queries` — applies only to migration
  design (e.g., a future complex data move), per the constitution's
  re-scoping under the EF/Dapper split.
- `dotnet-diag:microbenchmarking` — required to derive the
  FusionCache `FactorySoftTimeout` from a measured factory P50 before
  merge.

## Project Structure

### Documentation (this feature)

```text
specs/001-ledger-crud/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── ledger.v1.proto  # Phase 1 output (snapshot of src/Contracts/Protos/ledger.v1.proto)
├── checklists/
│   └── requirements.md  # Written by /speckit-specify
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── Domain/                                  # zero src/ deps
│   ├── Common/                              # Result, Error, ErrorType, DomainValidator
│   ├── Ledgers/                             # Ledger (sealed class + Builder), LedgerStatus, LedgerErrors
│   ├── Auditing/                            # AuditEntry (sealed class + Builder), AuditEventType
│   └── Currency/                            # ICurrencyCatalog, ValidIsoCurrencyAttribute
├── Application/                             # → Domain
│   ├── Abstractions/
│   │   ├── Identity/ICurrentUser.cs
│   │   └── Persistence/ILedgerRepository.cs, IAuditRepository.cs, ICurrencyCatalog.cs
│   └── Features/Ledgers/
│       ├── Commands/{CreateLedger,UpdateLedger,DeleteLedger}/
│       │   ├── <Verb>LedgerCommand.cs
│       │   ├── <Verb>LedgerHandler.cs
│       │   └── <Verb>LedgerRequestMap.cs    # protobuf ↔ command translation
│       └── Queries/{GetLedger,ListLedgers}/
│           ├── <Get|List>...Query.cs
│           ├── <Get|List>...Handler.cs
│           ├── <Get|List>...RequestMap.cs
│           └── LedgerListPage.cs            # immutable page DTO
├── Infrastructure/                          # → Application
│   ├── Persistence/
│   │   ├── LedgerRepository.cs              # vanilla Dapper over NpgsqlDataSource
│   │   ├── CachingLedgerRepository.cs       # FusionCache decorator (Scrutor-wired)
│   │   ├── AuditRepository.cs
│   │   ├── DataSourceFactory.cs
│   │   └── Sql/{Ledger,Audit}/*.sql         # embedded SQL resources
│   ├── Identity/AnonymousCurrentUser.cs     # X-Owner-Id header → ICurrentUser
│   ├── Currency/CurrencyCatalog.cs          # ImmutableHashSet<string> from RegionInfo
│   └── Background/AuditRetentionPurgeService.cs
├── Infrastructure.Migrations/               # → Application (DbContext NOT runtime-registered)
│   ├── LedgerMigrationsDbContext.cs
│   └── Migrations/
├── Contracts/                               # zero src/ deps
│   ├── Protos/
│   │   └── ledger.v1.proto                  # build-time source of truth
│   └── SddDemo.Ledger.Contracts.csproj      # <Protobuf Include="Protos\**\*.proto" />
└── Api/                                     # composition root
    ├── Program.cs
    ├── Grpc/
    │   ├── LedgersService.cs                # Ledgers.LedgersBase implementation
    │   ├── ResultToRpcExceptionMapper.cs    # boundary translator
    │   └── Interceptors/
    │       ├── ExceptionSafetyNetInterceptor.cs   # registered FIRST
    │       ├── ValidationInterceptor.cs           # transport-shape validation
    │       └── (auth interceptors — slot reserved per Principle V readiness)
    ├── Health/                              # /health/live, /health/ready
    └── Swagger/                             # JsonTranscoding + Microsoft.AspNetCore.Grpc.Swagger config

tests/
├── Domain.Tests/                            # Builder happy-path + validation-failure fixtures
├── Application.Tests/                       # handlers with NSubstitute mocks for repos / cache / IClock / ICurrentUser
├── Infrastructure.Tests/                    # Dapper repo + caching decorator vs. Testcontainers Postgres + Redis
└── Api.IntegrationTests/                    # WebApplicationFactory<Program> + GrpcChannel against the real DI graph

monitoring/
└── ledger-service.json                      # Grafana dashboard (Principle IV — repo root)

Directory.Build.props                        # net10.0, NRT, warnings-as-errors, analyzers
Directory.Packages.props                     # CPM (every NuGet version pinned here)
SddDemo.Ledger.sln
docker-compose.dev.yml                       # Postgres + Redis for local dev
```

**Structure Decision**: Standard 5-project Clean Architecture layout
mandated by Principle VI, plus the sibling `Infrastructure.Migrations`
sub-project required by the EF-vs-Dapper persistence split (Tech Stack
> Persistence). No deviations from the canonical layout — there is
nothing in this feature that needs to bend it.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*No violations. The plan adheres to every principle without
exceptions; no Complexity Tracking entries are required.*

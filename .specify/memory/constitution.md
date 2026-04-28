<!--
SYNC IMPACT REPORT
==================
Version change: 2.11.0 → 3.0.0
Bump rationale: MAJOR. Removes authentication and authorization from the
  constitution's scope. Principle V is renamed "Operational Hardening" and
  now governs only: TLS / HTTPS / HSTS, secrets management, server-side
  input validation, and vulnerable-dependency scanning. The "Scope —
  on-premise deployment, authn/authz deferred" sub-section, the "Deferred
  rules" sub-section (`[Authorize]`, anonymous opt-in, auth interceptors,
  authorization policies), the "Architectural readiness" sub-section
  (`ICurrentUser` / `ITenantContext` stubs, gateway-supplied identity
  header pattern, Dev fallback for identity headers, automatic
  re-activation triggers), and the auth-related portion of the Rationale
  are all removed. Side-effects: (i) `ErrorType.Unauthorized` is removed
  from the canonical Result-pattern enum; (ii) the gRPC mapping table
  loses the `Unauthorized → Unauthenticated` row; (iii) the quarterly
  Principle V audit drops question (2) about the auth re-activation
  trigger; (iv) the in-flight audit entry "From 2.9.0" (auth-stub
  preservation) and the "Dev-fallback-for-identity-header" sub-bullet of
  "From 2.10.2" are removed; (v) the pre-existing "Follow-up TODO" about
  the auth re-activation trigger is removed. Services that need an owner
  or tenant key for ownership scoping or audit-actor logging are free to
  introduce one as a feature-level concern (e.g., a header-driven
  `ICurrentUser` returning `Result<Guid>` with `ErrorType.Validation` on
  malformed input); the constitution no longer mandates such a seam.
  Historical changelog entries below are preserved verbatim — the
  v2.9.0 / v2.10.2 entries remain as a record of why prior versions
  required identity stubs, even though that requirement is now lifted.
  This is a deliberate scope contraction, not a security regression: the
  always-on rules (TLS, secrets, validation, dependency scanning) remain
  fully load-bearing.

Modified principles:
  - Principle V: "Security by Default" → "Operational Hardening". Active
    scope reduced to TLS, secrets, server-side validation, and dependency
    scanning. All authn/authz material removed.

Modified sections:
  - Result-pattern enum example: `Unauthorized` member removed.
  - gRPC error-mapping table: `Unauthorized → Unauthenticated` row removed.
  - Quarterly compliance audit: V question (2) (auth re-activation trigger)
    removed.
  - In-flight artifact audit: "From 2.9.0" entry removed in full;
    "From 2.10.2" entry trimmed to (i) `ASPNETCORE_ENVIRONMENT`
    forwarding only — the (ii) identity-Dev-fallback sub-bullet removed.
  - Follow-up TODOs: auth re-activation trigger entry removed.
  - Principle III rationale: "one ingress story, one auth model" trimmed
    to "one ingress story" — auth model is no longer an axis the
    constitution governs.
  - Decorator examples: "auth enrichment" removed from the list of
    canonical decorator use-cases.
  - AppHost `ASPNETCORE_ENVIRONMENT` forwarding rationale: parenthetical
    "AnonymousCurrentUser Dev fallback under Principle V" removed —
    Principle V no longer mentions Dev fallbacks.

Earlier amendment (preserved):
Version change: 2.10.2 → 2.11.0
Bump rationale: MINOR. Names **DbUp** as the sanctioned schema-migration
  tool and ratifies the **Aspire init-container migrator pattern** as the
  canonical way to apply schema during local dev bring-up — no manual
  schema step is permitted in the dev loop. The `Infrastructure.Migrations`
  project is a Generic Host console (`Microsoft.NET.Sdk` +
  `OutputType=Exe`) running a `BackgroundService` that invokes DbUp against
  the Aspire-injected connection string, then signals
  `IHostApplicationLifetime.StopApplication`. The AppHost registers it via
  `AddProject<TMigrator>("migrator").WithReference(postgres).WaitFor(postgres)`
  and the Api waits for it via `.WaitFor(migrator)` (NOT
  `.WaitForCompletion(migrator)` — Aspire 9.5 does not reliably transition
  short-lived `dotnet run` projects to "Finished", so dependents stay queued
  forever; `WaitFor` waits on "Running", which the Generic Host reaches
  before `BackgroundService.ExecuteAsync` fires the actual migration). The
  `Aspire init-container migrator` rule is added under Tech Stack > Local
  development orchestration. Persistence > Schema management bullet is
  rewritten to specify DbUp + numbered append-only SQL scripts. Earlier
  patches (2.10.1 launchSettings.json, 2.10.2 ASPNETCORE_ENVIRONMENT
  forwarding + Dev identity fallback) are preserved unchanged.

Earlier amendment (preserved):
Version change: 2.10.1 → 2.10.2
Bump rationale: PATCH. Tightens the v2.10.0 "canonical bring-up" promise
  with two further missing-precondition rules surfaced during 001-ledger-crud:
  (a) Aspire 9.x does NOT auto-propagate `ASPNETCORE_ENVIRONMENT` to child
  project resources, so the AppHost's `AddProject<T>("api")` call MUST
  forward it explicitly (`.WithEnvironment("ASPNETCORE_ENVIRONMENT",
  builder.Environment.EnvironmentName)`); without this the Api boots in
  Production and Development-gated middleware (Swagger, dev exception
  pages) silently disappears even though the AppHost itself is in
  Development. (b) When a service relies on a trusted-gateway-supplied
  identity header (e.g. `X-Owner-Id`) per Principle V's on-prem auth-deferred
  rules, `src/Api/appsettings.Development.json` MUST exist and MUST populate
  the corresponding Dev fallback config (e.g. `Identity:DevOwnerId`) so
  `dotnet run --project src/AppHost` reaches the Swagger happy path without
  the developer having to hand-craft the header on every request. Both rules
  are clarifications of existing principles, not new principles — no
  semantic change.

Earlier amendment (preserved):
Version change: 2.10.0 → 2.10.1
Bump rationale: PATCH. Clarification of the existing "Local development
  orchestration — .NET Aspire AppHost (NON-NEGOTIABLE)" bullet: the AppHost
  MUST ship `Properties/launchSettings.json` because Aspire 9.x reads
  `ASPNETCORE_URLS`, `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL`, and
  `ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL` from there at startup. Without it
  `dotnet run --project src/AppHost` throws
  `OptionsValidationException: ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL ... is
  not set` before any resource is provisioned, which silently breaks the
  v2.10.0 "canonical bring-up" promise. No principle semantics change;
  this is purely a missing-precondition fix surfaced during the
  001-ledger-crud feature. (See note at v2.10.0 below for the prior
  amendment.)

Earlier amendment (preserved):
Version change: 2.9.0 → 2.10.0
Bump rationale: MINOR. Tech Stack gains a new "Local development
  orchestration" bullet that mandates a .NET Aspire `*.AppHost` project
  per service as the canonical one-command bring-up of the service plus
  its infrastructure dependencies plus the Aspire dashboard (which
  doubles as the local OTLP collector for Principle IV signals). The
  bullet ships with two load-bearing carve-outs: (a) tests stay on
  Testcontainers (Principle II) — Aspire is NOT a test host; (b)
  production deployments do NOT use the AppHost — it is a developer-
  experience artifact, not a deployment artifact. A `docker-compose.dev.yml`
  MAY coexist as a fallback for SDK-less environments but the AppHost is
  the primary path. The "Observability backends" bullet is tightened in
  parallel: the Aspire dashboard is no longer a "MAY substitute" — it is
  the default local OTLP endpoint, supplied by the mandatory AppHost.

Modified principles:
  - None. (Principles I–VI unchanged.)

Modified sections:
  - Technology Stack & Platform Constraints — new "Local development
    orchestration" bullet inserted after "Observability backends".
    "Observability backends" bullet tightened to point at the AppHost-
    provided Aspire dashboard as the local OTLP collector by default.

Earlier history (oldest first):
  - 1.0.0 → 1.0.1 (PATCH): Principle I pointer to
    `.specify/memory/aspnet-core-best-practices.md`.
  - 1.0.1 → 1.1.0 (MINOR): mandatory `dotnet-test:*` + `dotnet-aspnet:*` tooling.
  - 1.1.0 → 1.1.1 (PATCH): Principle I scoped to compile-time conventions.
  - 1.1.1 → 1.2.0 (MINOR): mandatory `dotnet-data`/`dotnet-msbuild`/`dotnet-nuget`/
    `dotnet-diag` tooling; perf claims require BenchmarkDotNet evidence.
  - 1.2.0 → 2.0.0 (MAJOR): Principle III redefined — gRPC-first, HTTP restricted to
    health + Swagger, contracts as `.proto`. Sibling principles II/IV/V adjusted.
  - 2.0.0 → 2.1.0 (MINOR): Principle VI added — Clean Architecture, 5-project
    layout, hand-rolled CQRS, OO discipline, design-patterns rule.
  - 2.1.0 → 2.2.0 (MINOR): Principle VI gained the Result-pattern sub-section;
    Principle III's error bullet refined to defer to the boundary translator.
  - 2.2.0 → 2.3.0 (MINOR): Principle VI gained the outermost-safety-net rule
    (gRPC interceptor + HTTP exception middleware); `try`/`catch` restricted.
  - 2.3.0 → 2.4.0 (MINOR): Principle VI gained Repository-caching = Decorator
    mandate + Scrutor wiring mandate; Tech Stack gained "DI extensions" bullet.
  - 2.4.0 → 2.5.0 (MINOR): FusionCache mandated as the only cache abstraction
    with L1+L2+Backplane topology; `fusion-cache` skill named as canonical.
  - 2.5.0 → 2.6.0 (MINOR): Principle IV adopted `dotnet-observability` 5-phase
    flow; dashboards versioned as `monitoring/<service>.json`; Tech Stack
    gained "Observability backends" bullet.
  - 2.6.0 → 2.7.0 (MINOR): Persistence split into a dedicated schema-
    migration sub-project + Dapper (runtime CRUD) over Npgsql.
  - 2.7.0 → 2.8.0 (MINOR): consistency/ambiguity fixes from the 2.7.0
    internal review + Builder pattern for Domain construction + Data
    Annotations / `IValidatableObject` for validation (no bare `if` in
    Builders or handlers); `monitoring/` pinned to repo root; boundary
    translator named (`Api/Grpc/ResultToRpcExceptionMapper.cs`); coverage
    gate clarified as patch coverage; quarterly audit gained Principle VI;
    repository examples updated to `DapperUserRepository`;
    `Infrastructure` layer-structure description aligned with the Dapper +
    Npgsql + sibling-`Infrastructure.Migrations` model.
  - 2.8.0 → 2.8.1 (PATCH): Principle VI Validation rule scope made explicit
    — Data Annotations apply to every DTO-shaped type; protobuf messages
    and interceptor logic are explicit carve-outs.
  - 2.10.0 → 2.10.1 (PATCH): clarify that the mandatory AppHost MUST ship
    `Properties/launchSettings.json` defining `ASPNETCORE_URLS`,
    `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL`, and
    `ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL` (HTTPS by default; HTTP only
    with `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`) — without it Aspire 9.x
    fails startup before the dashboard binds.
  - 2.10.2 → 2.11.0 (MINOR): names **DbUp** as the sanctioned schema-
    migration tool and ratifies the Aspire init-container migrator pattern
    as the canonical schema-application path during dev bring-up.
    `Infrastructure.Migrations` is a Generic Host console running a
    `BackgroundService` that invokes DbUp against the Aspire-injected
    `ConnectionStrings:ledger`, then calls `StopApplication`. The AppHost
    chains it as `AddProject<TMigrator>("migrator").WithReference(postgres).WaitFor(postgres)`,
    and the Api uses `.WaitFor(migrator)` so the Api boots only after the
    migrator host reaches "Running". Manual schema-application steps are
    forbidden in feature quickstarts.
  - 2.10.1 → 2.10.2 (PATCH): two further canonical-bring-up clarifications:
    (a) the AppHost's `AddProject<T>` for the service MUST forward
    `ASPNETCORE_ENVIRONMENT` to the child via `.WithEnvironment(...)`
    because Aspire 9.x does not auto-propagate it, and Dev-gated middleware
    (Swagger, dev exception pages) silently disappears otherwise; (b) when
    a service uses a trusted-gateway-supplied identity header per
    Principle V's on-prem deferred-auth model, the service MUST ship
    `appsettings.Development.json` populating the Dev fallback config so
    Swagger / quickstart reaches the happy path without manual headers.
  - 2.8.1 → 2.9.0 (MINOR): Principle V restructured for on-premise
    deployment — authn/authz explicitly deferred (services run anonymously
    inside the trusted boundary); always-on rules (TLS, secrets, validation,
    dependency scanning) reaffirmed; automatic re-activation trigger when
    the system grows past the perimeter; architectural-readiness sub-
    section keeps interceptor chain + identity DI seams in place so
    re-engaging auth is configuration, not refactor; quarterly audit gained
    a two-question Principle V checklist.

Templates requiring updates (cumulative):
  - ⚠️ .specify/templates/plan-template.md — Constitution Check is generic
    and pulls rules from this file by reference, so no template edit is
    strictly required; however, when the Technical Context section is
    filled in for a .NET service, it SHOULD list the `src/AppHost/`
    Aspire project alongside the other src/ projects in the Project
    Structure block. (Per 2.10.0)
  - ✅ .specify/templates/spec-template.md — No principle-driven sections affected.
  - ⚠️ .specify/templates/tasks-template.md — Service scaffolding tasks SHOULD
    produce: (a) `Infrastructure.Migrations` Generic Host console
    sub-project (`Microsoft.NET.Sdk` + `OutputType=Exe`) running a
    `BackgroundService` that invokes DbUp against
    `ConnectionStrings:<service>`, with `Scripts/NNNN_*.sql` embedded
    resources; (b) `Npgsql` + `Dapper` package references and
    `AddNpgsqlDataSource(...)` in `Api`'s `Program.cs`;
    (c) `Infrastructure/Sql/` folder for embedded runtime SQL resources;
    (d) initial `Scripts/0001_Initial.sql` committed alongside the first repo;
    (e) `Domain/DomainValidator.cs` helper running
    `Validator.TryValidateObject(...)` and aggregating `ValidationResult`
    lists into `Result.Failure(Validation)`; (f)
    `Api/Grpc/ResultToRpcExceptionMapper.cs` boundary translator (fixed
    `ErrorType → StatusCode` mapping) before any handler-implementation
    task; (g) for each Domain type, a nested `sealed Builder` and the
    accompanying unit-test fixture exercising both happy-path and
    validation-failure paths. (Per 2.8.1) Validation attribute coverage
    extended to translation/mapping records and any DTO-shaped type that
    crosses a layer boundary. (Per 2.10.0) Add: (h) an `src/AppHost/<Service>.AppHost.csproj`
    referencing `Aspire.Hosting.AppHost`, with a `Program.cs` that wires
    Postgres + Redis + the `Api` project via Aspire hosting integrations
    (`builder.AddPostgres(...).AddDatabase(...)`, `builder.AddRedis(...)`,
    `builder.AddProject<Projects.Api>(...)`), the AppHost added to the
    solution file, and the canonical local bring-up step documented as
    `dotnet run --project src/AppHost` (replacing any standalone
    `docker-compose.dev.yml`-only bring-up; compose may remain as fallback).
    (Per 2.10.1) The AppHost scaffolding task MUST also produce
    `src/AppHost/Properties/launchSettings.json` with an `https` profile
    setting `applicationUrl`, `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL`, and
    `ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL` — without this file Aspire 9.x
    throws at host build time and the canonical bring-up never reaches
    the dashboard.

In-flight artifact audit required (cumulative):
  - From 2.7.0: runtime repository implementations MUST be Dapper-based
    over a pooled `NpgsqlDataSource` (no ORM at the runtime path). The
    public `IXxxRepository` contract returning `Result<T>` is unchanged.
  - From 2.8.0: (i) any Domain type with a public constructor MUST gain a
    nested `sealed Builder`, make its constructor private, and route
    construction through `Builder().With...().Build()`; (ii) any factory or
    handler that uses `if`-checks to return `Result.Failure(Validation)` MUST
    be rewritten with Data Annotations (or `IValidatableObject` for
    cross-property invariants) running through `DomainValidator.Validate<T>`;
    (iii) any service method that constructs `RpcException` directly MUST
    delegate to `ResultToRpcExceptionMapper`; (iv) any `try`/`catch` outside
    the Infrastructure boundary or the safety-net interceptor MUST be removed.
  - From 2.8.1: any DTO-shaped type that crosses a layer boundary
    (translation/mapping records in particular) MUST carry Data Annotations;
    any imperative `if`-validation in DTO-bound code MUST be rewritten as a
    `ValidationAttribute` or `IValidatableObject.Validate` call routed
    through `DomainValidator.Validate<T>`.
  - From 2.10.0: (i) any service whose only local-dev bring-up is
    `docker compose up` MUST add an `src/AppHost/` Aspire project; the
    compose file MAY remain as a fallback per the carve-out in the new
    Tech Stack bullet; (ii) any feature `plan.md` or `quickstart.md` that
    documents `docker compose -f docker-compose.dev.yml up` as the primary
    bring-up MUST be updated to document `dotnet run --project src/AppHost`
    as primary, with compose demoted to fallback; (iii) any test project
    using `Aspire.Hosting.Testing` / `DistributedApplicationTestingBuilder`
    on the canonical test path MUST be reverted to Testcontainers per
    Principle II — Aspire is not a test host.
  - From 2.10.1: any existing `src/AppHost/` project that lacks
    `Properties/launchSettings.json` MUST add it before merge. The file
    MUST define at least an `https` profile with `applicationUrl`,
    `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL`, and
    `ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL`. Verification: a clean
    `dotnet run --project src/AppHost` MUST start the dashboard without
    `OptionsValidationException`. CI SHOULD fail if the file is missing.
  - From 2.11.0: (i) `Infrastructure.Migrations` MUST be a Generic Host
    console (Microsoft.NET.Sdk + `OutputType=Exe`) running a
    `BackgroundService` that invokes DbUp against
    `ConnectionStrings:<service>` and then calls `StopApplication`;
    SQL ships under `Scripts/NNNN_*.sql` as embedded resources.
    (ii) Every AppHost MUST register the migrator as an init-container
    resource (see Local development orchestration) and the Api MUST
    `.WaitFor(migrator)` — builds without this wiring are review
    blockers. (iii) Every feature `quickstart.md` MUST state that schema
    is applied automatically by the migrator init-container; manual
    schema-application steps MUST NOT appear. Verification: a fresh
    `dotnet run --project src/AppHost` against an empty Postgres volume
    MUST yield a 2xx on a representative POST without ANY manual schema
    step.
  - From 2.10.2: every existing AppHost `Program.cs` MUST chain
    `.WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)`
    on every `AddProject<TService>(...)` call; missing forwarding is a
    review blocker.

Follow-up TODOs:
  - (2.10.0) None beyond the in-flight audit above; this amendment is
    purely additive at the Tech Stack layer with explicit non-goals
    (tests stay on Testcontainers; production stays on plain containers).
-->

# SDD Demo Constitution

## Core Principles

### I. Idiomatic C# & Code Quality

This principle is **deliberately scoped to compile-time and language-level
conventions**: target framework, analyzers, nullability, and idiomatic syntax. It
does NOT restate runtime or HTTP-pipeline rules.

> **Authoritative reference for runtime/HTTP behavior**:
> [`aspnet-core-best-practices.md`](./aspnet-core-best-practices.md) is the single
> source of truth for blocking-call avoidance, `HttpClient` pooling, `HttpContext`
> lifetime, response-write ordering, data-access patterns, large-object allocations,
> and `async void`. All code MUST conform to it; reviewers MUST cite it (not this
> principle) when raising those concerns. If guidance below appears to overlap with
> that document, the document wins.

Compile-time rules:

- All code MUST target the latest stable C# language version shipped with .NET 10.
- Nullable reference types MUST be enabled solution-wide; `<Nullable>enable</Nullable>`
  is non-negotiable.
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` MUST be set in
  `Directory.Build.props` so analyzer warnings fail the build, not the reviewer.
- Roslyn analyzers (`Microsoft.CodeAnalysis.NetAnalyzers`) and an `.editorconfig` MUST
  be in place; suppressions require an inline justification comment.
- Prefer expression-bodied members, `required` members, pattern matching,
  primary constructors, and async/await over manual continuations. **Records**
  are the default for DTOs (Application command/query inputs, transport
  translation records, immutable configuration shapes). Domain entities,
  aggregates, and value objects are `sealed class` with private constructors —
  they go through the Builder pattern in Principle VI (records' public primary
  constructor would bypass it).
- Public `async` methods MUST accept a `CancellationToken` and propagate it to every
  awaited call. *(Additive to the best-practices document, which mandates
  async-all-the-way but does not require explicit `CancellationToken` plumbing.)*

**Rationale**: Splitting "how the code is built and written" (this principle) from
"how the code behaves at runtime" (the best-practices document) eliminates the
ambiguity of two sources of truth that can drift apart. Reviewers always know which
document to cite; contributors always know which document to read.

### II. Test-First Discipline (NON-NEGOTIABLE)

TDD is mandatory. Every functional or contract change follows: write a failing test,
get user/reviewer approval on the test, watch it fail, implement the minimum to pass,
refactor.

- Unit tests MUST use xUnit with FluentAssertions; mocks via NSubstitute or Moq.
- gRPC contract changes (any edit to a `.proto`) MUST have an integration test that
  hosts the service via `WebApplicationFactory<TEntryPoint>`, opens a `GrpcChannel`
  over the test server's `HttpClient`, and exercises the generated client against
  the real DI container. HTTP-sidecar changes (health, Swagger) MUST be covered the
  same way at the HTTP level.
- Tests MUST run with `dotnet test` and complete deterministically; no `Thread.Sleep`,
  no reliance on machine clock or external network.
- Coverage gates: ≥80% **patch coverage** (lines added or modified in the PR
  diff), measured with `coverlet.collector` and a diff-aware reporter
  (`ReportGenerator` with `--reporttypes` including a diff filter, or the CI
  provider's native incremental coverage). Whole-project coverage is reported
  for trend tracking but is not the gate — the gate is patch coverage so a
  small bug fix is not held hostage to a low-coverage legacy file.
  Reviewers MAY block merge on a regression in either patch or trend coverage.
- **Required tooling — `dotnet-test` plugin skills.** Contributors and reviewers MUST
  use the installed `dotnet-test:*` skills as the canonical helpers for test work,
  rather than improvising equivalents:
  - **Authoring & review**: `dotnet-test:writing-mstest-tests`,
    `dotnet-test:test-anti-patterns`, `dotnet-test:dotnet-test-frameworks`,
    `dotnet-test:code-testing-agent`, `dotnet-test:code-testing-extensions`.
  - **Coverage**: `dotnet-test:coverage-analysis`, `dotnet-test:crap-score` — run
    before claiming the 80% gate is met; CRAP score MUST be inspected for any newly
    added high-complexity method.
  - **Refactoring for testability**: `dotnet-test:detect-static-dependencies`,
    `dotnet-test:generate-testability-wrappers`,
    `dotnet-test:migrate-static-to-wrapper` — apply when a unit under test depends on
    statics or framework singletons.
  - **Migrations**: `dotnet-test:migrate-mstest-v1v2-to-v3`,
    `dotnet-test:migrate-mstest-v3-to-v4`, `dotnet-test:migrate-vstest-to-mtp`,
    `dotnet-test:migrate-xunit-to-xunit-v3` — REQUIRED when touching legacy test
    projects; ad-hoc rewrites are forbidden.
  - **Execution**: `dotnet-test:run-tests`, `dotnet-test:filter-syntax`,
    `dotnet-test:mtp-hot-reload`, `dotnet-test:platform-detection`.

**Rationale**: ASP.NET Core's testability story (TestServer, in-memory hosts) is a
first-class capability — exercising it from day one is cheaper than retrofitting
characterization tests later. The `dotnet-test:*` skills encode Microsoft's current
testing guidance (MSTest v3/v4, MTP, coverage tooling); routing test work through them
keeps the suite consistent and avoids re-litigating settled choices in review.

### III. API-First Contracts & Versioning (gRPC-first)

Public service surface is governed by an explicit, reviewable contract written in
**Protocol Buffers**. **gRPC is the default and only transport for business
operations.**

- The HTTP surface of every service is restricted to exactly:
  1. `/health/live` and `/health/ready` (Principle IV);
  2. the Swagger UI (`/swagger`) and its OpenAPI document (`/swagger/v1/swagger.json`);
  3. nothing else. Any additional HTTP endpoint requires explicit reviewer sign-off
     and a `Complexity Tracking` entry in `plan.md`.
- Service contracts MUST be defined in `.proto` files. The build-time source of
  truth is `src/Contracts/Protos/` (consumed by `Grpc.Tools` from the `Contracts`
  project — see Principle VI). The planning snapshot in `specs/<feature>/contracts/`
  is a copy of the `.proto` as agreed during `/speckit-plan`; if it drifts from
  `src/Contracts/Protos/`, reviewers MUST flag the divergence. Generated C# (`*.cs`
  from `protoc`) is build output and MUST NOT be committed.
- Contracts MUST be documented via Swagger by enabling
  `Microsoft.AspNetCore.Grpc.JsonTranscoding` together with
  `Microsoft.AspNetCore.Grpc.Swagger`. Methods MUST carry `google.api.http`
  annotations so the OpenAPI document is complete. The transcoded JSON endpoints
  exist for documentation/exploration only — production clients MUST use the gRPC
  channel.
- Versioning is by **proto package**: `<org>.<service>.v1`, `<org>.<service>.v2`.
  Within a major version only **additive** changes are permitted (new optional
  fields, new methods, new enum values with `reserved` for removed ones). Breaking
  changes ship as a new package version that coexists with the old one until clients
  migrate. `Asp.Versioning` does not apply.
- Errors leave the gRPC service as `RpcException` with the appropriate
  `StatusCode`, but service methods MUST NOT throw `RpcException` directly —
  they receive a `Result<T>` from the Application layer and pass it to the
  boundary translator defined in Principle VI (Error handling). The translator
  is the single source of truth for the `ErrorType → StatusCode` mapping. Any
  unexpected exception that escapes the Result discipline is caught by the
  outermost gRPC interceptor (also Principle VI) and surfaced as
  `StatusCode.Internal` with a correlation ID — never with raw exception
  details on the wire. RFC 7807 Problem Details apply only to the HTTP sidecar
  (e.g., a 503 from a failed readiness probe).
- Protobuf messages MUST NOT be aliases for domain entities — keep a translation
  layer between transport messages and the domain model.
- Transport-shape validation (required fields present, formats parseable, enum
  values in range) MUST happen in a server interceptor or at the top of the
  service method. On failure it MUST return `Result.Failure(new Error(...,
  ErrorType.Validation))`, which the boundary translator (Principle VI) maps
  to `StatusCode.InvalidArgument` with a per-field detail. Service methods
  MUST NOT throw `RpcException` themselves — see the errors bullet above.
  Business-rule and invariant validation are handled at the Application and
  Domain layers respectively (Principle VI > Validation tiers). Silent
  coercion of input is forbidden at every tier.

**Rationale**: gRPC gives strong typing, code generation, bidirectional streaming,
and substantially lower latency than REST for service-to-service calls — which is
the dominant traffic shape for this system. Restricting HTTP to health + Swagger
keeps the operational surface small (one ingress story) while still
giving humans a browsable contract via Swagger UI. Locking documentation to
`Microsoft.AspNetCore.Grpc.Swagger` (rather than hand-maintained OpenAPI) prevents
the doc from drifting away from the wire format — `.proto` is the single source of
truth.

### IV. Observability & Operability

Production code MUST be debuggable from logs and telemetry alone.

**Canonical flow — `dotnet-observability` skill.**
The full observability lifecycle for any service (instrument code → export via OTLP
→ validate Prometheus scrape → generate and publish Grafana dashboard JSON →
iterate) MUST follow the 5-phase workflow defined by the `dotnet-observability`
skill (installed at `~/.claude/skills/dotnet-observability/`). The skill is the
authoritative reference for: per-component instrumentation patterns, naming and
cardinality conventions, dashboard generation, PromQL/TraceQL recipes, and MCP
vs. curl fallback for Prometheus/Grafana. Contributors MUST consult it before
authoring or modifying observability code. Within that flow:

- Wiring of `AddOpenTelemetry()` in `Program.cs` is delegated to the
  `dotnet-aspnet:configuring-opentelemetry-dotnet` skill (the
  `dotnet-observability` skill explicitly defers to it for that step).
- Deep-dives that start from a metric (latency regression, GC pressure, hangs)
  are delegated to the `dotnet-diag:*` skills already mandated below.

**Three-pillar requirement.**

- Structured logging via `ILogger<T>` with message templates and named parameters.
  String interpolation in log messages is forbidden.
- OpenTelemetry MUST be wired for traces, metrics, and logs (`OpenTelemetry.Extensions.Hosting`),
  exporting via **OTLP** with a single `UseOtlpExporter()` call so all three
  signals share one collector endpoint and one `trace_id` correlation. Resource
  attributes MUST include `service.name` and `service.version`. Because Principle
  III makes gRPC the default transport, `OpenTelemetry.Instrumentation.AspNetCore`
  and `OpenTelemetry.Instrumentation.GrpcNetClient` MUST both be registered so
  server- and client-side gRPC spans participate in the same trace. FusionCache's
  OTel instrumentation (Tech Stack > Caching) MUST be added to the same pipeline.

**Instrumentation conventions (per the `dotnet-observability` skill).**

- `Meter` instances MUST be created via `IMeterFactory.Create(...)`; raw
  `new Meter(...)` is forbidden.
- `ActivitySource` instances MUST be `static readonly` and registered with
  `AddSource(...)` — a glob like `AddSource("Orders.*")` is the recommended
  pattern (the #1 cause of "missing spans" is a name mismatch).
- Metric and span names MUST use **dot-namespacing** (`orders.processed`, NOT
  `orders_processed_total`). The Prometheus receiver appends suffixes during
  translation; doing it in code produces double `_total`.
- Tags / log template parameters MUST be **low-cardinality** — bounded enums,
  status codes, exception type names. IDs, UUIDs, emails, and `ex.Message` are
  forbidden as metric tags. High-cardinality data goes on spans, not metrics.
- Latency histograms MUST declare explicit buckets via
  `AddView(name, ExplicitBucketHistogramConfiguration { Boundaries = [...] })`.
- Error paths MUST hit all three pillars: span `SetStatus(Error, …)`, a counter
  increment with a bounded `reason` tag, and a structured `LogError`/`LogWarning`.
  **`activity.RecordException(ex)` is forbidden** — log via `ILogger`; the trace
  ID is attached automatically.
- `/health/live`, `/health/ready`, and `/metrics` MUST be filtered out of tracing
  via `AddAspNetCoreInstrumentation(o => o.Filter = ...)` to keep trace volume sane.

**Operational artifacts.**

- `MapHealthChecks("/health/live")` and `/health/ready` MUST exist on every
  service; readiness probes MUST verify downstream dependencies (DB, queue,
  Redis, etc.).
- Configuration MUST flow through `IOptions<T>` with `ValidateDataAnnotations()`
  and `ValidateOnStart()` so misconfiguration fails fast at startup, not first
  request.
- **Dashboard JSON is source code.** Every service MUST commit its Grafana
  dashboard at `monitoring/<service-name>.json` **at the repository root**
  (not under `src/Api/` or any per-project folder — repo root keeps it
  visible to CI and to humans browsing the repo). The file is generated and
  published via Phase 4 of the `dotnet-observability` skill. Dashboards live
  in git so panel changes are diffable and reviewable; ad-hoc dashboard
  edits in the Grafana UI that are not synced back to the JSON are review
  blockers.
- **MCP-preferred, curl-fallback.** Prometheus and Grafana operations
  (target probing, dashboard publish) SHOULD use the MCP servers documented in
  the skill. When the MCPs are unavailable, the curl fallbacks specified in the
  skill's `REFERENCE_MCP_WORKFLOW.md` MUST be used — never skip the validation
  step because the MCP is down.

**Required diagnostic tooling — `dotnet-diag` plugin skills.** When a service
is slow, leaking, or crashing, contributors MUST reach for the canonical
diagnostic skills before improvising:

- `dotnet-diag:analyzing-dotnet-performance` — first stop for any latency or
  CPU regression that escapes the dashboards.
- `dotnet-diag:dotnet-trace-collect` — collecting EventPipe traces in a running
  process.
- `dotnet-diag:dump-collect` — capturing memory dumps for post-mortem analysis.

These complement (do not replace) the OTel pipeline; OTel answers "is something
wrong?", these skills answer "what exactly is wrong?". The
`dotnet-observability` skill explicitly hands off to them in Phase 5 (Iterate)
when a metric points at a runtime issue.

**Rationale**: A service you cannot observe is a service you cannot operate.
Wiring this in at scaffold time costs minutes; retrofitting under incident
pressure costs hours. The `dotnet-observability` skill turns observability from
"copy snippets from a blog" into a deterministic 5-phase flow with validation
gates at each step — and it commits the dashboard alongside the code, so the
operational view of the service evolves in lockstep with the service itself.

### V. Operational Hardening

Operational hardening is not an optional cross-cutting concern. Authentication
and authorization are **outside the scope of this constitution** (per v3.0.0):
services do not implement an auth model, do not declare `[Authorize]`, do not
ship identity-stub abstractions as a constitutional readiness requirement, and
do not gate endpoints by caller identity at the framework layer. Features that
need an owner / tenant key for ownership scoping or audit-actor logging are
free to introduce one as a feature-level concern (e.g., a header-driven
`ICurrentUser` returning `Result<Guid>` with `ErrorType.Validation` on
malformed input) — that is a feature design choice, not a constitutional
mandate.

The four rules below apply day one and are non-negotiable.

- **HTTPS / TLS MUST be enforced.** gRPC requires HTTP/2; even on-premise
  traffic MUST be TLS-terminated at the service or at a trusted ingress.
  The HTTP sidecar MUST use `UseHttpsRedirection` and HSTS in non-Development
  environments. Self-signed certs are acceptable inside the perimeter as long
  as TLS itself is on; cleartext HTTP/2 (`h2c`) is forbidden.
- **Secrets MUST come from a secret store** — User Secrets in dev,
  environment variables / Key Vault / on-prem secret manager (HashiCorp Vault,
  etc.) in deployed environments. No secrets in `appsettings.*.json` checked
  into git, regardless of deployment target.
- **All inputs MUST be validated server-side.** Principle VI > Validation
  tiers covers the mechanism (Data Annotations + `IValidatableObject` routed
  through `DomainValidator.Validate<T>`). Anti-forgery tokens do **not** apply
  to gRPC; any future cookie-driven state-changing HTTP endpoint justified
  under Principle III MUST carry anti-forgery protection.
- **Dependencies MUST be scanned**
  (`dotnet list package --vulnerable --include-transitive`) on CI; high-severity
  advisories block merge.

**Rationale**: This system targets an on-premise deployment behind a trusted
network boundary, and the constitution does not pretend to govern the auth
model that future deployments may or may not need. The four rules above are
the load-bearing operational guarantees that an on-prem perimeter does not
provide on its own: an internal network does not stop insider mistakes,
supply-chain CVEs, accidentally-committed credentials, or unvalidated input
from corrupting state. Keeping this principle scoped to those four rules
means it stays applicable regardless of the deployment topology.

### VI. Design & Structure

This principle governs **how the codebase is organized and how objects relate**.
Code MUST be 100% object-oriented; functional or procedural styles are explicit
exceptions that require justification in `plan.md`.

**Layer structure (Clean Architecture, n-tier flavored).**

Every service MUST be split into the following projects under `src/`:

```
src/
  Domain/          entities, value objects, aggregates, domain events.
                   ZERO dependencies on other src/ projects or framework code.
  Application/     use cases (commands & queries), ports (interfaces), validation.
                   Depends on Domain only.
  Infrastructure/  Dapper repositories over Npgsql, message bus, outbound
                   HTTP/gRPC clients, secrets, etc. Depends on Application
                   (implements its ports). Never on Api. (DbUp migrations
                   live in a sibling Infrastructure.Migrations console
                   sub-project per Tech Stack > Persistence — physically
                   isolated so no EF/migration DI surface exists in runtime
                   composition.)
  Contracts/       .proto files (under Protos/) plus the generated gRPC service
                   base classes and message types. Redistributable to consumer
                   services as a NuGet package. Depends on nothing inside src/.
  Api/             gRPC host + HTTP sidecar (health + Swagger). Composition root
                   — the ONLY place Infrastructure types are instantiated.
                   Depends on Application + Infrastructure + Contracts.
tests/
  Domain.Tests / Application.Tests / Infrastructure.Tests / Api.IntegrationTests
```

The dependency rule is one-directional and MUST be enforced by project references:
`Api → Application → Domain`, `Infrastructure → Application → Domain`,
`Api → Infrastructure` (composition root only), `Api → Contracts`,
`Infrastructure → Contracts` (when implementing outbound gRPC clients). Reversal
is forbidden — a violation is a build error, not a code-review judgment call.

**CQRS without MediatR.**

Application logic MUST be split by intent into **commands** (state-changing) and
**queries** (side-effect-free reads), expressed via **folders + namespaces**, not
through a runtime mediator:

```
Application/
  Features/
    <FeatureName>/
      Commands/
        <Verb><Noun>Command.cs        — the input record (immutable)
        <Verb><Noun>Handler.cs        — public sealed class with a single
                                         async Handle(...) method
      Queries/
        Get<Noun>Query.cs
        Get<Noun>Handler.cs
```

Handlers are resolved directly through DI (`AddScoped<TVerbNounHandler>()`) and
invoked from the gRPC service in `Api`. There is no in-process bus, no
`IRequest<>`, no pipeline behaviors. Cross-cutting concerns (logging, validation,
auth) live as gRPC `Interceptor`s (Principle III) or as DI-registered decorators
around the handler.

**OO discipline.**

- **Polymorphism over inheritance.** Default to composition with interfaces
  (subtype polymorphism) injected via DI. Concrete classes MUST be `sealed` unless
  inheritance is explicitly justified in `plan.md`. Inheritance is permitted in
  `Domain` for aggregates, value objects, and domain events modeled with abstract
  bases — the rule is "no inheritance for code reuse", not "no inheritance ever".
- **No static state.** Forbidden:
  - `static` fields/properties that are not `const` or `static readonly` of a
    deeply immutable type;
  - `static` classes containing business logic;
  - hand-rolled singletons or service locators (DI manages lifetime).
- **Permitted static usage**:
  - `static` extension methods (idiomatic in C#);
  - `const` and `static readonly` of immutable types (`string`, `int`,
    `ImmutableArray<T>`, etc.);
  - pure stateless helpers (e.g., `Guard.AgainstNull(...)`) — they are functions,
    not collaborators, so DI does not apply;
  - `Program.Main` and source-generated `LoggerMessage` partials.

**Error handling — Result pattern, no exceptions across layer boundaries.**

Exceptions are expensive (see "Minimize exceptions" in
[`aspnet-core-best-practices.md`](./aspnet-core-best-practices.md)) and turn
control flow into something invisible to the type system. Inter-layer error
propagation MUST use a `Result<T>` value, not thrown exceptions:

- `Domain`, `Application`, and `Infrastructure` MUST return `Result<T>` (or the
  non-generic `Result` for void-shaped operations) for any operation that can
  fail. They MUST NOT `throw` to signal a business or expected outcome.
- The contract is hand-rolled and lives in `Domain` (zero deps):

  ```csharp
  public sealed record Error(string Code, string Message, ErrorType Type);

  public enum ErrorType
  {
      Validation, NotFound, Conflict, Forbidden, Failure
  }

  public readonly record struct Result<T>
  {
      public T? Value { get; }
      public Error? Error { get; }
      public bool IsSuccess => Error is null;
      public bool IsFailure => Error is not null;
      public static Result<T> Success(T value) => new(value, null);
      public static Result<T> Failure(Error error) => new(default, error);
      // implicit conversions from T and Error for ergonomic call sites
  }
  ```

  Adopting an external library (`FluentResults`, `ErrorOr`, `OneOf`) requires a
  constitution amendment.
- **Domain construction — Builder pattern (NON-NEGOTIABLE).** Aggregates,
  entities, and value objects MUST be constructed via a fluent Builder, never
  through a public constructor. The canonical shape:

  ```csharp
  public sealed class Money
  {
      [Required, StringLength(3, MinimumLength = 3)]
      public string Currency { get; private init; } = default!;

      [Range(0.01, double.MaxValue)]
      public decimal Amount { get; private init; }

      private Money() { }                          // private — invisible to callers
      public static MoneyBuilder Builder() => new();

      public sealed class MoneyBuilder
      {
          private string? _currency;
          private decimal _amount;

          public MoneyBuilder WithCurrency(string c) { _currency = c; return this; }
          public MoneyBuilder WithAmount(decimal a) { _amount = a; return this; }

          public Result<Money> Build()
          {
              var money = new Money { Currency = _currency!, Amount = _amount };
              return DomainValidator.Validate(money);   // returns Result<T>
          }
      }
  }
  ```

  Rules:
  - Constructors on Domain types MUST be private (or `internal` only when the
    Builder is in a different namespace within the same `Domain` project).
    Public constructors are review blockers — they would be the loophole that
    reintroduces "throw on invalid state".
  - The Builder is a `sealed` nested class on the type it constructs, exposed
    only via the type's `static Builder()` method.
  - `Build()` MUST return `Result<TSelf>` and MUST run validation (see next
    rule) before returning `Success`.
  - `record` is reserved for **DTOs** (Application command/query inputs,
    transport translation records) — Domain types are `sealed class` because a
    record's primary constructor is public and bypasses the Builder. This
    refines the "prefer records" guidance in Principle I.

- **Validation — Data Annotations + `IValidatableObject`, never bare `if`.**
  Validation MUST be expressed declaratively, never as imperative `if`-checks
  that return `Result.Failure`. The rule applies uniformly to **every
  DTO-shaped type in the codebase**:

  - Application **command** and **query** input records (`<Verb><Noun>Command`,
    `Get<Noun>Query`).
  - **Translation / mapping records** between transport and Application
    (e.g., a `CreateOrderRequest` mapping record between the protobuf message
    and the Application command, when one is needed).
  - **Configuration shapes** bound via `IOptions<T>` — Principle IV already
    requires `ValidateDataAnnotations()` + `ValidateOnStart()` for these; the
    same attribute set is used.
  - **Domain** entities, aggregates, and value objects (validated by their
    Builder's `Build()`).

  How it's expressed:

  - **Single-property invariants**: standard attributes from
    `System.ComponentModel.DataAnnotations` (`[Required]`, `[Range]`,
    `[StringLength]`, `[RegularExpression]`, `[EmailAddress]`, etc.) or a
    custom `ValidationAttribute` subclass for domain-specific predicates
    (e.g., `[ValidIsoCurrency]`).
  - **Cross-property invariants** (e.g., "EndDate > StartDate"): the type
    implements `IValidatableObject.Validate(ValidationContext ctx)` and yields
    `ValidationResult` instances. Imperative `if (a > b) return Failure(...)`
    in a Builder, handler, or DTO-bound code is a review blocker.
  - **Execution**: a single `DomainValidator.Validate<T>(T candidate)` helper
    in `Domain` runs `Validator.TryValidateObject(candidate, ..., validateAllProperties: true)`,
    aggregates `ValidationResult` instances into a single
    `Error("Validation", "...", ErrorType.Validation)`, and returns
    `Result<T>`. Builders, Application input validation, and any other
    DTO-shaped validation all call it; no direct calls to `Validator` from
    business code.

  Carve-outs (the only types NOT decorated with Data Annotations directly):

  - **Protobuf-generated messages** in `Contracts/` — they are codegen output
    and cannot be edited. Their validation happens at the **next hop**:
    either in the transport-shape validation interceptor that examines the
    incoming message, or after mapping into a hand-written Application input
    that IS decorated. Either path runs `DomainValidator.Validate<T>` on the
    decorated type.
  - **gRPC interceptor logic** itself, where the runtime may use control-flow
    checks against the incoming protobuf message before mapping it. The
    decorated Application input is re-validated at the handler boundary
    regardless.

- **Validation tiers (where each tier runs).**
  1. **Transport-shape validation** in a gRPC server `Interceptor` or at the
     top of the gRPC service method: required fields present, formats parse-
     able, enums in range. Maps to `Result.Failure(ErrorType.Validation)`.
  2. **Business-rule validation** in the Application handler (e.g., "user
     exists", "amount within configured limits", uniqueness): also
     `Result.Failure(ErrorType.Validation)` (or `Conflict`/`NotFound` as
     appropriate).
  3. **Invariant validation** at Domain construction time, run by the
     Builder's `Build()` via `DomainValidator`. Same `Result.Failure(Validation)`.
  All three tiers route through the same boundary translator (next bullet).

- **API boundary translation — `ResultToRpcExceptionMapper`.** The `Api`
  layer is the **only** place a `Result.Failure` becomes an exception, and
  only because gRPC's wire protocol requires `RpcException`. The translation
  lives in a single named helper at `Api/Grpc/ResultToRpcExceptionMapper.cs`
  (extension methods `ToReply<TReply>(this Result<...>)` /
  `ThrowIfFailure(this Result)`); a server `Interceptor` MAY wrap it for
  uniformity. **This component is distinct from the outermost safety-net
  interceptor described below** — the mapper is the *designed* path for
  expected `Result.Failure`s, the safety net is the *unexpected-exception*
  catcher. Fixed mapping:

  | `ErrorType`   | gRPC `StatusCode`     |
  |---------------|------------------------|
  | `Validation`  | `InvalidArgument`      |
  | `NotFound`    | `NotFound`             |
  | `Conflict`    | `AlreadyExists`        |
  | `Forbidden`   | `PermissionDenied`     |
  | `Failure`     | `Internal`             |

  Service classes MUST NOT construct `RpcException` themselves — they call the
  helper.
- **Exceptions that are still allowed** (narrow, exhaustive list):
  1. `OperationCanceledException` — cancellation propagates as an exception by
     framework convention; do not catch it except in the outermost interceptor
     (see below), which translates it to `StatusCode.Cancelled`.
  2. Genuinely unrecoverable conditions in startup code (`Program.cs`,
     `IOptions` validation) where fail-fast at process boot is the correct
     behavior. `BackgroundService` failures are explicitly in this category —
     the default .NET 6+ behavior of crashing the host on an unhandled
     `BackgroundService` exception is correct and MUST NOT be overridden.
  3. Library-thrown exceptions you cannot prevent (e.g., `DbUpdateException`)
     MUST be caught at the Infrastructure boundary and converted to a
     `Result.Failure(new Error(...))` before crossing back into Application.
  Any other `throw` in `Domain`, `Application`, or `Infrastructure` is a review
  blocker.

- **Outermost safety net — exception interceptor.** The Result discipline is the
  designed path; the interceptor is the safety net for what slips through despite
  it (programmer bug, third-party surprise, deserialization edge). Every service
  host MUST install:
  - A gRPC server `Interceptor` registered **first** in the interceptor chain so
    it wraps every call. It MUST:
    - pass `RpcException` through untouched — those came from the boundary
      translator and are intentional;
    - translate `OperationCanceledException` to
      `RpcException(new Status(StatusCode.Cancelled, ...))`;
    - convert any other exception to
      `RpcException(new Status(StatusCode.Internal, "An unexpected error
      occurred. Correlation: {traceId}"))` and log the original at `Error` level
      with the full stack and the current OTel trace ID (Principle IV);
    - **never** include the exception type, message, or stack in the wire
      response — only the correlation ID. Internal details stay in the logs.
  - An ASP.NET Core exception-handling middleware on the HTTP sidecar
    (`UseExceptionHandler` / `IExceptionHandler`), registered **first** in the
    pipeline, doing the analogous thing for health and Swagger requests and
    returning RFC 7807 ProblemDetails with the same correlation ID.

  Because this safety net always runs, **`try`/`catch` is forbidden everywhere
  except**:
  - the Infrastructure boundary, where library exceptions are caught and
    converted to `Result.Failure` (allowed-exceptions case 3 above);
  - inside the interceptor / middleware itself.

  Any other `try`/`catch` — especially nested ones in handlers — is a review
  blocker. The interceptor's purpose is to make defensive catching unnecessary,
  not to license sloppy `Result` usage; reviewers MUST still flag every `throw`
  in `Domain`/`Application`/`Infrastructure` as if the interceptor did not exist.
- **Tests**: assertions on failure paths use `result.IsFailure.Should().BeTrue()`
  and `result.Error!.Code.Should().Be("…")`. `Assert.Throws<…>` / FluentAssertions
  `.Should().Throw<…>()` is forbidden for code that should now return `Result`
  (Principle II, integration tests at the gRPC boundary still verify the
  translated `RpcException`).

**Design patterns.**

Patterns are tools, not goals. Use a pattern **only when the scenario justifies
it**; never introduce one preemptively. When a pattern IS used, name it explicitly
in `plan.md` (e.g., "Strategy for payment provider selection", "Specification for
query composition", "Decorator for handler-level caching") so reviewers can
evaluate the choice against the simpler alternative it replaced.

Specific pattern mandates:

- **Repository caching → Decorator (NON-NEGOTIABLE).** Caching MUST NOT be
  embedded inside repository implementations. The repository (e.g.,
  `DapperUserRepository : IUserRepository`) stays focused on persistence;
  caching is applied as a wrapping decorator
  (`CachingUserRepository : IUserRepository`) that holds the inner repository
  plus an `IFusionCache` (see "Caching" in the Technology Stack section for
  the mandated library and topology). This keeps the persistence class
  trivially testable and makes "is caching on?" a single registration line,
  not a code search.
- **All decorator wiring MUST use Scrutor**
  ([`Khellang.Scrutor`](https://github.com/khellang/Scrutor)). The canonical
  call is:

  ```csharp
  services.AddScoped<IUserRepository, DapperUserRepository>();
  services.Decorate<IUserRepository, CachingUserRepository>();
  ```

  Hand-rolled decoration (manually unwrapping `ServiceDescriptor` to re-register
  the inner) is forbidden — it is the exact problem Scrutor exists to solve.
  Before introducing **any** decorator (caching, logging, retry, timing,
  etc.), the contributor MUST first confirm that the wiring goes
  through `services.Decorate<>()`.
- **Result-pattern interaction for caching decorators**: a caching decorator
  MUST cache only `Result.Success` values; `Result.Failure` MUST NOT be cached
  (a `NotFound` may be remediated by a subsequent write, and caching a
  failure would make the system look broken). Cache invalidation on the
  corresponding write path is the responsibility of the command handler and
  MUST be called out in `plan.md` whenever caching is introduced.

**Rationale**: Clean Architecture's dependency rule keeps domain logic testable
and free of framework concerns; making the layers explicit projects converts
violations from review-time arguments into compile errors. Hand-rolled CQRS via
namespaces (instead of MediatR) keeps the call graph navigable in IDE
"Find References" — a documented MediatR pain point — without giving up the
read/write separation. Banning static state eliminates the most common source of
hidden coupling and test fragility in C# codebases. Favoring polymorphism over
inheritance prevents the slow accretion of fragile base-class hierarchies. The
Builder pattern makes invalid Domain construction syntactically impossible
(there is no public constructor to call), and pairing it with Data Annotations
+ `IValidatableObject` replaces a mosaic of imperative `if`-checks with a
declarative, uniformly-reportable validation layer that any reviewer can scan
at a glance. The Result pattern makes failure a first-class part of the type
system (the compiler won't let a caller forget to handle it), avoids the
runtime cost of exceptions on hot paths, and keeps the gRPC boundary the
single, predictable place where the translation to wire-level errors happens.
Naming design patterns explicitly when used (and only when used) keeps the
codebase honest about complexity.

## Technology Stack & Platform Constraints

- **Runtime/SDK**: .NET 10 LTS, ASP.NET Core 10. Projects MUST target `net10.0`.
  Multi-targeting is allowed only for shared libraries with a documented consumer.
- **Project layout**: production code lives in `src/<Project>/`, tests in
  `tests/<Project>.Tests/`. The per-service project breakdown (`Domain`,
  `Application`, `Infrastructure`, `Contracts`, `Api`) and the dependency rule
  between them are defined authoritatively in **Principle VI** — this bullet must
  not restate them. `Directory.Build.props` and `Directory.Packages.props` (Central
  Package Management) live at the repo root. The
  `dotnet-msbuild:directory-build-organization` skill is the canonical reference
  for organizing those files; the `dotnet-nuget:convert-to-cpm` skill MUST be used
  to migrate any project that still pins package versions in its `csproj`.
- **Service framework**: `Grpc.AspNetCore` for all business endpoints (Principle III).
  Minimal APIs are permitted **only** for the HTTP sidecar — the two health endpoints
  and the Swagger UI mount. Controllers are not permitted in new code. Any deviation
  requires a `Complexity Tracking` entry in `plan.md`.
- **gRPC tooling**: Code generation MUST use `Grpc.Tools` driven from
  `<Protobuf Include="..." />` items in csproj. Hand-written stubs are forbidden.
  `Microsoft.AspNetCore.Grpc.JsonTranscoding` and `Microsoft.AspNetCore.Grpc.Swagger`
  MUST be wired in every service host so the Swagger contract is always live.
- **DI extensions**: [`Scrutor`](https://github.com/khellang/Scrutor) is the
  canonical extension to `Microsoft.Extensions.DependencyInjection` for two
  things: assembly scanning (`Scan(...)`) when a feature legitimately benefits
  from convention-based registration, and decoration (`Decorate<TService,
  TDecorator>()`) which is **mandatory** for every decorator (Principle VI).
- **Observability backends**: OpenTelemetry → OTLP collector → Prometheus
  (metrics) + Tempo or Jaeger (traces) + Loki (logs). Grafana is the visualization
  layer; dashboards are committed to `monitoring/<service-name>.json` **at the
  repository root** (Principle IV). Local dev uses the **.NET Aspire dashboard**
  (provided by the mandatory AppHost — see "Local development orchestration"
  bullet below) as the local OTLP collector; production deployments MUST use
  the OTLP → Prometheus/Tempo/Loki path. CI smoke jobs without the .NET SDK
  MAY fall back to a standalone OTLP collector container. The
  `dotnet-observability` skill (Principle IV) is the canonical reference for
  the full toolchain wiring; this bullet only names the components.
- **Local development orchestration — .NET Aspire AppHost (NON-NEGOTIABLE).**
  Every service MUST ship a `*.AppHost` project (referencing
  `Aspire.Hosting.AppHost`) at `src/AppHost/` whose sole responsibility is to
  orchestrate the service plus its infrastructure dependencies (Postgres,
  Redis, message broker, etc.) for local developer bring-up. Rules:
  - **Canonical bring-up**: `dotnet run --project src/AppHost`. This single
    command MUST start the service, all of its declared dependencies, AND the
    .NET Aspire dashboard (which doubles as the local OTLP collector for
    Principle IV signals — see the "Observability backends" bullet above).
    Multi-step `docker compose up` + `dotnet run` recipes are forbidden as
    the primary path.
  - **Resource registration MUST go through Aspire hosting integrations** —
    `builder.AddPostgres(...).AddDatabase(...)`, `builder.AddRedis(...)`,
    `builder.AddProject<Projects.Api>(...)`, etc. Ad-hoc `Process.Start` or
    shell-out (`Exec`) from the AppHost is forbidden — those would bypass
    Aspire's lifecycle, dashboard wiring, and connection-string injection.
  - **Carve-out — tests**. Integration and infrastructure tests MUST continue
    to use **Testcontainers** (Principle II). Aspire is **not** a test host:
    `Aspire.Hosting.Testing` / `DistributedApplicationTestingBuilder` MUST
    NOT be used on the canonical test path because it duplicates the
    per-test-class isolation that Testcontainers already provides and would
    re-litigate the established `WebApplicationFactory<Program>` +
    `GrpcChannel` integration pattern. The AppHost is for `F5` / dev-loop
    only.
  - **Carve-out — production**. Production deployments MUST NOT use the
    AppHost. Production targets plain containers / k8s manifests; the
    AppHost is a developer-experience artifact, not a deployment artifact.
    The AppHost project MUST NOT be referenced from `Api` or any other
    runtime project — only from the solution file.
  - **`docker-compose.dev.yml` fallback**: a compose file MAY coexist as a
    fallback for environments without the .NET SDK (CI smoke jobs,
    air-gapped boxes, language-agnostic onboarding). When both exist, they
    MUST stay in sync (same image versions, same ports, same env vars) and
    the AppHost is canonical — divergence is a review blocker. If only one
    of the two exists, it MUST be the AppHost.
  - **Quickstart obligation**: every feature's `quickstart.md` MUST document
    `dotnet run --project src/AppHost` as the primary bring-up step; any
    compose-based recipe MUST be labeled "fallback".
  - **`Properties/launchSettings.json` is MANDATORY** (per 2.10.1). The AppHost
    project MUST ship `src/AppHost/Properties/launchSettings.json` defining
    at least one profile (canonical: `https`) that sets:
    - `applicationUrl` — the AppHost listen URL (HTTPS by default; HTTP is
      only permitted when `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` is also
      set in the same profile, e.g. for SDK-less CI smoke jobs).
    - `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL` — the local OTLP endpoint that
      doubles as the Principle IV collector (see "Observability backends").
    - `ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL` — the dashboard's resource
      service endpoint.
    Rationale: Aspire 9.x validates these at host build time and throws
    `OptionsValidationException` *before* any resource is provisioned if
    they are missing. Omitting this file silently breaks the canonical
    bring-up promise above. Tasks templates MUST include a setup task
    that creates this file alongside `<Service>.AppHost.csproj`. The
    `dotnet new aspire-apphost` template is the reference layout.
  - **Schema migrations run as an Aspire init-container — MANDATORY**
    (per 2.11.0). The `Infrastructure.Migrations` console (see Tech Stack >
    Persistence > Schema management) MUST be registered in the AppHost as
    a project resource that the service waits on:
    ```csharp
    var migrator = builder
        .AddProject<Projects.<Service>_Infrastructure_Migrations>("migrator")
        .WithReference(postgres)
        .WaitFor(postgres);

    builder.AddProject<Projects.<Service>_Api>("api")
        // ... env + references ...
        .WaitFor(migrator);    // NOT WaitForCompletion — see rationale.
    ```
    Rationale for `WaitFor` (not `WaitForCompletion`): in Aspire 9.5,
    `dotnet run` of a short-lived `Microsoft.NET.Sdk` console exits before
    DCP's resource state reconciler captures a "Finished" transition, so
    `WaitForCompletion` blocks dependents indefinitely. `WaitFor` waits on
    "Running", which the Generic Host reaches just before
    `BackgroundService.ExecuteAsync` fires — giving the migrator a small
    head start before the Api's first DB request. The race is acceptable
    only because: (a) the AppHost is a dev-only artifact (Production runs
    the migrator console as a separate step in the deploy pipeline);
    (b) the Api's first DB query happens at request time, not startup;
    (c) DbUp's `schemaversions` row write is part of the migration
    transaction, so partial-apply is impossible. Manual schema-application
    steps in `quickstart.md` (e.g. `psql -f`, hand-rolled runners) are
    FORBIDDEN — the canonical bring-up MUST land schema in one command.
  - **Forward `ASPNETCORE_ENVIRONMENT` to the service project — MANDATORY**
    (per 2.10.2). Aspire 9.x does NOT auto-propagate `ASPNETCORE_ENVIRONMENT`
    from the AppHost process to child project resources; child projects
    inherit only `DOTNET_RESOURCE_SERVICE_ENDPOINT_URL` and a handful of
    OTEL/DCP variables. The AppHost's `Program.cs` MUST therefore chain
    `.WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)`
    on the `AddProject<TService>(...)` call for every service it
    orchestrates. Without this, the service boots in `Production` even
    though the AppHost is in `Development`, and Development-gated
    middleware (Swagger, dev exception pages) is silently absent —
    making the canonical bring-up appear "up" while violating the
    Quickstart obligation above. Use `builder.Environment.EnvironmentName`
    (not the hard-coded literal `"Development"`) so the rule survives
    staging / Test profiles.
- **Caching**: [`ZiggyCreatures.FusionCache`](https://github.com/ZiggyCreatures/FusionCache)
  is the only sanctioned cache abstraction. Direct use of `IMemoryCache` /
  `IDistributedCache` / Microsoft `HybridCache` from business code is forbidden
  — handlers and decorators take an `IFusionCache` dependency. The `fusion-cache`
  skill is the canonical reference for configuration, code patterns, and
  troubleshooting; consult it before authoring or modifying caching code (and
  before answering caching design questions).
  - **Mandatory topology — L1 + L2 + Backplane**, even for single-node services.
    Rationale (per the skill): cold-start recovery from L2, dev/prod parity,
    zero-redesign horizontal scale-out, negligible operational cost. Defaulting
    to "just L1" is forbidden. Carve-outs (tests, throwaway scripts, air-gapped
    environments) MUST be declared explicitly in `plan.md` with the tradeoff
    spelled out.
  - **Mandatory wiring**: builder-based registration following
    `references/templates/program-setup.cs` from the `fusion-cache` skill —
    L1 = `IMemoryCache`, L2 = Redis via `IDistributedCache`, serializer =
    `FusionCacheSystemTextJsonSerializer` (`ZiggyCreatures.FusionCache.Serialization.SystemTextJson`),
    backplane via the matching backplane package. Newtonsoft is permitted only
    when DTOs carry Newtonsoft-specific attributes that cannot be changed.
  - **Mandatory `DefaultEntryOptions`**: `IsFailSafeEnabled = true` plus a
    `FactorySoftTimeout` chosen above the factory's measured P50 (the
    `dotnet-diag:microbenchmarking` skill or production telemetry — never a
    guess). Fail-safe MUST be set at write time, not read time.
  - **Forbidden anti-patterns** (review blockers): `MemoryDistributedCache` as
    a stand-in for L2 (it is in-process, not distributed — tests only); a
    backplane registered without a shared L2 (causes N independent factory
    re-runs instead of 1); embedding caching logic in repositories (already
    forbidden by Principle VI — restated here for proximity to the topology
    rules).
  - **Observability hook**: FusionCache's OpenTelemetry instrumentation
    (traces + metrics) MUST be registered alongside the OTel pipeline from
    Principle IV. Cache hit/miss/factory-timeout are first-class signals,
    not afterthoughts.
  - **Result-pattern interaction**: the caching decorator caches `Result.Success`
    only — `Result.Failure` MUST NOT be cached (Principle VI). Cache
    invalidation on the corresponding write path (via `Remove`, `Expire`, or
    `RemoveByTag`) is the responsibility of the command handler and MUST be
    declared in `plan.md` whenever a caching decorator is introduced.
- **Persistence — split-responsibility model**:
  - **Database**: PostgreSQL (latest stable major). The Npgsql provider is the
    only sanctioned client.
  - **Schema management → DbUp (MIGRATIONS ONLY).** A dedicated
    `Infrastructure.Migrations` sub-project owns schema evolution. Its
    shape is a Generic Host console (`Microsoft.NET.Sdk` +
    `<OutputType>Exe</OutputType>`) hosting a `BackgroundService` that, in
    `ExecuteAsync`, runs DbUp against the Aspire-injected
    `ConnectionStrings:<service>` and then calls
    `IHostApplicationLifetime.StopApplication`. SQL scripts ship as
    embedded resources under `Scripts/NNNN_*.sql` and are tracked in
    DbUp's `schemaversions` table — DbUp guarantees idempotency at the
    script-name level. Rules:
    - **DbUp is the only sanctioned migration tool.** No alternative
      schema-management framework (ORM-driven migrations, hand-rolled
      `psql -f` runners, etc.) is permitted. Runtime data access stays on
      Dapper per the bullet below.
    - **Scripts are append-only.** Once `0001_Initial.sql` is committed
      and applied to any environment, it MUST NOT be edited —
      corrections ship as `0002_*.sql` and forward. Numbering is
      monotonic; gaps are review blockers.
    - **No `IF NOT EXISTS` defensive guards.** Scripts MUST be authored
      against an empty database. The init-container migrator (see Local
      development orchestration) guarantees they run exactly once and
      DbUp's `schemaversions` row write is part of the same transaction
      as the DDL.
    - **Migrations MUST be applied via the AppHost migrator at dev time**
      (per Local development orchestration) and via the same console
      executable invoked at deploy time in production
      (`dotnet <Service>.Infrastructure.Migrations.dll`).
  - **Runtime CRUD → Dapper (vanilla).** Repository implementations use
    [`Dapper`](https://github.com/DapperLib/Dapper) over a pooled
    `NpgsqlDataSource` registered as a singleton via
    `services.AddNpgsqlDataSource(...)` from `Npgsql.DependencyInjection`.
    Rules:
    - Every query MUST be parameterized — string interpolation/concat into SQL
      is forbidden (SQL injection vector and a review blocker).
    - Reads use `QueryAsync<T>` / `QuerySingleOrDefaultAsync<T>` /
      `QueryMultipleAsync` (the latter for sibling queries on a single
      round-trip); writes use `ExecuteAsync` or `QuerySingleAsync<T>` with
      `RETURNING` for generated values.
    - Functions / stored procedures are called with
      `commandType: CommandType.StoredProcedure`.
    - Multi-statement commands MUST run inside a transaction
      (`conn.BeginTransactionAsync(ct)` + `tx.CommitAsync(ct)`).
    - Non-trivial SQL MAY live as embedded `.sql` resources under
      `Infrastructure/Sql/<Aggregate>/`; inline strings are acceptable for
      short, single-statement queries.
    - **Dapper extensions** (`Dapper.Contrib`, `Dapper.SimpleCRUD`,
      `Dapper.FastCrud`, etc.) are NOT permitted — vanilla Dapper + hand-
      written SQL only. They reintroduce the conventions and reflection
      overhead the EF-vs-Dapper split was meant to avoid.
    - The data-access guidance in `aspnet-core-best-practices.md` ("retrieve
      only what you need", "minimize round trips", "filter at DB", "no N+1")
      still applies — the rules are ORM-agnostic and now manifest as
      hand-written SQL discipline (selective `SELECT`, proper `JOIN`s,
      server-side `WHERE`/`ORDER BY`/`LIMIT`).
  - **Repository contract unchanged**: `IXxxRepository` still returns
    `Result<T>` (Principle VI); the implementation is now
    `DapperXxxRepository : IXxxRepository`. The caching decorator pattern
    (Tech Stack > Caching: FusionCache + Scrutor) wraps it identically — only
    the inner persistence mechanism changed.
- **Background work**: `IHostedService` / `BackgroundService` for in-process; for
  durable work, use a dedicated queue (Azure Service Bus, RabbitMQ) — do not rely on
  in-memory channels for cross-process work.
- **Build & CI**: `dotnet format` and `dotnet test` MUST run on every PR. Publishing
  uses `dotnet publish -c Release` with trimmed/AOT settings only when justified.
  When reviewing or modifying MSBuild logic (targets, props, custom tasks), the
  `dotnet-msbuild:msbuild-antipatterns` skill is the canonical anti-pattern checklist
  — analogous to `dotnet-test:test-anti-patterns` for tests.

## Development Workflow & Quality Gates

- The SDD workflow (`/speckit-specify` → `/speckit-plan` → `/speckit-tasks` →
  `/speckit-implement`) is the canonical path for new features. Bypassing it requires
  explicit reviewer sign-off in the PR.
- The Constitution Check gate in `plan.md` MUST evaluate every principle above. Any
  violation MUST appear in the plan's `Complexity Tracking` table with justification
  and a rejected simpler alternative.
- Pull requests MUST: pass CI (build, test, format, vulnerability scan), include or
  update tests for behavioral changes, and reference the relevant `specs/<feature>/`
  artifacts.
- Code review focuses on: principle compliance, contract impact, test coverage of new
  branches, and observability of new failure modes — not style (analyzers handle that).
- Performance budgets, when stated in `plan.md`'s Technical Context, are gates: a PR
  that regresses a stated p95 latency or throughput MUST justify the regression. Any
  perf claim (baseline or regression) in a PR description MUST be backed by a
  BenchmarkDotNet run produced via `dotnet-diag:microbenchmarking` — informal
  stopwatch numbers are not acceptable evidence.

## Governance

- This constitution supersedes ad-hoc team conventions. When a convention conflicts
  with the constitution, the constitution wins until amended.
- Amendments require: a PR modifying `.specify/memory/constitution.md`, a Sync Impact
  Report at the top of the file (see comment block), and approval from the project
  owner. Amendments take effect on merge.
- Versioning of this document follows semantic versioning:
  - **MAJOR**: A principle is removed, redefined incompatibly, or governance is
    materially restructured.
  - **MINOR**: A new principle or section is added, or existing guidance is materially
    expanded.
  - **PATCH**: Clarifications, typo fixes, non-semantic refinements.
- Compliance review: Every `/speckit-plan` invocation re-evaluates Constitution Check.
  Quarterly, the project owner SHOULD audit recent merges against principles II
  (test-first), V (operational hardening), and VI (Result pattern + no `try`/`catch` + no
  static state + Builder + Data Annotations) — the three most prone to silent
  drift. For V specifically the audit MUST verify that the four always-on rules
  (TLS, secrets, server-side validation, vulnerable-dependency scanning) are
  intact in every recently merged service.
- **Skill naming convention**: references of the form `namespace:skill-name`
  (e.g., `dotnet-test:run-tests`, `dotnet-aspnet:configuring-opentelemetry-dotnet`)
  are **plugin skills** enabled per-project in `.claude/settings.json` from
  the `dotnet-agent-skills` marketplace. Bare names (e.g., `fusion-cache`,
  `dotnet-observability`) are **user-level skills** installed under
  `~/.claude/skills/`. Both are equally authoritative; the syntax difference
  only reflects the install location.
- Runtime development guidance for AI assistants and contributors lives in `CLAUDE.md`
  (project root) and in the active feature's `plan.md` (linked from `CLAUDE.md`'s
  SPECKIT block).

**Version**: 3.0.0 | **Ratified**: 2026-04-26 | **Last Amended**: 2026-04-28

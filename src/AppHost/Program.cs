// Aspire AppHost — canonical local-dev bring-up per Constitution v2.10.0
// (Tech Stack > Local development orchestration).
//
// Run: `dotnet run --project src/AppHost`
//
// This single command starts:
//   • Postgres (via Aspire.Hosting.PostgreSQL)
//   • Redis    (via Aspire.Hosting.Redis)
//   • Api      (with ConnectionStrings:ledger and ConnectionStrings:redis injected)
//   • The .NET Aspire dashboard, which doubles as the local OTLP collector.
//
// Tests do NOT use this host (Testcontainers per Principle II).
// Production does NOT use this host (plain containers / k8s).

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("ledger");

var redis = builder.AddRedis("redis");

// DbUp migrations runner — applies pending Scripts/*.sql against the
// Aspire-provisioned Postgres on every bring-up, then exits. The Api waits
// for it so the schema is guaranteed before the first request lands
// (Constitution v2.11.0 — no manual `dotnet ef database update` step).
var migrator = builder.AddProject<Projects.SddDemo_Ledger_Infrastructure_Migrations>("migrator")
    .WithReference(postgres)
    .WaitFor(postgres);

builder.AddProject<Projects.SddDemo_Ledger_Api>("api")
    // Aspire 9.x does not auto-propagate ASPNETCORE_ENVIRONMENT to child
    // project resources; without this the Api boots as Production and the
    // dev-only Swagger middleware (Program.cs > UseSwagger gate) is skipped.
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(postgres)
    .WithReference(redis)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(migrator);

builder.Build().Run();

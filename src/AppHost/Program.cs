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

builder.AddProject<Projects.SddDemo_Ledger_Api>("api")
    .WithReference(postgres)
    .WithReference(redis)
    .WaitFor(postgres)
    .WaitFor(redis);

builder.Build().Run();

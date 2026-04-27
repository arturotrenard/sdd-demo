// Composition root — Phase 2 task T035 per plan.md / research.md / Constitution Principle VI.
//
// Surface:
//   • gRPC business endpoints (LedgersService — populated in Phase 3+)
//   • HTTP sidecar restricted to /health/{live,ready} + /swagger* (Constitution Principle III)
//
// Local dev:
//   • Canonical bring-up is `dotnet run --project src/AppHost` — Aspire injects
//     ConnectionStrings:ledger / ConnectionStrings:redis and the OTLP endpoint.
//   • The compose fallback (`docker compose -f docker-compose.dev.yml up`) sets the
//     same configuration keys via standard ConnectionStrings configuration.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using SddDemo.Ledger.Api.Caching;
using SddDemo.Ledger.Api.Configuration;
using SddDemo.Ledger.Api.Grpc.Interceptors;
using SddDemo.Ledger.Api.Health;
using SddDemo.Ledger.Api.Observability;
using SddDemo.Ledger.Application.Abstractions.Identity;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Infrastructure.Background;
using SddDemo.Ledger.Infrastructure.Currency;
using SddDemo.Ledger.Infrastructure.Identity;
using SddDemo.Ledger.Infrastructure.Persistence;
using SddDemo.Ledger.Domain.Currency;

var builder = WebApplication.CreateBuilder(args);

// --- Options ---------------------------------------------------------------------------------

builder.Services
    .AddOptions<LedgerOptions>()
    .BindConfiguration(LedgerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<AnonymousCurrentUserOptions>()
    .BindConfiguration("Identity")
    .ValidateDataAnnotations();

// --- Identity --------------------------------------------------------------------------------

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, AnonymousCurrentUser>();

// --- Currency catalog ------------------------------------------------------------------------

builder.Services.AddSingleton<ICurrencyCatalog, CurrencyCatalog>();

// --- Persistence -----------------------------------------------------------------------------

var pgConnection = builder.Configuration.GetConnectionString(DataSourceFactory.ConnectionStringName);
if (!string.IsNullOrWhiteSpace(pgConnection))
{
    builder.Services.AddLedgerDataSource(pgConnection);
}

// Repositories land in Phase 3+ (Scrutor-decorated with CachingLedgerRepository).
// Phase 2 leaves the registration slot reserved.
//   builder.Services.AddScoped<ILedgerRepository, LedgerRepository>();
//   builder.Services.Decorate<ILedgerRepository, CachingLedgerRepository>();
builder.Services.AddScoped<IAuditRepository, NotImplementedAuditRepository>();

// --- Caching (FusionCache L1 + Redis L2 + Backplane) -----------------------------------------

builder.Services.AddLedgerFusionCache(builder.Configuration);

// --- Observability ---------------------------------------------------------------------------

builder.Services.AddLedgerOpenTelemetry();

// --- Health checks ---------------------------------------------------------------------------

builder.Services.AddLedgerHealthChecks(builder.Configuration);

// --- Background work -------------------------------------------------------------------------

builder.Services.AddHostedService<AuditRetentionPurgeService>();

// --- gRPC pipeline ---------------------------------------------------------------------------

builder.Services.AddGrpc(opts =>
{
    opts.Interceptors.Add<ExceptionSafetyNetInterceptor>();
    opts.Interceptors.Add<ValidationInterceptor>();
});
builder.Services.AddSingleton<ExceptionSafetyNetInterceptor>();
builder.Services.AddSingleton<ValidationInterceptor>();
builder.Services.AddGrpcSwagger();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- Build pipeline --------------------------------------------------------------------------

var app = builder.Build();

// Constitution Principle V > Always-on — HTTPS hardening outside Development.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapLedgerHealthChecks();

// gRPC service mapping lands in Phase 3+ (LedgersService).
//   app.MapGrpcService<LedgersService>();

await app.RunAsync().ConfigureAwait(false);

// Required for WebApplicationFactory<Program> in tests/Api.IntegrationTests.
public partial class Program;

// Phase 2 stub — replaced in Phase 3+ when AuditRepository (Dapper) lands.
file sealed class NotImplementedAuditRepository : IAuditRepository
{
    public Task<SddDemo.Ledger.Domain.Common.Result> WriteAsync(
        AuditEntryToWrite entry,
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        CancellationToken cancellationToken) =>
        Task.FromResult(SddDemo.Ledger.Domain.Common.Result.Success());

    public Task<SddDemo.Ledger.Domain.Common.Result<int>> PurgeOlderThanAsync(
        TimeSpan retention,
        CancellationToken cancellationToken) =>
        Task.FromResult(SddDemo.Ledger.Domain.Common.Result<int>.Success(0));
}

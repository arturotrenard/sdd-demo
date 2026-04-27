using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SddDemo.Ledger.Infrastructure.Migrations;

/// <summary>
/// research.md §3 + data-model.md §2 — schema-management DbContext. Sole purpose is
/// to drive `dotnet ef migrations add` / `dotnet ef database update` for the
/// ledger / ledger_audit tables. NOT registered for runtime DI per Constitution
/// Tech Stack > Persistence — runtime queries use vanilla Dapper.
/// </summary>
public sealed class LedgerMigrationsDbContext(DbContextOptions<LedgerMigrationsDbContext> options) : DbContext(options)
{
    public DbSet<LedgerRow> Ledgers => Set<LedgerRow>();

    public DbSet<LedgerAuditRow> LedgerAudit => Set<LedgerAuditRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<LedgerRow>(entity =>
        {
            entity.ToTable("ledger");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(x => x.OwnerId).HasColumnName("owner_id").HasColumnType("uuid").IsRequired();
            entity.Property(x => x.Name).HasColumnName("name").HasColumnType("varchar(100)").IsRequired();
            entity.Property(x => x.Description).HasColumnName("description").HasColumnType("varchar(500)");
            entity.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasColumnType("char(3)").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasColumnType("smallint").IsRequired().HasDefaultValue((short)1);
            entity.Property(x => x.Version).HasColumnName("version").HasColumnType("bigint").IsRequired().HasDefaultValue(1L);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
            entity.Property(x => x.LastModifiedAt).HasColumnName("last_modified_at").HasColumnType("timestamptz").IsRequired();

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_ledger_status", "status IN (1, 2)");
                t.HasCheckConstraint("ck_ledger_version_positive", "version >= 1");
                t.HasCheckConstraint("ck_ledger_currency_alpha3", "currency_code ~ '^[A-Z]{3}$'");
            });

            // FR-003 — case-insensitive uniqueness within an owner is enforced by the
            // raw SQL index `ux_ledger_owner_name_lower (owner_id, lower(name))` added
            // by hand to the generated migration (EF cannot model functional indexes
            // declaratively without a Npgsql-specific extension).
            entity.HasIndex(x => new { x.OwnerId, x.Status, x.LastModifiedAt, x.Id })
                .HasDatabaseName("ix_ledger_owner_status_lastmodified")
                .IsDescending(false, false, true, true);
        });

        modelBuilder.Entity<LedgerAuditRow>(entity =>
        {
            entity.ToTable("ledger_audit");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd().UseSerialColumn();
            entity.Property(x => x.ActorId).HasColumnName("actor_id").HasColumnType("uuid").IsRequired();
            entity.Property(x => x.LedgerId).HasColumnName("ledger_id").HasColumnType("uuid").IsRequired();
            entity.Property(x => x.EventType).HasColumnName("event_type").HasColumnType("smallint").IsRequired();
            entity.Property(x => x.EventAt).HasColumnName("event_at").HasColumnType("timestamptz").IsRequired().HasDefaultValueSql("now()");
            entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();

            entity.ToTable(t => t.HasCheckConstraint("ck_audit_event_type", "event_type IN (1, 2, 3)"));

            entity.HasIndex(x => x.EventAt).HasDatabaseName("ix_ledger_audit_event_at");
            entity.HasIndex(x => new { x.LedgerId, x.EventAt })
                .HasDatabaseName("ix_ledger_audit_ledger_event_at")
                .IsDescending(false, true);
        });
    }
}

/// <summary>EF row model for the ledger table — used only by the migrations project.</summary>
public sealed class LedgerRow
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public short Status { get; set; } = 1;
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }
}

/// <summary>EF row model for the ledger_audit table — used only by the migrations project.</summary>
public sealed class LedgerAuditRow
{
    public long Id { get; set; }
    public Guid ActorId { get; set; }
    public Guid LedgerId { get; set; }
    public short EventType { get; set; }
    public DateTimeOffset EventAt { get; set; }
    public string Payload { get; set; } = "{}";
}

/// <summary>
/// Design-time factory so `dotnet ef migrations add` works without a running app.
/// Connection string sourced from env var <c>LEDGER_MIGRATIONS_CONNECTION</c> at
/// design time, falling back to localhost defaults.
/// </summary>
public sealed class LedgerMigrationsDbContextFactory : IDesignTimeDbContextFactory<LedgerMigrationsDbContext>
{
    public LedgerMigrationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("LEDGER_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Username=ledger;Password=ledger;Database=ledger";

        var options = new DbContextOptionsBuilder<LedgerMigrationsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new LedgerMigrationsDbContext(options);
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SddDemo.Ledger.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(100)", nullable: false),
                    description = table.Column<string>(type: "varchar(500)", nullable: true),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger", x => x.id);
                    table.CheckConstraint("ck_ledger_currency_alpha3", "currency_code ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_ledger_status", "status IN (1, 2)");
                    table.CheckConstraint("ck_ledger_version_positive", "version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "ledger_audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<short>(type: "smallint", nullable: false),
                    event_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_audit", x => x.id);
                    table.CheckConstraint("ck_audit_event_type", "event_type IN (1, 2, 3)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_owner_status_lastmodified",
                table: "ledger",
                columns: new[] { "owner_id", "status", "last_modified_at", "id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_audit_event_at",
                table: "ledger_audit",
                column: "event_at");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_audit_ledger_event_at",
                table: "ledger_audit",
                columns: new[] { "ledger_id", "event_at" },
                descending: new[] { false, true });

            // FR-003 — case-insensitive uniqueness within an owner. Functional index on
            // (owner_id, lower(name)) cannot be modeled declaratively in EF Core; added
            // here as raw SQL per data-model.md §2.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_ledger_owner_name_lower ON ledger (owner_id, lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_ledger_owner_name_lower;");

            migrationBuilder.DropTable(
                name: "ledger");

            migrationBuilder.DropTable(
                name: "ledger_audit");
        }
    }
}

using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetShield.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_AddDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discovery_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<IPAddress>(type: "inet", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    times_seen = table.Column<int>(type: "integer", nullable: false),
                    last_rtt_milliseconds = table.Column<double>(type: "double precision", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    first_seen_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_seen_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    promoted_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovery_candidates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discovery_ignores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovery_ignores", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discovery_run_hosts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<IPAddress>(type: "inet", nullable: false),
                    rtt_milliseconds = table.Column<double>(type: "double precision", nullable: true),
                    outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovery_run_hosts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discovery_run_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collector_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    first_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    address_count = table.Column<int>(type: "integer", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovery_run_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discovery_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seed_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seed_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ranges = table.Column<string[]>(type: "text[]", nullable: false),
                    exclusions = table.Column<string[]>(type: "text[]", nullable: false),
                    address_count = table.Column<long>(type: "bigint", nullable: false),
                    job_count = table.Column<int>(type: "integer", nullable: false),
                    jobs_completed = table.Column<int>(type: "integer", nullable: false),
                    jobs_failed = table.Column<int>(type: "integer", nullable: false),
                    responded_count = table.Column<int>(type: "integer", nullable: false),
                    new_candidate_count = table.Column<int>(type: "integer", nullable: false),
                    known_candidate_count = table.Column<int>(type: "integer", nullable: false),
                    existing_device_count = table.Column<int>(type: "integer", nullable: false),
                    ignored_count = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovery_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discovery_seeds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ranges = table.Column<string[]>(type: "text[]", nullable: false),
                    exclusions = table.Column<string[]>(type: "text[]", nullable: false),
                    interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovery_seeds", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discovery_candidates_status_last_seen_at_id",
                table: "discovery_candidates",
                columns: new[] { "status", "last_seen_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_discovery_ignores_cidr",
                table: "discovery_ignores",
                column: "cidr",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discovery_run_hosts_run_id_id",
                table: "discovery_run_hosts",
                columns: new[] { "run_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_discovery_run_jobs_collector_job_id",
                table: "discovery_run_jobs",
                column: "collector_job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discovery_run_jobs_run_id_applied_at",
                table: "discovery_run_jobs",
                columns: new[] { "run_id", "applied_at" });

            migrationBuilder.CreateIndex(
                name: "ix_discovery_runs_seed_id_status",
                table: "discovery_runs",
                columns: new[] { "seed_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_discovery_runs_started_at_id",
                table: "discovery_runs",
                columns: new[] { "started_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_discovery_seeds_deleted_at",
                table: "discovery_seeds",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "ix_discovery_seeds_enabled_next_run_at",
                table: "discovery_seeds",
                columns: new[] { "enabled", "next_run_at" });

            // Written by hand for the reason ix_devices_primary_ip_address_live is: EF will not
            // index a property whose CLR type is not comparable, and IPAddress is not. The
            // guarantee is what makes a re-run update a candidate rather than add a second one.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ix_discovery_candidates_address
                    ON discovery_candidates (address);
                """);

            // Unique among live seeds only, so that a removed seed releases its name for the one
            // replacing it while its runs still resolve. A filtered index is not expressible in
            // the model builder either.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ix_discovery_seeds_name_live
                    ON discovery_seeds (lower(name))
                    WHERE deleted_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_discovery_seeds_name_live;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_discovery_candidates_address;");

            migrationBuilder.DropTable(
                name: "discovery_candidates");

            migrationBuilder.DropTable(
                name: "discovery_ignores");

            migrationBuilder.DropTable(
                name: "discovery_run_hosts");

            migrationBuilder.DropTable(
                name: "discovery_run_jobs");

            migrationBuilder.DropTable(
                name: "discovery_runs");

            migrationBuilder.DropTable(
                name: "discovery_seeds");
        }
    }
}

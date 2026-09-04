using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetShield.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_AddCollectorJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collector_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credential_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parameters = table.Column<string>(type: "jsonb", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    lease_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    leased_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    leased_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    detail = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    result = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collector_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "collectors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    running = table.Column<int>(type: "integer", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collectors", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collector_jobs_claimable",
                table: "collector_jobs",
                columns: new[] { "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_collector_jobs_credential_profile_id",
                table: "collector_jobs",
                column: "credential_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_collector_jobs_device_id_created_at",
                table: "collector_jobs",
                columns: new[] { "device_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_collectors_last_seen_at",
                table: "collectors",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ix_collectors_normalized_name",
                table: "collectors",
                column: "normalized_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collector_jobs");

            migrationBuilder.DropTable(
                name: "collectors");
        }
    }
}

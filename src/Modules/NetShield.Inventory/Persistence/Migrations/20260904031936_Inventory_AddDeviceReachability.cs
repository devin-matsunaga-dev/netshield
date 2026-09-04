using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetShield.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_AddDeviceReachability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_reachability",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pending_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pending_observations = table.Column<int>(type: "integer", nullable: false),
                    next_probe_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_probe_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_rtt_milliseconds = table.Column<double>(type: "double precision", nullable: true),
                    last_loss_percent = table.Column<double>(type: "double precision", nullable: true),
                    last_applied_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_reachability", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_reachability_device_id",
                table: "device_reachability",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_reachability_next_probe_at",
                table: "device_reachability",
                column: "next_probe_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_reachability");
        }
    }
}

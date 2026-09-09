using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetShield.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_AddVlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "last_vlan_count",
                table: "device_topology_scans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_vlan_error",
                table: "device_topology_scans",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_vlan_job_id",
                table: "device_topology_scans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_vlan_walk_at",
                table: "device_topology_scans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_vlan_walk_at",
                table: "device_topology_scans",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "vlan_table",
                table: "device_topology_scans",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "vlans_supported",
                table: "device_topology_scans",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "device_vlans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vlan_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    if_indexes = table.Column<int[]>(type: "integer[]", nullable: false),
                    untagged_if_indexes = table.Column<int[]>(type: "integer[]", nullable: false),
                    port_count = table.Column<int>(type: "integer", nullable: false),
                    unresolved_port_count = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    first_discovered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_vlans", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_topology_scans_next_vlan_walk_at",
                table: "device_topology_scans",
                column: "next_vlan_walk_at");

            migrationBuilder.CreateIndex(
                name: "ix_device_vlans_device_id_live",
                table: "device_vlans",
                columns: new[] { "device_id", "withdrawn_at" });

            migrationBuilder.CreateIndex(
                name: "ix_device_vlans_open",
                table: "device_vlans",
                columns: new[] { "device_id", "vlan_id" },
                unique: true,
                filter: "withdrawn_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_device_vlans_vlan_id_live",
                table: "device_vlans",
                columns: new[] { "vlan_id", "withdrawn_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_vlans");

            migrationBuilder.DropIndex(
                name: "ix_device_topology_scans_next_vlan_walk_at",
                table: "device_topology_scans");

            migrationBuilder.DropColumn(
                name: "last_vlan_count",
                table: "device_topology_scans");

            migrationBuilder.DropColumn(
                name: "last_vlan_error",
                table: "device_topology_scans");

            migrationBuilder.DropColumn(
                name: "last_vlan_job_id",
                table: "device_topology_scans");

            migrationBuilder.DropColumn(
                name: "last_vlan_walk_at",
                table: "device_topology_scans");

            migrationBuilder.DropColumn(
                name: "next_vlan_walk_at",
                table: "device_topology_scans");

            migrationBuilder.DropColumn(
                name: "vlan_table",
                table: "device_topology_scans");

            migrationBuilder.DropColumn(
                name: "vlans_supported",
                table: "device_topology_scans");
        }
    }
}

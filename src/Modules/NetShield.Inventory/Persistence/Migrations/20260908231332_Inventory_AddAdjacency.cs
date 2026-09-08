using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetShield.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_AddAdjacency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_adjacencies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    a_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    a_if_index = table.Column<int>(type: "integer", nullable: false),
                    a_interface_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    b_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    b_if_index = table.Column<int>(type: "integer", nullable: true),
                    b_interface_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    b_chassis_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    b_chassis_id_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    b_port_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    b_system_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    adjacency_key = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    sources_a = table.Column<string[]>(type: "text[]", nullable: false),
                    sources_b = table.Column<string[]>(type: "text[]", nullable: false),
                    confidence = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    observed_from_a = table.Column<bool>(type: "boolean", nullable: false),
                    observed_from_b = table.Column<bool>(type: "boolean", nullable: false),
                    first_discovered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_adjacencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device_neighbors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    local_if_index = table.Column<int>(type: "integer", nullable: false),
                    local_interface_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    remote_chassis_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    remote_chassis_id_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    remote_port_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    remote_port_id_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    remote_port_description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    remote_system_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    remote_system_description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    remote_platform = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    remote_management_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    remote_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    remote_if_index = table.Column<int>(type: "integer", nullable: true),
                    remote_interface_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    remote_key = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    adjacency_key = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    adjacency_id = table.Column<Guid>(type: "uuid", nullable: true),
                    capabilities = table.Column<int>(type: "integer", nullable: true),
                    evidence_count = table.Column<int>(type: "integer", nullable: true),
                    first_discovered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_neighbors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device_topology_scans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    next_neighbor_walk_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_neighbor_walk_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_neighbor_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lldp_supported = table.Column<bool>(type: "boolean", nullable: true),
                    cdp_supported = table.Column<bool>(type: "boolean", nullable: true),
                    last_lldp_count = table.Column<int>(type: "integer", nullable: true),
                    last_cdp_count = table.Column<int>(type: "integer", nullable: true),
                    last_neighbor_error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    next_route_walk_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_route_walk_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_route_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    routing_supported = table.Column<bool>(type: "boolean", nullable: true),
                    route_table = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_route_count = table.Column<int>(type: "integer", nullable: true),
                    last_next_hop_count = table.Column<int>(type: "integer", nullable: true),
                    last_route_error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_topology_scans", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_adjacencies_a_device_id_live",
                table: "device_adjacencies",
                columns: new[] { "a_device_id", "withdrawn_at" });

            migrationBuilder.CreateIndex(
                name: "ix_device_adjacencies_b_device_id_live",
                table: "device_adjacencies",
                columns: new[] { "b_device_id", "withdrawn_at" });

            migrationBuilder.CreateIndex(
                name: "ix_device_adjacencies_open",
                table: "device_adjacencies",
                columns: new[] { "a_device_id", "a_if_index", "adjacency_key" },
                unique: true,
                filter: "withdrawn_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_device_neighbors_adjacency_id",
                table: "device_neighbors",
                column: "adjacency_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_neighbors_device_id_live",
                table: "device_neighbors",
                columns: new[] { "device_id", "withdrawn_at" });

            migrationBuilder.CreateIndex(
                name: "ix_device_neighbors_open",
                table: "device_neighbors",
                columns: new[] { "device_id", "source", "local_if_index", "remote_key" },
                unique: true,
                filter: "withdrawn_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_device_neighbors_remote_device_id",
                table: "device_neighbors",
                column: "remote_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_topology_scans_device_id",
                table: "device_topology_scans",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_topology_scans_next_neighbor_walk_at",
                table: "device_topology_scans",
                column: "next_neighbor_walk_at");

            migrationBuilder.CreateIndex(
                name: "ix_device_topology_scans_next_route_walk_at",
                table: "device_topology_scans",
                column: "next_route_walk_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_adjacencies");

            migrationBuilder.DropTable(
                name: "device_neighbors");

            migrationBuilder.DropTable(
                name: "device_topology_scans");
        }
    }
}

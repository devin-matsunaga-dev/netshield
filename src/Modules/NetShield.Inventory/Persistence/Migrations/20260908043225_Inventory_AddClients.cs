using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetShield.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_AddClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_ip_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observed_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    observed_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_ip_bindings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "client_port_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    if_index = table.Column<int>(type: "integer", nullable: false),
                    vlan_id = table.Column<int>(type: "integer", nullable: true),
                    mac_count_on_port = table.Column<int>(type: "integer", nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    observed_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    observed_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_port_bindings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mac_address = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false),
                    oui = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    locally_administered = table.Column<bool>(type: "boolean", nullable: false),
                    hostname = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device_client_scans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    next_walk_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_walk_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_applied_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    neighbors_supported = table.Column<bool>(type: "boolean", nullable: true),
                    forwarding_supported = table.Column<bool>(type: "boolean", nullable: true),
                    last_neighbor_count = table.Column<int>(type: "integer", nullable: true),
                    last_forwarding_count = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_client_scans", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_ip_bindings_client_id_from",
                table: "client_ip_bindings",
                columns: new[] { "client_id", "observed_from" });

            migrationBuilder.CreateIndex(
                name: "ix_client_ip_bindings_device_id",
                table: "client_ip_bindings",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_port_bindings_client_id_from",
                table: "client_port_bindings",
                columns: new[] { "client_id", "observed_from" });

            migrationBuilder.CreateIndex(
                name: "ix_client_port_bindings_device_id_if_index",
                table: "client_port_bindings",
                columns: new[] { "device_id", "if_index" });

            migrationBuilder.CreateIndex(
                name: "ix_client_port_bindings_open",
                table: "client_port_bindings",
                columns: new[] { "client_id", "device_id" },
                unique: true,
                filter: "observed_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_clients_last_seen_at_id",
                table: "clients",
                columns: new[] { "last_seen_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_clients_mac_address",
                table: "clients",
                column: "mac_address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clients_oui",
                table: "clients",
                column: "oui");

            migrationBuilder.CreateIndex(
                name: "ix_device_client_scans_device_id",
                table: "device_client_scans",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_client_scans_next_walk_at",
                table: "device_client_scans",
                column: "next_walk_at");

            // Two indexes on `ip_address` that EF cannot declare. It refuses to index a property
            // whose CLR type is not IComparable and IPAddress is not, which is the same reason
            // `ix_devices_primary_ip_address_live` is written by hand. `inet` is worth keeping:
            // it validates the value and normalises the notation, so one address cannot be
            // stored under two spellings and hold two open bindings the unique index cannot see.
            //
            // Because they are absent from InventoryDbContextModelSnapshot, a later
            // `dotnet ef migrations add` will neither drop nor recreate them — which is what we
            // want, and it does mean the snapshot is not the whole schema for this table. Any
            // later change to either index has to be written the same way.

            // The resolution query's index: an address, and its intervals newest first.
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_client_ip_bindings_address_from
                    ON client_ip_bindings (ip_address, observed_from DESC);
                """);

            // The invariant the whole package rests on: an address has at most one open binding.
            // With this in place a handler that failed to close the previous holder before
            // opening the next fails its insert, rather than producing an address that resolves
            // to two different clients at one instant.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ix_client_ip_bindings_address_open
                    ON client_ip_bindings (ip_address)
                    WHERE observed_to IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_client_ip_bindings_address_open;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_client_ip_bindings_address_from;");

            migrationBuilder.DropTable(
                name: "client_ip_bindings");

            migrationBuilder.DropTable(
                name: "client_port_bindings");

            migrationBuilder.DropTable(
                name: "clients");

            migrationBuilder.DropTable(
                name: "device_client_scans");
        }
    }
}

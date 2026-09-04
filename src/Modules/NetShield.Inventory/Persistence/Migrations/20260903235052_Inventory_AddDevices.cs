using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetShield.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_AddDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hostname = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    primary_ip_address = table.Column<IPAddress>(type: "inet", nullable: false),
                    vendor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    os_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    serial_number = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    site = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    criticality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    environment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    owner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_devices_created_at_id",
                table: "devices",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_devices_deleted_at",
                table: "devices",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "ix_devices_hostname",
                table: "devices",
                column: "hostname");

            // The one uniqueness guarantee this table makes: no two live devices share a primary
            // address. Written as SQL because EF will not index a property whose CLR type is not
            // comparable, and IPAddress is not — see DeviceConfiguration. Partial on
            // deleted_at IS NULL, so removing a device releases its address for a replacement.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ix_devices_primary_ip_address_live
                    ON devices (primary_ip_address)
                    WHERE deleted_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_devices_primary_ip_address_live;");

            migrationBuilder.DropTable(
                name: "devices");
        }
    }
}

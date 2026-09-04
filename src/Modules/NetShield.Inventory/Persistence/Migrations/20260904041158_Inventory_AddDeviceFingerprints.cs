using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetShield.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_AddDeviceFingerprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_fingerprints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reduced_capability = table.Column<bool>(type: "boolean", nullable: false),
                    sys_object_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sys_descr = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    sys_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sys_contact = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sys_location = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    uptime_seconds = table.Column<double>(type: "double precision", nullable: true),
                    model = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    os_version = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    serial_number = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    interface_count = table.Column<int>(type: "integer", nullable: false),
                    interfaces_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    overridden_fields = table.Column<string[]>(type: "text[]", nullable: false),
                    last_walk_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_applied_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_fingerprints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device_interfaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    if_index = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    alias = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    interface_type = table.Column<int>(type: "integer", nullable: true),
                    mtu = table.Column<int>(type: "integer", nullable: true),
                    speed_bits_per_second = table.Column<long>(type: "bigint", nullable: true),
                    physical_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    admin_status = table.Column<int>(type: "integer", nullable: true),
                    oper_status = table.Column<int>(type: "integer", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_interfaces", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_fingerprints_device_id",
                table: "device_fingerprints",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_interfaces_device_id_if_index",
                table: "device_interfaces",
                columns: new[] { "device_id", "if_index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_fingerprints");

            migrationBuilder.DropTable(
                name: "device_interfaces");
        }
    }
}

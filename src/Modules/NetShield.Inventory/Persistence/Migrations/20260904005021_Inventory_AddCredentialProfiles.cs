using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetShield.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_AddCredentialProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credential_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    username = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    auth_algorithm = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    privacy_algorithm = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    key_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    wrapped_data_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    material_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    material_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credential_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device_credential_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_credential_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_credential_profiles_credential_profiles_credential_p",
                        column: x => x.credential_profile_id,
                        principalTable: "credential_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_device_credential_profiles_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credential_profiles_created_at_id",
                table: "credential_profiles",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_credential_profiles_deleted_at",
                table: "credential_profiles",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "ix_credential_profiles_key_id",
                table: "credential_profiles",
                column: "key_id");

            migrationBuilder.CreateIndex(
                name: "ix_credential_profiles_normalized_name_live",
                table: "credential_profiles",
                column: "normalized_name",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_device_credential_profiles_credential_profile_id",
                table: "device_credential_profiles",
                column: "credential_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_credential_profiles_device_id",
                table: "device_credential_profiles",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_credential_profiles_device_id_credential_profile_id",
                table: "device_credential_profiles",
                columns: new[] { "device_id", "credential_profile_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_credential_profiles");

            migrationBuilder.DropTable(
                name: "credential_profiles");
        }
    }
}

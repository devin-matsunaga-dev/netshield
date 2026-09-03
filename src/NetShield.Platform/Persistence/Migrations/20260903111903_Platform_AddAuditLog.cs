using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetShield.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Platform_AddAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    actor_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    source_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    target_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    target_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true),
                    http_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_actor_user_id",
                table: "audit_log",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_created_at",
                table: "audit_log",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_target",
                table: "audit_log",
                columns: new[] { "target_type", "target_id" });

            // The append-only rule, in the database. ARCHITECTURE.md and CLAUDE.md both put it
            // in the same words: no update or delete path may ever be written for audit_log.
            // A code-level guard is enforced by NetShield.ArchitectureTests; this is the half
            // that still holds when someone opens psql.
            //
            // The trigger is FOR EACH STATEMENT rather than FOR EACH ROW so that an UPDATE or a
            // DELETE matching no rows fails too -- a statement that would silently succeed
            // against an empty table is a rule with a hole in it. TRUNCATE is covered by the
            // same trigger because TRUNCATE bypasses row-level triggers entirely, and it is the
            // fastest way to empty a table for anyone who thinks to try it.
            //
            // CONVENTIONS.md permits exactly two triggers in this system, and this is one of them.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION netshield_audit_log_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_log is append-only; % is not permitted', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_log_append_only
                BEFORE UPDATE OR DELETE OR TRUNCATE ON audit_log
                FOR EACH STATEMENT EXECUTE FUNCTION netshield_audit_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_log_append_only ON audit_log;");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS netshield_audit_log_append_only();");
        }
    }
}

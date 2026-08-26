using Jundiai.Api;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jundiai.Api.Migrations;

[DbContext(typeof(JundiaiDbContext))]
[Migration("202608260445_InitialDurableFoundation")]
public sealed class InitialDurableFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "idempotency_keys",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                InstitutionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Key = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                ResponseHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_idempotency_keys", x => x.Id));

        migrationBuilder.CreateTable(
            name: "integration_outbox",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                InstitutionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                IdempotencyKey = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_integration_outbox", x => x.Id));

        migrationBuilder.CreateTable(
            name: "platform_envelopes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CheckpointId = table.Column<Guid>(type: "uuid", nullable: false),
                InstitutionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                HealthUnitId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                Kind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                ResourceId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Label = table.Column<string>(type: "text", nullable: true),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_platform_envelopes", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_keys_ExpiresAt",
            table: "idempotency_keys",
            column: "ExpiresAt");
        migrationBuilder.CreateIndex(
            name: "IX_idempotency_keys_InstitutionId_Scope_Key",
            table: "idempotency_keys",
            columns: new[] { "InstitutionId", "Scope", "Key" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_integration_outbox_InstitutionId_IdempotencyKey",
            table: "integration_outbox",
            columns: new[] { "InstitutionId", "IdempotencyKey" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_integration_outbox_Status_CreatedAt",
            table: "integration_outbox",
            columns: new[] { "Status", "CreatedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_platform_envelopes_CheckpointId",
            table: "platform_envelopes",
            column: "CheckpointId");
        migrationBuilder.CreateIndex(
            name: "IX_platform_envelopes_InstitutionId_Kind_ResourceId",
            table: "platform_envelopes",
            columns: new[] { "InstitutionId", "Kind", "ResourceId" });
        migrationBuilder.CreateIndex(
            name: "IX_platform_envelopes_OccurredAt",
            table: "platform_envelopes",
            column: "OccurredAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "idempotency_keys");
        migrationBuilder.DropTable(name: "integration_outbox");
        migrationBuilder.DropTable(name: "platform_envelopes");
    }
}

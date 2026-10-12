using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FimpleCaseStudy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLowCashOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AtmId = table.Column<Guid>(type: "uuid", nullable: false),
                    CassetteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Denomination = table.Column<int>(type: "integer", nullable: false),
                    BeforeAvailable = table.Column<int>(type: "integer", nullable: false),
                    Available = table.Column<int>(type: "integer", nullable: false),
                    Threshold = table.Column<int>(type: "integer", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.Id);
                    table.CheckConstraint("ck_outbox_counts", "\"BeforeAvailable\" BETWEEN 0 AND 100 AND \"Available\" BETWEEN 0 AND 9 AND \"Threshold\" = 10 AND \"Attempts\" >= 0");
                    table.CheckConstraint("ck_outbox_delivery", "\"NextAttemptAt\" >= \"OccurredAt\" AND (\"SentAt\" IS NULL OR \"SentAt\" >= \"OccurredAt\")");
                    table.CheckConstraint("ck_outbox_note", "\"Slot\" BETWEEN 1 AND 4 AND \"Currency\" IN ('TRY','USD','EUR') AND \"Denomination\" IN (20,50,100,500)");
                    table.CheckConstraint("ck_outbox_reason", "\"Reason\" IN ('INITIAL_SERVICE_LOW_CASH','AVAILABLE_COUNT_FELL_BELOW_THRESHOLD')");
                    table.ForeignKey(
                        name: "FK_outbox_cassettes_CassetteId_AtmId",
                        columns: x => new { x.CassetteId, x.AtmId },
                        principalSchema: "cash",
                        principalTable: "cassettes",
                        principalColumns: new[] { "Id", "AtmId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_AtmId_OccurredAt",
                schema: "cash",
                table: "outbox",
                columns: new[] { "AtmId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_CassetteId_AtmId",
                schema: "cash",
                table: "outbox",
                columns: new[] { "CassetteId", "AtmId" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_NextAttemptAt_OccurredAt",
                schema: "cash",
                table: "outbox",
                columns: new[] { "NextAttemptAt", "OccurredAt" },
                filter: "\"SentAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_OperationId_CassetteId",
                schema: "cash",
                table: "outbox",
                columns: new[] { "OperationId", "CassetteId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox",
                schema: "cash");
        }
    }
}

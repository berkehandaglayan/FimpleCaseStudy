using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FimpleCaseStudy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reconciliations",
                schema: "cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AtmId = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LinesJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reconciliations", x => x.Id);
                    table.CheckConstraint("ck_reconciliation_actor", "length(btrim(\"Actor\")) > 0");
                    table.ForeignKey(
                        name: "FK_reconciliations_atms_AtmId",
                        column: x => x.AtmId,
                        principalSchema: "cash",
                        principalTable: "atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reconciliations_AtmId_OccurredAt",
                schema: "cash",
                table: "reconciliations",
                columns: new[] { "AtmId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reconciliations",
                schema: "cash");
        }
    }
}

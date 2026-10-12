using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FimpleCaseStudy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                schema: "cash",
                table: "reconciliations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                schema: "cash",
                table: "reconciliations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RatesJson",
                schema: "cash",
                table: "reconciliations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "cash",
                table: "reconciliations",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "LegacyCompleted");

            // Pre-v0.8 counts have no historical FX snapshot; do not invent one or retroactively block service.
            migrationBuilder.Sql("ALTER TABLE cash.reconciliations ALTER COLUMN \"Status\" DROP DEFAULT;");

            migrationBuilder.AddColumn<decimal>(
                name: "TotalDifferenceTry",
                schema: "cash",
                table: "reconciliations",
                type: "numeric(20,6)",
                precision: 20,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                schema: "cash",
                columns: table => new
                {
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RateToTry = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rates", x => x.Currency);
                    table.CheckConstraint("ck_exchange_rate", "\"Currency\" IN ('TRY','USD','EUR') AND \"RateToTry\" > 0 AND \"RateToTry\" <= 999999 AND (\"Currency\" <> 'TRY' OR \"RateToTry\" = 1)");
                });

            migrationBuilder.InsertData(
                schema: "cash",
                table: "exchange_rates",
                columns: new[] { "Currency", "RateToTry" },
                values: new object[,]
                {
                    { "EUR", 45m },
                    { "TRY", 1m },
                    { "USD", 40m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_reconciliations_AtmId_Status",
                schema: "cash",
                table: "reconciliations",
                columns: new[] { "AtmId", "Status" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_reconciliation_review",
                schema: "cash",
                table: "reconciliations",
                sql: "(\"Status\" = 'LegacyCompleted' AND \"TotalDifferenceTry\" IS NULL AND \"RatesJson\" IS NULL AND \"ApprovedBy\" IS NULL AND \"ApprovedAt\" IS NULL)\nOR (\"TotalDifferenceTry\" IS NOT NULL AND \"RatesJson\" IS NOT NULL AND (\n    (\"Status\" = 'Completed' AND \"TotalDifferenceTry\" BETWEEN 0 AND 1000 AND \"ApprovedBy\" IS NULL AND \"ApprovedAt\" IS NULL)\n    OR (\"Status\" = 'AwaitingApproval' AND \"TotalDifferenceTry\" > 1000 AND \"ApprovedBy\" IS NULL AND \"ApprovedAt\" IS NULL)\n    OR (\"Status\" = 'Approved' AND \"TotalDifferenceTry\" > 1000 AND \"ApprovedBy\" IS NOT NULL AND \"ApprovedAt\" IS NOT NULL\n        AND length(btrim(\"ApprovedBy\")) > 0 AND lower(btrim(\"ApprovedBy\")) <> lower(btrim(\"Actor\")) AND \"ApprovedAt\" >= \"OccurredAt\")))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exchange_rates",
                schema: "cash");

            migrationBuilder.DropIndex(
                name: "IX_reconciliations_AtmId_Status",
                schema: "cash",
                table: "reconciliations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reconciliation_review",
                schema: "cash",
                table: "reconciliations");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "cash",
                table: "reconciliations");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                schema: "cash",
                table: "reconciliations");

            migrationBuilder.DropColumn(
                name: "RatesJson",
                schema: "cash",
                table: "reconciliations");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "cash",
                table: "reconciliations");

            migrationBuilder.DropColumn(
                name: "TotalDifferenceTry",
                schema: "cash",
                table: "reconciliations");
        }
    }
}

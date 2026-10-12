using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FimpleCaseStudy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawalReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "cash",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "withdrawal_quotes",
                schema: "cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AtmId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAmount = table.Column<int>(type: "integer", nullable: false),
                    LimitReservation = table.Column<int>(type: "integer", nullable: false),
                    OptionsJson = table.Column<string>(type: "text", nullable: false),
                    ReservationsJson = table.Column<string>(type: "text", nullable: false),
                    SelectedOption = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_withdrawal_quotes", x => x.Id);
                    table.CheckConstraint("ck_quote_amounts", "\"LimitReservation\" > 0 AND \"ConfirmedAmount\" >= 0");
                    table.CheckConstraint("ck_quote_confirmation", "(\"Status\" = 'Confirmed' AND \"ConfirmedAt\" IS NOT NULL AND \"ConfirmedAmount\" > 0 AND \"SelectedOption\" IS NOT NULL) OR (\"Status\" <> 'Confirmed' AND \"ConfirmedAt\" IS NULL AND \"ConfirmedAmount\" = 0 AND \"SelectedOption\" IS NULL)");
                    table.CheckConstraint("ck_quote_currency", "\"Currency\" IN ('TRY','USD','EUR')");
                    table.CheckConstraint("ck_quote_expiration", "\"ExpiresAt\" = \"CreatedAt\" + interval '60 seconds'");
                    table.CheckConstraint("ck_quote_status", "\"Status\" IN ('Open','Confirmed','Cancelled','Expired')");
                    table.ForeignKey(
                        name: "FK_withdrawal_quotes_atms_AtmId",
                        column: x => x.AtmId,
                        principalSchema: "cash",
                        principalTable: "atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_withdrawal_quotes_AtmId",
                schema: "cash",
                table: "withdrawal_quotes",
                column: "AtmId");

            migrationBuilder.CreateIndex(
                name: "IX_withdrawal_quotes_CardHash_Currency_ConfirmedAt",
                schema: "cash",
                table: "withdrawal_quotes",
                columns: new[] { "CardHash", "Currency", "ConfirmedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_withdrawal_quotes_Status_ExpiresAt",
                schema: "cash",
                table: "withdrawal_quotes",
                columns: new[] { "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "cash");

            migrationBuilder.DropTable(
                name: "withdrawal_quotes",
                schema: "cash");
        }
    }
}

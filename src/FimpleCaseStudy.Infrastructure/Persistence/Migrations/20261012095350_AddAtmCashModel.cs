using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FimpleCaseStudy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAtmCashModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cash");

            migrationBuilder.CreateTable(
                name: "atms",
                schema: "cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_atms", x => x.Id);
                    table.CheckConstraint("ck_atm_name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("ck_atm_state", "\"State\" IN ('Unconfigured','Configured','Loaded','InService','Maintenance')");
                });

            migrationBuilder.CreateTable(
                name: "cassettes",
                schema: "cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AtmId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Denomination = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cassettes", x => x.Id);
                    table.UniqueConstraint("AK_cassettes_Id_AtmId", x => new { x.Id, x.AtmId });
                    table.CheckConstraint("ck_cassette_configuration", "(\"Currency\" IS NULL AND \"Denomination\" IS NULL) OR (\"Kind\" = 'Recycling' AND \"Currency\" IS NOT NULL AND \"Denomination\" IS NOT NULL AND \"Currency\" IN ('TRY','USD','EUR') AND \"Denomination\" IN (20,50,100,500))");
                    table.CheckConstraint("ck_cassette_slot_kind", "(\"Slot\" BETWEEN 1 AND 4 AND \"Kind\" = 'Recycling') OR (\"Slot\" = 5 AND \"Kind\" = 'Deposit')");
                    table.ForeignKey(
                        name: "FK_cassettes_atms_AtmId",
                        column: x => x.AtmId,
                        principalSchema: "cash",
                        principalTable: "atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "balances",
                schema: "cash",
                columns: table => new
                {
                    CassetteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Denomination = table.Column<int>(type: "integer", nullable: false),
                    OnHand = table.Column<int>(type: "integer", nullable: false),
                    Reserved = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_balances", x => new { x.CassetteId, x.Currency, x.Denomination });
                    table.CheckConstraint("ck_balance_counts", "\"OnHand\" BETWEEN 0 AND 100 AND \"Reserved\" BETWEEN 0 AND \"OnHand\"");
                    table.CheckConstraint("ck_balance_note", "\"Currency\" IN ('TRY','USD','EUR') AND \"Denomination\" IN (20,50,100,500)");
                    table.ForeignKey(
                        name: "FK_balances_cassettes_CassetteId",
                        column: x => x.CassetteId,
                        principalSchema: "cash",
                        principalTable: "cassettes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "movements",
                schema: "cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AtmId = table.Column<Guid>(type: "uuid", nullable: false),
                    CassetteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Denomination = table.Column<int>(type: "integer", nullable: false),
                    PhysicalDelta = table.Column<int>(type: "integer", nullable: false),
                    ReservedDelta = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movements", x => x.Id);
                    table.CheckConstraint("ck_movement_delta", "\"PhysicalDelta\" BETWEEN -100 AND 100 AND \"ReservedDelta\" BETWEEN -100 AND 100 AND (\"PhysicalDelta\" <> 0 OR \"ReservedDelta\" <> 0)");
                    table.CheckConstraint("ck_movement_note", "\"Currency\" IN ('TRY','USD','EUR') AND \"Denomination\" IN (20,50,100,500)");
                    table.CheckConstraint("ck_movement_type", "\"Type\" IN ('Load','Unload','Withdrawal','Deposit','Reservation','ReservationRelease','ReconciliationAdjustment')");
                    table.ForeignKey(
                        name: "FK_movements_cassettes_CassetteId_AtmId",
                        columns: x => new { x.CassetteId, x.AtmId },
                        principalSchema: "cash",
                        principalTable: "cassettes",
                        principalColumns: new[] { "Id", "AtmId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cassettes_AtmId_Slot",
                schema: "cash",
                table: "cassettes",
                columns: new[] { "AtmId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_movements_AtmId_OccurredAt",
                schema: "cash",
                table: "movements",
                columns: new[] { "AtmId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_movements_CassetteId_AtmId",
                schema: "cash",
                table: "movements",
                columns: new[] { "CassetteId", "AtmId" });

            migrationBuilder.CreateIndex(
                name: "IX_movements_OperationId",
                schema: "cash",
                table: "movements",
                column: "OperationId");

            migrationBuilder.Sql("""
                CREATE FUNCTION cash.reject_movement_changes() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'Cash movements are append-only' USING ERRCODE = '55000';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER movements_append_only
                BEFORE UPDATE OR DELETE OR TRUNCATE ON cash.movements
                FOR EACH STATEMENT EXECUTE FUNCTION cash.reject_movement_changes();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "balances",
                schema: "cash");

            migrationBuilder.DropTable(
                name: "movements",
                schema: "cash");

            migrationBuilder.Sql("DROP FUNCTION cash.reject_movement_changes();");

            migrationBuilder.DropTable(
                name: "cassettes",
                schema: "cash");

            migrationBuilder.DropTable(
                name: "atms",
                schema: "cash");
        }
    }
}

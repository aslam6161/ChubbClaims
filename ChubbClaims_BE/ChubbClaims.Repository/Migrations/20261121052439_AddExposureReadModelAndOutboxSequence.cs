using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChubbClaims.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddExposureReadModelAndOutboxSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_ProcessedAt_OccurredAt",
                table: "OutboxMessages");

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "OutboxMessages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.CreateTable(
                name: "ExposureByMarket",
                columns: table => new
                {
                    Market = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OpenClaims = table.Column<int>(type: "int", nullable: false),
                    OutstandingReserve = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LastEventAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExposureByMarket", x => new { x.Market, x.Currency, x.ClaimType });
                });

            migrationBuilder.CreateTable(
                name: "ProcessedEvents",
                columns: table => new
                {
                    ConsumerGroup = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedEvents", x => new { x.ConsumerGroup, x.EventId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAt_Sequence",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAt", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExposureByMarket");

            migrationBuilder.DropTable(
                name: "ProcessedEvents");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_ProcessedAt_Sequence",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "OutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAt_OccurredAt",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAt", "OccurredAt" });
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChubbClaims.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClaimDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    UploadedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedByRole = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimDocuments_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimDocuments_ClaimId_UploadedAt",
                table: "ClaimDocuments",
                columns: new[] { "ClaimId", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimDocuments_StorageKey",
                table: "ClaimDocuments",
                column: "StorageKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimDocuments");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemovePaydayExpenseLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaydayExpenseLinks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaydayExpenseLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaydayExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaydayExpenseLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaydayExpenseLinks_CompanyId_JobId",
                table: "PaydayExpenseLinks",
                columns: new[] { "CompanyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaydayExpenseLinks_CompanyId_PaydayExpenseId",
                table: "PaydayExpenseLinks",
                columns: new[] { "CompanyId", "PaydayExpenseId" },
                unique: true);
        }
    }
}

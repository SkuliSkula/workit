using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class DrivingOnTimeEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DrivingUnits",
                table: "TimeEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.DropIndex(
                name: "IX_DrivingEntries_CompanyId_EmployeeId",
                table: "DrivingEntries");

            migrationBuilder.DropIndex(
                name: "IX_DrivingEntries_CompanyId_JobId",
                table: "DrivingEntries");

            migrationBuilder.DropTable(name: "DrivingEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DrivingUnits",
                table: "TimeEntries");

            migrationBuilder.CreateTable(
                name: "DrivingEntries",
                columns: table => new
                {
                    Id         = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId  = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId      = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkDate   = table.Column<DateOnly>(type: "date", nullable: false),
                    Units      = table.Column<int>(type: "integer", nullable: false),
                    Notes      = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrivingEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DrivingEntries_CompanyId_EmployeeId",
                table: "DrivingEntries",
                columns: new[] { "CompanyId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_DrivingEntries_CompanyId_JobId",
                table: "DrivingEntries",
                columns: new[] { "CompanyId", "JobId" });
        }
    }
}

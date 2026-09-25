using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAbsenceHoursPerDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every absence recorded before this column existed was a whole day
            // off, so they backfill to a standard day rather than to nothing.
            migrationBuilder.AddColumn<decimal>(
                name: "HoursPerDay",
                table: "AbsenceRequests",
                type: "numeric",
                nullable: false,
                defaultValue: 8m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HoursPerDay",
                table: "AbsenceRequests");
        }
    }
}

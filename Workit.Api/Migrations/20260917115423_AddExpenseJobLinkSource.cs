using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseJobLinkSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "JobLinkSource",
                table: "Expenses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Links made before this column existed were all chosen by an owner.
            migrationBuilder.Sql("""UPDATE "Expenses" SET "JobLinkSource" = 1 WHERE "JobId" IS NOT NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JobLinkSource",
                table: "Expenses");
        }
    }
}

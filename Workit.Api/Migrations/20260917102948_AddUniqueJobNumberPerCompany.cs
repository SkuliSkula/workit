using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueJobNumberPerCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Numbers were allocated as MAX + 1 with nothing enforcing uniqueness,
            // so two jobs in a company may share one (a race, or legacy rows at 0).
            // Give every duplicate past the first a fresh number above the
            // company's current maximum — and the matching code suffix — so the
            // unique index below can be created. The first holder of each number
            // keeps it; the rest are renumbered in a stable (Id) order.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id", "CompanyId",
                           ROW_NUMBER() OVER (PARTITION BY "CompanyId", "JobNumber" ORDER BY "Id") AS dup_rank
                    FROM "Jobs"
                ),
                renumbered AS (
                    SELECT r."Id",
                           (SELECT MAX(m."JobNumber") FROM "Jobs" m WHERE m."CompanyId" = r."CompanyId")
                               + ROW_NUMBER() OVER (PARTITION BY r."CompanyId" ORDER BY r."Id") AS new_number
                    FROM ranked r
                    WHERE r.dup_rank > 1
                )
                UPDATE "Jobs" j
                SET "JobNumber" = n.new_number,
                    "Code" = regexp_replace(j."Code", '[0-9]+$', lpad(n.new_number::text, 3, '0'))
                FROM renumbered n
                WHERE j."Id" = n."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_CompanyId_JobNumber",
                table: "Jobs",
                columns: new[] { "CompanyId", "JobNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_CompanyId_JobNumber",
                table: "Jobs");
        }
    }
}

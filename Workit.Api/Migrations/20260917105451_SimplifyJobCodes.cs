using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyJobCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Job codes drop the customer initials: "REP-BBH-107" becomes "REP107",
            // the category's short code plus the job number. Rewritten for every
            // existing job so the whole system reads the same way. The CASE mirrors
            // JobEndpoints.GetCategoryCode and the JobCategory enum's integer values.
            migrationBuilder.Sql("""
                UPDATE "Jobs"
                SET "Code" = CASE "Category"
                        WHEN 0 THEN 'NI'
                        WHEN 1 THEN 'REP'
                        WHEN 2 THEN 'IW'
                        WHEN 3 THEN 'DWG'
                        WHEN 4 THEN 'OFF'
                        WHEN 5 THEN 'MNT'
                        WHEN 6 THEN 'INS'
                        WHEN 7 THEN 'CON'
                        ELSE 'JOB'
                    END || lpad("JobNumber"::text, 3, '0');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The customer initials are not recoverable from the new code; rolling
            // back leaves the simplified codes in place.
        }
    }
}

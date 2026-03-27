using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingDrivingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These columns were defined in orphan migration files (AddDriving, DrivingOnTimeEntry)
            // that had no Designer.cs and were therefore never applied to the database.
            // Use conditional DDL so this is safe to run even if the columns already exist
            // (e.g. on environments that did somehow get them).
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'Companies' AND column_name = 'DrivingUnitPrice'
                    ) THEN
                        ALTER TABLE ""Companies"" ADD COLUMN ""DrivingUnitPrice"" numeric NOT NULL DEFAULT 0;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'TimeEntries' AND column_name = 'DrivingUnits'
                    ) THEN
                        ALTER TABLE ""TimeEntries"" ADD COLUMN ""DrivingUnits"" integer NOT NULL DEFAULT 0;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DrivingUnitPrice", table: "Companies");
            migrationBuilder.DropColumn(name: "DrivingUnits",     table: "TimeEntries");
        }
    }
}

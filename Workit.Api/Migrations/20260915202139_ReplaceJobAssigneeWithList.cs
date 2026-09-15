using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceJobAssigneeWithList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The scaffold drops the old column first, which would discard any
            // assignment made in the window the single-assignee field was live.
            // Add the list, carry the value across, then drop.
            migrationBuilder.AddColumn<List<Guid>>(
                name: "AssignedEmployeeIds",
                table: "Jobs",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.Sql(
                "UPDATE \"Jobs\" SET \"AssignedEmployeeIds\" = ARRAY[\"AssignedEmployeeId\"] " +
                "WHERE \"AssignedEmployeeId\" IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "AssignedEmployeeId",
                table: "Jobs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedEmployeeId",
                table: "Jobs",
                type: "uuid",
                nullable: true);

            // Only the first assignee survives a rollback; the old shape has one slot.
            migrationBuilder.Sql(
                "UPDATE \"Jobs\" SET \"AssignedEmployeeId\" = \"AssignedEmployeeIds\"[1] " +
                "WHERE cardinality(\"AssignedEmployeeIds\") > 0;");

            migrationBuilder.DropColumn(
                name: "AssignedEmployeeIds",
                table: "Jobs");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompTimeGrantLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompTimeAllocationsJson",
                table: "leave_request",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "comp_time_grant",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompTimeAllocationsJson",
                table: "leave_request");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "comp_time_grant");
        }
    }
}

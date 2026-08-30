using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "department",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "department",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "department",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "department",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "department",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("UPDATE department SET \"IsSystem\" = TRUE WHERE \"Id\" IN ('general', 'finance', 'hr', 'engineering', 'sales')");

            migrationBuilder.CreateIndex(
                name: "IX_department_ParentId",
                table: "department",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_department_TenantId_ParentId_SortOrder_Name",
                table: "department",
                columns: new[] { "TenantId", "ParentId", "SortOrder", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_department_department_ParentId",
                table: "department",
                column: "ParentId",
                principalTable: "department",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_department_department_ParentId",
                table: "department");

            migrationBuilder.DropIndex(
                name: "IX_department_ParentId",
                table: "department");

            migrationBuilder.DropIndex(
                name: "IX_department_TenantId_ParentId_SortOrder_Name",
                table: "department");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "department");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "department");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "department");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "department");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "department");
        }
    }
}

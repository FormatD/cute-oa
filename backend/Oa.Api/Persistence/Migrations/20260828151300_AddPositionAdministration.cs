using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PositionId",
                table: "oa_user",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "position",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DepartmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "ACTIVE"),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_position", x => x.Id);
                    table.ForeignKey(
                        name: "FK_position_department_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "department",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_oa_user_PositionId",
                table: "oa_user",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_position_DepartmentId",
                table: "position",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_position_TenantId_DepartmentId_Name",
                table: "position",
                columns: new[] { "TenantId", "DepartmentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_position_TenantId_DepartmentId_Status_SortOrder_Name",
                table: "position",
                columns: new[] { "TenantId", "DepartmentId", "Status", "SortOrder", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_oa_user_position_PositionId",
                table: "oa_user",
                column: "PositionId",
                principalTable: "position",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_oa_user_position_PositionId",
                table: "oa_user");

            migrationBuilder.DropTable(
                name: "position");

            migrationBuilder.DropIndex(
                name: "IX_oa_user_PositionId",
                table: "oa_user");

            migrationBuilder.DropColumn(
                name: "PositionId",
                table: "oa_user");
        }
    }
}

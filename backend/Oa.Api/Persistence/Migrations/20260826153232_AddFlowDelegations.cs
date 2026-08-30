using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowDelegations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DelegationId",
                table: "flow_task",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalAssigneeId",
                table: "flow_task",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalAssigneeName",
                table: "flow_task",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DelegationId",
                table: "expense_task",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalAssigneeId",
                table: "expense_task",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalAssigneeName",
                table: "expense_task",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "flow_delegation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DelegateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BusinessType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_delegation", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flow_task_DelegationId",
                table: "flow_task",
                column: "DelegationId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_task_DelegationId",
                table: "expense_task",
                column: "DelegationId");

            migrationBuilder.CreateIndex(
                name: "IX_flow_delegation_TenantId_OwnerId_Status_StartAt_EndAt",
                table: "flow_delegation",
                columns: new[] { "TenantId", "OwnerId", "Status", "StartAt", "EndAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_expense_task_flow_delegation_DelegationId",
                table: "expense_task",
                column: "DelegationId",
                principalTable: "flow_delegation",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_flow_task_flow_delegation_DelegationId",
                table: "flow_task",
                column: "DelegationId",
                principalTable: "flow_delegation",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_expense_task_flow_delegation_DelegationId",
                table: "expense_task");

            migrationBuilder.DropForeignKey(
                name: "FK_flow_task_flow_delegation_DelegationId",
                table: "flow_task");

            migrationBuilder.DropTable(
                name: "flow_delegation");

            migrationBuilder.DropIndex(
                name: "IX_flow_task_DelegationId",
                table: "flow_task");

            migrationBuilder.DropIndex(
                name: "IX_expense_task_DelegationId",
                table: "expense_task");

            migrationBuilder.DropColumn(
                name: "DelegationId",
                table: "flow_task");

            migrationBuilder.DropColumn(
                name: "OriginalAssigneeId",
                table: "flow_task");

            migrationBuilder.DropColumn(
                name: "OriginalAssigneeName",
                table: "flow_task");

            migrationBuilder.DropColumn(
                name: "DelegationId",
                table: "expense_task");

            migrationBuilder.DropColumn(
                name: "OriginalAssigneeId",
                table: "expense_task");

            migrationBuilder.DropColumn(
                name: "OriginalAssigneeName",
                table: "expense_task");
        }
    }
}

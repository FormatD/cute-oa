using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentFlowInstanceId",
                table: "leave_request",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FlowInstanceId",
                table: "flow_task",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FlowInstanceId",
                table: "expense_task",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentFlowInstanceId",
                table: "expense_claim",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "flow_instance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    BusinessType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApplicantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProcessDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessDefinitionCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProcessDefinitionVersion = table.Column<int>(type: "integer", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_instance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flow_instance_process_definition_ProcessDefinitionId",
                        column: x => x.ProcessDefinitionId,
                        principalTable: "process_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "flow_action",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    FlowInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromAssigneeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FromAssigneeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ToAssigneeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ToAssigneeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_action", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flow_action_flow_instance_FlowInstanceId",
                        column: x => x.FlowInstanceId,
                        principalTable: "flow_instance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flow_task_FlowInstanceId",
                table: "flow_task",
                column: "FlowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_task_FlowInstanceId",
                table: "expense_task",
                column: "FlowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_flow_action_FlowInstanceId_OccurredAt",
                table: "flow_action",
                columns: new[] { "FlowInstanceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_flow_instance_ProcessDefinitionId",
                table: "flow_instance",
                column: "ProcessDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_flow_instance_TenantId_ApplicantId_Status_StartedAt",
                table: "flow_instance",
                columns: new[] { "TenantId", "ApplicantId", "Status", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_flow_instance_TenantId_BusinessType_BusinessId_Attempt",
                table: "flow_instance",
                columns: new[] { "TenantId", "BusinessType", "BusinessId", "Attempt" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_expense_task_flow_instance_FlowInstanceId",
                table: "expense_task",
                column: "FlowInstanceId",
                principalTable: "flow_instance",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_flow_task_flow_instance_FlowInstanceId",
                table: "flow_task",
                column: "FlowInstanceId",
                principalTable: "flow_instance",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_expense_task_flow_instance_FlowInstanceId",
                table: "expense_task");

            migrationBuilder.DropForeignKey(
                name: "FK_flow_task_flow_instance_FlowInstanceId",
                table: "flow_task");

            migrationBuilder.DropTable(
                name: "flow_action");

            migrationBuilder.DropTable(
                name: "flow_instance");

            migrationBuilder.DropIndex(
                name: "IX_flow_task_FlowInstanceId",
                table: "flow_task");

            migrationBuilder.DropIndex(
                name: "IX_expense_task_FlowInstanceId",
                table: "expense_task");

            migrationBuilder.DropColumn(
                name: "CurrentFlowInstanceId",
                table: "leave_request");

            migrationBuilder.DropColumn(
                name: "FlowInstanceId",
                table: "flow_task");

            migrationBuilder.DropColumn(
                name: "FlowInstanceId",
                table: "expense_task");

            migrationBuilder.DropColumn(
                name: "CurrentFlowInstanceId",
                table: "expense_claim");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowSla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowAutoSkip",
                table: "process_node",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EscalateAfterHours",
                table: "process_node",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<string>(
                name: "EscalationTarget",
                table: "process_node",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "DIRECT_MANAGER");

            migrationBuilder.AddColumn<int>(
                name: "HandlingHours",
                table: "process_node",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<string>(
                name: "MissingAssigneeAction",
                table: "process_node",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "BLOCK");

            migrationBuilder.AddColumn<int>(
                name: "ReminderBeforeHours",
                table: "process_node",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.CreateTable(
                name: "flow_task_sla",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    FlowInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    AssigneeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssigneeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HandlingHours = table.Column<int>(type: "integer", nullable: false),
                    ReminderBeforeHours = table.Column<int>(type: "integer", nullable: false),
                    EscalateAfterHours = table.Column<int>(type: "integer", nullable: false),
                    EscalationTarget = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AllowAutoSkip = table.Column<bool>(type: "boolean", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_task_sla", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flow_task_sla_flow_instance_FlowInstanceId",
                        column: x => x.FlowInstanceId,
                        principalTable: "flow_instance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flow_sla_alert_delivery",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    FlowTaskSlaId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AlertType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_sla_alert_delivery", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flow_sla_alert_delivery_flow_task_sla_FlowTaskSlaId",
                        column: x => x.FlowTaskSlaId,
                        principalTable: "flow_task_sla",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flow_sla_alert_delivery_FlowTaskSlaId",
                table: "flow_sla_alert_delivery",
                column: "FlowTaskSlaId");

            migrationBuilder.CreateIndex(
                name: "IX_flow_sla_alert_delivery_TenantId_TaskId_RecipientId_AlertTy~",
                table: "flow_sla_alert_delivery",
                columns: new[] { "TenantId", "TaskId", "RecipientId", "AlertType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_flow_task_sla_FlowInstanceId",
                table: "flow_task_sla",
                column: "FlowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_flow_task_sla_TenantId_CompletedAt_CancelledAt_DueAt",
                table: "flow_task_sla",
                columns: new[] { "TenantId", "CompletedAt", "CancelledAt", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_flow_task_sla_TenantId_TaskId",
                table: "flow_task_sla",
                columns: new[] { "TenantId", "TaskId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flow_sla_alert_delivery");

            migrationBuilder.DropTable(
                name: "flow_task_sla");

            migrationBuilder.DropColumn(
                name: "AllowAutoSkip",
                table: "process_node");

            migrationBuilder.DropColumn(
                name: "EscalateAfterHours",
                table: "process_node");

            migrationBuilder.DropColumn(
                name: "EscalationTarget",
                table: "process_node");

            migrationBuilder.DropColumn(
                name: "HandlingHours",
                table: "process_node");

            migrationBuilder.DropColumn(
                name: "MissingAssigneeAction",
                table: "process_node");

            migrationBuilder.DropColumn(
                name: "ReminderBeforeHours",
                table: "process_node");
        }
    }
}

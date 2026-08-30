using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTravelManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "travel_request",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApplicantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApplicantName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DepartmentName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: false),
                    EstimatedBudget = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    ItineraryJson = table.Column<string>(type: "jsonb", nullable: false),
                    CompanionIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ProcessDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessDefinitionCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProcessDefinitionVersion = table.Column<int>(type: "integer", nullable: true),
                    CurrentFlowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_travel_request", x => x.Id);
                    table.ForeignKey(
                        name: "FK_travel_request_process_definition_ProcessDefinitionId",
                        column: x => x.ProcessDefinitionId,
                        principalTable: "process_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "travel_task",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    TravelRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssigneeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssigneeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalAssigneeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OriginalAssigneeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DelegationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_travel_task", x => x.Id);
                    table.ForeignKey(
                        name: "FK_travel_task_flow_delegation_DelegationId",
                        column: x => x.DelegationId,
                        principalTable: "flow_delegation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_travel_task_flow_instance_FlowInstanceId",
                        column: x => x.FlowInstanceId,
                        principalTable: "flow_instance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_travel_task_travel_request_TravelRequestId",
                        column: x => x.TravelRequestId,
                        principalTable: "travel_request",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_travel_request_ProcessDefinitionId",
                table: "travel_request",
                column: "ProcessDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_travel_request_TenantId_ApplicantId_Status_StartDate",
                table: "travel_request",
                columns: new[] { "TenantId", "ApplicantId", "Status", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_travel_request_TenantId_Number",
                table: "travel_request",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_travel_task_DelegationId",
                table: "travel_task",
                column: "DelegationId");

            migrationBuilder.CreateIndex(
                name: "IX_travel_task_FlowInstanceId",
                table: "travel_task",
                column: "FlowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_travel_task_TenantId_AssigneeId_Status_Sequence",
                table: "travel_task",
                columns: new[] { "TenantId", "AssigneeId", "Status", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_travel_task_TravelRequestId",
                table: "travel_task",
                column: "TravelRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "travel_task");

            migrationBuilder.DropTable(
                name: "travel_request");
        }
    }
}

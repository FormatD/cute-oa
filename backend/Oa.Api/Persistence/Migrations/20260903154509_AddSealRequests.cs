using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSealRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "seal_request",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApplicantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApplicantName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DepartmentName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DocumentCategory = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DocumentName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SealType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Copies = table.Column<int>(type: "integer", nullable: false),
                    IsOut = table.Column<bool>(type: "boolean", nullable: false),
                    OutStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OutEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OutCustodian = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsDemo = table.Column<bool>(type: "boolean", nullable: false),
                    ProcessDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessDefinitionCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProcessDefinitionVersion = table.Column<int>(type: "integer", nullable: true),
                    CurrentFlowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seal_request", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seal_request_process_definition_ProcessDefinitionId",
                        column: x => x.ProcessDefinitionId,
                        principalTable: "process_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "seal_execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    SealRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OperatorName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seal_execution", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seal_execution_seal_request_SealRequestId",
                        column: x => x.SealRequestId,
                        principalTable: "seal_request",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seal_return",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    SealRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SealCondition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReceiverName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seal_return", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seal_return_seal_request_SealRequestId",
                        column: x => x.SealRequestId,
                        principalTable: "seal_request",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seal_task",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    SealRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssigneeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssigneeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalAssigneeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OriginalAssigneeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DelegationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seal_task", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seal_task_flow_delegation_DelegationId",
                        column: x => x.DelegationId,
                        principalTable: "flow_delegation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_seal_task_flow_instance_FlowInstanceId",
                        column: x => x.FlowInstanceId,
                        principalTable: "flow_instance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_seal_task_seal_request_SealRequestId",
                        column: x => x.SealRequestId,
                        principalTable: "seal_request",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_seal_execution_SealRequestId",
                table: "seal_execution",
                column: "SealRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seal_request_ProcessDefinitionId",
                table: "seal_request",
                column: "ProcessDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_seal_request_TenantId_ApplicantId_Status_CreatedAt",
                table: "seal_request",
                columns: new[] { "TenantId", "ApplicantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_seal_request_TenantId_Number",
                table: "seal_request",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seal_request_TenantId_Status_CreatedAt",
                table: "seal_request",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_seal_return_SealRequestId",
                table: "seal_return",
                column: "SealRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seal_task_DelegationId",
                table: "seal_task",
                column: "DelegationId");

            migrationBuilder.CreateIndex(
                name: "IX_seal_task_FlowInstanceId",
                table: "seal_task",
                column: "FlowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_seal_task_SealRequestId",
                table: "seal_task",
                column: "SealRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_seal_task_TenantId_AssigneeId_Status_Sequence",
                table: "seal_task",
                columns: new[] { "TenantId", "AssigneeId", "Status", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "seal_execution");

            migrationBuilder.DropTable(
                name: "seal_return");

            migrationBuilder.DropTable(
                name: "seal_task");

            migrationBuilder.DropTable(
                name: "seal_request");
        }
    }
}

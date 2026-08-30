using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    public partial class AddProcessDefinitions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "process_definition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BusinessType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_process_definition", x => x.Id));

            migrationBuilder.CreateTable(
                name: "process_rule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    MaxValue = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_rule", x => x.Id);
                    table.ForeignKey("FK_process_rule_process_definition_ProcessDefinitionId", x => x.ProcessDefinitionId, "process_definition", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "process_node",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    AssigneeKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_node", x => x.Id);
                    table.ForeignKey("FK_process_node_process_rule_ProcessRuleId", x => x.ProcessRuleId, "process_rule", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<Guid>(name: "ProcessDefinitionId", table: "leave_request", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ProcessDefinitionCode", table: "leave_request", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<int>(name: "ProcessDefinitionVersion", table: "leave_request", type: "integer", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "ProcessDefinitionId", table: "expense_claim", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ProcessDefinitionCode", table: "expense_claim", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<int>(name: "ProcessDefinitionVersion", table: "expense_claim", type: "integer", nullable: true);

            migrationBuilder.CreateIndex(name: "IX_process_definition_TenantId_BusinessType_Status", table: "process_definition", columns: new[] { "TenantId", "BusinessType", "Status" });
            migrationBuilder.CreateIndex(name: "IX_process_definition_TenantId_Code_Version", table: "process_definition", columns: new[] { "TenantId", "Code", "Version" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_process_rule_ProcessDefinitionId_Sequence", table: "process_rule", columns: new[] { "ProcessDefinitionId", "Sequence" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_process_node_ProcessRuleId_Sequence", table: "process_node", columns: new[] { "ProcessRuleId", "Sequence" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_leave_request_ProcessDefinitionId", table: "leave_request", column: "ProcessDefinitionId");
            migrationBuilder.CreateIndex(name: "IX_expense_claim_ProcessDefinitionId", table: "expense_claim", column: "ProcessDefinitionId");

            migrationBuilder.AddForeignKey(name: "FK_leave_request_process_definition_ProcessDefinitionId", table: "leave_request", column: "ProcessDefinitionId", principalTable: "process_definition", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_expense_claim_process_definition_ProcessDefinitionId", table: "expense_claim", column: "ProcessDefinitionId", principalTable: "process_definition", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_leave_request_process_definition_ProcessDefinitionId", table: "leave_request");
            migrationBuilder.DropForeignKey(name: "FK_expense_claim_process_definition_ProcessDefinitionId", table: "expense_claim");
            migrationBuilder.DropTable(name: "process_node");
            migrationBuilder.DropTable(name: "process_rule");
            migrationBuilder.DropTable(name: "process_definition");
            migrationBuilder.DropColumn(name: "ProcessDefinitionId", table: "leave_request");
            migrationBuilder.DropColumn(name: "ProcessDefinitionCode", table: "leave_request");
            migrationBuilder.DropColumn(name: "ProcessDefinitionVersion", table: "leave_request");
            migrationBuilder.DropColumn(name: "ProcessDefinitionId", table: "expense_claim");
            migrationBuilder.DropColumn(name: "ProcessDefinitionCode", table: "expense_claim");
            migrationBuilder.DropColumn(name: "ProcessDefinitionVersion", table: "expense_claim");
        }
    }
}

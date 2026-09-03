using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations;

[DbContext(typeof(OaDbContext))]
[Migration("20260903010000_AddPurchaseRequests")]
public sealed class AddPurchaseRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "purchase_request",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "text", nullable: false),
                Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ApplicantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ApplicantName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DepartmentName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Purpose = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                RequiredDate = table.Column<DateOnly>(type: "date", nullable: false),
                SuggestedSupplier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ItemsJson = table.Column<string>(type: "jsonb", nullable: false),
                ItemSearchText = table.Column<string>(type: "text", nullable: false),
                ItemCount = table.Column<int>(type: "integer", nullable: false),
                EstimatedTotal = table.Column<decimal>(type: "numeric(16,2)", precision: 16, scale: 2, nullable: false),
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
                table.PrimaryKey("PK_purchase_request", x => x.Id);
                table.ForeignKey("FK_purchase_request_process_definition_ProcessDefinitionId", x => x.ProcessDefinitionId, "process_definition", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "purchase_order",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "text", nullable: false),
                PurchaseRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                Supplier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                OrderNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ActualAmount = table.Column<decimal>(type: "numeric(16,2)", precision: 16, scale: 2, nullable: false),
                OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                ExpectedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: false),
                Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_purchase_order", x => x.Id);
                table.ForeignKey("FK_purchase_order_purchase_request_PurchaseRequestId", x => x.PurchaseRequestId, "purchase_request", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "purchase_receipt",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "text", nullable: false),
                PurchaseRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                ReceivedDate = table.Column<DateOnly>(type: "date", nullable: false),
                Result = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_purchase_receipt", x => x.Id);
                table.ForeignKey("FK_purchase_receipt_purchase_request_PurchaseRequestId", x => x.PurchaseRequestId, "purchase_request", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "purchase_task",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "text", nullable: false),
                PurchaseRequestId = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("PK_purchase_task", x => x.Id);
                table.ForeignKey("FK_purchase_task_flow_delegation_DelegationId", x => x.DelegationId, "flow_delegation", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_purchase_task_flow_instance_FlowInstanceId", x => x.FlowInstanceId, "flow_instance", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_purchase_task_purchase_request_PurchaseRequestId", x => x.PurchaseRequestId, "purchase_request", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_purchase_request_ProcessDefinitionId", "purchase_request", "ProcessDefinitionId");
        migrationBuilder.CreateIndex("IX_purchase_request_TenantId_ApplicantId_Status_CreatedAt", "purchase_request", new[] { "TenantId", "ApplicantId", "Status", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_purchase_request_TenantId_Number", "purchase_request", new[] { "TenantId", "Number" }, unique: true);
        migrationBuilder.CreateIndex("IX_purchase_request_TenantId_RequiredDate_Status", "purchase_request", new[] { "TenantId", "RequiredDate", "Status" });
        migrationBuilder.CreateIndex("IX_purchase_order_PurchaseRequestId", "purchase_order", "PurchaseRequestId", unique: true);
        migrationBuilder.CreateIndex("IX_purchase_order_TenantId_OrderNumber", "purchase_order", new[] { "TenantId", "OrderNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_purchase_receipt_PurchaseRequestId", "purchase_receipt", "PurchaseRequestId", unique: true);
        migrationBuilder.CreateIndex("IX_purchase_task_DelegationId", "purchase_task", "DelegationId");
        migrationBuilder.CreateIndex("IX_purchase_task_FlowInstanceId", "purchase_task", "FlowInstanceId");
        migrationBuilder.CreateIndex("IX_purchase_task_PurchaseRequestId", "purchase_task", "PurchaseRequestId");
        migrationBuilder.CreateIndex("IX_purchase_task_TenantId_AssigneeId_Status_Sequence", "purchase_task", new[] { "TenantId", "AssigneeId", "Status", "Sequence" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "purchase_order");
        migrationBuilder.DropTable(name: "purchase_receipt");
        migrationBuilder.DropTable(name: "purchase_task");
        migrationBuilder.DropTable(name: "purchase_request");
    }
}

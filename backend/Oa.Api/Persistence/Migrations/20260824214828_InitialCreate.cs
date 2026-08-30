using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expense_claim",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApplicantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApplicantName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DepartmentName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Project = table.Column<string>(type: "text", nullable: true),
                    PayeeAccountName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayeeAccount = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BankName = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_claim", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "leave_balance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LeaveType = table.Column<int>(type: "integer", nullable: false),
                    Entitled = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    Frozen = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    Used = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leave_balance", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "leave_request",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApplicantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApplicantName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartPeriod = table.Column<int>(type: "integer", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndPeriod = table.Column<int>(type: "integer", nullable: false),
                    Days = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leave_request", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "expense_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReceiptNumber = table.Column<string>(type: "text", nullable: true),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_expense_item_expense_claim_ExpenseClaimId",
                        column: x => x.ExpenseClaimId,
                        principalTable: "expense_claim",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expense_task",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    ExpenseClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssigneeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssigneeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_task", x => x.Id);
                    table.ForeignKey(
                        name: "FK_expense_task_expense_claim_ExpenseClaimId",
                        column: x => x.ExpenseClaimId,
                        principalTable: "expense_claim",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_record",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TransactionNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    ProofFile = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OperatorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_record", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_record_expense_claim_ExpenseClaimId",
                        column: x => x.ExpenseClaimId,
                        principalTable: "expense_claim",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flow_task",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    LeaveRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssigneeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssigneeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_task", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flow_task_leave_request_LeaveRequestId",
                        column: x => x.LeaveRequestId,
                        principalTable: "leave_request",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expense_claim_TenantId_ApplicantId_Status_CreatedAt",
                table: "expense_claim",
                columns: new[] { "TenantId", "ApplicantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_expense_claim_TenantId_Number",
                table: "expense_claim",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_expense_item_ExpenseClaimId",
                table: "expense_item",
                column: "ExpenseClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_task_ExpenseClaimId",
                table: "expense_task",
                column: "ExpenseClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_task_TenantId_AssigneeId_Status_Sequence",
                table: "expense_task",
                columns: new[] { "TenantId", "AssigneeId", "Status", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_flow_task_LeaveRequestId",
                table: "flow_task",
                column: "LeaveRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_flow_task_TenantId_AssigneeId_Status_Sequence",
                table: "flow_task",
                columns: new[] { "TenantId", "AssigneeId", "Status", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_leave_balance_TenantId_UserId_LeaveType",
                table: "leave_balance",
                columns: new[] { "TenantId", "UserId", "LeaveType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leave_request_TenantId_ApplicantId_Status_StartDate",
                table: "leave_request",
                columns: new[] { "TenantId", "ApplicantId", "Status", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_leave_request_TenantId_Number",
                table: "leave_request",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_record_ExpenseClaimId",
                table: "payment_record",
                column: "ExpenseClaimId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expense_item");

            migrationBuilder.DropTable(
                name: "expense_task");

            migrationBuilder.DropTable(
                name: "flow_task");

            migrationBuilder.DropTable(
                name: "leave_balance");

            migrationBuilder.DropTable(
                name: "payment_record");

            migrationBuilder.DropTable(
                name: "leave_request");

            migrationBuilder.DropTable(
                name: "expense_claim");
        }
    }
}

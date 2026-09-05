using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetAndPaymentClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BudgetPoolId",
                table: "purchase_request",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidTotalAmount",
                table: "purchase_request",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "purchase_request",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "UNPAID");

            migrationBuilder.AddColumn<decimal>(
                name: "PrepaymentLimitRate",
                table: "purchase_request",
                type: "numeric(6,4)",
                precision: 6,
                scale: 4,
                nullable: false,
                defaultValue: 0.50m);

            migrationBuilder.AddColumn<Guid>(
                name: "BudgetPoolId",
                table: "expense_claim",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceCount",
                table: "expense_claim",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidTotalAmount",
                table: "expense_claim",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "expense_claim",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "UNPAID");

            migrationBuilder.CreateTable(
                name: "budget",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DepartmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpenseCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProjectId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    CommittedAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    ActualAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "expense_invoice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpenseClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InvoiceCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InvoiceFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BillingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AmountWithoutTax = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    TaxRate = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    VerificationCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AttachmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_invoice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_expense_invoice_expense_claim_ExpenseClaimId",
                        column: x => x.ExpenseClaimId,
                        principalTable: "expense_claim",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_transaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BusinessType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    BatchTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayerAccount = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayeeName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayeeAccount = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayeeBank = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TransactionNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    FeeAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    ProofAttachmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OperatorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OperatorName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_transaction", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "budget_transaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OperatorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_transaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_budget_transaction_budget_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "budget",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_budget_TenantId_DepartmentId_ExpenseCategory_ProjectId_Year~",
                table: "budget",
                columns: new[] { "TenantId", "DepartmentId", "ExpenseCategory", "ProjectId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_budget_TenantId_DepartmentId_Year_Month_Status",
                table: "budget",
                columns: new[] { "TenantId", "DepartmentId", "Year", "Month", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_budget_transaction_BudgetId_CreatedAt",
                table: "budget_transaction",
                columns: new[] { "BudgetId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_budget_transaction_TenantId_BusinessType_BusinessId",
                table: "budget_transaction",
                columns: new[] { "TenantId", "BusinessType", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_expense_invoice_ExpenseClaimId",
                table: "expense_invoice",
                column: "ExpenseClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_invoice_TenantId_BillingDate_InvoiceType",
                table: "expense_invoice",
                columns: new[] { "TenantId", "BillingDate", "InvoiceType" });

            migrationBuilder.CreateIndex(
                name: "IX_expense_invoice_TenantId_InvoiceFingerprint_Status",
                table: "expense_invoice",
                columns: new[] { "TenantId", "InvoiceFingerprint", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_transaction_TenantId_BusinessType_BusinessId_Sequen~",
                table: "payment_transaction",
                columns: new[] { "TenantId", "BusinessType", "BusinessId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_transaction_TenantId_TransactionNumber",
                table: "payment_transaction",
                columns: new[] { "TenantId", "TransactionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budget_transaction");

            migrationBuilder.DropTable(
                name: "expense_invoice");

            migrationBuilder.DropTable(
                name: "payment_transaction");

            migrationBuilder.DropTable(
                name: "budget");

            migrationBuilder.DropColumn(
                name: "BudgetPoolId",
                table: "purchase_request");

            migrationBuilder.DropColumn(
                name: "PaidTotalAmount",
                table: "purchase_request");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "purchase_request");

            migrationBuilder.DropColumn(
                name: "PrepaymentLimitRate",
                table: "purchase_request");

            migrationBuilder.DropColumn(
                name: "BudgetPoolId",
                table: "expense_claim");

            migrationBuilder.DropColumn(
                name: "InvoiceCount",
                table: "expense_claim");

            migrationBuilder.DropColumn(
                name: "PaidTotalAmount",
                table: "expense_claim");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "expense_claim");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetAndPaymentConsistency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_transaction_TenantId_BusinessType_BusinessId_Sequen~",
                table: "payment_transaction");

            migrationBuilder.AddColumn<string>(
                name: "ActionKey",
                table: "budget_transaction",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_payment_transaction_sequence",
                table: "payment_transaction",
                columns: new[] { "TenantId", "BusinessType", "BusinessId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_expense_invoice_active_fingerprint",
                table: "expense_invoice",
                columns: new[] { "TenantId", "InvoiceFingerprint" },
                unique: true,
                filter: "\"Status\" IN ('COMMITTED', 'PAID')");

            migrationBuilder.CreateIndex(
                name: "UX_budget_transaction_action_key",
                table: "budget_transaction",
                columns: new[] { "TenantId", "ActionKey" },
                unique: true,
                filter: "\"ActionKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_payment_transaction_sequence",
                table: "payment_transaction");

            migrationBuilder.DropIndex(
                name: "UX_expense_invoice_active_fingerprint",
                table: "expense_invoice");

            migrationBuilder.DropIndex(
                name: "UX_budget_transaction_action_key",
                table: "budget_transaction");

            migrationBuilder.DropColumn(
                name: "ActionKey",
                table: "budget_transaction");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transaction_TenantId_BusinessType_BusinessId_Sequen~",
                table: "payment_transaction",
                columns: new[] { "TenantId", "BusinessType", "BusinessId", "Sequence" });
        }
    }
}

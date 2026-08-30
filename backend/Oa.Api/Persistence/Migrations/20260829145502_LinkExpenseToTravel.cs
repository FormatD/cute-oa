using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkExpenseToTravel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TravelRequestId",
                table: "expense_claim",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_expense_claim_TravelRequestId",
                table: "expense_claim",
                column: "TravelRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_expense_claim_travel_request_TravelRequestId",
                table: "expense_claim",
                column: "TravelRequestId",
                principalTable: "travel_request",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_expense_claim_travel_request_TravelRequestId",
                table: "expense_claim");

            migrationBuilder.DropIndex(
                name: "IX_expense_claim_TravelRequestId",
                table: "expense_claim");

            migrationBuilder.DropColumn(
                name: "TravelRequestId",
                table: "expense_claim");
        }
    }
}

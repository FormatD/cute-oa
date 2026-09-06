using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessConfigRuleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmployeeRank",
                table: "travel_request",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOverStandard",
                table: "travel_request",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OverStandardReason",
                table: "travel_request",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryCityTier",
                table: "travel_request",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardHotelDailyLimit",
                table: "travel_request",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardMealDailyAllowance",
                table: "travel_request",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "StandardTransportation",
                table: "travel_request",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustodianUserId",
                table: "seal_request",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxOutDays",
                table: "seal_request",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "RiskMetric",
                table: "seal_request",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "AcceptanceRoleOrAssignee",
                table: "purchase_request",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AmountTier",
                table: "purchase_request",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "purchase_request",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchaserUserId",
                table: "purchase_request",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresAcceptance",
                table: "purchase_request",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentType",
                table: "file_object",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "OTHER");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "announcement",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "COMPANY_NEWS");

            migrationBuilder.CreateTable(
                name: "comp_time_grant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GrantedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiredAt = table.Column<DateOnly>(type: "date", nullable: false),
                    Days = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    UsedDays = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    FrozenDays = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comp_time_grant", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_comp_time_grant_TenantId_UserId_ExpiredAt",
                table: "comp_time_grant",
                columns: new[] { "TenantId", "UserId", "ExpiredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comp_time_grant");

            migrationBuilder.DropColumn(
                name: "EmployeeRank",
                table: "travel_request");

            migrationBuilder.DropColumn(
                name: "IsOverStandard",
                table: "travel_request");

            migrationBuilder.DropColumn(
                name: "OverStandardReason",
                table: "travel_request");

            migrationBuilder.DropColumn(
                name: "PrimaryCityTier",
                table: "travel_request");

            migrationBuilder.DropColumn(
                name: "StandardHotelDailyLimit",
                table: "travel_request");

            migrationBuilder.DropColumn(
                name: "StandardMealDailyAllowance",
                table: "travel_request");

            migrationBuilder.DropColumn(
                name: "StandardTransportation",
                table: "travel_request");

            migrationBuilder.DropColumn(
                name: "CustodianUserId",
                table: "seal_request");

            migrationBuilder.DropColumn(
                name: "MaxOutDays",
                table: "seal_request");

            migrationBuilder.DropColumn(
                name: "RiskMetric",
                table: "seal_request");

            migrationBuilder.DropColumn(
                name: "AcceptanceRoleOrAssignee",
                table: "purchase_request");

            migrationBuilder.DropColumn(
                name: "AmountTier",
                table: "purchase_request");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "purchase_request");

            migrationBuilder.DropColumn(
                name: "PurchaserUserId",
                table: "purchase_request");

            migrationBuilder.DropColumn(
                name: "RequiresAcceptance",
                table: "purchase_request");

            migrationBuilder.DropColumn(
                name: "AttachmentType",
                table: "file_object");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "announcement");
        }
    }
}

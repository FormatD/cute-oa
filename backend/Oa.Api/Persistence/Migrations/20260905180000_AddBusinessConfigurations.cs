using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations;

[DbContext(typeof(OaDbContext))]
[Migration("20260905180000_AddBusinessConfigurations")]
public sealed class AddBusinessConfigurations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "business_configuration",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Domain = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Version = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ContentJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                UpdatedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PublishedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                PublishedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ConcurrencyVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_business_configuration", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_business_configuration_TenantId_Domain_Code_Version",
            table: "business_configuration",
            columns: ["TenantId", "Domain", "Code", "Version"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_business_configuration_TenantId_Domain_Code_Status_Effective~",
            table: "business_configuration",
            columns: ["TenantId", "Domain", "Code", "Status", "EffectiveFrom"]);

        migrationBuilder.CreateIndex(
            name: "IX_business_configuration_TenantId_CreatedAt",
            table: "business_configuration",
            columns: ["TenantId", "CreatedAt"]);

        // Add snapshot and version columns to leave_request
        migrationBuilder.AddColumn<Guid>(
            name: "ConfigVersionId",
            table: "leave_request",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "ConfigVersionNumber",
            table: "leave_request",
            type: "integer",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "ConfigSnapshotJson",
            table: "leave_request",
            type: "jsonb",
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ConfigResolvedAt",
            table: "leave_request",
            type: "timestamp with time zone",
            nullable: true);

        // Add snapshot and version columns to expense_claim
        migrationBuilder.AddColumn<Guid>(
            name: "ConfigVersionId",
            table: "expense_claim",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "ConfigVersionNumber",
            table: "expense_claim",
            type: "integer",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "ConfigSnapshotJson",
            table: "expense_claim",
            type: "jsonb",
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ConfigResolvedAt",
            table: "expense_claim",
            type: "timestamp with time zone",
            nullable: true);

        // Add snapshot and version columns to travel_request
        migrationBuilder.AddColumn<Guid>(
            name: "ConfigVersionId",
            table: "travel_request",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "ConfigVersionNumber",
            table: "travel_request",
            type: "integer",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "ConfigSnapshotJson",
            table: "travel_request",
            type: "jsonb",
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ConfigResolvedAt",
            table: "travel_request",
            type: "timestamp with time zone",
            nullable: true);

        // Add snapshot and version columns to purchase_request
        migrationBuilder.AddColumn<Guid>(
            name: "ConfigVersionId",
            table: "purchase_request",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "ConfigVersionNumber",
            table: "purchase_request",
            type: "integer",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "ConfigSnapshotJson",
            table: "purchase_request",
            type: "jsonb",
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ConfigResolvedAt",
            table: "purchase_request",
            type: "timestamp with time zone",
            nullable: true);

        // Add snapshot, version and risk level columns to seal_request
        migrationBuilder.AddColumn<string>(
            name: "RiskLevel",
            table: "seal_request",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "ConfigVersionId",
            table: "seal_request",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "ConfigVersionNumber",
            table: "seal_request",
            type: "integer",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "ConfigSnapshotJson",
            table: "seal_request",
            type: "jsonb",
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ConfigResolvedAt",
            table: "seal_request",
            type: "timestamp with time zone",
            nullable: true);

        // Foreign keys for reference integrity
        migrationBuilder.AddForeignKey(
            name: "FK_leave_request_business_configuration_ConfigVersionId",
            table: "leave_request",
            column: "ConfigVersionId",
            principalTable: "business_configuration",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_expense_claim_business_configuration_ConfigVersionId",
            table: "expense_claim",
            column: "ConfigVersionId",
            principalTable: "business_configuration",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_travel_request_business_configuration_ConfigVersionId",
            table: "travel_request",
            column: "ConfigVersionId",
            principalTable: "business_configuration",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_purchase_request_business_configuration_ConfigVersionId",
            table: "purchase_request",
            column: "ConfigVersionId",
            principalTable: "business_configuration",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_seal_request_business_configuration_ConfigVersionId",
            table: "seal_request",
            column: "ConfigVersionId",
            principalTable: "business_configuration",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_leave_request_business_configuration_ConfigVersionId",
            table: "leave_request");
        migrationBuilder.DropForeignKey(
            name: "FK_expense_claim_business_configuration_ConfigVersionId",
            table: "expense_claim");
        migrationBuilder.DropForeignKey(
            name: "FK_travel_request_business_configuration_ConfigVersionId",
            table: "travel_request");
        migrationBuilder.DropForeignKey(
            name: "FK_purchase_request_business_configuration_ConfigVersionId",
            table: "purchase_request");
        migrationBuilder.DropForeignKey(
            name: "FK_seal_request_business_configuration_ConfigVersionId",
            table: "seal_request");

        migrationBuilder.DropColumn(name: "ConfigVersionId", table: "leave_request");
        migrationBuilder.DropColumn(name: "ConfigVersionNumber", table: "leave_request");
        migrationBuilder.DropColumn(name: "ConfigSnapshotJson", table: "leave_request");
        migrationBuilder.DropColumn(name: "ConfigResolvedAt", table: "leave_request");

        migrationBuilder.DropColumn(name: "ConfigVersionId", table: "expense_claim");
        migrationBuilder.DropColumn(name: "ConfigVersionNumber", table: "expense_claim");
        migrationBuilder.DropColumn(name: "ConfigSnapshotJson", table: "expense_claim");
        migrationBuilder.DropColumn(name: "ConfigResolvedAt", table: "expense_claim");

        migrationBuilder.DropColumn(name: "ConfigVersionId", table: "travel_request");
        migrationBuilder.DropColumn(name: "ConfigVersionNumber", table: "travel_request");
        migrationBuilder.DropColumn(name: "ConfigSnapshotJson", table: "travel_request");
        migrationBuilder.DropColumn(name: "ConfigResolvedAt", table: "travel_request");

        migrationBuilder.DropColumn(name: "ConfigVersionId", table: "purchase_request");
        migrationBuilder.DropColumn(name: "ConfigVersionNumber", table: "purchase_request");
        migrationBuilder.DropColumn(name: "ConfigSnapshotJson", table: "purchase_request");
        migrationBuilder.DropColumn(name: "ConfigResolvedAt", table: "purchase_request");

        migrationBuilder.DropColumn(name: "RiskLevel", table: "seal_request");
        migrationBuilder.DropColumn(name: "ConfigVersionId", table: "seal_request");
        migrationBuilder.DropColumn(name: "ConfigVersionNumber", table: "seal_request");
        migrationBuilder.DropColumn(name: "ConfigSnapshotJson", table: "seal_request");
        migrationBuilder.DropColumn(name: "ConfigResolvedAt", table: "seal_request");

        migrationBuilder.DropTable(name: "business_configuration");
    }
}

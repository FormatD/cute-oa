using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations;

[DbContext(typeof(OaDbContext))]
[Migration("20260901020000_AddAnnualLeaveBalanceYears")]
public sealed class AddAnnualLeaveBalanceYears : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "BalanceYear", table: "leave_request", type: "integer", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "Adjustment", table: "leave_balance", type: "numeric(6,1)", precision: 6, scale: 1, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>(name: "StatutoryEntitled", table: "leave_balance", type: "numeric(6,1)", precision: 6, scale: 1, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "UpdatedAt", table: "leave_balance", type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP");
        migrationBuilder.AddColumn<int>(name: "Version", table: "leave_balance", type: "integer", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<int>(name: "Year", table: "leave_balance", type: "integer", nullable: false, defaultValue: 2026);

        migrationBuilder.Sql("UPDATE leave_balance SET \"StatutoryEntitled\" = \"Entitled\" WHERE \"StatutoryEntitled\" = 0");
        migrationBuilder.DropIndex(name: "IX_leave_balance_TenantId_UserId_LeaveType", table: "leave_balance");
        migrationBuilder.CreateIndex(name: "IX_leave_balance_TenantId_UserId_LeaveType_Year", table: "leave_balance", columns: new[] { "TenantId", "UserId", "LeaveType", "Year" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_leave_balance_TenantId_UserId_LeaveType_Year", table: "leave_balance");
        migrationBuilder.DropColumn(name: "BalanceYear", table: "leave_request");
        migrationBuilder.DropColumn(name: "Adjustment", table: "leave_balance");
        migrationBuilder.DropColumn(name: "StatutoryEntitled", table: "leave_balance");
        migrationBuilder.DropColumn(name: "UpdatedAt", table: "leave_balance");
        migrationBuilder.DropColumn(name: "Version", table: "leave_balance");
        migrationBuilder.DropColumn(name: "Year", table: "leave_balance");
        migrationBuilder.CreateIndex(name: "IX_leave_balance_TenantId_UserId_LeaveType", table: "leave_balance", columns: new[] { "TenantId", "UserId", "LeaveType" }, unique: true);
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations;

[DbContext(typeof(OaDbContext))]
[Migration("20260830040000_AddAttendanceMonthLock")]
public sealed class AddAttendanceMonthLock : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "attendance_month_lock",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Month = table.Column<DateOnly>(type: "date", nullable: false),
                IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                LockReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                LockedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                LockedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                LockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UnlockedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                UnlockedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                UnlockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UnlockReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_attendance_month_lock", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_attendance_month_lock_TenantId_IsLocked_Month",
            table: "attendance_month_lock",
            columns: new[] { "TenantId", "IsLocked", "Month" });

        migrationBuilder.CreateIndex(
            name: "IX_attendance_month_lock_TenantId_Month",
            table: "attendance_month_lock",
            columns: new[] { "TenantId", "Month" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "attendance_month_lock");
    }
}

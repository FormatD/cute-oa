using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations;

[DbContext(typeof(OaDbContext))]
[Migration("20260902020000_AddAttendanceMonthSnapshots")]
public sealed class AddAttendanceMonthSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "attendance_month_snapshot",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Month = table.Column<DateOnly>(type: "date", nullable: false),
                Sequence = table.Column<int>(type: "integer", nullable: false),
                SnapshotJson = table.Column<string>(type: "text", nullable: false),
                SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RowCount = table.Column<int>(type: "integer", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LockReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_attendance_month_snapshot", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_attendance_month_snapshot_TenantId_Month_CreatedAt",
            table: "attendance_month_snapshot",
            columns: new[] { "TenantId", "Month", "CreatedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_attendance_month_snapshot_TenantId_Month_Sequence",
            table: "attendance_month_snapshot",
            columns: new[] { "TenantId", "Month", "Sequence" },
            unique: true);

        migrationBuilder.Sql("""
            WITH employee_summary AS (
                SELECT l."TenantId", l."Month", l."LockedBy", l."LockedByName", l."LockedAt", l."LockReason",
                       r."UserId", max(r."EmployeeName") AS "EmployeeName", max(r."DepartmentName") AS "DepartmentName",
                       count(*) FILTER (WHERE r."Status" <> 'REST_DAY')::int AS "ScheduledDays",
                       count(*) FILTER (WHERE r."CheckInAt" IS NOT NULL OR r."CheckOutAt" IS NOT NULL)::int AS "AttendedDays",
                       count(*) FILTER (WHERE r."Status" = 'NORMAL')::int AS "NormalDays",
                       count(*) FILTER (WHERE r."Status" IN ('LATE', 'LATE_AND_EARLY'))::int AS "LateDays",
                       count(*) FILTER (WHERE r."Status" IN ('EARLY_LEAVE', 'LATE_AND_EARLY'))::int AS "EarlyLeaveDays",
                       count(*) FILTER (WHERE r."Status" = 'MISSING_PUNCH')::int AS "MissingPunchDays",
                       count(*) FILTER (WHERE r."Status" = 'ABSENT')::int AS "AbsentDays",
                       count(*) FILTER (WHERE r."Status" = 'LEAVE')::int AS "LeaveDays",
                       count(*) FILTER (WHERE r."Status" = 'CORRECTED')::int AS "CorrectedDays",
                       0 AS "PendingAppeals", coalesce(sum(r."WorkedMinutes"), 0)::int AS "WorkedMinutes"
                FROM attendance_month_lock l
                JOIN attendance_record r ON r."TenantId" = l."TenantId" AND r."WorkDate" >= l."Month" AND r."WorkDate" < l."Month" + INTERVAL '1 month'
                WHERE l."IsLocked" = TRUE
                GROUP BY l."TenantId", l."Month", l."LockedBy", l."LockedByName", l."LockedAt", l."LockReason", r."UserId"
            ), payload AS (
                SELECT "TenantId", "Month", "LockedBy", "LockedByName", "LockedAt", "LockReason",
                       jsonb_agg(jsonb_build_object(
                           'UserId', "UserId", 'EmployeeName', "EmployeeName", 'DepartmentName', "DepartmentName", 'Month', "Month",
                           'ScheduledDays', "ScheduledDays", 'AttendedDays', "AttendedDays", 'NormalDays', "NormalDays", 'LateDays', "LateDays",
                           'EarlyLeaveDays', "EarlyLeaveDays", 'MissingPunchDays', "MissingPunchDays", 'AbsentDays', "AbsentDays",
                           'LeaveDays', "LeaveDays", 'CorrectedDays', "CorrectedDays", 'PendingAppeals', "PendingAppeals", 'WorkedMinutes', "WorkedMinutes"
                       ) ORDER BY "DepartmentName", "EmployeeName", "UserId")::text AS snapshot_json,
                       count(*)::int AS row_count
                FROM employee_summary
                GROUP BY "TenantId", "Month", "LockedBy", "LockedByName", "LockedAt", "LockReason"
            )
            INSERT INTO attendance_month_snapshot
                ("Id", "TenantId", "Month", "Sequence", "SnapshotJson", "SnapshotHash", "RowCount", "CreatedBy", "CreatedByName", "LockReason", "CreatedAt")
            SELECT gen_random_uuid(), "TenantId", "Month", 1, snapshot_json,
                   encode(sha256(convert_to(snapshot_json, 'UTF8')), 'hex'), row_count,
                   coalesce("LockedBy", 'system'), coalesce("LockedByName", '历史迁移'), coalesce("LockReason", '历史封账快照迁移'), coalesce("LockedAt", now())
            FROM payload;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "attendance_month_snapshot");
}

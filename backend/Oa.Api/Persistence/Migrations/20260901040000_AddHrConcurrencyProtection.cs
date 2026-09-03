using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations;

[DbContext(typeof(OaDbContext))]
[Migration("20260901040000_AddHrConcurrencyProtection")]
public sealed class AddHrConcurrencyProtection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "UX_attendance_appeal_pending_record",
            table: "attendance_appeal",
            column: "AttendanceRecordId",
            unique: true,
            filter: "\"Status\" = 'PENDING'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "UX_attendance_appeal_pending_record", table: "attendance_appeal");
    }
}

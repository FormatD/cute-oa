using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance_record",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmployeeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DepartmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DepartmentName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ShiftName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ScheduledStart = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ScheduledEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    CheckInAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CheckOutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OriginalStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    WorkedMinutes = table.Column<int>(type: "integer", nullable: false),
                    LateMinutes = table.Column<int>(type: "integer", nullable: false),
                    EarlyLeaveMinutes = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_record", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attendance_record_oa_user_UserId",
                        column: x => x.UserId,
                        principalTable: "oa_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendance_shift",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WorkStart = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    WorkEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    BreakMinutes = table.Column<int>(type: "integer", nullable: false),
                    LateToleranceMinutes = table.Column<int>(type: "integer", nullable: false),
                    EarlyLeaveToleranceMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_shift", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "attendance_appeal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttendanceRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SubmittedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmittedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReviewedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReviewComment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_appeal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attendance_appeal_attendance_record_AttendanceRecordId",
                        column: x => x.AttendanceRecordId,
                        principalTable: "attendance_record",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_appeal_AttendanceRecordId",
                table: "attendance_appeal",
                column: "AttendanceRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_appeal_TenantId_Status_SubmittedAt",
                table: "attendance_appeal",
                columns: new[] { "TenantId", "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_record_TenantId_UserId_WorkDate",
                table: "attendance_record",
                columns: new[] { "TenantId", "UserId", "WorkDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_record_TenantId_WorkDate_Status_UserId",
                table: "attendance_record",
                columns: new[] { "TenantId", "WorkDate", "Status", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_record_UserId",
                table: "attendance_record",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_shift_TenantId_Code",
                table: "attendance_shift",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_shift_TenantId_IsDefault_IsEnabled",
                table: "attendance_shift",
                columns: new[] { "TenantId", "IsDefault", "IsEnabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_appeal");

            migrationBuilder.DropTable(
                name: "attendance_shift");

            migrationBuilder.DropTable(
                name: "attendance_record");
        }
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations;

[DbContext(typeof(OaDbContext))]
[Migration("20260830152603_AddPersonnelCases")]
public sealed class AddPersonnelCases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "personnel_case",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                EmployeeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DepartmentName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                OwnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                OwnerName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Version = table.Column<int>(type: "integer", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CompletedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletionComment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CancelledBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CancelledByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_personnel_case", x => x.Id);
                table.ForeignKey(
                    name: "FK_personnel_case_oa_user_UserId",
                    column: x => x.UserId,
                    principalTable: "oa_user",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "personnel_case_task",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PersonnelCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Required = table.Column<bool>(type: "boolean", nullable: false),
                AssigneeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AssigneeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CompletionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CompletedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CompletedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_personnel_case_task", x => x.Id);
                table.ForeignKey(
                    name: "FK_personnel_case_task_personnel_case_PersonnelCaseId",
                    column: x => x.PersonnelCaseId,
                    principalTable: "personnel_case",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_personnel_case_UserId", table: "personnel_case", column: "UserId");
        migrationBuilder.CreateIndex(name: "IX_personnel_case_TenantId_Number", table: "personnel_case", columns: new[] { "TenantId", "Number" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_personnel_case_TenantId_Status_EffectiveDate_UpdatedAt", table: "personnel_case", columns: new[] { "TenantId", "Status", "EffectiveDate", "UpdatedAt" });
        migrationBuilder.CreateIndex(name: "IX_personnel_case_TenantId_UserId_Type_EffectiveDate", table: "personnel_case", columns: new[] { "TenantId", "UserId", "Type", "EffectiveDate" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_personnel_case_task_PersonnelCaseId_Code", table: "personnel_case_task", columns: new[] { "PersonnelCaseId", "Code" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_personnel_case_task_TenantId_AssigneeId_Status_DueDate", table: "personnel_case_task", columns: new[] { "TenantId", "AssigneeId", "Status", "DueDate" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "personnel_case_task");
        migrationBuilder.DropTable(name: "personnel_case");
    }
}

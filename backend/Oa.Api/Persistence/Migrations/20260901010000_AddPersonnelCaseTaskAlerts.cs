using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations;

[DbContext(typeof(OaDbContext))]
[Migration("20260901010000_AddPersonnelCaseTaskAlerts")]
public sealed class AddPersonnelCaseTaskAlerts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "personnel_case_alert_delivery",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PersonnelCaseTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AlertType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_personnel_case_alert_delivery", x => x.Id);
                table.ForeignKey(
                    name: "FK_personnel_case_alert_delivery_oa_user_RecipientId",
                    column: x => x.RecipientId,
                    principalTable: "oa_user",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_personnel_case_alert_delivery_personnel_case_task_PersonnelCaseTaskId",
                    column: x => x.PersonnelCaseTaskId,
                    principalTable: "personnel_case_task",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_personnel_case_alert_delivery_RecipientId",
            table: "personnel_case_alert_delivery",
            column: "RecipientId");

        migrationBuilder.CreateIndex(
            name: "UX_personnel_case_alert_delivery_task_recipient_type",
            table: "personnel_case_alert_delivery",
            columns: new[] { "PersonnelCaseTaskId", "RecipientId", "AlertType" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_personnel_case_alert_delivery_TenantId_DeliveredAt",
            table: "personnel_case_alert_delivery",
            columns: new[] { "TenantId", "DeliveredAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "personnel_case_alert_delivery");
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonnelProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personnel_event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ChangedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChangedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personnel_event", x => x.Id);
                    table.ForeignKey(
                        name: "FK_personnel_event_oa_user_UserId",
                        column: x => x.UserId,
                        principalTable: "oa_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "personnel_profile",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmployeeNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WorkEmail = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    WorkPhone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WorkLocation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EmploymentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PersonnelStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    HireDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProbationEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RegularizedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CumulativeWorkStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DepartureDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DepartureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personnel_profile", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_personnel_profile_oa_user_UserId",
                        column: x => x.UserId,
                        principalTable: "oa_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_personnel_event_TenantId_UserId_EffectiveDate_CreatedAt",
                table: "personnel_event",
                columns: new[] { "TenantId", "UserId", "EffectiveDate", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_personnel_event_UserId",
                table: "personnel_event",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_personnel_profile_TenantId_EmployeeNumber",
                table: "personnel_profile",
                columns: new[] { "TenantId", "EmployeeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_personnel_profile_TenantId_PersonnelStatus_HireDate",
                table: "personnel_profile",
                columns: new[] { "TenantId", "PersonnelStatus", "HireDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "personnel_event");

            migrationBuilder.DropTable(
                name: "personnel_profile");
        }
    }
}

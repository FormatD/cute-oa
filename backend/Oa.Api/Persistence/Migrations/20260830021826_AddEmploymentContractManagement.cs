using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmploymentContractManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employment_contract",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContractNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmployeeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DepartmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DepartmentName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PositionName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WorkLocation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ContractType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SignedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ProbationStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ProbationEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ProjectDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RenewalOfId = table.Column<Guid>(type: "uuid", nullable: true),
                    RenewalSequence = table.Column<int>(type: "integer", nullable: false),
                    OpenEndedReviewRequired = table.Column<bool>(type: "boolean", nullable: false),
                    OpenEndedReviewReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TerminationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TerminationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDemo = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employment_contract", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employment_contract_employment_contract_RenewalOfId",
                        column: x => x.RenewalOfId,
                        principalTable: "employment_contract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employment_contract_oa_user_UserId",
                        column: x => x.UserId,
                        principalTable: "oa_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_alert_ack",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThresholdDays = table.Column<int>(type: "integer", nullable: false),
                    AcknowledgedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcknowledgedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_alert_ack", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contract_alert_ack_employment_contract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "employment_contract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employment_contract_event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ChangedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChangedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employment_contract_event", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employment_contract_event_employment_contract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "employment_contract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contract_alert_ack_ContractId_ThresholdDays",
                table: "contract_alert_ack",
                columns: new[] { "ContractId", "ThresholdDays" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contract_alert_ack_TenantId_AcknowledgedAt",
                table: "contract_alert_ack",
                columns: new[] { "TenantId", "AcknowledgedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_employment_contract_RenewalOfId",
                table: "employment_contract",
                column: "RenewalOfId");

            migrationBuilder.CreateIndex(
                name: "IX_employment_contract_TenantId_ContractNumber",
                table: "employment_contract",
                columns: new[] { "TenantId", "ContractNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employment_contract_TenantId_UserId_Status_EndDate",
                table: "employment_contract",
                columns: new[] { "TenantId", "UserId", "Status", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_employment_contract_UserId",
                table: "employment_contract",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_employment_contract_event_ContractId",
                table: "employment_contract_event",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_employment_contract_event_TenantId_ContractId_CreatedAt",
                table: "employment_contract_event",
                columns: new[] { "TenantId", "ContractId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_alert_ack");

            migrationBuilder.DropTable(
                name: "employment_contract_event");

            migrationBuilder.DropTable(
                name: "employment_contract");
        }
    }
}

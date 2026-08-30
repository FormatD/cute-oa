using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractAlertDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contract_alert_delivery",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThresholdDays = table.Column<int>(type: "integer", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_alert_delivery", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contract_alert_delivery_employment_contract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "employment_contract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contract_alert_delivery_oa_user_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "oa_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contract_alert_delivery_ContractId_RecipientId_ThresholdDays",
                table: "contract_alert_delivery",
                columns: new[] { "ContractId", "RecipientId", "ThresholdDays" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contract_alert_delivery_RecipientId",
                table: "contract_alert_delivery",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_contract_alert_delivery_TenantId_DeliveredAt",
                table: "contract_alert_delivery",
                columns: new[] { "TenantId", "DeliveredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_alert_delivery");
        }
    }
}

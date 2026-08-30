using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowCopyRecipients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flow_copy_recipient",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    BusinessType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApplicantName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_copy_recipient", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flow_copy_recipient_TenantId_BusinessType_BusinessId_Recipi~",
                table: "flow_copy_recipient",
                columns: new[] { "TenantId", "BusinessType", "BusinessId", "RecipientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_flow_copy_recipient_TenantId_RecipientId_ReadAt_AvailableAt",
                table: "flow_copy_recipient",
                columns: new[] { "TenantId", "RecipientId", "ReadAt", "AvailableAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flow_copy_recipient");
        }
    }
}

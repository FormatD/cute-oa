using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_category",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DepartmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_category", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_document",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    TagsJson = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsMustRead = table.Column<bool>(type: "boolean", nullable: false),
                    DepartmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    DownloadCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublishedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDemo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_document", x => x.Id);
                    table.ForeignKey(
                        name: "FK_knowledge_document_document_category_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "document_category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_acknowledgement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersion = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DepartmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DepartmentName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_acknowledgement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_acknowledgement_knowledge_document_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "knowledge_document",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_version",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ChangeNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublishedByName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_version", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_version_knowledge_document_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "knowledge_document",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_acknowledgement_DocumentId",
                table: "document_acknowledgement",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_document_acknowledgement_TenantId_DocumentId_DocumentVersio~",
                table: "document_acknowledgement",
                columns: new[] { "TenantId", "DocumentId", "DocumentVersion", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_acknowledgement_TenantId_UserId_AcknowledgedAt",
                table: "document_acknowledgement",
                columns: new[] { "TenantId", "UserId", "AcknowledgedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_document_category_TenantId_Code",
                table: "document_category",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_category_TenantId_SortOrder",
                table: "document_category",
                columns: new[] { "TenantId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_document_version_DocumentId",
                table: "document_version",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_document_version_TenantId_DocumentId_Version",
                table: "document_version",
                columns: new[] { "TenantId", "DocumentId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_document_CategoryId",
                table: "knowledge_document",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_document_TenantId_CategoryId_Status",
                table: "knowledge_document",
                columns: new[] { "TenantId", "CategoryId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_document_TenantId_Number",
                table: "knowledge_document",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_document_TenantId_Status_EffectiveDate",
                table: "knowledge_document",
                columns: new[] { "TenantId", "Status", "EffectiveDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_acknowledgement");

            migrationBuilder.DropTable(
                name: "document_version");

            migrationBuilder.DropTable(
                name: "knowledge_document");

            migrationBuilder.DropTable(
                name: "document_category");
        }
    }
}

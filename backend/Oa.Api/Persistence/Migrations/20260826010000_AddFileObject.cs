using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Oa.Api.Persistence;

#nullable disable

namespace Oa.Api.Persistence.Migrations;

[DbContext(typeof(OaDbContext))]
[Migration("20260826010000_AddFileObject")]
public partial class AddFileObject : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "file_object",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "text", nullable: false),
                OwnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                OriginalName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StoredName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Size = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_file_object", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_file_object_TenantId_OwnerId_CreatedAt", table: "file_object", columns: new[] { "TenantId", "OwnerId", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "file_object");
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations;

[DbContext(typeof(OaDbContext))]
[Migration("20260901030000_AddContractOverlapProtection")]
public sealed class AddContractOverlapProtection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist");
        migrationBuilder.Sql("""
            ALTER TABLE employment_contract
            ADD CONSTRAINT "EX_employment_contract_active_period"
            EXCLUDE USING gist (
                "TenantId" WITH =,
                "UserId" WITH =,
                daterange("StartDate", COALESCE("EndDate", 'infinity'::date), '[]') WITH &&
            )
            WHERE ("Status" = 'ACTIVE')
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE employment_contract
            DROP CONSTRAINT IF EXISTS "EX_employment_contract_active_period"
            """);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Oa.Api.Persistence;

public sealed class OaDbContextFactory : IDesignTimeDbContextFactory<OaDbContext>
{
    public OaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("OA_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5433;Database=oa;Username=oa;Password=oa_dev_password";
        var options = new DbContextOptionsBuilder<OaDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new OaDbContext(options);
    }
}

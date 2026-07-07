using BTCPayServer.Abstractions.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.Clink.Data;

public class ClinkDbContext : DbContext
{
    public ClinkDbContext(DbContextOptions<ClinkDbContext> options) : base(options) { }
}

public class ClinkDbContextFactory : BaseDbContextFactory<ClinkDbContext>
{
    public ClinkDbContextFactory(IOptions<DatabaseOptions> options) : base(options, "BTCPayServer.Plugins.Clink") { }

    public override ClinkDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<ClinkDbContext>();
        ConfigureBuilder(builder);
        return new ClinkDbContext(builder.Options);
    }
}

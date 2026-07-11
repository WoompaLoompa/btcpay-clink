using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.Plugins.Clink.Data;

public class ClinkDbContext : DbContext
{
    public ClinkDbContext(DbContextOptions<ClinkDbContext> options) : base(options) { }
}

public class ClinkDbContextFactory : BaseDbContextFactory<ClinkDbContext>
{
    public ClinkDbContextFactory(IOptions<DatabaseOptions> options) : base(options, "BTCPayServer.Plugins.Clink") { }

    public override ClinkDbContext CreateContext(Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<ClinkDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new ClinkDbContext(builder.Options);
    }
}

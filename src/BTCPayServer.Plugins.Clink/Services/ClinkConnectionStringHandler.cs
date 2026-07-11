using BTCPayServer.Lightning;
using NBitcoin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Clink.Services;

public class ClinkConnectionStringHandler : ILightningConnectionStringHandler
{
    private readonly ClinkNostrBridge _bridge;
    private readonly NostrEventStore _store;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailNdebitStore _emailNdebitStore;

    public ClinkConnectionStringHandler(ClinkNostrBridge bridge, NostrEventStore store, ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory, EmailNdebitStore emailNdebitStore)
    {
        _bridge = bridge;
        _store = store;
        _loggerFactory = loggerFactory;
        _scopeFactory = scopeFactory;
        _emailNdebitStore = emailNdebitStore;
    }

    public ILightningClient? Create(string connectionString, Network network, out string? error)
    {
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "clink-noffer")
        {
            error = null;
            return null;
        }

        if (!kv.TryGetValue("noffer", out var noffer) || string.IsNullOrEmpty(noffer))
        {
            error = "The key 'noffer' is mandatory for clink-noffer connection strings";
            return null;
        }

        kv.TryGetValue("ndebit", out var ndebit);

        error = null;
        var logger = _loggerFactory.CreateLogger<ClinkLightningClient>();
        return new ClinkLightningClient(noffer, network, _bridge, _store, ndebit, logger, _scopeFactory, _emailNdebitStore);
    }
}

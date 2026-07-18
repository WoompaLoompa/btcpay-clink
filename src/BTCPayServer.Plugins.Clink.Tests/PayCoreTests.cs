using BTCPayServer.Lightning;
using BTCPayServer.Plugins.Clink.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BTCPayServer.Plugins.Clink.Tests;

public class PayCoreTests
{
    // Test PayCore guard: empty bolt11 returns error (not Ok)
    [Fact]
    public async Task PayCore_Empty_Bolt11_Returns_Error()
    {
        var (client, _) = CreateClient(ndebit: "ndebit1-test");
        var result = await client.Pay("", CancellationToken.None);
        Assert.Equal(PayResult.Error, result.Result);
    }

    // Test PayCore guard: non-lnbc strings returns error
    [Fact]
    public async Task PayCore_InvalidBolt11_Returns_Error()
    {
        var (client, _) = CreateClient(ndebit: "ndebit1-test");
        var result = await client.Pay("invalid", CancellationToken.None);
        Assert.Equal(PayResult.Error, result.Result);
    }

    // Test PayCore returns error when no ndebit configured (the critical fix)
    [Fact]
    public async Task PayCore_NoNdebit_Returns_Error_Not_Ok()
    {
        var (client, _) = CreateClient(ndebit: null);
        var result = await client.Pay("lnbc1valid", CancellationToken.None);
        Assert.Equal(PayResult.Error, result.Result);
        Assert.Contains("ndebit", result.ErrorDetail, StringComparison.OrdinalIgnoreCase);
    }

    private static (ClinkLightningClient client, InMemoryStoreRepository repo) CreateClient(string? ndebit)
    {
        var repo = new InMemoryStoreRepository();
        var bridge = new ClinkNostrBridge(Mock.Of<ILogger<ClinkNostrBridge>>());
        var store = new NostrEventStore(repo);
        var logger = Mock.Of<ILogger<ClinkLightningClient>>();
        var scopeFactory = Mock.Of<IServiceScopeFactory>();
        var emailStore = new EmailNdebitStore(repo);
        var client = new ClinkLightningClient("test-store", "noffer1test", NBitcoin.Network.Main, bridge, store, ndebit, null, logger, scopeFactory, emailStore);
        return (client, repo);
    }
}

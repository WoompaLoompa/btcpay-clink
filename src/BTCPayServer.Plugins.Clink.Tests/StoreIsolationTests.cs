using BTCPayServer.Plugins.Clink.Services;
using Xunit;

namespace BTCPayServer.Plugins.Clink.Tests;

public class StoreIsolationTests
{
    private readonly InMemoryStoreRepository _repo = new();
    private readonly NostrEventStore _storeA;
    private readonly NostrEventStore _storeB;
    private readonly NdebitRegistry _ndebitA;
    private readonly NdebitRegistry _ndebitB;
    private readonly EmailNdebitStore _emailA;
    private readonly EmailNdebitStore _emailB;

    public StoreIsolationTests()
    {
        _storeA = new NostrEventStore(_repo);
        _storeB = new NostrEventStore(_repo);
        _ndebitA = new NdebitRegistry(_repo);
        _ndebitB = new NdebitRegistry(_repo);
        _emailA = new EmailNdebitStore(_repo);
        _emailB = new EmailNdebitStore(_repo);
    }

    [Fact]
    public async Task NostrEventStore_StoreA_Invoice_Not_Visible_In_StoreB()
    {
        await _storeA.Store("store-1", "inv-1", new NostrEventData
        {
            EventId = "event-1",
            Bolt11 = "lnbc1",
            AmountSats = 1000
        });

        var result = await _storeB.GetByInvoiceId("store-2", "inv-1");
        Assert.Null(result);
    }

    [Fact]
    public async Task NostrEventStore_MarkPaid_Is_Isolated()
    {
        await _storeA.Store("store-1", "inv-1", new NostrEventData { EventId = "e1", Bolt11 = "lnbc1", AmountSats = 1000 });
        await _storeA.MarkPaid("store-1", "inv-1");

        var paidB = await _storeB.GetPaidAt("store-2", "inv-1");
        Assert.Null(paidB);
    }

    [Fact]
    public async Task NdebitRegistry_Store_Is_Isolated()
    {
        await _ndebitA.Store("store-1", "btcpay-inv-1", "ndebit1-abc");
        var fromB = await _ndebitB.GetByInvoiceId("store-2", "btcpay-inv-1");
        Assert.Null(fromB);
    }

    [Fact]
    public async Task EmailNdebitStore_Set_Is_Isolated()
    {
        await _emailA.Set("store-1", "user@example.com", "ndebit1-xyz");
        var fromB = await _emailB.Get("store-2", "user@example.com");
        Assert.Null(fromB);
    }

    [Fact]
    public async Task EmailNdebitStore_Email_Is_CaseInsensitive()
    {
        await _emailA.Set("store-1", "User@Example.COM", "ndebit1-test");
        var result = await _emailA.Get("store-1", "user@example.com");
        Assert.Equal("ndebit1-test", result);
    }
}

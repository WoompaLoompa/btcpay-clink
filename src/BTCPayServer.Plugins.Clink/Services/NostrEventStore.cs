using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Plugins.Clink.Models;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Clink.Services;

public class NostrEventStore
{
    private const string SettingKeyPrefix = "BTCPayServer.Plugins.Clink.NostrStore";

    private readonly IStoreRepository _storeRepository;

    public NostrEventStore(IStoreRepository storeRepository)
    {
        _storeRepository = storeRepository;
    }

    public async Task Store(string storeId, string invoiceId, NostrEventData data)
    {
        var snapshot = await LoadSnapshot(storeId);
        snapshot.Invoices[invoiceId] = data;
        snapshot.EventIdIndex[data.EventId] = invoiceId;
        await SaveSnapshot(storeId, snapshot);
    }

    public async Task LinkBtcpayInvoice(string storeId, string btcpayInvoiceId, string ourInvoiceId)
    {
        var snapshot = await LoadSnapshot(storeId);
        snapshot.BtcpayLink[btcpayInvoiceId] = ourInvoiceId;
        await SaveSnapshot(storeId, snapshot);
    }

    public async Task<string?> GetOurInvoiceIdByBtcpayInvoice(string storeId, string btcpayInvoiceId)
    {
        var snapshot = await LoadSnapshot(storeId);
        return snapshot.BtcpayLink.TryGetValue(btcpayInvoiceId, out var ourId) ? ourId : null;
    }

    public async Task<NostrEventData?> GetByBtcpayInvoiceId(string storeId, string btcpayInvoiceId)
    {
        var ourId = await GetOurInvoiceIdByBtcpayInvoice(storeId, btcpayInvoiceId);
        return ourId != null ? await GetByInvoiceId(storeId, ourId) : null;
    }

    public async Task<NostrEventData?> GetByInvoiceId(string storeId, string invoiceId)
    {
        var snapshot = await LoadSnapshot(storeId);
        return snapshot.Invoices.TryGetValue(invoiceId, out var data) ? data : null;
    }

    public async Task<string?> GetInvoiceIdByEventId(string storeId, string eventId)
    {
        var snapshot = await LoadSnapshot(storeId);
        return snapshot.EventIdIndex.TryGetValue(eventId, out var invoiceId) ? invoiceId : null;
    }

    public async Task MarkPaid(string storeId, string invoiceId)
    {
        var snapshot = await LoadSnapshot(storeId);
        snapshot.PaidAt[invoiceId] = DateTimeOffset.UtcNow;
        await SaveSnapshot(storeId, snapshot);
    }

    public async Task<DateTimeOffset?> GetPaidAt(string storeId, string invoiceId)
    {
        var snapshot = await LoadSnapshot(storeId);
        return snapshot.PaidAt.TryGetValue(invoiceId, out var paid) ? paid : null;
    }

    public async Task MarkPaidByBtcpayInvoice(string storeId, string btcpayInvoiceId)
    {
        var ourId = await GetOurInvoiceIdByBtcpayInvoice(storeId, btcpayInvoiceId);
        if (ourId != null)
            await MarkPaid(storeId, ourId);
    }

    public async Task Remove(string storeId, string invoiceId)
    {
        var snapshot = await LoadSnapshot(storeId);
        if (snapshot.Invoices.Remove(invoiceId, out var data))
            snapshot.EventIdIndex.Remove(data.EventId);
        snapshot.PaidAt.Remove(invoiceId);
        await SaveSnapshot(storeId, snapshot);
    }

    public async Task<List<KeyValuePair<string, NostrEventData>>> GetAll(string storeId)
    {
        var snapshot = await LoadSnapshot(storeId);
        return snapshot.Invoices.ToList();
    }

    private async Task<StoreSnapshot> LoadSnapshot(string storeId)
    {
        var data = await _storeRepository.GetSettingAsync<string>(storeId, SettingKeyPrefix);
        if (string.IsNullOrEmpty(data))
            return new StoreSnapshot();
        try
        {
            return JsonConvert.DeserializeObject<StoreSnapshot>(data) ?? new StoreSnapshot();
        }
        catch
        {
            return new StoreSnapshot();
        }
    }

    private async Task SaveSnapshot(string storeId, StoreSnapshot snapshot)
    {
        var json = JsonConvert.SerializeObject(snapshot);
        await _storeRepository.UpdateSetting(storeId, SettingKeyPrefix, json);
    }

    private class StoreSnapshot
    {
        public Dictionary<string, NostrEventData> Invoices { get; set; } = new();
        public Dictionary<string, string> EventIdIndex { get; set; } = new();
        public Dictionary<string, DateTimeOffset?> PaidAt { get; set; } = new();
        public Dictionary<string, string> BtcpayLink { get; set; } = new();
    }
}

public class NostrEventData
{
    public string EventId { get; set; } = "";
    public string Bolt11 { get; set; } = "";
    public long AmountSats { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public TimeSpan Expiry { get; set; }
    public string FromPub { get; set; } = "";
    public string PrivkeyHex { get; set; } = "";
}

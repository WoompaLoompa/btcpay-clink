using System.Collections.Concurrent;
using BTCPayServer.Plugins.Clink.Models;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Clink.Services;

public class NostrEventStore
{
    private static readonly string FilePath = Path.Combine(
        Path.GetDirectoryName(typeof(NostrEventStore).Assembly.Location) ?? "/tmp",
        "clink-nostr-store.json");

    private readonly ConcurrentDictionary<string, NostrEventData> _byInvoiceId = new();
    private readonly ConcurrentDictionary<string, string> _byEventId = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset?> _paidAt = new();
    private readonly ConcurrentDictionary<string, string> _byBtcpayInvoiceId = new();

    public NostrEventStore()
    {
        LoadFromFile();
    }

    public void Store(string invoiceId, NostrEventData data)
    {
        _byInvoiceId[invoiceId] = data;
        _byEventId[data.EventId] = invoiceId;
        SaveToFile();
    }

    public void LinkBtcpayInvoice(string btcpayInvoiceId, string ourInvoiceId)
    {
        _byBtcpayInvoiceId[btcpayInvoiceId] = ourInvoiceId;
        SaveToFile();
    }

    public string? GetOurInvoiceIdByBtcpayInvoice(string btcpayInvoiceId)
    {
        return _byBtcpayInvoiceId.TryGetValue(btcpayInvoiceId, out var ourId) ? ourId : null;
    }

    public NostrEventData? GetByBtcpayInvoiceId(string btcpayInvoiceId)
    {
        var ourId = GetOurInvoiceIdByBtcpayInvoice(btcpayInvoiceId);
        return ourId != null ? GetByInvoiceId(ourId) : null;
    }

    public NostrEventData? GetByInvoiceId(string invoiceId)
    {
        return _byInvoiceId.TryGetValue(invoiceId, out var data) ? data : null;
    }

    public string? GetInvoiceIdByEventId(string eventId)
    {
        return _byEventId.TryGetValue(eventId, out var invoiceId) ? invoiceId : null;
    }

    public void MarkPaid(string invoiceId)
    {
        _paidAt[invoiceId] = DateTimeOffset.UtcNow;
        SaveToFile();
    }

    public DateTimeOffset? GetPaidAt(string invoiceId)
    {
        return _paidAt.TryGetValue(invoiceId, out var paid) ? paid : null;
    }

    public void MarkPaidByBtcpayInvoice(string btcpayInvoiceId)
    {
        var ourId = GetOurInvoiceIdByBtcpayInvoice(btcpayInvoiceId);
        if (ourId != null)
            MarkPaid(ourId);
    }

    public void Remove(string invoiceId)
    {
        if (_byInvoiceId.TryRemove(invoiceId, out var data))
            _byEventId.TryRemove(data.EventId, out _);
        _paidAt.TryRemove(invoiceId, out _);
        SaveToFile();
    }

    public IEnumerable<KeyValuePair<string, NostrEventData>> GetAll()
    {
        return _byInvoiceId;
    }

    private class StoreSnapshot
    {
        public Dictionary<string, NostrEventData> Invoices { get; set; } = new();
        public Dictionary<string, string> EventIdIndex { get; set; } = new();
        public Dictionary<string, DateTimeOffset?> PaidAt { get; set; } = new();
        public Dictionary<string, string> BtcpayLink { get; set; } = new();
    }

    private void SaveToFile()
    {
        try
        {
            var snapshot = new StoreSnapshot
            {
                Invoices = _byInvoiceId.ToDictionary(kv => kv.Key, kv => kv.Value),
                EventIdIndex = _byEventId.ToDictionary(kv => kv.Key, kv => kv.Value),
                PaidAt = _paidAt.ToDictionary(kv => kv.Key, kv => kv.Value),
                BtcpayLink = _byBtcpayInvoiceId.ToDictionary(kv => kv.Key, kv => kv.Value),
            };
            var json = JsonConvert.SerializeObject(snapshot);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best-effort persistence
        }
    }

    private void LoadFromFile()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;
            var json = File.ReadAllText(FilePath);
            var snapshot = JsonConvert.DeserializeObject<StoreSnapshot>(json);
            if (snapshot == null)
                return;
            foreach (var kv in snapshot.Invoices)
                _byInvoiceId[kv.Key] = kv.Value;
            foreach (var kv in snapshot.EventIdIndex)
                _byEventId[kv.Key] = kv.Value;
            foreach (var kv in snapshot.PaidAt)
                _paidAt[kv.Key] = kv.Value;
            foreach (var kv in snapshot.BtcpayLink)
                _byBtcpayInvoiceId[kv.Key] = kv.Value;
        }
        catch
        {
            // Best-effort recovery
        }
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

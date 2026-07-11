using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Clink.Services;

public class NdebitRegistry
{
    private static readonly string FilePath = Path.Combine(
        Path.GetDirectoryName(typeof(NdebitRegistry).Assembly.Location) ?? "/tmp",
        "clink-ndebit-registry.json");

    private readonly ConcurrentDictionary<string, string> _byInvoiceId = new();

    public NdebitRegistry()
    {
        LoadFromFile();
    }

    public void Store(string btcpayInvoiceId, string ndebit)
    {
        _byInvoiceId[btcpayInvoiceId] = ndebit;
        SaveToFile();
    }

    public string? GetByInvoiceId(string btcpayInvoiceId)
    {
        return _byInvoiceId.TryGetValue(btcpayInvoiceId, out var ndebit) ? ndebit : null;
    }

    public void Remove(string btcpayInvoiceId)
    {
        _byInvoiceId.TryRemove(btcpayInvoiceId, out _);
        SaveToFile();
    }

    private void SaveToFile()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_byInvoiceId.ToDictionary(kv => kv.Key, kv => kv.Value));
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
            var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            if (data == null)
                return;
            foreach (var kv in data)
                _byInvoiceId[kv.Key] = kv.Value;
        }
        catch
        {
            // Best-effort recovery
        }
    }
}

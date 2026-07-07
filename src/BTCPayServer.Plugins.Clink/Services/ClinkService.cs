using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Plugins.Clink.Models;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Clink.Services;

public class ClinkService
{
    private const string SettingKey = "BTCPayServer.Plugins.Clink";

    private readonly IStoreRepository _storeRepository;

    public ClinkService(IStoreRepository storeRepository)
    {
        _storeRepository = storeRepository;
    }

    public async Task<ClinkSettings?> GetSettings(string storeId)
    {
        return await _storeRepository.GetSettingAsync<ClinkSettings>(storeId, SettingKey);
    }

    public async Task SetSettings(string storeId, ClinkSettings settings)
    {
        await _storeRepository.UpdateSetting(storeId, SettingKey, settings);
    }

    public async Task<bool> IsEnabled(string storeId)
    {
        var settings = await GetSettings(storeId);
        return settings is { Enabled: true, Noffer: not null };
    }

    public async Task<int> ConvertToSats(string storeId, decimal amount, string currency)
    {
        var settings = await GetSettings(storeId);
        if (settings == null) return 0;

        if (string.Equals(currency, "BTC", StringComparison.OrdinalIgnoreCase))
            return (int)Math.Round(amount * 100_000_000m);

        if (settings.FixedBtcRate.HasValue && settings.FixedBtcRate.Value > 0)
        {
            var btcAmount = amount / settings.FixedBtcRate.Value;
            return (int)Math.Round(btcAmount * 100_000_000m);
        }

        var btcPrice = await FetchBtcPrice(currency);
        if (btcPrice <= 0) return 0;

        var btc = amount / btcPrice;
        return (int)Math.Round(btc * 100_000_000m);
    }

    private const string PaymentRecordsKey = "BTCPayServer.Plugins.Clink.Payments";

    public async Task RecordPayment(string storeId, string invoiceId, Models.PaymentNotification notification)
    {
        var records = await _storeRepository.GetSettingAsync<Dictionary<string, ClinkPaymentData>>(storeId, PaymentRecordsKey) ?? new();
        records[invoiceId] = new ClinkPaymentData
        {
            Noffer = (await GetSettings(storeId))?.Noffer,
            AmountSats = notification.AmountSats,
            Bolt11 = notification.Bolt11,
            InvoiceId = invoiceId,
            CreatedAt = DateTimeOffset.UtcNow,
            PaidAt = notification.Paid ? DateTimeOffset.UtcNow : null,
            Status = notification.Paid ? ClinkPaymentStatus.Paid : ClinkPaymentStatus.InvoiceGenerated
        };
        await _storeRepository.UpdateSetting(storeId, PaymentRecordsKey, records);
    }

    public async Task<bool> IsPaymentRecorded(string storeId, string invoiceId)
    {
        var records = await _storeRepository.GetSettingAsync<Dictionary<string, ClinkPaymentData>>(storeId, PaymentRecordsKey);
        return records?.TryGetValue(invoiceId, out var data) == true && data.Status == ClinkPaymentStatus.Paid;
    }

    private static async Task<decimal> FetchBtcPrice(string currency)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = $"https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies={currency.ToLowerInvariant()}";
            var response = await http.GetStringAsync(url);
            var data = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, decimal>>>(response);
            if (data != null && data.TryGetValue("bitcoin", out var prices) && prices.TryGetValue(currency.ToLowerInvariant(), out var price))
                return price;
        }
        catch
        {
        }
        return 0;
    }
}

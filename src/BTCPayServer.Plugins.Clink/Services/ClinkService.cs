using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Plugins.Clink.Models;

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

    private const string PaymentRecordsKey = "BTCPayServer.Plugins.Clink.Payments";

    public async Task RecordPayment(string storeId, string invoiceId, PaymentNotification notification)
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
}

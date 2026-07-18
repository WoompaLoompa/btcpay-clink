using BTCPayServer.Abstractions.Contracts;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Clink.Services;

public class NdebitRegistry
{
    private const string SettingKey = "BTCPayServer.Plugins.Clink.NdebitRegistry";

    private readonly IStoreRepository _storeRepository;

    public NdebitRegistry(IStoreRepository storeRepository)
    {
        _storeRepository = storeRepository;
    }

    public async Task Store(string storeId, string btcpayInvoiceId, string ndebit)
    {
        var data = await LoadData(storeId);
        data[btcpayInvoiceId] = ndebit;
        await SaveData(storeId, data);
    }

    public async Task<string?> GetByInvoiceId(string storeId, string btcpayInvoiceId)
    {
        var data = await LoadData(storeId);
        return data.TryGetValue(btcpayInvoiceId, out var ndebit) ? ndebit : null;
    }

    public async Task Remove(string storeId, string btcpayInvoiceId)
    {
        var data = await LoadData(storeId);
        if (data.Remove(btcpayInvoiceId))
            await SaveData(storeId, data);
    }

    private async Task<Dictionary<string, string>> LoadData(string storeId)
    {
        return await _storeRepository.GetSettingAsync<Dictionary<string, string>>(storeId, SettingKey)
               ?? new Dictionary<string, string>();
    }

    private async Task SaveData(string storeId, Dictionary<string, string> data)
    {
        await _storeRepository.UpdateSetting(storeId, SettingKey, data);
    }
}

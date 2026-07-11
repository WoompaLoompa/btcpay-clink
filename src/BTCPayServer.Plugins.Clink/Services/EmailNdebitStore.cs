using System.Collections.Concurrent;
using BTCPayServer.Abstractions.Contracts;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Clink.Services;

public class EmailNdebitStore
{
    private const string SettingKeyPrefix = "BTCPayServer.Plugins.Clink.EmailNdebits";

    private readonly IStoreRepository _storeRepository;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _cache = new();

    public EmailNdebitStore(IStoreRepository storeRepository)
    {
        _storeRepository = storeRepository;
    }

    public async Task<string?> Get(string storeId, string email)
    {
        email = email.Trim().ToLowerInvariant();
        if (_cache.TryGetValue(storeId, out var storeMap) && storeMap.TryGetValue(email, out var ndebit))
            return ndebit;

        var data = await _storeRepository.GetSettingAsync<Dictionary<string, string>>(storeId, SettingKeyPrefix);
        if (data != null && data.TryGetValue(email, out ndebit))
        {
            _cache.GetOrAdd(storeId, _ => new ConcurrentDictionary<string, string>())[email] = ndebit;
            return ndebit;
        }

        return null;
    }

    public async Task Set(string storeId, string email, string ndebit)
    {
        email = email.Trim().ToLowerInvariant();
        var data = await _storeRepository.GetSettingAsync<Dictionary<string, string>>(storeId, SettingKeyPrefix)
                   ?? new Dictionary<string, string>();
        data[email] = ndebit;
        await _storeRepository.UpdateSetting(storeId, SettingKeyPrefix, data);
        _cache.GetOrAdd(storeId, _ => new ConcurrentDictionary<string, string>())[email] = ndebit;
    }

    public async Task Remove(string storeId, string email)
    {
        email = email.Trim().ToLowerInvariant();
        var data = await _storeRepository.GetSettingAsync<Dictionary<string, string>>(storeId, SettingKeyPrefix);
        if (data != null && data.Remove(email))
            await _storeRepository.UpdateSetting(storeId, SettingKeyPrefix, data);
        if (_cache.TryGetValue(storeId, out var storeMap))
            storeMap.TryRemove(email, out _);
    }

    public async Task<Dictionary<string, string>> GetAll(string storeId)
    {
        var data = await _storeRepository.GetSettingAsync<Dictionary<string, string>>(storeId, SettingKeyPrefix);
        return data ?? new Dictionary<string, string>();
    }
}

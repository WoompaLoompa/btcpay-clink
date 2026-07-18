using BTCPayServer.Abstractions.Contracts;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Clink.Tests;

public class InMemoryStoreRepository : IStoreRepository
{
    private readonly Dictionary<string, Dictionary<string, string>> _data = new();

    public Task<T?> GetSettingAsync<T>(string storeId, string name) where T : class
    {
        if (_data.TryGetValue(storeId, out var storeData) && storeData.TryGetValue(name, out var json))
            return Task.FromResult(JsonConvert.DeserializeObject<T>(json));
        return Task.FromResult<T?>(null);
    }

    public Task<Dictionary<string, T?>> GetSettingsAsync<T>(string name) where T : class
    {
        var result = new Dictionary<string, T?>();
        foreach (var (sid, storeData) in _data)
        {
            if (storeData.TryGetValue(name, out var json))
                result[sid] = JsonConvert.DeserializeObject<T>(json);
        }
        return Task.FromResult(result);
    }

    public Task UpdateSetting<T>(string storeId, string name, T obj) where T : class
    {
        if (!_data.ContainsKey(storeId))
            _data[storeId] = new Dictionary<string, string>();
        _data[storeId][name] = JsonConvert.SerializeObject(obj);
        return Task.CompletedTask;
    }
}

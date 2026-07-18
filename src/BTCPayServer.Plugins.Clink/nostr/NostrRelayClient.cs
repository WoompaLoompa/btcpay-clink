using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BTCPayServer.Plugins.Clink.Nostr;

public class NostrRelayClient : IAsyncDisposable
{
    private ClientWebSocket? _ws;
    private Uri? _relayUri;
    private CancellationTokenSource? _receiveCts;
    private readonly Dictionary<string, Action<JsonElement>> _subscriptions = new();

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public async Task ConnectAsync(string relayUrl, CancellationToken ct = default)
    {
        _relayUri = new Uri(relayUrl);
        _ws = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        await _ws.ConnectAsync(_relayUri, ct);
        _receiveCts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_receiveCts.Token);
    }

    public async Task PublishAsync(JsonElement eventObj, CancellationToken ct = default)
    {
        EnsureConnected();
        var msg = JsonSerializer.Serialize(new object?[] { "EVENT", eventObj });
        var bytes = Encoding.UTF8.GetBytes(msg);
        await _ws!.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    public async Task<string> SubscribeAsync(JsonElement filter, int timeoutSeconds,
        CancellationToken ct = default)
    {
        EnsureConnected();
        var subId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        void OnEvent(JsonElement ev)
        {
            if (!tcs.Task.IsCompleted)
                tcs.TrySetResult(ev);
        }

        _subscriptions[subId] = OnEvent;

        var msg = JsonSerializer.Serialize(new object?[] { "REQ", subId, filter });
        var bytes = Encoding.UTF8.GetBytes(msg);
        await _ws!.SendAsync(bytes, WebSocketMessageType.Text, true, ct);

        try
        {
            using var _ = cts.Token.Register(() => tcs.TrySetCanceled());
            var result = await tcs.Task;
            return result.ToString();
        }
        finally
        {
            _subscriptions.Remove(subId);
            _ = CloseSubscriptionAsync(subId, CancellationToken.None);
        }
    }

    private async Task CloseSubscriptionAsync(string subId, CancellationToken ct)
    {
        try
        {
            var msg = JsonSerializer.Serialize(new object?[] { "CLOSE", subId });
            var bytes = Encoding.UTF8.GetBytes(msg);
            await _ws!.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        catch { }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[65536];
        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessMessage(json);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2)
                return;

            var type = root[0].GetString();
            if (type == "EVENT" && root.GetArrayLength() >= 3)
            {
                var subId = root[1].GetString();
                if (subId != null && _subscriptions.TryGetValue(subId, out var handler))
                    handler(root[2].Clone());
            }
        }
        catch { }
    }

    private void EnsureConnected()
    {
        if (_ws is not { State: WebSocketState.Open })
            throw new InvalidOperationException("Not connected to Nostr relay");
    }

    public async ValueTask DisposeAsync()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        if (_ws != null)
        {
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
            catch { }
            _ws.Dispose();
        }
    }
}

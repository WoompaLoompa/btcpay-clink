using System.Diagnostics;
using System.Reflection;
using System.Text;
using BTCPayServer.Plugins.Clink.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Clink.Services;

public class ClinkNostrBridge
{
    private readonly string? _scriptPath;
    private readonly ILogger<ClinkNostrBridge> _logger;

    public ClinkNostrBridge(ILogger<ClinkNostrBridge> logger)
    {
        _logger = logger;
        _scriptPath = ExtractBundle();
        _logger.LogInformation("ClinkNostrBridge initialized, scriptPath={ScriptPath}", _scriptPath ?? "(null)");
    }

    public async Task<NostrInvoiceResult> RequestInvoice(
        string noffer, long amountSats, string? description, int expiresInSeconds,
        CancellationToken cancellation = default)
    {
        if (amountSats <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amountSats));

        var input = JsonConvert.SerializeObject(new
        {
            noffer,
            amountSats,
            description,
            expiresInSeconds,
            timeoutSeconds = 60,
            additionalRelays = (string?)null
        });

        _logger.LogInformation("RequestInvoice: calling bridge for {Sats} sats", amountSats);
        var output = await RunBridge("request-invoice", input, cancellation);
        _logger.LogInformation("RequestInvoice: bridge returned: {Output}", output[..Math.Min(output.Length, 200)]);

        var result = JsonConvert.DeserializeObject<NostrInvoiceResult>(output);

        if (result == null || string.IsNullOrEmpty(result.Bolt11))
        {
            var error = JsonConvert.DeserializeObject<NostrErrorResult>(output);
            _logger.LogError("RequestInvoice: no BOLT11 returned: {Error}", error?.Error ?? "unknown");
            throw new InvalidOperationException(error?.Error ?? "No BOLT11 returned from CLINK node");
        }

        _logger.LogInformation("RequestInvoice: got BOLT11={Bolt11}, eventId={EventId}",
            result.Bolt11[..Math.Min(result.Bolt11.Length, 60)], result.EventId);
        return result;
    }

    public async Task<NostrPayResult> PayInvoice(string ndebit, string bolt11, long amountSats, CancellationToken cancellation = default)
    {
        if (string.IsNullOrEmpty(ndebit))
            throw new ArgumentException("ndebit is required", nameof(ndebit));

        var input = JsonConvert.SerializeObject(new
        {
            ndebit,
            bolt11,
            amountSats,
            timeoutSeconds = 45,
            additionalRelays = (string?)null
        });

        _logger.LogInformation("PayInvoice: calling bridge to pay {Bolt11} via ndebit", bolt11[..Math.Min(bolt11.Length, 60)]);
        var output = await RunBridge("pay-invoice", input, cancellation);

        var result = JsonConvert.DeserializeObject<NostrPayResult>(output);
        if (result == null || result.Res != "ok")
        {
            var error = JsonConvert.DeserializeObject<NostrErrorResult>(output);
            _logger.LogError("PayInvoice: failed: {Error}", error?.Error ?? "unknown");
            throw new InvalidOperationException(error?.Error ?? "Failed to pay invoice via ndebit");
        }

        _logger.LogInformation("PayInvoice: paid successfully, preimage={Preimage}", result.Preimage ?? "(none)");
        return result;
    }

    public async Task<bool> CheckPayment(string noffer, string eventId, string fromPub, string privkeyHex, CancellationToken cancellation = default)
    {
        var input = JsonConvert.SerializeObject(new
        {
            noffer,
            eventId,
            fromPub,
            privkeyHex,
            timeoutSeconds = 30,
            additionalRelays = (string?)null
        });

        try
        {
            var output = await RunBridge("check-payment", input, cancellation);
            var result = JsonConvert.DeserializeObject<NostrCheckResult>(output);
            return result?.Paid == true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> RunBridge(string command, string stdin, CancellationToken cancellation)
    {
        if (_scriptPath == null)
            throw new InvalidOperationException("Nostr bridge script not found");

        var psi = new ProcessStartInfo
        {
            FileName = "node",
            ArgumentList = { _scriptPath, command },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };

        _logger.LogInformation("RunBridge: starting node {Script} {Cmd}", _scriptPath, command);
        process.Start();
        _logger.LogInformation("RunBridge: started PID={Pid}", process.Id);

        await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();

        // Read stdout/stderr in parallel, then wait for exit (avoids deadlock)
        var readTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        var timeout = Task.Delay(TimeSpan.FromSeconds(120));
        var exitTask = process.WaitForExitAsync(cancellation);
        var completed = await Task.WhenAny(exitTask, timeout);

        var error = await errorTask;

        if (completed == timeout)
        {
            _logger.LogWarning("RunBridge: timed out after 120s, killing PID={Pid}, stderr={Stderr}",
                process.Id, error[..Math.Min(error.Length, 500)]);
            try { process.Kill(); } catch { }
            throw new TimeoutException($"Nostr bridge timed out for command '{command}'. Stderr: {error[..Math.Min(error.Length, 500)]}");
        }

        var output = await readTask;

        _logger.LogInformation("RunBridge: exit code {Code}, stdout_len={StdoutLen}, stderr_len={StderrLen}, stderr={Stderr}",
            process.ExitCode,
            output.Length,
            error.Length,
            error[..Math.Min(error.Length, 500)]);

        if (process.ExitCode != 0)
        {
            var detail = !string.IsNullOrEmpty(output) ? output : error;
            var msg = $"Nostr bridge '{command}' exited with code {process.ExitCode}: {detail[..Math.Min(detail.Length, 500)]}";
            _logger.LogError("RunBridge: {Msg}", msg);
            throw new InvalidOperationException(msg);
        }

        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("RunBridge: command '{Cmd}' had stderr output: {Stderr}",
                command, error[..Math.Min(error.Length, 500)]);
        }

        return output.Trim();
    }

    private static string? ExtractBundle()
    {
        var assembly = typeof(ClinkNostrBridge).Assembly;
        var resourceName = "BTCPayServer.Plugins.Clink.nostr.clink-bridge.bundle.mjs";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;

        var tempDir = Path.Combine(Path.GetTempPath(), "btcpay-clink-nostr");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "clink-bridge.bundle.mjs");

        if (!File.Exists(tempFile))
        {
            using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fs);
        }

        return tempFile;
    }
}

using System.Security.Cryptography;
using BTCPayServer.Lightning;
using BTCPayServer.Services.Invoices;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Clink.Services;

public class ClinkLightningClient : ILightningClient
{
    private const long MinimumSats = 10;

    private string _storeId;
    private string _noffer;
    private readonly string? _ndebit;
    private string? _additionalRelays;
    private readonly Network _network;
    private readonly ClinkNostrBridge _bridge;
    private readonly NostrEventStore _store;
    private readonly ILogger<ClinkLightningClient> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailNdebitStore _emailNdebitStore;

    public ClinkLightningClient(string storeId, string noffer, Network network, ClinkNostrBridge bridge, NostrEventStore store,
        string? ndebit, string? additionalRelays, ILogger<ClinkLightningClient> logger,
        IServiceScopeFactory scopeFactory, EmailNdebitStore emailNdebitStore)
    {
        _storeId = storeId;
        _noffer = noffer;
        _ndebit = ndebit;
        _additionalRelays = additionalRelays;
        _network = network;
        _bridge = bridge;
        _store = store;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _emailNdebitStore = emailNdebitStore;
        _logger.LogInformation("MARKER-V2-0-5 ClinkLightningClient created for store {StoreId}, noffer={Noffer}, hasNdebit={HasNdebit}",
            storeId, noffer[..Math.Min(noffer.Length, 40)], ndebit != null);
    }

    public string Noffer => _noffer;
    public string? Ndebit => _ndebit;

    public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default)
    {
        var storeId = await GetValidatedStoreIdAsync();
        _logger.LogInformation("GetInvoice: id={Id}", invoiceId);
        var data = await _store.GetByInvoiceId(storeId, invoiceId);
        if (data != null)
        {
            var paidAt = await _store.GetPaidAt(storeId, invoiceId);
            if (paidAt != null)
                return BuildInvoice(data, invoiceId, paidAt.Value);

            if (!string.IsNullOrEmpty(data.FromPub) && !string.IsNullOrEmpty(data.PrivkeyHex))
            {
                try
                {
                    _logger.LogInformation("GetInvoice: polling Nostr for eventId={EventId}", data.EventId);
                    var (paid, preimage) = await _bridge.CheckPayment(_noffer, data.EventId, data.FromPub, data.PrivkeyHex, _additionalRelays, cancellation);
                    if (paid && !string.IsNullOrEmpty(preimage))
                    {
                        if (VerifyPreimage(preimage, data.Bolt11))
                        {
                            _logger.LogInformation("GetInvoice: payment detected and preimage verified!");
                            await _store.MarkPaid(storeId, invoiceId);
                            return BuildInvoice(data, invoiceId, DateTimeOffset.UtcNow);
                        }
                        _logger.LogWarning("GetInvoice: preimage mismatch for eventId={EventId}", data.EventId);
                    }
                    else if (paid)
                    {
                        _logger.LogWarning("GetInvoice: relay reported ok but no preimage for eventId={EventId}", data.EventId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "GetInvoice: CheckPayment poll failed");
                }
            }

            return new LightningInvoice
            {
                Id = invoiceId,
                BOLT11 = data.Bolt11,
                Amount = LightMoney.Satoshis(data.AmountSats),
                ExpiresAt = data.CreatedAt + data.Expiry,
                Status = LightningInvoiceStatus.Unpaid,
            };
        }

        _logger.LogInformation("GetInvoice: invoice not found in store");
        return new LightningInvoice { Id = invoiceId, Status = LightningInvoiceStatus.Unpaid };
    }

    public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
        CancellationToken cancellation = default)
    {
        var storeId = await GetValidatedStoreIdAsync();
        var invoiceId = Guid.NewGuid().ToString();
        var sats = (long)amount.ToDecimal(LightMoneyUnit.Satoshi);
        var expiresInSeconds = (int)Math.Ceiling(expiry.TotalSeconds);

        if (sats < MinimumSats)
        {
            _logger.LogWarning("CreateInvoice: amount {Sats} sats below minimum {Min}, throwing", sats, MinimumSats);
            throw new ArgumentOutOfRangeException(nameof(amount),
                $"Minimum invoice amount is {MinimumSats} sats, requested {sats} sats");
        }

        _logger.LogInformation("CreateInvoice: id={Id}, sats={Sats}, desc={Desc}", invoiceId, sats, description);

        var result = await _bridge.RequestInvoice(_noffer, sats, description, expiresInSeconds, _additionalRelays, cancellation);

        _logger.LogInformation("CreateInvoice: got BOLT11 for invoice {Id}", invoiceId);

        var btcpayMatch = System.Text.RegularExpressions.Regex.Match(description, @"\(Order ID:\s*([^\s)]+)\)");
        var btcpayInvoiceId = btcpayMatch.Success ? btcpayMatch.Groups[1].Value : null;

        var storeData = new NostrEventData
        {
            EventId = result.EventId,
            Bolt11 = result.Bolt11,
            AmountSats = sats,
            CreatedAt = DateTimeOffset.UtcNow,
            Expiry = expiry,
            FromPub = result.FromPub,
            PrivkeyHex = result.PrivkeyHex,
        };
        await _store.Store(storeId, invoiceId, storeData);
        if (btcpayInvoiceId != null)
        {
            await _store.LinkBtcpayInvoice(storeId, btcpayInvoiceId, invoiceId);
            _logger.LogInformation("CreateInvoice: linked BTCPay invoice {BtcpayId} -> our invoice {OurId}",
                btcpayInvoiceId, invoiceId);
        }

        if (!string.IsNullOrEmpty(_ndebit))
        {
            try
            {
                _logger.LogInformation("CreateInvoice: auto-paying via connection string ndebit for invoice {Id}", invoiceId);
                var payResult = await _bridge.PayInvoice(_ndebit, result.Bolt11, sats, _additionalRelays, cancellation);
                _logger.LogInformation("CreateInvoice: auto-pay got response for {Id}, preimage={Preimage}",
                    invoiceId, payResult.Preimage ?? "(none)");

                if (!string.IsNullOrEmpty(payResult.Preimage) && VerifyPreimage(payResult.Preimage, result.Bolt11))
                {
                    _logger.LogInformation("CreateInvoice: preimage verified, marking paid for {Id}", invoiceId);
                    await _store.MarkPaid(storeId, invoiceId);
                    return new LightningInvoice
                    {
                        Id = invoiceId,
                        BOLT11 = result.Bolt11,
                        Amount = amount,
                        ExpiresAt = DateTimeOffset.UtcNow + expiry,
                        PaidAt = DateTimeOffset.UtcNow,
                        Status = LightningInvoiceStatus.Paid,
                    };
                }

                _logger.LogWarning("CreateInvoice: auto-pay preimage missing or invalid for {Id}", invoiceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateInvoice: auto-pay via connection string ndebit failed for invoice {Id}", invoiceId);
            }
        }

        if (btcpayInvoiceId != null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var invoiceRepo = scope.ServiceProvider.GetRequiredService<InvoiceRepository>();
                var invoice = await invoiceRepo.GetInvoice(btcpayInvoiceId);
                var email = invoice?.Metadata?.BuyerEmail;
                if (!string.IsNullOrEmpty(email) && invoice != null)
                {
                    var customerNdebit = await _emailNdebitStore.Get(storeId, email);
                    if (!string.IsNullOrEmpty(customerNdebit))
                    {
                        _logger.LogInformation("CreateInvoice: auto-paying for {Id} via customer ndebit for email {Email}",
                            invoiceId, email);
                        var payResult = await _bridge.PayInvoice(customerNdebit, result.Bolt11, sats, _additionalRelays, cancellation);
                        _logger.LogInformation("CreateInvoice: customer ndebit got response for {Id}, preimage={Preimage}",
                            invoiceId, payResult.Preimage ?? "(none)");

                        if (!string.IsNullOrEmpty(payResult.Preimage) && VerifyPreimage(payResult.Preimage, result.Bolt11))
                        {
                            _logger.LogInformation("CreateInvoice: customer ndebit preimage verified for {Id}", invoiceId);
                            await _store.MarkPaid(storeId, invoiceId);
                            return new LightningInvoice
                            {
                                Id = invoiceId,
                                BOLT11 = result.Bolt11,
                                Amount = amount,
                                ExpiresAt = DateTimeOffset.UtcNow + expiry,
                                PaidAt = DateTimeOffset.UtcNow,
                                Status = LightningInvoiceStatus.Paid,
                            };
                        }

                        _logger.LogWarning("CreateInvoice: customer ndebit preimage missing or invalid for {Id}", invoiceId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateInvoice: customer ndebit auto-pay failed for invoice {Id}", invoiceId);
            }
        }

        return new LightningInvoice
        {
            Id = invoiceId,
            BOLT11 = result.Bolt11,
            Amount = amount,
            ExpiresAt = DateTimeOffset.UtcNow + expiry,
            Status = LightningInvoiceStatus.Unpaid,
        };
    }

    public async Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest,
        CancellationToken cancellation = default)
    {
        var expiry = createInvoiceRequest.Expiry;
        return await CreateInvoice(createInvoiceRequest.Amount, createInvoiceRequest.Description ?? "",
            expiry, cancellation);
    }

    public async Task<string> GenerateInvoice(string storeId, LightMoney amount, string description, string currency)
    {
        var invoice = await CreateInvoice(amount, description, TimeSpan.FromMinutes(10));
        return invoice.BOLT11;
    }

    public async Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation = default)
    {
        var storeId = await GetValidatedStoreIdAsync();
        var hash = paymentHash.ToString();
        var invoiceId = await _store.GetInvoiceIdByEventId(storeId, hash);
        if (invoiceId != null)
        {
            var data = await _store.GetByInvoiceId(storeId, invoiceId);
            if (data != null)
            {
                var paidAt = await _store.GetPaidAt(storeId, invoiceId);
                return new LightningInvoice
                {
                    Id = invoiceId,
                    BOLT11 = data.Bolt11,
                    Amount = LightMoney.Satoshis(data.AmountSats),
                    ExpiresAt = data.CreatedAt + data.Expiry,
                    Status = paidAt != null ? LightningInvoiceStatus.Paid : LightningInvoiceStatus.Unpaid,
                };
            }
        }
        return new LightningInvoice { Status = LightningInvoiceStatus.Unpaid };
    }

    public async Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default)
    {
        var storeId = await GetValidatedStoreIdAsync();
        var all = await _store.GetAll(storeId);
        var result = new List<LightningInvoice>();
        foreach (var kv in all)
        {
            var paidAt = await _store.GetPaidAt(storeId, kv.Key);
            result.Add(new LightningInvoice
            {
                Id = kv.Key,
                BOLT11 = kv.Value.Bolt11,
                Amount = LightMoney.Satoshis(kv.Value.AmountSats),
                ExpiresAt = kv.Value.CreatedAt + kv.Value.Expiry,
                Status = paidAt != null ? LightningInvoiceStatus.Paid : LightningInvoiceStatus.Unpaid,
            });
        }
        return result.ToArray();
    }

    public Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = default)
    {
        return ListInvoices(cancellation);
    }

    public async Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default)
    {
        var storeId = await GetValidatedStoreIdAsync();
        var invoiceId = await _store.GetInvoiceIdByEventId(storeId, paymentHash);
        if (invoiceId != null)
        {
            var data = await _store.GetByInvoiceId(storeId, invoiceId);
            var paidAt = await _store.GetPaidAt(storeId, invoiceId);
            if (data != null && paidAt != null)
            {
                return new LightningPayment
                {
                    Id = paymentHash,
                    PaymentHash = paymentHash,
                    Status = LightningPaymentStatus.Complete,
                    BOLT11 = data.Bolt11,
                    CreatedAt = data.CreatedAt,
                    Amount = LightMoney.Satoshis(data.AmountSats),
                };
            }
        }
        return new LightningPayment
        {
            PaymentHash = paymentHash,
            Status = LightningPaymentStatus.Unknown,
        };
    }

    public async Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default)
    {
        var storeId = await GetValidatedStoreIdAsync();
        var all = await _store.GetAll(storeId);
        var result = new List<LightningPayment>();
        foreach (var kv in all)
        {
            var paidAt = await _store.GetPaidAt(storeId, kv.Key);
            if (paidAt != null)
            {
                result.Add(new LightningPayment
                {
                    Id = kv.Value.EventId,
                    PaymentHash = kv.Value.EventId,
                    Status = LightningPaymentStatus.Complete,
                    BOLT11 = kv.Value.Bolt11,
                    CreatedAt = kv.Value.CreatedAt,
                    Amount = LightMoney.Satoshis(kv.Value.AmountSats),
                });
            }
        }
        return result.ToArray();
    }

    public Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = default)
    {
        return ListPayments(cancellation);
    }

    public async Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default)
    {
        return new ClinkInvoiceListener(this, cancellation);
    }

    public Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default)
    {
        return Task.FromResult(new LightningNodeInformation
        {
            BlockHeight = 0,
            Alias = "CLINK nOffer Node",
        });
    }

    public Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = default)
    {
        return Task.FromResult(new LightningNodeBalance(
            new OnchainBalance(),
            new OffchainBalance()));
    }

    public Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = default)
    {
        return Task.FromResult(new PayResponse(PayResult.Error, "Use Pay(string bolt11, ...) instead"));
    }

    public Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = default)
    {
        return PayCore(bolt11, cancellation);
    }

    public Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default)
    {
        return PayCore(bolt11, cancellation);
    }

    private async Task<string> GetValidatedStoreIdAsync()
    {
        _logger.LogInformation("GetValidatedStoreIdAsync: _storeId=[{StoreId}] _noffer=[{Noffer}]", _storeId, _noffer[..Math.Min(_noffer.Length, 40)]);
        if (!string.IsNullOrEmpty(_storeId))
            return _storeId;
        using var scope = _scopeFactory.CreateScope();
        var clinkService = scope.ServiceProvider.GetRequiredService<ClinkService>();
        var db = scope.ServiceProvider.GetRequiredService<BTCPayServer.Data.ApplicationDbContext>();
        var stores = await db.Stores.ToListAsync();
        _logger.LogInformation("GetValidatedStoreIdAsync: scanning {Count} stores", stores.Count);
        foreach (var store in stores)
        {
            var settings = await clinkService.GetSettings(store.Id);
            _logger.LogInformation("GetValidatedStoreIdAsync: store={Id} settings.Noffer=[{Noffer}]", store.Id, settings?.Noffer?[..Math.Min(settings.Noffer.Length, 60)]);
            if (settings?.Noffer == _noffer)
            {
                _logger.LogInformation("GetValidatedStoreIdAsync: exact match for store {Id}", store.Id);
                _storeId = store.Id;
                _additionalRelays = settings.AdditionalRelays ?? _additionalRelays;
                return _storeId;
            }
        }
        _logger.LogError("GetValidatedStoreIdAsync: no store found with matching noffer=[{Noffer}]", _noffer[..Math.Min(_noffer.Length, 40)]);
        throw new InvalidOperationException("Could not resolve store for the configured noffer — no store has a matching CLINK offer");
    }

    private async Task<PayResponse> PayCore(string bolt11, CancellationToken cancellation = default)
    {
        if (string.IsNullOrEmpty(bolt11) || !bolt11.StartsWith("lnbc"))
            return new PayResponse(PayResult.Error, "Invalid BIP21 invoice");

        var storeId = await GetValidatedStoreIdAsync();

        if (!string.IsNullOrEmpty(_ndebit))
        {
            try
            {
                _logger.LogInformation("PayCore: paying via ndebit, bolt11={Bolt11}",
                    bolt11[..Math.Min(bolt11.Length, 60)]);

                var all = await _store.GetAll(storeId);
                var decoded = all.FirstOrDefault(kv => kv.Value.Bolt11 == bolt11);
                var amountSats = decoded.Key != null ? decoded.Value.AmountSats : 0;

                var result = await _bridge.PayInvoice(_ndebit, bolt11, amountSats, _additionalRelays, cancellation);

                if (string.IsNullOrEmpty(result.Preimage))
                {
                    _logger.LogError("PayCore: no preimage received for {Bolt11}",
                        bolt11[..Math.Min(bolt11.Length, 60)]);
                    return new PayResponse(PayResult.Error, "Payment completed but no preimage received — cannot verify settlement");
                }

                if (!VerifyPreimage(result.Preimage, bolt11))
                {
                    _logger.LogError("PayCore: preimage does not match payment hash for {Bolt11}",
                        bolt11[..Math.Min(bolt11.Length, 60)]);
                    return new PayResponse(PayResult.Error, "Preimage does not match invoice payment hash");
                }

                _logger.LogInformation("PayCore: preimage verified for {Bolt11}",
                    bolt11[..Math.Min(bolt11.Length, 60)]);

                var invoiceId = decoded.Key;
                if (invoiceId != null)
                {
                    await _store.MarkPaid(storeId, invoiceId);
                }

                _logger.LogInformation("PayCore: ndebit payment OK, preimage={Preimage}",
                    result.Preimage);
                return new PayResponse(PayResult.Ok);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PayCore: ndebit payment failed");
                return new PayResponse(PayResult.Error, ex.Message);
            }
        }

        _logger.LogWarning("PayCore: no ndebit configured, cannot pay invoice {Bolt11}",
            bolt11[..Math.Min(bolt11.Length, 60)]);
        return new PayResponse(PayResult.Error, "No CLINK ndebit configured for this store. Configure ndebit in the connection string to enable payouts.");
    }

    public Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest,
        CancellationToken cancellation = default)
    {
        throw new NotSupportedException("CLINK nOffer does not support opening channels");
    }

    public Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default)
    {
        throw new NotSupportedException("CLINK nOffer does not support on-chain deposits");
    }

    public Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = default)
    {
        return Task.FromResult(ConnectionResult.Ok);
    }

    public async Task CancelInvoice(string invoiceId, CancellationToken cancellation = default)
    {
        var storeId = await GetValidatedStoreIdAsync();
        await _store.Remove(storeId, invoiceId);
    }

    public Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default)
    {
        return Task.FromResult(new LightningChannel[0]);
    }

    private bool VerifyPreimage(string preimage, string bolt11)
    {
        try
        {
            var parsed = BOLT11PaymentRequest.Parse(bolt11, _network);
            var preimageBytes = Convert.FromHexString(preimage);
            var computedHash = SHA256.HashData(preimageBytes);
            return computedHash.SequenceEqual(parsed.PaymentHash.ToBytes());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VerifyPreimage: could not verify preimage for bolt11={Bolt11}",
                bolt11[..Math.Min(bolt11.Length, 60)]);
            return false;
        }
    }

    private LightningInvoice BuildInvoice(NostrEventData data, string invoiceId, DateTimeOffset paidAt)
    {
        return new LightningInvoice
        {
            Id = invoiceId,
            BOLT11 = data.Bolt11,
            Amount = LightMoney.Satoshis(data.AmountSats),
            ExpiresAt = data.CreatedAt + data.Expiry,
            PaidAt = paidAt,
            Status = LightningInvoiceStatus.Paid,
        };
    }

    private class ClinkInvoiceListener : ILightningInvoiceListener
    {
        private readonly ClinkLightningClient _client;
        private readonly CancellationToken _cancellation;
        private string? _resolvedStoreId;

        public ClinkInvoiceListener(ClinkLightningClient client, CancellationToken cancellation)
        {
            _client = client;
            _cancellation = cancellation;
        }

        private async Task<string> GetStoreIdAsync()
        {
            if (_resolvedStoreId != null) return _resolvedStoreId;
            _resolvedStoreId = await _client.GetValidatedStoreIdAsync();
            return _resolvedStoreId;
        }

        public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellationToken)
        {
            var storeId = await GetStoreIdAsync();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellation);
            while (!linked.Token.IsCancellationRequested)
            {
                var all = await _client._store.GetAll(storeId);
                foreach (var kv in all)
                {
                    var paidAt = await _client._store.GetPaidAt(storeId, kv.Key);
                    if (paidAt != null)
                        return _client.BuildInvoice(kv.Value, kv.Key, paidAt.Value);

                    if (!string.IsNullOrEmpty(kv.Value.FromPub) && !string.IsNullOrEmpty(kv.Value.PrivkeyHex))
                    {
                        try
                        {
                            var (paid, preimage) = await _client._bridge.CheckPayment(
                                _client._noffer, kv.Value.EventId, kv.Value.FromPub, kv.Value.PrivkeyHex,
                                _client._additionalRelays, linked.Token);
                            if (paid && !string.IsNullOrEmpty(preimage) && _client.VerifyPreimage(preimage, kv.Value.Bolt11))
                            {
                                await _client._store.MarkPaid(storeId, kv.Key);
                                _client._logger.LogInformation("WaitInvoice: payment detected and preimage verified for eventId={EventId}",
                                    kv.Value.EventId);
                                return _client.BuildInvoice(kv.Value, kv.Key, DateTimeOffset.UtcNow);
                            }
                        }
                        catch { }
                    }
                }
                try
                {
                    await Task.Delay(3000, linked.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            throw new TaskCanceledException();
        }

        public void Dispose() { }
    }
}

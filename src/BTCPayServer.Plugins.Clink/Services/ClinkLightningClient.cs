using BTCPayServer.Lightning;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Clink.Services;

public class ClinkLightningClient : ILightningClient
{
    private const long MinimumSats = 10;

    private readonly string _noffer;
    private readonly string? _ndebit;
    private readonly Network _network;
    private readonly ClinkNostrBridge _bridge;
    private readonly NostrEventStore _store;
    private readonly ILogger<ClinkLightningClient> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailNdebitStore _emailNdebitStore;

    public ClinkLightningClient(string noffer, Network network, ClinkNostrBridge bridge, NostrEventStore store,
        string? ndebit, ILogger<ClinkLightningClient> logger,
        IServiceScopeFactory scopeFactory, EmailNdebitStore emailNdebitStore)
    {
        _noffer = noffer;
        _ndebit = ndebit;
        _network = network;
        _bridge = bridge;
        _store = store;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _emailNdebitStore = emailNdebitStore;
        _logger.LogInformation("ClinkLightningClient created for noffer={Noffer}, hasNdebit={HasNdebit}", noffer[..Math.Min(noffer.Length, 40)], ndebit != null);
    }

    public string Noffer => _noffer;
    public string? Ndebit => _ndebit;

    public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default)
    {
        _logger.LogInformation("GetInvoice: id={Id}", invoiceId);
        var data = _store.GetByInvoiceId(invoiceId);
        if (data != null)
        {
            var paidAt = _store.GetPaidAt(invoiceId);
            if (paidAt != null)
                return BuildInvoice(data, invoiceId, paidAt.Value);

            // Poll Nostr for kind 21001 payment confirmation receipt
            if (!string.IsNullOrEmpty(data.FromPub) && !string.IsNullOrEmpty(data.PrivkeyHex))
            {
                try
                {
                    _logger.LogInformation("GetInvoice: polling Nostr for eventId={EventId}", data.EventId);
                    var paid = await _bridge.CheckPayment(_noffer, data.EventId, data.FromPub, data.PrivkeyHex, cancellation);
                    if (paid)
                    {
                        _logger.LogInformation("GetInvoice: payment detected!");
                        _store.MarkPaid(invoiceId);
                        return BuildInvoice(data, invoiceId, DateTimeOffset.UtcNow);
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

        var result = await _bridge.RequestInvoice(_noffer, sats, description, expiresInSeconds, cancellation);

        _logger.LogInformation("CreateInvoice: got BOLT11 for invoice {Id}", invoiceId);

        // Extract BTCPay invoice ID from description for later ndebit lookup
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
        _store.Store(invoiceId, storeData);
        if (btcpayInvoiceId != null)
        {
            _store.LinkBtcpayInvoice(btcpayInvoiceId, invoiceId);
            _logger.LogInformation("CreateInvoice: linked BTCPay invoice {BtcpayId} -> our invoice {OurId}", btcpayInvoiceId, invoiceId);
        }

        // Auto-pay via ndebit when configured (subscription auto-renewal)
        if (!string.IsNullOrEmpty(_ndebit))
        {
            try
            {
                _logger.LogInformation("CreateInvoice: auto-paying via ndebit for invoice {Id}", invoiceId);
                var payResult = await _bridge.PayInvoice(_ndebit, result.Bolt11, sats, cancellation);
                _logger.LogInformation("CreateInvoice: auto-pay OK for {Id}, preimage={Preimage}", invoiceId, payResult.Preimage ?? "(none)");
                _store.MarkPaid(invoiceId);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateInvoice: auto-pay via connection string ndebit failed for invoice {Id}", invoiceId);
            }
        }

        // Auto-pay via customer's stored ndebit (from previous checkout, by buyerEmail)
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
                    var customerNdebit = await _emailNdebitStore.Get(invoice.StoreId, email);
                    if (!string.IsNullOrEmpty(customerNdebit))
                    {
                        _logger.LogInformation("CreateInvoice: auto-paying for {Id} via customer ndebit for email {Email}", invoiceId, email);
                        var payResult = await _bridge.PayInvoice(customerNdebit, result.Bolt11, sats, cancellation);
                        _logger.LogInformation("CreateInvoice: customer ndebit auto-pay OK for {Id}, preimage={Preimage}", invoiceId, payResult.Preimage ?? "(none)");
                        _store.MarkPaid(invoiceId);
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

    public Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation = default)
    {
        var hash = paymentHash.ToString();
        var invoiceId = _store.GetInvoiceIdByEventId(hash);
        if (invoiceId != null)
        {
            var data = _store.GetByInvoiceId(invoiceId);
            if (data != null)
            {
                var paidAt = _store.GetPaidAt(invoiceId);
                return Task.FromResult(new LightningInvoice
                {
                    Id = invoiceId,
                    BOLT11 = data.Bolt11,
                    Amount = LightMoney.Satoshis(data.AmountSats),
                    ExpiresAt = data.CreatedAt + data.Expiry,
                    Status = paidAt != null ? LightningInvoiceStatus.Paid : LightningInvoiceStatus.Unpaid,
                });
            }
        }
        return Task.FromResult(new LightningInvoice { Status = LightningInvoiceStatus.Unpaid });
    }

    public Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default)
    {
        return Task.FromResult(_store.GetAll().Select(kv =>
        {
            var paidAt = _store.GetPaidAt(kv.Key);
            return new LightningInvoice
            {
                Id = kv.Key,
                BOLT11 = kv.Value.Bolt11,
                Amount = LightMoney.Satoshis(kv.Value.AmountSats),
                ExpiresAt = kv.Value.CreatedAt + kv.Value.Expiry,
                Status = paidAt != null ? LightningInvoiceStatus.Paid : LightningInvoiceStatus.Unpaid,
            };
        }).ToArray());
    }

    public Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = default)
    {
        return ListInvoices(cancellation);
    }

    public Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default)
    {
        var invoiceId = _store.GetInvoiceIdByEventId(paymentHash);
        if (invoiceId != null)
        {
            var data = _store.GetByInvoiceId(invoiceId);
            var paidAt = _store.GetPaidAt(invoiceId);
            if (data != null && paidAt != null)
            {
                return Task.FromResult(new LightningPayment
                {
                    Id = paymentHash,
                    PaymentHash = paymentHash,
                    Status = LightningPaymentStatus.Complete,
                    BOLT11 = data.Bolt11,
                    CreatedAt = data.CreatedAt,
                    Amount = LightMoney.Satoshis(data.AmountSats),
                });
            }
        }
        return Task.FromResult(new LightningPayment
        {
            PaymentHash = paymentHash,
            Status = LightningPaymentStatus.Unknown,
        });
    }

    public Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default)
    {
        return Task.FromResult(_store.GetAll()
            .Where(kv => _store.GetPaidAt(kv.Key) != null)
            .Select(kv => new LightningPayment
            {
                Id = kv.Value.EventId,
                PaymentHash = kv.Value.EventId,
                Status = LightningPaymentStatus.Complete,
                BOLT11 = kv.Value.Bolt11,
                CreatedAt = kv.Value.CreatedAt,
                Amount = LightMoney.Satoshis(kv.Value.AmountSats),
            })
            .ToArray());
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
        var info = new LightningNodeInformation
        {
            BlockHeight = 0,
            Alias = "CLINK nOffer Node",
        };
        return Task.FromResult(info);
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

    private async Task<PayResponse> PayCore(string bolt11, CancellationToken cancellation = default)
    {
        if (string.IsNullOrEmpty(bolt11) || !bolt11.StartsWith("lnbc"))
            return new PayResponse(PayResult.Error, "Invalid BIP21 invoice");

        if (!string.IsNullOrEmpty(_ndebit))
        {
            try
            {
                _logger.LogInformation("PayCore: paying via ndebit, bolt11={Bolt11}", bolt11[..Math.Min(bolt11.Length, 60)]);

                var decoded = _store.GetAll().FirstOrDefault(kv => kv.Value.Bolt11 == bolt11);
                var amountSats = decoded.Key != null ? decoded.Value.AmountSats : 0;

                var result = await _bridge.PayInvoice(_ndebit, bolt11, amountSats, cancellation);
                _logger.LogInformation("PayCore: ndebit payment OK, preimage={Preimage}", result.Preimage ?? "(none)");

                if (decoded.Key != null)
                {
                    _store.MarkPaid(decoded.Key);
                }

                return new PayResponse(PayResult.Ok);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PayCore: ndebit payment failed");
                return new PayResponse(PayResult.Error, ex.Message);
            }
        }

        var match = _store.GetAll().FirstOrDefault(kv => kv.Value.Bolt11 == bolt11);
        if (match.Key != null)
        {
            _store.MarkPaid(match.Key);
            _logger.LogInformation("PayCore: marked invoice {Id} as paid (local, no ndebit)", match.Key);
        }

        return new PayResponse(PayResult.Ok);
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

    public Task CancelInvoice(string invoiceId, CancellationToken cancellation = default)
    {
        _store.Remove(invoiceId);
        return Task.CompletedTask;
    }

    public Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default)
    {
        return Task.FromResult(new LightningChannel[0]);
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

        public ClinkInvoiceListener(ClinkLightningClient client, CancellationToken cancellation)
        {
            _client = client;
            _cancellation = cancellation;
        }

        public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellation);
            while (!linked.Token.IsCancellationRequested)
            {
                foreach (var kv in _client._store.GetAll())
                {
                    var paidAt = _client._store.GetPaidAt(kv.Key);
                    if (paidAt != null)
                        return _client.BuildInvoice(kv.Value, kv.Key, paidAt.Value);

                    // Poll Nostr for payment confirmation receipt
                    if (!string.IsNullOrEmpty(kv.Value.FromPub) && !string.IsNullOrEmpty(kv.Value.PrivkeyHex))
                    {
                        try
                        {
                            var paid = await _client._bridge.CheckPayment(_client._noffer, kv.Value.EventId, kv.Value.FromPub, kv.Value.PrivkeyHex, linked.Token);
                            if (paid)
                            {
                                _client._store.MarkPaid(kv.Key);
                                _client._logger.LogInformation("WaitInvoice: payment detected via Nostr for eventId={EventId}", kv.Value.EventId);
                                return _client.BuildInvoice(kv.Value, kv.Key, DateTimeOffset.UtcNow);
                            }
                        }
                        catch
                        {
                            // Ignore polling errors in listener
                        }
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

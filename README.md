# BTCPay Server CLINK Plugin (v1.0.4)

Accept **Bitcoin Lightning** payments on your BTCPay Server via the **CLINK protocol** ([clinkme.dev](https://clinkme.dev)). Customers pay with [ShockWallet](https://shockwallet.app), [ZEUS](https://zeusln.com), [Amethyst](https://amethyst.social), [Electrum](https://github.com/BareBits/electrum_clink) or any CLINK-compatible Lightning wallet. Enable Lightning Auto-Renew Subscription payments. All communication flows over Nostr relays — **no Node.js required**, no web server for your Lightning node.

## How It Works

1. **Merchant** generates a CLINK Offer string (`nOffer1...`) from their CLINK-compatible Lightning node
2. **Customer** checks out and selects "Lightning (CLINK)" as payment method
3. The plugin uses the `nOffer` to request a BOLT11 Lightning invoice from the merchant's node over Nostr (pure C# Nostr protocol — NIP-44 encrypted kind 21001 events)
4. Customer scans the QR code and pays with any Lightning wallet
5. Payment is confirmed via CLINK protocol receipt

No Node.js, no native binaries, no external runtime dependencies. All Nostr communication is handled natively in C# via `ClientWebSocket`, `ChaCha20Poly1305`, and pure C# secp256k1 arithmetic.

## Features

- CLINK Offer (nOffer) based Lightning payments via Nostr
- Pure C# Nostr protocol — no Node.js, no JavaScript bridge
- NIP-44 v2 encrypted communication (ChaCha20Poly1305 + HKDF + secp256k1 ECDH)
- Subscription auto-renewals via nDebit protocol
- Admin configuration page for store-level settings
- QR code display for easy mobile payment
- Payment polling and automatic status updates
- Configurable invoice timeout and poll interval
- Support for additional Nostr relays
- Lightning setup accordion integration in store settings
- Store-isolated state via `IStoreRepository` (no file-based persistence)

## Requirements

- [BTCPay Server](https://github.com/btcpayserver/btcpayserver) v2.4.0+
- .NET SDK 10.0+
- A CLINK-compatible Lightning wallet/node (ShockWallet, Lightning.Pub, ZEUS, Amethyst)

## Installation

### From Release

1. Download the latest release `.btcpay` plugin package from the [releases page](https://github.com/WoompaLoompa/btcpay-clink/releases)
2. Go to **Server Settings > Plugins** in your BTCPay Server
3. Upload and install the plugin
4. Go to your store settings and click **CLINK Lightning** in the integrations nav
5. Configure your CLINK offer string

### Development / Manual

```bash
# Clone with submodules
git clone --recurse-submodules https://github.com/WoompaLoompa/btcpay-clink.git
cd btcpayserver-clink

# Or if already cloned without submodules
git submodule update --init --recursive

# Build the plugin
dotnet build

# Register for local debugging
./plugin-register.sh
```

No JavaScript build step required. The plugin has zero runtime dependencies beyond .NET and BTCPay Server.

## Configuration

Navigate to your store settings and click **CLINK Lightning** in the integrations sidebar.

| Setting | Description |
|---------|-------------|
| **Enable/Disable** | Turn CLINK payments on/off for this store |
| **CLINK Offer String** | Your `nOffer1...` string from ShockWallet / Lightning.Pub |
| **Title** | Payment method title shown at checkout |
| **Description** | Payment method description shown at checkout |
| **Invoice Timeout** | Seconds before the Lightning invoice expires (default: 600) |
| **Poll Interval** | Milliseconds between payment status checks (default: 5000) |
| **Additional Relays** | Optional Nostr relays for redundancy (one per line) |

### Generating a noffer

#### ShockWallet
1. Open ShockWallet
2. Go to **Menu > Linked Apps > nOffer**
3. Copy the `noffer1...` string
4. Paste into the plugin settings

#### Lightning.Pub (self-hosted)
1. Log into your Lightning.Pub dashboard
2. Navigate to **Offers**
3. Copy the generated `noffer1...` string

#### ZEUS Wallet
1. Open ZEUS wallet
2. Go to **ZEUS Pay > Manage Offer**
3. Copy the offer string

## Project Structure

```
btcpayserver-clink/
├── src/BTCPayServer.Plugins.Clink/
│   ├── Plugin.cs                        # Plugin entry point
│   ├── Controllers/
│   │   └── ClinkController.cs           # Admin + payment API endpoints
│   ├── Services/
│   │   ├── ClinkService.cs              # Settings management + BTC conversion
│   │   ├── ClinkNostrBridge.cs          # C# Nostr protocol orchestrator
│   │   ├── ClinkLightningClient.cs      # ILightningClient implementation
│   │   ├── ClinkConnectionStringHandler.cs  # Connection string parser
│   │   ├── NostrEventStore.cs           # Store-scoped Nostr event state
│   │   ├── NdebitRegistry.cs            # Store-scoped nDebit registry
│   │   └── EmailNdebitStore.cs          # Email → nDebit mapping
│   ├── Nostr/
│   │   ├── Secp256k1Point.cs            # Pure C# secp256k1 (lift_x, ECDH, Schnorr)
│   │   ├── Nip44.cs                     # NIP-44 v2 encrypt/decrypt
│   │   ├── NostrRelayClient.cs          # WebSocket Nostr relay client
│   │   ├── Bech32Decoder.cs             # Bech32 decode + CLINK TLV parser
│   │   └── ClinkProtocol.cs             # CLINK protocol (request/check/pay)
│   ├── Models/
│   │   ├── ClinkSettings.cs             # Store configuration model
│   │   ├── ClinkPaymentData.cs          # Payment tracking model
│   │   └── PaymentNotification.cs       # Payment notification payload
│   ├── Views/
│   │   ├── Clink/
│   │   │   ├── Configure.cshtml         # Admin configuration page
│   │   │   ├── ClinkStoreNav.cshtml     # Store nav extension
│   │   │   ├── LightningSetupCustom.cshtml  # Lightning setup accordion
│   │   │   ├── ClinkCheckoutPayment.cshtml  # Checkout integration
│   │   │   └── NdebitSetup.cshtml       # Auto-pay setup page
│   │   ├── _ViewImports.cshtml
│   │   └── _ViewStart.cshtml
│   ├── Resources/
│   │   ├── js/
│   │   │   └── clink-payment.js         # Client-side checkout script
│   │   └── css/
│   │       └── clink-payment.css        # Checkout styles
│   └── Data/
│       └── ClinkDbContext.cs            # Database context
├── src/BTCPayServer.Plugins.Clink.Tests/
│   ├── StoreIsolationTests.cs           # Store isolation regression tests
│   ├── PayCoreTests.cs                  # PayCore regression tests
│   ├── Nip44Tests.cs                    # NIP-44 crypto tests
│   └── Bech32DecoderTests.cs            # Bech32/CLINK format tests
├── plugin-env.sh                        # Dev environment setup
├── plugin-register.sh                   # Debug registration script
└── README.md
```

## License

 GPL-3.0

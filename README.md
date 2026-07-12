# BTCPay Server CLINK Plugin

Accept **Bitcoin Lightning** payments on your BTCPay Server via the **CLINK protocol** ([clinkme.dev](https://clinkme.dev)). Customers pay with [ShockWallet](https://shockwallet.app), [ZEUS](https://zeusln.com), [Amethyst](https://amethyst.social), [Electrum](https://github.com/BareBits/electrum_clink) or any CLINK-compatible Lightning wallet. Enable Lightning Auto-Renew Subscription payments. All communication flows over Nostr relays. No web server required for your Lightning node.

## How It Works

1. **Merchant** generates a CLINK Offer string (`noffer1...`) from their CLINK-compatible Lightning node
2. **Customer** checks out and selects "Lightning (CLINK)" as payment method
3. The plugin uses the `noffer` to request a BOLT11 Lightning invoice from the merchant's node over Nostr
4. Customer scans the QR code and pays with any Lightning wallet
5. Payment is confirmed via CLINK protocol receipt

No web server required for the Lightning node. All communication flows over Nostr relays.

## Features

- CLINK Offer (noffer) based Lightning payments via Nostr
- Admin configuration page for store-level settings
- Client-side invoice generation using the CLINK SDK
- QR code display for easy mobile payment
- Payment polling and automatic status updates
- Support for fixed or live (CoinGecko) BTC exchange rates
- Configurable invoice timeout and poll interval
- Support for additional Nostr relays

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

# Build the JavaScript bundle (optional - pre-built included)
cd src/BTCPayServer.Plugins.Clink
npm install && npm run build
cd ../..

# Build the plugin
dotnet build

# Register for local debugging
./plugin-register.sh
```

## Configuration

Navigate to your store settings and click **CLINK Lightning** in the integrations sidebar.

| Setting | Description |
|---------|-------------|
| **Enable/Disable** | Turn CLINK payments on/off for this store |
| **CLINK Offer String** | Your `noffer1...` string from ShockWallet / Lightning.Pub |
| **Title** | Payment method title shown at checkout |
| **Description** | Payment method description shown at checkout |
| **Invoice Timeout** | Seconds before the Lightning invoice expires (default: 600) |
| **Poll Interval** | Milliseconds between payment status checks (default: 5000) |
| **Fixed BTC Rate** | Optional fixed BTC price in your store currency instead of live CoinGecko rate |
| **Additional Relays** | Optional Nostr relays for redundancy (one per line) |

### Generating a noffer

#### ShockWallet (mobile)
1. Open ShockWallet
2. Go to **Receive > CLINK Offer**
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
│   ├── Plugin.cs                    # Plugin entry point
│   ├── Controllers/
│   │   └── ClinkController.cs       # Admin + payment API endpoints
│   ├── Services/
│   │   └── ClinkService.cs          # Settings management + BTC conversion
│   ├── Models/
│   │   ├── ClinkSettings.cs         # Store configuration model
│   │   └── ClinkPaymentData.cs      # Payment tracking model
│   ├── Views/
│   │   ├── Clink/
│   │   │   ├── Configure.cshtml     # Admin configuration page
│   │   │   ├── ClinkStoreNav.cshtml # Store nav extension
│   │   │   └── ClinkCheckoutPayment.cshtml  # Checkout integration
│   │   └── _ViewImports.cshtml
│   ├── Resources/
│   │   ├── js/
│   │   │   ├── clink-payment.js     # Source (ES module)
│   │   │   └── clink-payment.min.js # Built bundle
│   │   └── css/
│   │       └── clink-payment.css    # Checkout styles
│   ├── Data/
│   │   └── ClinkDbContext.cs        # Database context
│   ├── package.json                 # JS dependencies
│   └── build.mjs                    # esbuild config
├── plugin-env.sh                    # Dev environment setup
├── plugin-register.sh               # Debug registration script
└── README.md
```

## License

 GPL-3.0

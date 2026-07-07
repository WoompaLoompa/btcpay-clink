#!/bin/bash
# Register the plugin for local development debugging
# Usage: ./plugin-register.sh

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLUGIN_NAME="BTCPayServer.Plugins.Clink"
PLUGIN_DLL="$SCRIPT_DIR/src/$PLUGIN_NAME/bin/Debug/net10.0/$PLUGIN_NAME.dll"

if [ ! -f "$PLUGIN_DLL" ]; then
    echo "Building plugin first..."
    dotnet build "$SCRIPT_DIR/src/$PLUGIN_NAME/$PLUGIN_NAME.csproj" -c Debug
fi

BTCPAY_DEV_JSON="$SCRIPT_DIR/submodules/btcpayserver/BTCPayServer/appsettings.dev.json"

if [ ! -f "$BTCPAY_DEV_JSON" ]; then
    echo '{}' > "$BTCPAY_DEV_JSON"
fi

# Add or update DEBUG_PLUGINS entry
tmp=$(mktemp)
python3 -c "
import json
with open('$BTCPAY_DEV_JSON', 'r') as f:
    config = json.load(f)
config['DEBUG_PLUGINS'] = '$PLUGIN_DLL'
with open('$BTCPAY_DEV_JSON', 'w') as f:
    json.dump(config, f, indent=2)
"

echo "Plugin registered for debugging."
echo "appsettings.dev.json: $BTCPAY_DEV_JSON"
echo "Plugin DLL: $PLUGIN_DLL"
echo ""
echo "Start BTCPay Server in development mode to load the plugin."

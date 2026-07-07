#!/bin/bash
# Set up development environment for BTCPay Server CLINK Plugin
# Source this file to set environment variables

PLUGIN_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BTCPAY_DIR="$PLUGIN_DIR/submodules/btcpayserver"

export BTCPAY_DEV_PLUGIN_DIR="$PLUGIN_DIR"
export ASPNETCORE_ENVIRONMENT="Development"

if [ ! -d "$BTCPAY_DIR" ]; then
    echo "WARNING: BTCPay Server submodule not found at $BTCPAY_DIR"
    echo "Run: git submodule update --init --recursive"
fi

echo "BTCPay Server CLINK Plugin development environment"
echo "Plugin directory: $PLUGIN_DIR"
echo "BTCPay directory: $BTCPAY_DIR"

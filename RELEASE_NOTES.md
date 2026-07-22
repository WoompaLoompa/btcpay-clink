## v1.0.5

### What's Changed

- **NIP-44 v2 spec-compliant HKDF** — Fixed two critical HKDF incompatibilities that prevented wire-level interoperability with the official CLINK SDK and Lightning.Pub. Conversation key derivation now uses the correct `"nip44-v2"` salt (was zero bytes). Message key derivation now uses a single HKDF-Expand with raw nonce as info (was HKDF-Extract + Expand with `"nip44-v2-message-key"` string). Events encrypted by this plugin can now be decrypted by Lightning.Pub and vice versa.
- **TLV decoder consistency** — Fixed `ParseNdebitTlv` to use 1-byte TLV lengths matching `ParseNofferTlv` and the NIP-44 TLV spec. Previously used 2-byte big-endian lengths.
- **Settlement verification hardened** — All `MarkPaid` code paths (PayCore, StoreNdebit, CheckPayment, GetInvoice poll) now require a verified preimage with SHA256 hash match. No settlement occurs on `"res": "ok"` alone.
- **storeId validation** — `GetValidatedStoreIdAsync` uses DB lookup to match stores by noffer. Throws `InvalidOperationException` if no match found instead of silently defaulting to empty string.
- **ndebit credential exposure eliminated** — ndebit credentials are placed inside the NIP-44 encrypted payload only, never in plaintext Nostr event tags. Public tags contain only `"p"` (merchant pubkey) and `"clink_version"`.
- **Subscribe-before-publish pattern** — `ClinkProtocol.RequestInvoiceAsync` and `PayInvoiceAsync` now subscribe to the relay before publishing events, eliminating a race condition where responses were missed.
- **Debug logging removed** — All `Console.Error.WriteLine` debug statements removed from `NostrRelayClient.cs`.

### Fixes

- **BTCPay Server review issues (v1.0.4 rejection)** — All 4 issues from the plugin review addressed:
  1. NIP-44/TLV/SDK compatibility — HKDF parameters now match nostr-tools v2.15.1
  2. ndebit credential exposure — moved to encrypted payload only
  3. Settlement verification — preimage required for all MarkPaid paths
  4. storeId validation — DB lookup, throws on no match

### Download

Download the `BTCPayServer.Plugins.Clink-1.0.5.btcpay` file from [plugin source code](https://github.com/WoompaLoompa/btcpay-clink/releases/) or from the official [BTCpayServer plugin repository](https://plugin-builder.btcpayserver.org/public/plugins/clink) and upload it in your instance via **Server Settings > Plugins**.

---

## v1.0.4

### What's Changed

- **Pure C# Nostr protocol** — Replaced the Node.js Nostr bridge (`clink-bridge.bundle.mjs`) with a native C# implementation. All Nostr communication now runs in-process via `ClientWebSocket`, `ChaCha20Poly1305`, and pure C# secp256k1 arithmetic. **No Node.js required at runtime.**
- **Pure C# secp256k1** — Implemented `Secp256k1Point.cs` with lift_x, ECDH, tagged hashes (BIP340), and Schnorr signing using `System.Numerics.BigInteger` — no `NBitcoin.Secp256k1` native assembly dependency.
- **NIP-44 v2 encryption** — New `Nip44.cs` implements the NIP-44 v2 spec (ChaCha20Poly1305 + HKDF + secp256k1 ECDH conversation keys).
- **CLINK protocol in C#** — `ClinkProtocol.cs` handles invoice requests, payment checks, and invoice payments over Nostr kind 21001/21002 events.
- **Store-isolated state** — `NostrEventStore` and `NdebitRegistry` now use `IStoreRepository` for per-store persistence instead of file-based JSON stores.
- **PayCore fix** — `ClinkLightningClient.Pay()` now returns `PayResult.Error("No CLINK ndebit configured")` when no nDebit is available, instead of falsely marking invoices as paid.
- **Secured endpoints** — `StoreNdebit` validates ndebit format (`ndebit1` prefix, max 5000 chars) and invoice ownership; `NotifyPayment` validates invoice belongs to the store before recording payment.
- **Connection string store isolation** — `ClinkConnectionStringHandler` now extracts `storeId` and `relays` from the connection string for proper store-scoped routing.
- **Checkout UI improvements** — `ClinkCheckoutPayment.cshtml` now includes the missing `data-bolt11`, `data-payment-hash`, and `data-store-id` attributes for frontend integration.
- **Unit test project** — New `BTCPayServer.Plugins.Clink.Tests` project with 19 tests covering store isolation (5), PayCore regression (3), NIP-44 crypto (5), and bech32/CLINK format (5).

### Breaking Changes

- **Node.js 22+ is no longer required.** The plugin now has zero runtime dependencies beyond .NET and BTCPay Server. All existing functionality is preserved in pure C#.
- **File-based persistence removed.** `clink-nostr-store.json` and `clink-ndebit-registry.json` no longer exist. State is stored via `IStoreRepository` in BTCPay Server's configured database.
- **Embedded resource removed.** `nostr/clink-bridge.bundle.mjs` is no longer embedded. The `nostr/` directory and `package.json` / `build.mjs` have been removed from the project.

### Fixes

- **BTCPay Server review issues (v1.0.3 rejection)** — All 5 issues addressed:
  - PayCore no longer returns fake success on empty nDebit
  - Store isolation enforced via `IStoreRepository` (no cross-store state)
  - Anonymous `StoreNdebit` endpoint validates invoice proof-of-access
  - Node.js dependency eliminated (pure C# Nostr)
  - Regression test coverage added (19 unit tests)
- **`BTCPayServer.Lightning.Common` dependency** — Added to fix `PayResult` type resolution.
- **`StaticWebAssetsEnabled=false`** — Added to test project to fix MSB3030 build errors.

### Download

Download the `BTCPayServer.Plugins.Clink-1.0.4.btcpay` file from [plugin source code](https://github.com/WoompaLoompa/btcpay-clink/releases/) or from the official [BTCpayServer plugin repository](https://plugin-builder.btcpayserver.org/public/plugins/clink) and upload it in your instance via **Server Settings > Plugins**.

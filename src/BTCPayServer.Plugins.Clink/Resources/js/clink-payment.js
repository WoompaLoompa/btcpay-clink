import { ClinkSDK, generateSecretKey, decodeBech32 } from '@shocknet/clink-sdk';

const QR_API = 'https://api.qrserver.com/v1/create-qr-code/?size=300x300&data=';

function el(tag, attrs = {}, children = []) {
  const elem = document.createElement(tag);
  for (const [key, val] of Object.entries(attrs)) {
    if (key === 'className') elem.className = val;
    else if (key === 'textContent') elem.textContent = val;
    else if (key === 'innerHTML') elem.innerHTML = val;
    else if (key.startsWith('on')) elem.addEventListener(key.slice(2).toLowerCase(), val);
    else elem.setAttribute(key, val);
  }
  for (const child of children) {
    if (typeof child === 'string') elem.appendChild(document.createTextNode(child));
    else if (child) elem.appendChild(child);
  }
  return elem;
}

class ClinkPaymentUI {
  constructor(root, opts) {
    this.root = root;
    this.noffer = opts.noffer;
    this.amountSats = opts.amountSats;
    this.orderId = opts.orderId;
    this.description = opts.description;
    this.timeout = (parseInt(opts.timeout, 10) || 600) * 1000;
    this.pollInterval = parseInt(opts.pollInterval, 10) || 5000;
    this.additionalRelays = opts.additionalRelays || '';
    this.invoiceUrl = opts.invoiceUrl;
    this.invoiceStatusUrl = opts.invoiceStatusUrl;
    this.i18n = Object.assign({
      generatingInvoice: 'Generating Lightning Invoice...',
      scanToPay: 'Scan with your Lightning Wallet to pay',
      copyInvoice: 'Copy Invoice',
      invoiceCopied: 'Copied!',
      waitingPayment: 'Waiting for payment confirmation...',
      paymentConfirmed: 'Payment confirmed! Redirecting...',
      paymentError: 'Error generating invoice. Please try again.',
      expired: 'Invoice expired. Please try again.',
      openInWallet: 'Open in Wallet',
    }, opts.i18n || {});

    this.invoice = null;
    this.paid = false;
    this.pollTimer = null;
    this.startTime = Date.now();
    this.ephemeralKey = null;
    this.sdk = null;

    this.renderLoading();
    this.generateInvoice();
  }

  renderLoading() {
    this.root.innerHTML = '';
    this.root.appendChild(
      el('div', { className: 'clink-container' }, [
        el('div', { className: 'clink-header' }, [
          el('div', { className: 'clink-bolt-icon' }, ['\u26A1']),
          el('h4', { textContent: this.i18n.generatingInvoice }),
        ]),
        el('div', { className: 'clink-loader' }, [
          el('div', { className: 'clink-spinner' }),
        ]),
      ])
    );
  }

  async generateInvoice() {
    try {
      if (!this.noffer || !this.noffer.startsWith('noffer1')) {
        throw new Error('Invalid noffer string');
      }

      const decoded = decodeBech32(this.noffer);
      if (!decoded || decoded.type !== 'noffer') {
        throw new Error('Invalid noffer type');
      }

      const offer = decoded.data;

      this.ephemeralKey = generateSecretKey();

      const relays = [offer.relay];
      if (this.additionalRelays) {
        const extra = this.additionalRelays.split('\n').map(r => r.trim()).filter(Boolean);
        relays.push(...extra);
      }

      this.sdk = new ClinkSDK({
        privateKey: this.ephemeralKey,
        relays,
        toPubKey: offer.pubkey,
        defaultTimeoutSeconds: Math.floor(this.timeout / 1000) || 600,
      });

      if (!this.amountSats || this.amountSats <= 0) {
        throw new Error('Invalid amount');
      }

      const desc = this.description || '';

      const receiptCallback = () => {
        this.onPaymentConfirmed();
      };

      const response = await this.sdk.Noffer(
        {
          offer: offer.offer,
          amount_sats: this.amountSats,
          description: desc.substring(0, 100),
          expires_in_seconds: Math.floor(this.timeout / 1000) || 600,
        },
        receiptCallback
      );

      if ('bolt11' in response && response.bolt11) {
        this.invoice = response.bolt11;
        await this.confirmInvoice();
        this.renderInvoice();
      } else if ('error' in response) {
        const errData = response;
        let errMsg = errData.error || 'Unknown error';
        if (errData.range) {
          errMsg += ` (allowed range: ${errData.range.min} - ${errData.range.max} sats)`;
        }
        throw new Error(errMsg);
      } else {
        throw new Error('Unexpected response from CLINK provider');
      }
    } catch (err) {
      console.error('CLINK invoice generation failed:', err);
      this.renderError(err.message || 'Failed to generate invoice');
    }
  }

  async confirmInvoice() {
    if (!this.invoiceUrl || !this.invoice) return;
    try {
      await fetch(this.invoiceUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          invoiceId: this.orderId,
          bolt11: this.invoice,
          amountSats: this.amountSats,
        }),
      });
    } catch (err) {
      console.error('Failed to report invoice to backend:', err);
    }
  }

  renderInvoice() {
    const bolt11 = this.invoice;
    const encodedBolt11 = encodeURIComponent(bolt11.toUpperCase());
    const qrUrl = `${QR_API}${encodedBolt11}`;
    const walletUrl = `lightning:${bolt11.toUpperCase()}`;

    this.root.innerHTML = '';
    this.root.appendChild(
      el('div', { className: 'clink-container' }, [
        el('div', { className: 'clink-header' }, [
          el('div', { className: 'clink-bolt-icon' }, ['\u26A1']),
          el('h4', { textContent: this.i18n.scanToPay }),
        ]),
        el('div', { className: 'clink-qr' }, [
          el('img', {
            src: qrUrl,
            alt: 'Lightning Invoice QR Code',
            className: 'clink-qr-img',
          }),
        ]),
        el('div', { className: 'clink-amount' }, [
          el('span', { textContent: `${this.amountSats} sats` }),
        ]),
        el('div', { className: 'clink-actions' }, [
          el('a', {
            href: walletUrl,
            className: 'clink-btn clink-btn-primary',
            textContent: this.i18n.openInWallet,
          }),
          el('button', {
            className: 'clink-btn clink-btn-secondary',
            textContent: this.i18n.copyInvoice,
            onClick: () => this.copyInvoice(bolt11),
          }),
        ]),
        el('div', { className: 'clink-status' }, [
          el('div', { className: 'clink-status-waiting' }, [
            el('div', { className: 'clink-spinner clink-spinner-sm' }),
            el('span', { className: 'ms-2', textContent: this.i18n.waitingPayment }),
          ]),
        ]),
      ])
    );

    this.startPolling();
  }

  async copyInvoice(bolt11) {
    try {
      await navigator.clipboard.writeText(bolt11.toUpperCase());
      const btn = this.root.querySelector('.clink-btn-secondary');
      if (btn) {
        const original = btn.textContent;
        btn.textContent = this.i18n.invoiceCopied;
        setTimeout(() => { btn.textContent = original; }, 2000);
      }
    } catch {
      const textarea = document.createElement('textarea');
      textarea.value = bolt11.toUpperCase();
      document.body.appendChild(textarea);
      textarea.select();
      document.execCommand('copy');
      document.body.removeChild(textarea);
    }
  }

  startPolling() {
    if (this.pollTimer) clearInterval(this.pollTimer);

    this.pollTimer = setInterval(async () => {
      if (this.paid) return;

      if (Date.now() - this.startTime > this.timeout) {
        this.renderExpired();
        return;
      }

      if (!this.invoiceStatusUrl) return;

      try {
        const resp = await fetch(this.invoiceStatusUrl);
        const json = await resp.json();
        if (json.paid) {
          this.onPaymentConfirmed();
        }
      } catch (err) {
        console.error('Poll error:', err);
      }
    }, this.pollInterval);
  }

  onPaymentConfirmed() {
    if (this.paid) return;
    this.paid = true;

    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }

    this.root.innerHTML = '';
    this.root.appendChild(
      el('div', { className: 'clink-container' }, [
        el('div', { className: 'clink-header' }, [
          el('div', { className: 'clink-check-icon' }, ['\u2713']),
          el('h4', { textContent: this.i18n.paymentConfirmed }),
        ]),
      ])
    );

    if (this.invoiceUrl) {
      fetch(this.invoiceUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          invoiceId: this.orderId,
          paid: true,
        }),
      }).catch(() => {});
    }

    setTimeout(() => {
      window.location.reload();
    }, 1500);
  }

  renderError(message) {
    this.root.innerHTML = '';
    this.root.appendChild(
      el('div', { className: 'clink-container clink-error-container' }, [
        el('div', { className: 'clink-error-icon' }, ['\u2715']),
        el('p', { className: 'clink-error-msg', textContent: message }),
        el('button', {
          className: 'clink-btn clink-btn-primary',
          textContent: 'Try Again',
          onClick: () => {
            this.startTime = Date.now();
            this.generateInvoice();
          },
        }),
      ])
    );
  }

  renderExpired() {
    this.root.innerHTML = '';
    this.root.appendChild(
      el('div', { className: 'clink-container clink-error-container' }, [
        el('div', { className: 'clink-error-icon' }, ['\u23F0']),
        el('p', { className: 'clink-error-msg', textContent: this.i18n.expired }),
        el('button', {
          className: 'clink-btn clink-btn-primary',
          textContent: 'Try Again',
          onClick: () => {
            this.startTime = Date.now();
            this.generateInvoice();
          },
        }),
      ])
    );
  }
}

(function () {
  const root = document.getElementById('clink-payment-root');
  if (!root) return;

  const noffer = root.getAttribute('data-noffer');
  if (!noffer) return;

  const opts = {
    noffer,
    amountSats: parseInt(root.getAttribute('data-amount-sats'), 10) || 0,
    orderId: root.getAttribute('data-order-id') || '',
    description: root.getAttribute('data-description') || '',
    timeout: root.getAttribute('data-timeout') || '600',
    pollInterval: root.getAttribute('data-poll-interval') || '5000',
    additionalRelays: root.getAttribute('data-additional-relays') || '',
    invoiceUrl: root.getAttribute('data-invoice-url') || '',
    invoiceStatusUrl: root.getAttribute('data-invoice-status-url') || '',
    i18n: {
      generatingInvoice: root.getAttribute('data-i18n-generating') || undefined,
      scanToPay: root.getAttribute('data-i18n-scan') || undefined,
      copyInvoice: root.getAttribute('data-i18n-copy') || undefined,
      invoiceCopied: root.getAttribute('data-i18n-copied') || undefined,
      waitingPayment: root.getAttribute('data-i18n-waiting') || undefined,
      paymentConfirmed: root.getAttribute('data-i18n-confirmed') || undefined,
      paymentError: root.getAttribute('data-i18n-error') || undefined,
      expired: root.getAttribute('data-i18n-expired') || undefined,
      openInWallet: root.getAttribute('data-i18n-openwallet') || undefined,
    },
  };

  new ClinkPaymentUI(root, opts);
})();

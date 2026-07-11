import { finalizeEvent, nip44 } from "nostr-tools";
import { SimplePool, generateSecretKey, getPublicKey, decodeBech32,
  SendNdebitRequest, newNdebitPaymentRequest } from "@shocknet/clink-sdk";

const { getConversationKey, encrypt, decrypt } = nip44;

async function requestInvoice(noffer, amountSats, description, expiresInSeconds, timeoutSeconds, additionalRelays) {
  const decoded = decodeBech32(noffer);
  const relays = [decoded.data.relay, ...(additionalRelays || [])];
  const toPub = decoded.data.pubkey;
  const pool = new SimplePool();
  const pk = generateSecretKey();
  const pub = getPublicKey(pk);

  const data = {
    offer: decoded.data.offer,
    amount_sats: amountSats,
    description: description || undefined,
    expires_in_seconds: expiresInSeconds || 3600,
  };

  const content = encrypt(JSON.stringify(data), getConversationKey(pk, toPub));
  const event = {
    content,
    created_at: Math.floor(Date.now() / 1000),
    kind: 21001,
    pubkey: pub,
    tags: [['p', toPub], ['clink_version', '1']]
  };
  const signed = finalizeEvent(event, pk);
  const eventId = signed.id;

  return new Promise((resolve, reject) => {
    let resolved = false;
    const timer = setTimeout(() => {
      if (!resolved) {
        resolved = true;
        pool.close(relays);
        reject(new Error('No response from noffer'));
      }
    }, (timeoutSeconds || 60) * 1000);

    const filter = {
      since: Math.floor(Date.now() / 1000) - 1,
      kinds: [21001],
      '#p': [pub],
      '#e': [eventId],
    };

    const closer = pool.subscribeMany(relays, [filter], {
      onevent: (e) => {
        if (!resolved) {
          try {
            const decrypted = decrypt(e.content, getConversationKey(pk, toPub));
            const result = JSON.parse(decrypted);
            resolved = true;
            clearTimeout(timer);
            closer.close();
            pool.close(relays);
            if (result.bolt11) {
              resolve({ bolt11: result.bolt11, eventId, fromPub: pub, privkeyHex: Buffer.from(pk).toString('hex') });
            } else {
              reject(new Error(result.error || 'Unknown error'));
            }
          } catch (err) {
            // skip events that don't decrypt
          }
        }
      },
    });

    Promise.all(pool.publish(relays, signed)).catch((err) => {
      if (!resolved) {
        resolved = true;
        clearTimeout(timer);
        closer.close();
        pool.close(relays);
        reject(err);
      }
    });
  });
}

async function checkPayment(noffer, eventId, fromPub, privkeyHex, timeoutSeconds, additionalRelays) {
  const decoded = decodeBech32(noffer);
  const relays = [decoded.data.relay, ...(additionalRelays || [])];
  const toPub = decoded.data.pubkey;

  const privateKey = privkeyHex ? new Uint8Array(Buffer.from(privkeyHex, 'hex')) : generateSecretKey();
  const publicKey = fromPub || getPublicKey(privateKey);

  const pool = new SimplePool();

  return new Promise((resolve) => {
    let resolved = false;
    const timer = setTimeout(() => {
      if (!resolved) { resolved = true; pool.close(relays); resolve({ paid: false }); }
    }, (timeoutSeconds || 30) * 1000);

    const filter = {
      since: Math.floor(Date.now() / 1000) - 86400,
      kinds: [21001, 21002],
      '#p': [publicKey],
      authors: [toPub],
    };

    const closer = pool.subscribeMany(relays, [filter], {
      onevent: (e) => {
        if (!resolved) {
          try {
            const decrypted = decrypt(e.content, getConversationKey(privateKey, toPub));
            const response = JSON.parse(decrypted);
            if (response.res === "ok") {
              resolved = true;
              clearTimeout(timer);
              closer.close();
              pool.close(relays);
              resolve({ paid: true });
            }
          } catch (err) {
            console.error(`[checkPayment] decrypt error: ${err.message}`);
          }
        }
      },
      oneose: () => {},
    });
  });
}

async function payInvoice(ndebit, bolt11, amountSats, timeoutSeconds, additionalRelays) {
  const decoded = decodeBech32(ndebit);
  const pool = new SimplePool();
  const pk = generateSecretKey();
  const relays = [decoded.data.relay, ...(additionalRelays || [])];
  const toPub = decoded.data.pubkey;

  const data = newNdebitPaymentRequest(bolt11, amountSats);
  const result = await SendNdebitRequest(pool, pk, relays, toPub, data, timeoutSeconds || 45);
  pool.close(relays);
  return result;
}

const [command] = process.argv.slice(2);

switch (command) {
  case "request-invoice": {
    const stdin = await readStdin();
    const params = JSON.parse(stdin);
    try {
      const result = await requestInvoice(
        params.noffer, params.amountSats, params.description,
        params.expiresInSeconds, params.timeoutSeconds, params.additionalRelays
      );
      process.stdout.write(JSON.stringify(result) + "\n");
      process.exit(0);
    } catch (err) {
      process.stdout.write(JSON.stringify({ error: err.message }) + "\n");
      process.exit(1);
    }
    break;
  }
  case "check-payment": {
    const stdin = await readStdin();
    const params = JSON.parse(stdin);
    try {
      const result = await checkPayment(params.noffer, params.eventId, params.fromPub, params.privkeyHex, params.timeoutSeconds, params.additionalRelays);
      process.stdout.write(JSON.stringify(result) + "\n");
      process.exit(0);
    } catch (err) {
      process.stdout.write(JSON.stringify({ error: err.message }) + "\n");
      process.exit(1);
    }
    break;
  }
  case "pay-invoice": {
    const stdin = await readStdin();
    const params = JSON.parse(stdin);
    try {
      const result = await payInvoice(params.ndebit, params.bolt11, params.amountSats, params.timeoutSeconds, params.additionalRelays);
      process.stdout.write(JSON.stringify(result) + "\n");
      process.exit(0);
    } catch (err) {
      process.stdout.write(JSON.stringify({ error: err.message }) + "\n");
      process.exit(1);
    }
    break;
  }
  default:
    process.stdout.write(JSON.stringify({ error: "Unknown command: " + command }) + "\n");
    process.exit(1);
}

function readStdin() {
  return new Promise((resolve, reject) => {
    let data = "";
    process.stdin.setEncoding("utf8");
    process.stdin.on("data", (chunk) => { data += chunk; });
    process.stdin.on("end", () => resolve(data));
    process.stdin.on("error", reject);
  });
}

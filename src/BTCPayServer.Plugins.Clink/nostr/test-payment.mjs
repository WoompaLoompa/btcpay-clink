import { SimplePool, generateSecretKey, getPublicKey, finalizeEvent, nip44 } from "nostr-tools";
import { decodeBech32 } from "@shocknet/clink-sdk";

const { getConversationKey, encrypt, decrypt } = nip44;

const noffer = "noffer1qvqsyqjqvcexxdph89nrse3nvs6k2d35893xze3hxfnxgwpjvf3rxvf4vyunve3j8y6rqdpkx56nzc3k8pjnwcfkx33rydenxajrjdfjv9snqdgprfmhxue69uhhxarjvee8jtnndphkx6ewdejhgam0wf4sqgrka4zlqr820wk9nkxsklfqfpy02vva0wtvzs8lkm7t424s5y75fc6vyk7n";
const amountSats = 10;
const description = "Test payment detection " + Date.now();

// Step 1: Request invoice
console.log("=== Step 1: Requesting invoice ===");
let result, privateKey, publicKey, eventId, relay, toPub;
try {
  const decoded = decodeBech32(noffer);
  relay = decoded.data.relay;
  toPub = decoded.data.pubkey;
  const pool = new SimplePool();
  privateKey = generateSecretKey();
  publicKey = getPublicKey(privateKey);

  const data = {
    offer: decoded.data.offer,
    amount_sats: amountSats,
    description,
    expires_in_seconds: 3600,
  };

  const content = encrypt(JSON.stringify(data), getConversationKey(privateKey, toPub));
  const event = {
    content,
    created_at: Math.floor(Date.now() / 1000),
    kind: 21001,
    pubkey: publicKey,
    tags: [['p', toPub], ['clink_version', '1']],
  };
  const signed = finalizeEvent(event, privateKey);
  eventId = signed.id;

  console.log(`Relay: ${relay}`);
  console.log(`ToPub: ${toPub}`);
  console.log(`Our Pubkey: ${publicKey}`);
  console.log(`EventId: ${eventId}`);

  result = await new Promise((resolve, reject) => {
    let resolved = false;
    const timer = setTimeout(() => { if(!resolved){ pool.close([relay]); reject(new Error("timeout")); } }, 30000);
    const filter = { since: Math.floor(Date.now()/1000)-1, kinds: [21001], '#p': [publicKey], '#e': [eventId] };
    const closer = pool.subscribeMany([relay], [filter], {
      onevent: (e) => {
        if (!resolved) {
          resolved = true;
          clearTimeout(timer);
          try {
            const decrypted = decrypt(e.content, getConversationKey(privateKey, toPub));
            const resp = JSON.parse(decrypted);
            pool.close([relay]);
            if (resp.bolt11) resolve(resp);
            else reject(new Error(resp.error || "unknown error"));
          } catch(err) { reject(new Error("decrypt: "+err.message)); }
        }
      },
      oneose: () => {},
    });
    Promise.all(pool.publish([relay], signed)).catch(err => { if(!resolved){ resolved=true; clearTimeout(timer); closer.close(); pool.close([relay]); reject(err); } });
  });

  console.log("\n=== BOLT11 Invoice Created ===");
  console.log(`BOLT11: ${result.bolt11}`);
  console.log(`Amount: ${amountSats} sats`);
  console.log(`Description: ${description}`);
  console.log(`\nPay this invoice from ShockWallet, then the script will detect payment...`);
  console.log(`(checkPayment polls every 3 seconds for up to 120 seconds)\n`);
} catch(err) {
  console.error("FAILED:", err.message);
  process.exit(1);
}

// Step 2: Check payment periodically
console.log("=== Step 2: Waiting for payment ===");
const privkeyHex = Buffer.from(privateKey).toString('hex');
const timeoutMs = 120000;
const started = Date.now();

while (Date.now() - started < timeoutMs) {
  try {
    const paid = await checkPayment(noffer, eventId, publicKey, privkeyHex, 10);
    if (paid) {
      console.log("\n=== PAYMENT DETECTED! ===");
      process.exit(0);
    } else {
      const elapsed = Math.round((Date.now() - started) / 1000);
      process.stderr.write(`\r[${elapsed}s] Not paid yet...`);
    }
  } catch(e) {
    console.error("\nCheck error:", e.message);
  }
  await sleep(3000);
}

console.log("\nTimeout waiting for payment.");
process.exit(1);

async function checkPayment(noffer, eventId, fromPub, privkeyHex, timeoutSeconds) {
  const decoded = decodeBech32(noffer);
  const pool = new SimplePool();
  const privKey = new Uint8Array(Buffer.from(privkeyHex, 'hex'));
  const relay = decoded.data.relay;
  const toPub = decoded.data.pubkey;

  return new Promise((resolve) => {
    let resolved = false;
    const timer = setTimeout(() => {
      if (!resolved) {
        pool.close([relay]);
        resolve(false);
      }
    }, (timeoutSeconds || 15) * 1000);

    const filter = {
      since: Math.floor(Date.now() / 1000) - 86400,
      kinds: [21001],
      '#e': [eventId],
      '#p': [fromPub],
    };

    const closer = pool.subscribeMany([relay], [filter], {
      onevent: (e) => {
        try {
          const decrypted = decrypt(e.content, getConversationKey(privKey, toPub));
          const response = JSON.parse(decrypted);
          if (response.res === "ok") {
            if (!resolved) {
              resolved = true;
              clearTimeout(timer);
              closer.close();
              pool.close([relay]);
              resolve(true);
            }
          }
        } catch {}
      },
      oneose: () => {},
    });
  });
}

function sleep(ms) {
  return new Promise(r => setTimeout(r, ms));
}

import { SimplePool, generateSecretKey, getPublicKey, finalizeEvent, nip44 } from "nostr-tools";
import { decodeBech32 } from "@shocknet/clink-sdk";

const { getConversationKey, encrypt, decrypt } = nip44;

const noffer = "noffer1qvqsyqjqvcexxdph89nrse3nvs6k2d35893xze3hxfnxgwpjvf3rxvf4vyunve3j8y6rqdpkx56nzc3k8pjnwcfkx33rydenxajrjdfjv9snqdgprfmhxue69uhhxarjvee8jtnndphkx6ewdejhgam0wf4sqgrka4zlqr820wk9nkxsklfqfpy02vva0wtvzs8lkm7t424s5y75fc6vyk7n";
const amountSats = 10;
const description = "Test2 " + Date.now();

console.log("=== Step 1: Requesting invoice ===");
let responseEventId, privateKey, publicKey, eventId, relay, toPub;
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
  console.log(`ToPub (node): ${toPub}`);
  console.log(`Our Pubkey: ${publicKey}`);
  console.log(`Our EventID: ${eventId}`);

  const invResult = await new Promise((resolve, reject) => {
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

  console.log(`\nBOLT11: ${invResult.bolt11.substring(0, 60)}...`);
  console.log(`Response eventId from content: ${invResult.eventId}`);
  responseEventId = invResult.eventId;

  console.log(`\nNow subscribe to see what events arrive...`);
  console.log(`Subscribing to relays=${relay}, ourPub=${publicKey}, ourEvt=${eventId}, respEvt=${responseEventId}`);
} catch(err) {
  console.error("FAILED:", err.message);
  process.exit(1);
}

// Subscribe to ALL events matching both possible #e values
const pool2 = new SimplePool();
const privkeyHex = Buffer.from(privateKey).toString('hex');
const privKey = new Uint8Array(Buffer.from(privkeyHex, 'hex'));

console.log("\n=== Listening for payment (120s timeout) ===");

const filter1 = {
  since: Math.floor(Date.now()/1000) - 86400,
  kinds: [21001],
  '#e': [eventId],
  '#p': [publicKey],
};

const filter2 = {
  since: Math.floor(Date.now()/1000) - 86400,
  kinds: [21001],
  '#e': [responseEventId],
  '#p': [publicKey],
};

const filter3 = {
  since: Math.floor(Date.now()/1000) - 86400,
  kinds: [21001, 21002, 21003],
  '#p': [publicKey],
};

console.log(`Filter1 (ourEventId + #p): ${JSON.stringify(filter1)}`);
console.log(`Filter2 (respEventId + #p): ${JSON.stringify(filter2)}`);
console.log(`Filter3 (all kinds, #p): ${JSON.stringify(filter3)}`);

const started = Date.now();
const timer = setTimeout(() => {
  console.log("\nTimeout!");
  pool2.close([relay]);
  process.exit(1);
}, 120000);

let eventCount = 0;
const closer = pool2.subscribeMany([relay], [filter1, filter2, filter3], {
  onevent: (e) => {
    eventCount++;
    const elapsed = Math.round((Date.now() - started) / 1000);
    console.log(`\n[${elapsed}s] EVENT #${eventCount}: kind=${e.kind} id=${e.id.substring(0,16)}... pubkey=${e.pubkey.substring(0,16)}... tags=${JSON.stringify(e.tags)}`);
    try {
      const decrypted = decrypt(e.content, getConversationKey(privKey, toPub));
      const parsed = JSON.parse(decrypted);
      console.log(`  Decrypted: ${JSON.stringify(parsed)}`);
      if (parsed.res === "ok") {
        console.log("  *** PAYMENT DETECTED! ***");
        clearTimeout(timer);
        closer.close();
        pool2.close([relay]);
        process.exit(0);
      }
    } catch(err) {
      console.log(`  Decrypt FAILED: ${err.message}`);
    }
  },
  oneose: () => {
    console.log(`\n[${Math.round((Date.now()-started)/1000)}s] EOSE reached, waiting for real-time events...`);
  },
});

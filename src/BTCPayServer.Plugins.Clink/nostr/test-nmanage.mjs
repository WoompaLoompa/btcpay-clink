import { SimplePool, generateSecretKey, getPublicKey, finalizeEvent, nip44 } from "nostr-tools";
import { decodeBech32 } from "@shocknet/clink-sdk";

const { getConversationKey, encrypt, decrypt } = nip44;

const noffer = "noffer1qvqsyqjqvcexxdph89nrse3nvs6k2d35893xze3hxfnxgwpjvf3rxvf4vyunve3j8y6rqdpkx56nzc3k8pjnwcfkx33rydenxajrjdfjv9snqdgprfmhxue69uhhxarjvee8jtnndphkx6ewdejhgam0wf4sqgrka4zlqr820wk9nkxsklfqfpy02vva0wtvzs8lkm7t424s5y75fc6vyk7n";
const eventIdToQuery = "8c58ec5b404865d3c59876ec11205a0a95b5c70ab3960c17aca577bbaa6d6514";

const decoded = decodeBech32(noffer);
const relay = decoded.data.relay;
const toPub = decoded.data.pubkey;

console.log(`Node pubkey: ${toPub}`);
console.log(`Relay: ${relay}`);
console.log(`Offer ID: ${decoded.data.offer}`);

// Try different nManage query patterns
const queries = [
  // Pattern 1: Query invoice status by resource
  { resource: "invoice", action: "get", invoice: { id: eventIdToQuery } },
  // Pattern 2: Query offer details
  { resource: "offer", action: "get", offer: { id: decoded.data.offer } },
  // Pattern 3: Query payment status
  { resource: "payment", action: "status", payment: { eventId: eventIdToQuery } },
  // Pattern 4: List offers (to see what's available)
  { resource: "offer", action: "list" },
];

async function sendNmanageRequest(queryPayload) {
  const pool = new SimplePool();
  const privateKey = generateSecretKey();
  const publicKey = getPublicKey(privateKey);

  const content = encrypt(JSON.stringify(queryPayload), getConversationKey(privateKey, toPub));
  const event = {
    content,
    created_at: Math.floor(Date.now() / 1000),
    kind: 21003,
    pubkey: publicKey,
    tags: [['p', toPub], ['clink_version', '1']],
  };
  const signed = finalizeEvent(event, privateKey);

  return new Promise((resolve, reject) => {
    let resolved = false;
    const timer = setTimeout(() => {
      if (!resolved) { pool.close([relay]); reject(new Error("timeout")); }
    }, 15000);

    const filter = {
      since: Math.floor(Date.now() / 1000) - 1,
      kinds: [21003],
      '#p': [publicKey],
      '#e': [signed.id],
    };

    const closer = pool.subscribeMany([relay], [filter], {
      onevent: (e) => {
        if (!resolved) {
          resolved = true;
          clearTimeout(timer);
          try {
            const decrypted = decrypt(e.content, getConversationKey(privateKey, toPub));
            pool.close([relay]);
            resolve(JSON.parse(decrypted));
          } catch(err) {
            reject(new Error("decrypt failed: " + err.message));
          }
        }
      },
      oneose: () => {},
    });

    Promise.all(pool.publish([relay], signed)).catch(err => {
      if (!resolved) { resolved = true; clearTimeout(timer); closer.close(); pool.close([relay]); reject(err); }
    });
  });
}

for (let i = 0; i < queries.length; i++) {
  console.log(`\n=== Query ${i+1}: ${queries[i].action} ${queries[i].resource} ===`);
  console.log(`Payload: ${JSON.stringify(queries[i], null, 2)}`);
  try {
    const result = await sendNmanageRequest(queries[i]);
    console.log(`Response: ${JSON.stringify(result, null, 2)}`);
  } catch(err) {
    console.log(`Error: ${err.message}`);
  }
}

console.log("\nDone.");

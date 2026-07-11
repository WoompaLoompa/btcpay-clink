import { SimplePool, generateSecretKey, getPublicKey, finalizeEvent, nip44 } from "nostr-tools";
import { decodeBech32 } from "@shocknet/clink-sdk";

const { getConversationKey, encrypt, decrypt } = nip44;

const noffer = "noffer1qvqsyqjqvcexxdph89nrse3nvs6k2d35893xze3hxfnxgwpjvf3rxvf4vyunve3j8y6rqdpkx56nzc3k8pjnwcfkx33rydenxajrjdfjv9snqdgprfmhxue69uhhxarjvee8jtnndphkx6ewdejhgam0wf4sqgrka4zlqr820wk9nkxsklfqfpy02vva0wtvzs8lkm7t424s5y75fc6vyk7n";
const eventId = "8c58ec5b404865d3c59876ec11205a0a95b5c70ab3960c17aca577bbaa6d6514";

const decoded = decodeBech32(noffer);
const relay = decoded.data.relay;
const toPub = decoded.data.pubkey;

const queries = [
  // Try various invoice query formats
  { resource: "invoice", action: "get", invoice: { id: eventId } },
  { resource: "invoice", action: "get", invoice: { event_id: eventId } },
  { resource: "invoice", action: "get", id: eventId },
  { resource: "invoice", action: "status", invoice: { id: eventId } },
  { resource: "invoice", action: "list", limit: 1 },
  // Try simple status query on offer
  { resource: "offer", action: "list", pointer: decoded.data.offer },
  // Try generic "query" action
  { resource: "invoice", action: "query", id: eventId },
];

async function sendNmanageRequest(queryPayload, timeoutSeconds = 15) {
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
    }, timeoutSeconds * 1000);

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
          } catch(err) { reject(new Error("decrypt failed: " + err.message)); }
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
  console.log(`\n=== Query ${i+1} ===`);
  console.log(`${JSON.stringify(queries[i])}`);
  try {
    const result = await sendNmanageRequest(queries[i]);
    console.log(`=> ${JSON.stringify(result)}`);
  } catch(err) {
    console.log(`=> Error: ${err.message}`);
  }
}

console.log("\nDone.");

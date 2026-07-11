import { SimplePool, generateSecretKey, getPublicKey, finalizeEvent, nip44 } from "nostr-tools";
import { decodeBech32 } from "@shocknet/clink-sdk";
import { decode } from "light-bolt11-decoder";

const { getConversationKey, encrypt, decrypt } = nip44;

const noffer = "noffer1qvqsyqjqvcexxdph89nrse3nvs6k2d35893xze3hxfnxgwpjvf3rxvf4vyunve3j8y6rqdpkx56nzc3k8pjnwcfkx33rydenxajrjdfjv9snqdgprfmhxue69uhhxarjvee8jtnndphkx6ewdejhgam0wf4sqgrka4zlqr820wk9nkxsklfqfpy02vva0wtvzs8lkm7t424s5y75fc6vyk7n";
const bolt11 = "lnbc100n1p49pexcpp5shw07h0spws2jd52c63gayx660yvn73r5qz83z3e0fx7g9fmtefsdql23jhxapjyqcnwwpnxcurvve48qcnwvqcqzzsxqrrsssp5xp6g75ftfj25fdupf2mvk6nl8k5cculxjwjun2n36s6vej3kw8lq9qxpqysgqlkglx3wj72vl3m0dp6jfsrqz0rcl0ukdpn28srktqaqjurslh5493wv2cgsfxdvkt8txc0cdajureu9kcfzrphge75q6p3s7sjrweqqqfmmcca";

const decoded = decodeBech32(noffer);
const relay = decoded.data.relay;
const toPub = decoded.data.pubkey;

// Decode BOLT11 to get payment hash
const bolt11Decoded = decode(bolt11);
console.log("BOLT11 decoded:", JSON.stringify(bolt11Decoded, null, 2));

// Find the payment hash tag
let paymentHash = null;
let description = null;
let amount = null;
for (const section of bolt11Decoded.sections || []) {
  if (section.name === "payment_hash") {
    paymentHash = section.value;
  }
  if (section.name === "description") {
    description = section.value;
  }
  if (section.name === "amount") {
    amount = section.value;
  }
}
console.log(`\nPayment hash: ${paymentHash}`);
console.log(`Description: ${description}`);
console.log(`Amount: ${amount}`);

const queries = [
  // Query by payment hash
  { resource: "invoice", action: "get", invoice: { id: paymentHash } },
  { resource: "invoice", action: "get", invoice: { payment_hash: paymentHash } },
  { resource: "invoice", action: "get", invoice: { payment_hash: paymentHash, bolt11: bolt11 } },
  // Query the offer with a simpler request
  { resource: "offer", action: "get", offer: { id: "f2c479f8f3d5e649baf72fd82bb315a96f294046551b68e7a64b2737d952aa05" } },
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

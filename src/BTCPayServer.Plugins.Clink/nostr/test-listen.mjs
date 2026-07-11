import { SimplePool } from "nostr-tools";
import { decodeBech32 } from "@shocknet/clink-sdk";

const noffer = "noffer1qvqsyqjqvcexxdph89nrse3nvs6k2d35893xze3hxfnxgwpjvf3rxvf4vyunve3j8y6rqdpkx56nzc3k8pjnwcfkx33rydenxajrjdfjv9snqdgprfmhxue69uhhxarjvee8jtnndphkx6ewdejhgam0wf4sqgrka4zlqr820wk9nkxsklfqfpy02vva0wtvzs8lkm7t424s5y75fc6vyk7n";
const eventId = "8c58ec5b404865d3c59876ec11205a0a95b5c70ab3960c17aca577bbaa6d6514";
const ourPub = "fafbafcb1ff539feac759c3f2ba5b54961e963a4fbe0bedc36c262643cf67898";

const decoded = decodeBech32(noffer);
const relay = decoded.data.relay;
const pool = new SimplePool();

console.log(`Listening on ${relay}...`);
console.log(`EventId: ${eventId}`);
console.log(`Our pubkey: ${ourPub}`);
console.log(`\nPlease pay the invoice now! I'll log everything that arrives.\n`);

const started = Date.now();
let eventCount = 0;

const filters = [
  { since: Math.floor(Date.now()/1000)-86400, kinds: [21001,21002,21003], '#e': [eventId] },
  { since: Math.floor(Date.now()/1000)-86400, kinds: [21001,21002,21003], '#p': [ourPub] },
];

const closer = pool.subscribeMany([relay], filters, {
  onevent: (e) => {
    eventCount++;
    const elapsed = Math.round((Date.now() - started) / 1000);
    console.log(`\n[${elapsed}s] EVENT #${eventCount}`);
    console.log(`  kind=${e.kind} id=${e.id.substring(0,20)}... pubkey=${e.pubkey}`);
    console.log(`  tags=${JSON.stringify(e.tags)}`);
    console.log(`  content(preview)=${e.content.substring(0,40)}...`);
  },
  oneose: () => {
    console.log(`\n[${Math.round((Date.now()-started)/1000)}s] EOSE - stored events done, waiting real-time...`);
    console.log(`Total events received from stored: ${eventCount}`);
  },
});

setTimeout(() => {
  console.log(`\nTimeout after 5 min. Total events: ${eventCount}`);
  closer.close();
  pool.close([relay]);
  process.exit(0);
}, 300000);

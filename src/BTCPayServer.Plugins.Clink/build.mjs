import * as esbuild from 'esbuild';

const watch = process.argv.includes('--watch');

const nostrBridgeConfig = {
  entryPoints: ['nostr/clink-bridge.mjs'],
  outfile: 'nostr/clink-bridge.bundle.mjs',
  bundle: true,
  minify: false,
  sourcemap: false,
  platform: 'node',
  target: 'node22',
  format: 'esm',
  external: ['ws', 'bufferutil', 'utf-8-validate'],
};

if (watch) {
  const ctx = await esbuild.context(nostrBridgeConfig);
  await ctx.watch();
  console.log('Watching bridge bundle...');
} else {
  await esbuild.build(nostrBridgeConfig);
  console.log('Built clink-bridge.bundle.mjs');
}

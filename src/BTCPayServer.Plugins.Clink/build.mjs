import * as esbuild from 'esbuild';

const config = {
  entryPoints: ['Resources/js/clink-payment.js'],
  outfile: 'Resources/js/clink-payment.min.js',
  bundle: true,
  minify: true,
  sourcemap: false,
  target: 'es2020',
  format: 'esm',
};

const watch = process.argv.includes('--watch');

if (watch) {
  const ctx = await esbuild.context(config);
  await ctx.watch();
  console.log('Watching for changes...');
} else {
  await esbuild.build(config);
  console.log('Built clink-payment.min.js');
}

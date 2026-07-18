export {}

// Client-side JS bundling (if needed in the future)
// import * as esbuild from 'esbuild';
// const watch = process.argv.includes('--watch');
// 
// const config = {
//   entryPoints: ['Resources/js/clink-payment.js'],
//   outfile: 'Resources/js/clink-payment.min.js',
//   bundle: true,
//   minify: true,
//   format: 'esm',
//   platform: 'browser',
//   target: 'es2022',
// };
// 
// if (watch) {
//   const ctx = await esbuild.context(config);
//   await ctx.watch();
// } else {
//   await esbuild.build(config);
// }

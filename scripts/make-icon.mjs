// Renders src/Ember.App/Assets/ember.svg into ember.ico (PNG-compressed entries, 16–256 px)
// and the UI/docs favicons. Needs Playwright with Chromium:  node scripts/make-icon.mjs
import { createRequire } from 'node:module';
import { readFileSync, writeFileSync, copyFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const svgPath = join(root, 'src/Ember.App/Assets/ember.svg');
const svg = readFileSync(svgPath, 'utf8');
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

const browser = await chromium.launch();
const page = await browser.newPage();
const pngs = [];
for (const size of sizes) {
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(
    `<html><body style="margin:0;background:transparent">` +
      `<img src="data:image/svg+xml;base64,${Buffer.from(svg).toString('base64')}" width="${size}" height="${size}"></body></html>`,
  );
  pngs.push(await page.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } }));
}
await browser.close();

// ICO: 6-byte header, 16-byte directory entry per image, then the PNG data.
const header = Buffer.alloc(6);
header.writeUInt16LE(0, 0);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(sizes.length, 4);
let offset = 6 + 16 * sizes.length;
const entries = sizes.map((size, i) => {
  const e = Buffer.alloc(16);
  e.writeUInt8(size >= 256 ? 0 : size, 0);
  e.writeUInt8(size >= 256 ? 0 : size, 1);
  e.writeUInt16LE(1, 4);
  e.writeUInt16LE(32, 6);
  e.writeUInt32LE(pngs[i].length, 8);
  e.writeUInt32LE(offset, 12);
  offset += pngs[i].length;
  return e;
});
writeFileSync(join(root, 'src/Ember.App/Assets/ember.ico'), Buffer.concat([header, ...entries, ...pngs]));

for (const dir of ['ui/public', 'docs/public']) {
  mkdirSync(join(root, dir), { recursive: true });
  copyFileSync(svgPath, join(root, dir, 'favicon.svg'));
}
console.log('ember.ico written with sizes', sizes.join(', '));

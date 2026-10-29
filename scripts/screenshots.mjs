// Takes screenshots of every UI page with the sample data (mock bridge), for the docs and for
// checking texts and umlauts. Needs a running "npm run preview" (port 4173) and Playwright.
//   node scripts/screenshots.mjs [outDir] [lang...]
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');

const out = process.argv[2] ?? 'docs/src/assets/screens';
const langs = process.argv.slice(3).length ? process.argv.slice(3) : ['de'];
const routes = ['start', 'proxy', 'git', 'software', 'power', 'system', 'ai', 'access', 'settings', 'log'];
const base = process.env.EMBER_PREVIEW ?? 'http://localhost:4173';

mkdirSync(out, { recursive: true });
const browser = await chromium.launch();
for (const lang of langs) {
  for (const scheme of ['light', 'dark']) {
    const page = await browser.newPage({ viewport: { width: 1280, height: 860 }, colorScheme: scheme, locale: lang });
    for (const route of routes) {
      await page.goto(`${base}/?lang=${lang}&clean#/${route}`);
      await page.waitForSelector('.page', { timeout: 10000 });
      await page.waitForTimeout(700);
      if (route === 'power') await page.click('.toggle').catch(() => {});
      await page.waitForTimeout(250);
      await page.screenshot({ path: join(out, `${route}-${lang}-${scheme}.png`), fullPage: route !== 'log' });
    }
    await page.close();
  }
}
await browser.close();
console.log(`screenshots written to ${out}`);

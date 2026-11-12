// Copies the built handbook (docs/dist, Astro Starlight) into the UI as ui/dist/Ember/, so Ember shows
// it offline with Ctrl+K. The docs are built for https://pixelshober.github.io/Ember/; served from
// https://ui.ember.invalid/Ember/ every link stays the same. Run after "npm run build" in ui/ and docs/.
import { cpSync, existsSync, rmSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const from = join(root, 'docs', 'dist');
const to = join(root, 'ui', 'dist', 'Ember');

if (!existsSync(join(from, 'index.html'))) {
  console.error('docs/dist fehlt: erst in docs/ "npm ci" und "npm run build" ausführen.');
  process.exit(1);
}
if (!existsSync(join(root, 'ui', 'dist', 'index.html'))) {
  console.error('ui/dist fehlt: erst in ui/ "npm run build" ausführen.');
  process.exit(1);
}
rmSync(to, { recursive: true, force: true });
cpSync(from, to, { recursive: true });
console.log('Handbuch eingebettet: ui/dist/Ember');

// Writes latest.json and SHA256SUMS.txt for a release.
//   node scripts/make-manifest.mjs --version 1.3.0 --code-version 1.2.0 --exe out/Ember.exe --ui out/ember-ui-1.3.0.zip --notes <url> --out out
import { createHash } from 'node:crypto';
import { readFileSync, statSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';

const args = Object.fromEntries(
  process.argv.slice(2).reduce((acc, cur, i, all) => (cur.startsWith('--') ? [...acc, [cur.slice(2), all[i + 1]]] : acc), []),
);
for (const required of ['version', 'exe', 'ui', 'out']) {
  if (!args[required]) {
    console.error(`missing --${required}`);
    process.exit(1);
  }
}

const sha = (file) => createHash('sha256').update(readFileSync(file)).digest('hex');
const uiManifest = JSON.parse(readFileSync(join(import.meta.dirname, '../ui/public/ui-manifest.json'), 'utf8'));
const appInfo = readFileSync(join(import.meta.dirname, '../src/Ember.Core/Infrastructure/AppInfo.cs'), 'utf8');
const bridge = Number(/BridgeVersion\s*=\s*(\d+)/.exec(appInfo)?.[1] ?? 1);

const manifest = {
  version: args.version,
  bridge,
  published: new Date().toISOString(),
  exe: { name: basename(args.exe), sha256: sha(args.exe), size: statSync(args.exe).size, codeVersion: args['code-version'] || args.version },
  ui: { name: basename(args.ui), sha256: sha(args.ui), version: args.version, requiresBridge: uiManifest.requiresBridge },
  notes: args.notes ?? null,
};

writeFileSync(join(args.out, 'latest.json'), JSON.stringify(manifest, null, 2) + '\n');
writeFileSync(
  join(args.out, 'SHA256SUMS.txt'),
  [args.exe, args.ui].map((f) => `${sha(f)}  ${basename(f)}`).join('\n') + '\n',
);
console.log(JSON.stringify(manifest, null, 2));

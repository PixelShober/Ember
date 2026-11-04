// Dependency guard: Ember ships with exactly one external runtime library (Microsoft.Web.WebView2)
// plus the Svelte runtime in the UI bundle. Everything else is build/test tooling.
//   node scripts/check-deps.mjs            (needs the .NET SDK for "dotnet list package")
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const allowed = {
  'src/Ember.App/Ember.App.csproj': ['Microsoft.Web.WebView2'],
  'src/Ember.Core/Ember.Core.csproj': [],
};
const allowedUi = ['svelte'];
// Added implicitly by the .NET SDK for single-file publishing; build-time only, nothing of it ships.
const sdkBuildTools = ['Microsoft.NET.ILLink.Tasks'];
const errors = [];

for (const [project, ok] of Object.entries(allowed)) {
  const out = execFileSync('dotnet', ['list', join(root, project), 'package', '--include-transitive', '--format', 'json'], { encoding: 'utf8' });
  const json = JSON.parse(out);
  const ids = new Set();
  for (const p of json.projects ?? []) {
    for (const fw of p.frameworks ?? []) {
      for (const pkg of [...(fw.topLevelPackages ?? []), ...(fw.transitivePackages ?? [])]) ids.add(pkg.id);
    }
  }
  console.log(`${project}: ${[...ids].join(', ') || '(no packages)'}`);
  for (const id of ids) if (!ok.includes(id) && !sdkBuildTools.includes(id)) errors.push(`${project}: unexpected runtime package ${id}`);
}

const ui = JSON.parse(readFileSync(join(root, 'ui/package.json'), 'utf8'));
const uiDeps = Object.keys(ui.dependencies ?? {});
console.log(`ui/package.json dependencies: ${uiDeps.join(', ')}`);
for (const d of uiDeps) if (!allowedUi.includes(d)) errors.push(`ui/package.json: unexpected runtime dependency ${d} (dev tools belong in devDependencies)`);

// The UI must not need a newer bridge than the exe offers.
const appInfo = readFileSync(join(root, 'src/Ember.Core/Infrastructure/AppInfo.cs'), 'utf8');
const bridge = Number(/BridgeVersion\s*=\s*(\d+)/.exec(appInfo)?.[1]);
const uiManifest = JSON.parse(readFileSync(join(root, 'ui/public/ui-manifest.json'), 'utf8'));
console.log(`bridge: exe ${bridge}, UI requires ${uiManifest.requiresBridge}`);
if (!(uiManifest.requiresBridge <= bridge)) errors.push(`ui-manifest.json requires bridge ${uiManifest.requiresBridge}, but AppInfo.BridgeVersion is ${bridge}`);

if (errors.length) {
  console.error('\n' + errors.join('\n'));
  process.exit(1);
}
console.log('Dependencies OK.');

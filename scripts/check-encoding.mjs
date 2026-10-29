// Guards the texts: every text file is valid UTF-8 without mojibake, German and English
// locale files have the same keys and placeholders, and every key the UI uses exists.
//   node scripts/check-encoding.mjs
import { execSync } from 'node:child_process';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { extname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const textExt = new Set(['.cs', '.csproj', '.props', '.targets', '.xaml', '.manifest', '.json', '.md', '.mdx', '.ts', '.js', '.mjs',
  '.svelte', '.css', '.html', '.yml', '.yaml', '.txt', '.xml', '.svg', '.ps1', '.astro', '.sln', '.editorconfig', '.gitignore', '.gitattributes']);
const skipDirs = new Set(['node_modules', 'dist', 'bin', 'obj', '.git', '.astro', 'artifacts', 'release', 'TestResults']);

function listFiles() {
  try {
    const out = execSync('git ls-files -co --exclude-standard', { cwd: root, encoding: 'utf8' }).split('\n').filter(Boolean);
    if (out.length > 0) return out;
  } catch {
    // not a git checkout – walk the tree
  }
  const files = [];
  const walk = (dir) => {
    for (const name of readdirSync(dir)) {
      if (skipDirs.has(name)) continue;
      const full = join(dir, name);
      if (statSync(full).isDirectory()) walk(full);
      else files.push(relative(root, full));
    }
  };
  walk(root);
  return files;
}

// UTF-8 bytes decoded as Windows-1252 turn "ä" into "A-tilde + currency sign" and so on. Written as escapes.
const mojibake = /Ã[¤¶¼\u0084\u0096\u009C\u009F„–œŸš]|â€[žœ“”™š˜]|Â[ ­·]/;
const decoder = new TextDecoder('utf-8', { fatal: true });
const errors = [];

for (const file of listFiles()) {
  const ext = extname(file) || file.slice(file.lastIndexOf('/') + 1);
  if (!textExt.has(ext) && !textExt.has('.' + ext)) continue;
  let text;
  try {
    text = decoder.decode(readFileSync(join(root, file)));
  } catch {
    errors.push(`${file}: is not valid UTF-8`);
    continue;
  }
  const lines = text.split('\n');
  lines.forEach((line, i) => {
    if (mojibake.test(line)) errors.push(`${file}:${i + 1}: looks like broken umlauts (mojibake): ${line.trim().slice(0, 80)}`);
    if (line.includes(String.fromCharCode(0xfffd))) errors.push(`${file}:${i + 1}: contains the replacement character U+FFFD`);
  });
}

// ---- locale files
const flatten = (obj, prefix = '', into = {}) => {
  for (const [k, v] of Object.entries(obj)) {
    const key = prefix ? `${prefix}.${k}` : k;
    if (v && typeof v === 'object') flatten(v, key, into);
    else into[key] = v;
  }
  return into;
};
const de = flatten(JSON.parse(readFileSync(join(root, 'ui/src/locales/de.json'), 'utf8')));
const en = flatten(JSON.parse(readFileSync(join(root, 'ui/src/locales/en.json'), 'utf8')));
const ph = (s) => [...String(s).matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join(',');
for (const k of Object.keys(de)) {
  if (!(k in en)) errors.push(`en.json: missing key ${k}`);
  else if (ph(de[k]) !== ph(en[k])) errors.push(`placeholders differ for ${k}: de {${ph(de[k])}} en {${ph(en[k])}}`);
}
for (const k of Object.keys(en)) if (!(k in de)) errors.push(`de.json: missing key ${k}`);

const translit = /\b(fuer|ueber|Ueber|koennen|muessen|waehlen|loeschen|Loeschen|pruefen|Pruefen|zurueck|Zurueck|oeffnen|Oeffnen|schliessen|Schliessen|Groesse|ausfuehren|geloescht|geprueft|Schluessel|aendern|Aenderung)\b/;
for (const [k, v] of Object.entries(de)) if (translit.test(v)) errors.push(`de.json ${k}: use real umlauts/ß instead of "${v.match(translit)[0]}"`);

// ---- keys used by the UI
const uiFiles = listFiles().filter((f) => f.startsWith('ui/src/') && /\.(svelte|ts)$/.test(f) && !f.endsWith('.test.ts'));
for (const file of uiFiles) {
  const text = readFileSync(join(root, file), 'utf8');
  for (const m of text.matchAll(/\bt\(\s*'([a-zA-Z0-9.\-]+)'/g)) {
    if (!(m[1] in de)) errors.push(`${file}: unknown translation key "${m[1]}"`);
  }
}

if (errors.length) {
  console.error(errors.join('\n'));
  console.error(`\n${errors.length} problem(s) found.`);
  process.exit(1);
}
console.log(`Encoding and translations OK (${Object.keys(de).length} keys, ${uiFiles.length} UI files checked).`);

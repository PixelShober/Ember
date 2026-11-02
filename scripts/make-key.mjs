// Creates a starter key for Ember (the key a user enters when there is no team.json yet).
//   node scripts/make-key.mjs <proxy|proxyPlaywright|all>
// Prints the key (hand it out, never commit it) and the line for src/Ember.Core/Config/StarterKeys.cs.
// The key has 100 random bits, so its SHA-256 can sit in the public code.
import { createHash, randomBytes } from 'node:crypto';

const levels = ['proxy', 'proxyPlaywright', 'all'];
const level = process.argv[2];
if (!levels.includes(level)) {
  console.error(`usage: node scripts/make-key.mjs <${levels.join('|')}>`);
  process.exit(1);
}

// Crockford base32: no I, L, O, U, so the key survives being read out or typed from paper.
const alphabet = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
const bytes = randomBytes(20);
const chars = [...bytes].map((b) => alphabet[b & 31]).join('');
const key = 'EMBER-' + chars.match(/.{5}/g).join('-');

// Same normalisation as StarterKeys.Normalize in the exe.
const normalized = chars;
const hash = createHash('sha256').update('ember-key-v1:' + normalized, 'utf8').digest('hex');

console.log(`key:  ${key}`);
console.log(`code: ("${level}", "${hash}"),`);

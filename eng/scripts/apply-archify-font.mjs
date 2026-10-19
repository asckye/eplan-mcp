// Puts the project's diagram font, Maple Mono, first in the font stacks of the locally installed
// archify skill template, so every diagram rendered afterwards uses it where it is installed.
//
// Why the template and not the finished HTML: archify's finalize/check gates bind their receipts to the
// delivered bytes, so editing a diagram after delivery would break its provenance. Fonts are referenced
// by name only (no embedded file, no network URL); readers without Maple Mono fall back to archify's
// original stack, and the HTML stays self-contained.
//
// Idempotent. Run after restoring or updating the skill:
//   node eng/scripts/apply-archify-font.mjs
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const template = fileURLToPath(new URL('../../.claude/skills/archify/assets/template.html', import.meta.url));
const maple = "'Maple Mono NF CN', 'Maple Mono CN', 'Maple Mono'";

// The two stacks archify uses for running text and for code/labels (archify 3.2 template).
const stacks = [
  "font-family: -apple-system, BlinkMacSystemFont,",
  "font-family: 'JetBrains Mono', ui-monospace,",
];

const original = readFileSync(template, 'utf8');
if (original.includes(maple)) {
  console.log(`Maple Mono is already first in ${template}`);
  process.exit(0);
}

let html = original;
for (const stack of stacks) {
  const count = html.split(stack).length - 1;
  if (count === 0) {
    console.error(`Font stack not found (did the archify template change?): ${stack}`);
    process.exit(1);
  }
  html = html.replaceAll(stack, stack.replace('font-family: ', `font-family: ${maple}, `));
  console.log(`Prepended Maple Mono to ${count} occurrence(s) of: ${stack}`);
}

writeFileSync(template, html, 'utf8');
console.log(`Updated ${template}`);

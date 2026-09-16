import { readFileSync, readdirSync } from 'node:fs';
import { Script } from 'node:vm';
import { parse } from 'parse5';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const webRoot = path.resolve(fileURLToPath(new URL('..', import.meta.url)));
const sourceRoots = ['assets', 'concepts'].map(directory => path.join(webRoot, directory));

function collectJavaScript(directory) {
  return readdirSync(directory, { withFileTypes: true })
    .flatMap((entry) => {
      const candidate = path.join(directory, entry.name);
      if (entry.isDirectory()) return collectJavaScript(candidate);
      return entry.isFile() && entry.name.endsWith('.js') ? [candidate] : [];
    })
    .sort((left, right) => left.localeCompare(right));
}

const files = sourceRoots.flatMap(collectJavaScript);
if (files.length === 0) {
  throw new Error('No first-party website JavaScript files were found.');
}

const failures = [];
function checkInlineScripts(node) {
  const attrs = new Map((node.attrs ?? []).map(({ name, value }) => [name, value]));
  if (node.tagName === 'script' && !attrs.has('src') && !attrs.has('type')) {
    try { new Script((node.childNodes ?? []).map(child => child.value ?? '').join('')); }
    catch (error) { failures.push('index.html inline script: ' + error.message); }
  }
  for (const child of node.childNodes ?? []) checkInlineScripts(child);
}
checkInlineScripts(parse(readFileSync(path.join(webRoot, 'index.html'), 'utf8')));
for (const file of files) {
  const relative = path.relative(webRoot, file);
  const result = spawnSync(process.execPath, ['--check', relative], {
    cwd: webRoot,
    encoding: 'utf8',
  });

  if (result.status !== 0) {
    failures.push(relative + '\n' + (result.stderr || result.stdout));
  }
}

if (failures.length > 0) {
  throw new Error('JavaScript syntax validation failed:\n\n' + failures.join('\n\n'));
}

console.log('JavaScript syntax passed (' + files.length + ' first-party files).');

import { readdirSync } from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const webRoot = path.resolve(fileURLToPath(new URL('..', import.meta.url)));
const assetsRoot = path.join(webRoot, 'assets');

function collectJavaScript(directory) {
  return readdirSync(directory, { withFileTypes: true })
    .flatMap((entry) => {
      const candidate = path.join(directory, entry.name);
      if (entry.isDirectory()) return collectJavaScript(candidate);
      return entry.isFile() && entry.name.endsWith('.js') ? [candidate] : [];
    })
    .sort((left, right) => left.localeCompare(right));
}

const files = collectJavaScript(assetsRoot);
if (files.length === 0) {
  throw new Error('No first-party website JavaScript files were found.');
}

const failures = [];
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
